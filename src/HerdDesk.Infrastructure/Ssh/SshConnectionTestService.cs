using System.Diagnostics;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Configuration;

namespace HerdDesk.Infrastructure.Ssh;

public sealed class SshConnectionTestService : ISshConnectionTester, ISshConfigPreview
{
    private readonly OpenSshLocator _locator;
    private readonly ISshProcessRunner _runner;
    private readonly ISshHostKeyStore _trust;
    private readonly OpenSshConfigResolver _preview;
    private readonly string _knownHostsFile;
    private readonly IClock _clock;
    private CancellationTokenSource? _run;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HostKeyCandidate? _pendingUnknown;

    public SshConnectionTestService(AppDataPaths paths, IClock? clock = null)
        : this(
            OpenSshLocator.SystemDefault(),
            new SshOwnedProcessRunner(),
            new HostKeyTrustStore(paths),
            paths.KnownHostsFile,
            clock)
    {
    }

    internal SshConnectionTestService(
        OpenSshLocator locator,
        ISshProcessRunner runner,
        ISshHostKeyStore trust,
        string knownHostsFile,
        IClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(trust);
        ArgumentException.ThrowIfNullOrWhiteSpace(knownHostsFile);
        _locator = locator;
        _runner = runner;
        _trust = trust;
        _preview = new OpenSshConfigResolver(locator, runner);
        _knownHostsFile = knownHostsFile;
        _clock = clock ?? new SystemClock();
    }

    public ValueTask<SshPreviewResult> PreviewAsync(
        SshDeviceSettings settings, CancellationToken cancellationToken = default) =>
        _preview.PreviewAsync(settings, cancellationToken);

    public async ValueTask<SshConnectionTestResult> TestUntilHostKeyAsync(
        SshDeviceSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var run = await BeginRunAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stages = new List<SshStageResult>();
            var executable = await RunExecutableAsync(run.Token, stages).ConfigureAwait(false);
            if (executable is not null)
                return executable;

            var preview = await RunPreviewAsync(settings, run.Token, stages).ConfigureAwait(false);
            if (preview.Result is not null)
                return preview.Result;

            return await RunHostKeyAsync(settings, preview.Configuration!, run.Token, stages)
                .ConfigureAwait(false);
        }
        finally
        {
            CompleteRun(run);
        }
    }

    public async ValueTask<SshConnectionTestResult> ConfirmUnknownHostAsync(
        HostKeyCandidate candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (_pendingUnknown is null ||
            _pendingUnknown.KeyBlobSha256 != candidate.KeyBlobSha256 ||
            _pendingUnknown.Host != HostKeyTrustStore.NormalizeHost(candidate.Host) ||
            _pendingUnknown.Port != candidate.Port)
        {
            return Failed(SshCodes.HostKeyUnknown, [], null, null);
        }

        var written = await _trust.ConfirmUnknownAsync(
            candidate with { VerifiedOutOfBand = false, Host = HostKeyTrustStore.NormalizeHost(candidate.Host) },
            cancellationToken).ConfigureAwait(false);
        if (!written.Succeeded)
        {
            return Failed(
                written.Code ?? SshCodes.PersistenceFailed,
                [],
                null,
                new HostKeyAssessment(
                    written.Code == SshCodes.HostKeyChanged ? HostKeyStatus.Changed : HostKeyStatus.UnknownCandidate,
                    candidate with { VerifiedOutOfBand = false },
                    written.Revision ?? 0));
        }

        _pendingUnknown = null;
        return new(
            SshConnectionTestPhase.AwaitingHostVerification,
            null,
            [],
            null,
            new HostKeyAssessment(HostKeyStatus.Trusted, candidate with { VerifiedOutOfBand = true },
                written.Revision!.Value));
    }

    public async ValueTask<SshConnectionTestResult> AuthenticateAsync(
        SshDeviceSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var run = await BeginRunAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stages = new List<SshStageResult>();
            var executable = await RunExecutableAsync(run.Token, stages).ConfigureAwait(false);
            if (executable is not null)
                return executable;
            var preview = await RunPreviewAsync(settings, run.Token, stages).ConfigureAwait(false);
            if (preview.Result is not null)
                return preview.Result;
            var host = await RunHostKeyAsync(settings, preview.Configuration!, run.Token, stages)
                .ConfigureAwait(false);
            if (host.HostKey?.Status != HostKeyStatus.Trusted)
                return host;
            return await RunProbeAsync(settings, preview.Configuration!, host.HostKey, run.Token, stages)
                .ConfigureAwait(false);
        }
        finally
        {
            CompleteRun(run);
        }
    }

    public async ValueTask<SshConnectionTestResult> CancelAsync()
    {
        var current = _run;
        if (current is not null)
        {
            try
            {
                await current.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        return new(SshConnectionTestPhase.Cancelled, SshCodes.TestCancelled, [], null, null);
    }

    private async ValueTask<CancellationTokenSource> BeginRunAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_run is { } previous)
            {
                try
                {
                    await previous.CancelAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                }
            }

            var next = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _run = next;
            return next;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void CompleteRun(CancellationTokenSource run)
    {
        if (ReferenceEquals(_run, run))
            _run = null;
        run.Dispose();
    }

    private async ValueTask<SshConnectionTestResult?> RunExecutableAsync(
        CancellationToken cancellationToken, List<SshStageResult> stages)
    {
        var started = Stopwatch.GetTimestamp();
        if (!_locator.SshExists ||
            !SshProcessSpecFactory.TryVersion(_locator, out var spec, out _))
        {
            stages.Add(Stage(SshConnectionTestPhase.Executable, SshCodes.ExecutableUnavailable, started));
            return Failed(SshCodes.ExecutableUnavailable, stages, null, null);
        }

        var ran = await RunSpecAsync(spec, cancellationToken).ConfigureAwait(false);
        if (ran.Cancelled)
        {
            stages.Add(Stage(SshConnectionTestPhase.Executable, SshCodes.TestCancelled, started));
            return Cancelled(stages, null, null);
        }

        if (ran.TimedOut)
        {
            stages.Add(Stage(SshConnectionTestPhase.Executable, SshCodes.TestTimeout, started));
            return Failed(SshCodes.TestTimeout, stages, null, null);
        }

        var text = ran.Stderr.Length > 0 ? ran.Stderr : ran.Stdout;
        if (ran.ExitCode != 0 || !OpenSshGParser.LooksLikeOpenSshVersion(text))
        {
            stages.Add(Stage(SshConnectionTestPhase.Executable, SshCodes.ExecutableUnavailable, started));
            return Failed(SshCodes.ExecutableUnavailable, stages, null, null);
        }

        stages.Add(Stage(SshConnectionTestPhase.Executable, SshCodes.Ok, started,
            [new SshStageMetadatum("openssh", "yes")]));
        return null;
    }

    private async ValueTask<(SshConnectionTestResult? Result, SshResolvedConfiguration? Configuration)>
        RunPreviewAsync(
            SshDeviceSettings settings,
            CancellationToken cancellationToken,
            List<SshStageResult> stages)
    {
        var started = Stopwatch.GetTimestamp();
        if (!SshProcessSpecFactory.TryConfigPreview(_locator, settings, out var spec, out var code))
        {
            stages.Add(Stage(SshConnectionTestPhase.ConfigPreview, code, started));
            return (Failed(code, stages, null, null), null);
        }

        var ran = await RunSpecAsync(spec, cancellationToken).ConfigureAwait(false);
        if (ran.Cancelled)
        {
            stages.Add(Stage(SshConnectionTestPhase.ConfigPreview, SshCodes.TestCancelled, started));
            return (Cancelled(stages, null, null), null);
        }

        if (ran.TimedOut)
        {
            stages.Add(Stage(SshConnectionTestPhase.ConfigPreview, SshCodes.TestTimeout, started));
            return (Failed(SshCodes.TestTimeout, stages, null, null), null);
        }

        if (ran.ExitCode != 0)
        {
            stages.Add(Stage(SshConnectionTestPhase.ConfigPreview, SshCodes.ConfigInvalid, started));
            return (Failed(SshCodes.ConfigInvalid, stages, null, null), null);
        }

        if (!OpenSshGParser.TryParse(ran.Stdout, settings, out var parsed, out var parseCode))
        {
            var fail = parseCode == SshCodes.Ok ? SshCodes.ConfigInvalid : parseCode;
            stages.Add(Stage(SshConnectionTestPhase.ConfigPreview, fail, started));
            return (Failed(fail, stages, null, null), null);
        }

        stages.Add(Stage(
            SshConnectionTestPhase.ConfigPreview,
            SshCodes.Ok,
            started,
            [
                new SshStageMetadatum("identity_file_count", parsed.IdentityFiles.Count.ToString()),
                new SshStageMetadatum("has_proxy_jump", parsed.ProxyJump is null ? "no" : "yes"),
                new SshStageMetadatum("has_agent", parsed.IdentityAgent is null ? "no" : "yes")
            ]));
        return (null, parsed);
    }

    private async ValueTask<SshConnectionTestResult> RunHostKeyAsync(
        SshDeviceSettings settings,
        SshResolvedConfiguration preview,
        CancellationToken cancellationToken,
        List<SshStageResult> stages)
    {
        var hops = new List<(string Host, int Port, SshHopKind Kind)>();
        if (!string.IsNullOrEmpty(preview.ProxyJump))
            hops.Add((preview.ProxyJump, 22, SshHopKind.ProxyJump));
        hops.Add((preview.Hostname, preview.Port, SshHopKind.Target));

        HostKeyAssessment? last = null;
        foreach (var hop in hops)
        {
            var started = Stopwatch.GetTimestamp();
            if (!SshProcessSpecFactory.TryHostKeyScan(_locator, hop.Host, hop.Port, out var spec, out var code))
            {
                stages.Add(Stage(SshConnectionTestPhase.HostKey, code, started));
                return Failed(code, stages, preview, new(HostKeyStatus.Unavailable, null, 0));
            }

            var ran = await RunSpecAsync(spec, cancellationToken).ConfigureAwait(false);
            if (ran.Cancelled)
            {
                stages.Add(Stage(SshConnectionTestPhase.HostKey, SshCodes.TestCancelled, started));
                return Cancelled(stages, preview, last);
            }

            if (ran.TimedOut)
            {
                stages.Add(Stage(SshConnectionTestPhase.HostKey, SshCodes.TestTimeout, started));
                return Failed(SshCodes.TestTimeout, stages, preview, last);
            }

            if (!OpenSshGParser.TryParseKeyScan(
                    ran.Stdout, hop.Host, hop.Port, hop.Kind, _clock.UtcNow, out var candidate, out _))
            {
                stages.Add(Stage(SshConnectionTestPhase.HostKey, SshCodes.HostKeyUnknown, started));
                return Failed(SshCodes.HostKeyUnknown, stages, preview,
                    new(HostKeyStatus.Unavailable, null, 0));
            }

            last = await _trust.AssessAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (last.Status == HostKeyStatus.Unavailable)
            {
                stages.Add(Stage(SshConnectionTestPhase.HostKey, SshCodes.PersistenceFailed, started,
                    [new SshStageMetadatum("hop", hop.Kind == SshHopKind.ProxyJump ? "proxy_jump" : "target")]));
                return Failed(SshCodes.PersistenceFailed, stages, preview, last);
            }

            stages.Add(Stage(SshConnectionTestPhase.HostKey, MapHostCode(last.Status), started,
                [new SshStageMetadatum("hop", hop.Kind == SshHopKind.ProxyJump ? "proxy_jump" : "target")]));
            if (last.Status == HostKeyStatus.Changed)
            {
                _pendingUnknown = null;
                return Failed(SshCodes.HostKeyChanged, stages, preview, last);
            }

            if (last.Status == HostKeyStatus.UnknownCandidate)
            {
                _pendingUnknown = last.Candidate;
                return new(
                    SshConnectionTestPhase.AwaitingHostVerification,
                    SshCodes.HostKeyUnknown,
                    stages,
                    preview,
                    last);
            }
        }

        _pendingUnknown = null;
        return new(SshConnectionTestPhase.AwaitingHostVerification, null, stages, preview, last);
    }

    private async ValueTask<SshConnectionTestResult> RunProbeAsync(
        SshDeviceSettings settings,
        SshResolvedConfiguration preview,
        HostKeyAssessment hostKey,
        CancellationToken cancellationToken,
        List<SshStageResult> stages)
    {
        var started = Stopwatch.GetTimestamp();
        if (!SshProcessSpecFactory.TryAuthProbe(_locator, settings, _knownHostsFile, out var spec, out var code))
        {
            stages.Add(Stage(SshConnectionTestPhase.Authenticating, code, started));
            return Failed(code, stages, preview, hostKey);
        }

        var ran = await RunSpecAsync(spec, cancellationToken).ConfigureAwait(false);
        if (ran.Cancelled)
        {
            stages.Add(Stage(SshConnectionTestPhase.Authenticating, SshCodes.TestCancelled, started));
            return Cancelled(stages, preview, hostKey);
        }

        if (ran.TimedOut)
        {
            stages.Add(Stage(SshConnectionTestPhase.Authenticating, SshCodes.TestTimeout, started));
            return Failed(SshCodes.TestTimeout, stages, preview, hostKey);
        }

        if (ran.ExitCode != 0)
        {
            var classified = OpenSshGParser.ClassifyAuthOutput(ran.Stderr);
            stages.Add(Stage(SshConnectionTestPhase.Authenticating, classified, started));
            return Failed(classified, stages, preview, hostKey);
        }

        stages.Add(Stage(SshConnectionTestPhase.Authenticating, SshCodes.Ok, started));
        return new(SshConnectionTestPhase.Succeeded, null, stages, preview, hostKey);
    }

    private async ValueTask<SshProcessRunResult> RunSpecAsync(
        SshProcessSpec spec, CancellationToken cancellationToken)
    {
        try
        {
            return await _runner.RunAsync(spec, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new(255, "", "", false, true, null);
        }
        catch (Exception)
        {
            return new(255, "", "", false, cancellationToken.IsCancellationRequested, null);
        }
    }

    private static string MapHostCode(HostKeyStatus status) => status switch
    {
        HostKeyStatus.Trusted => SshCodes.Ok,
        HostKeyStatus.Changed => SshCodes.HostKeyChanged,
        HostKeyStatus.UnknownCandidate => SshCodes.HostKeyUnknown,
        _ => SshCodes.TestFailed
    };

    private static SshStageResult Stage(
        SshConnectionTestPhase phase,
        string code,
        long started,
        IReadOnlyList<SshStageMetadatum>? metadata = null) =>
        new(
            phase,
            code,
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            metadata ?? []);

    private static SshConnectionTestResult Failed(
        string code,
        IReadOnlyList<SshStageResult> stages,
        SshResolvedConfiguration? preview,
        HostKeyAssessment? hostKey) =>
        new(SshConnectionTestPhase.Failed, code, stages, preview, hostKey);

    private static SshConnectionTestResult Cancelled(
        IReadOnlyList<SshStageResult> stages,
        SshResolvedConfiguration? preview,
        HostKeyAssessment? hostKey) =>
        new(SshConnectionTestPhase.Cancelled, SshCodes.TestCancelled, stages, preview, hostKey);
}
