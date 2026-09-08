using System.Diagnostics;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Configuration;
using HerdDesk.Infrastructure.Diagnostics;

namespace HerdDesk.Infrastructure.Ssh;

public sealed class RemoteHelperDeploymentService : IHelperDeploymentService
{
    private readonly IHelperDeploymentPlanner _planner;
    private readonly IHelperPublisher _publisher;
    private readonly HelperDeploymentReceiptStore _receipts;
    private readonly IDiagnosticSink? _diagnostics;
    private readonly DiagnosticAliasProjector? _aliases;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _run;
    private SshDeviceSettings? _settings;
    private byte[]? _payload;
    private string? _stagingId;
    private HelperConsentValues? _consent;

    public RemoteHelperDeploymentService(AppDataPaths paths, string applicationVersion, IClock? clock = null)
        : this(
            paths,
            new TrustedHelperManifestProvider(applicationVersion),
            OpenSshLocator.SystemDefault(),
            new SshOwnedProcessRunner(),
            clock)
    {
    }

    internal RemoteHelperDeploymentService(
        AppDataPaths paths,
        ITrustedHelperManifest manifest,
        OpenSshLocator locator,
        ISshProcessRunner runner,
        IClock? clock = null,
        IDiagnosticSink? diagnostics = null,
        DiagnosticAliasProjector? aliases = null,
        HelperReceiptStoreHooks? receiptHooks = null)
        : this(
            new HelperDeploymentPlanner(
                manifest,
                new RemotePlatformProbe(locator, runner, paths.KnownHostsFile, manifest.Targets)),
            new RemoteHelperPublisher(locator, runner, paths.KnownHostsFile),
            new HelperDeploymentReceiptStore(paths, receiptHooks),
            diagnostics,
            aliases)
    {
        _ = clock;
    }

    internal RemoteHelperDeploymentService(
        IHelperDeploymentPlanner planner,
        IHelperPublisher publisher,
        HelperDeploymentReceiptStore receipts,
        IDiagnosticSink? diagnostics = null,
        DiagnosticAliasProjector? aliases = null)
    {
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(receipts);
        _planner = planner;
        _publisher = publisher;
        _receipts = receipts;
        _diagnostics = diagnostics;
        _aliases = aliases;
    }

    public HelperDeploymentPhase Phase { get; private set; } = HelperDeploymentPhase.Idle;
    public DeploymentPlan? Plan { get; private set; }

    public async ValueTask<HelperDeploymentResult> PlanAsync(
        DeviceId device,
        SessionKey session,
        SshDeviceSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Phase = HelperDeploymentPhase.Planning;
            _consent = null;
            _payload = null;
            _stagingId = null;
            _settings = settings;
            Plan = null;
            var started = Stopwatch.GetTimestamp();
            var planned = await _planner.PlanAsync(device, session, settings, cancellationToken)
                .ConfigureAwait(false);
            if (!planned.Succeeded)
            {
                Phase = HelperDeploymentPhase.Failed;
                var fail = Result(Phase, planned.Code ?? HelperCodes.ManifestUntrusted, null, null,
                    [Stage(HelperDeploymentPhase.Planning, planned.Code ?? HelperCodes.ManifestUntrusted, started)]);
                WriteDiagnostic("planning", DiagnosticOutcome.Failure, fail.Code, device, session);
                return fail;
            }

            Plan = planned.Plan;
            _payload = planned.Payload;
            Phase = HelperDeploymentPhase.AwaitingConsent;
            WriteDiagnostic("planning", DiagnosticOutcome.Success, null, device, session);
            return Result(
                Phase,
                HelperCodes.ConsentRequired,
                Plan,
                null,
                [Stage(HelperDeploymentPhase.Planning, HelperCodes.Ok, started)]);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<HelperDeploymentResult> ConfirmAsync(
        HelperConsentValues consent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consent);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        CancellationTokenSource? run = null;
        try
        {
            run = StartRun(cancellationToken);
            if (Phase != HelperDeploymentPhase.AwaitingConsent || Plan is null || _payload is null ||
                _settings is null)
            {
                return Fail(HelperCodes.ConsentRequired);
            }

            var plan = Plan;
            var payload = _payload;
            var settings = _settings;
            if (consent.Device != plan.Device ||
                consent.Version != plan.Version ||
                !string.Equals(consent.Sha256, plan.Sha256, StringComparison.Ordinal))
            {
                Phase = HelperDeploymentPhase.AwaitingConsent;
                return Result(Phase, HelperCodes.ConsentStale, plan, null, []);
            }

            var payloadHash = HelperRemoteScripts.PayloadSha256(payload);
            if (payloadHash != plan.Sha256 || payload.Length != plan.Length)
            {
                Phase = HelperDeploymentPhase.Failed;
                return Fail(HelperCodes.HashMismatch);
            }

            _consent = consent;
            var stagingId = HelperRemoteScripts.NewStagingId();
            _stagingId = stagingId;
            Phase = HelperDeploymentPhase.Uploading;
            var uploadStarted = Stopwatch.GetTimestamp();
            HelperPublishResult published;
            try
            {
                published = await _publisher.PublishAsync(
                        plan, settings, payload, stagingId, run.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                published = new(HelperCodes.Cancelled, false);
            }

            if (!published.Succeeded)
            {
                var code = published.Code ?? HelperCodes.UploadFailed;
                if (code == HelperCodes.Cancelled)
                {
                    await CleanupStagingAsync().ConfigureAwait(false);
                    Phase = HelperDeploymentPhase.Cancelled;
                    return Result(Phase, HelperCodes.Cancelled, plan, null,
                        [Stage(HelperDeploymentPhase.Uploading, HelperCodes.Cancelled, uploadStarted)]);
                }

                Phase = HelperDeploymentPhase.Failed;
                var fail = Result(Phase, code, plan, null,
                    [Stage(MapPublishPhase(code), code, uploadStarted)]);
                WriteDiagnostic("publish", DiagnosticOutcome.Failure, code, plan.Device, plan.Session);
                return fail;
            }

            Phase = HelperDeploymentPhase.Activating;
            var activateStarted = Stopwatch.GetTimestamp();
            var key = new HelperReceiptKey(
                plan.Device,
                plan.Session.EndpointKey,
                plan.Session.SessionName,
                plan.Target.Triple,
                plan.RemoteHomeSha256);
            (DeploymentReceipt? Receipt, string? Code) activated;
            try
            {
                activated = await _receipts.ActivateAsync(key, plan.Version, plan.Sha256, run.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Phase = HelperDeploymentPhase.Failed;
                return Result(
                    Phase,
                    HelperCodes.Cancelled,
                    plan,
                    null,
                    [
                        Stage(HelperDeploymentPhase.Uploading, HelperCodes.Ok, uploadStarted),
                        Stage(HelperDeploymentPhase.Activating, HelperCodes.Cancelled, activateStarted)
                    ]);
            }

            if (activated.Code is not null)
            {
                Phase = HelperDeploymentPhase.Failed;
                var fail = Result(Phase, activated.Code, plan, null,
                [
                    Stage(HelperDeploymentPhase.Uploading, HelperCodes.Ok, uploadStarted),
                    Stage(HelperDeploymentPhase.Activating, activated.Code, activateStarted)
                ]);
                WriteDiagnostic("activating", DiagnosticOutcome.Failure, activated.Code, plan.Device, plan.Session);
                return fail;
            }

            Phase = HelperDeploymentPhase.Succeeded;
            _stagingId = null;
            WriteDiagnostic("activating", DiagnosticOutcome.Success, null, plan.Device, plan.Session);
            return Result(
                Phase,
                null,
                plan,
                activated.Receipt,
                [
                    Stage(HelperDeploymentPhase.Uploading, HelperCodes.Ok, uploadStarted),
                    Stage(HelperDeploymentPhase.Activating, HelperCodes.Ok, activateStarted)
                ]);
        }
        finally
        {
            if (run is not null)
                CompleteRun(run);
            _gate.Release();
        }
    }

    public async ValueTask<HelperDeploymentResult> CancelAsync()
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

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Phase is HelperDeploymentPhase.Succeeded or HelperDeploymentPhase.RolledBack)
                return Result(Phase, null, Plan, null, []);
            if (Phase == HelperDeploymentPhase.Idle)
                return Result(HelperDeploymentPhase.Idle, HelperCodes.Cancelled, null, null, []);
            await CleanupStagingAsync().ConfigureAwait(false);
            _consent = null;
            Phase = HelperDeploymentPhase.Cancelled;
            WriteDiagnostic("cancel", DiagnosticOutcome.Failure, HelperCodes.Cancelled,
                Plan?.Device, Plan?.Session);
            return Result(Phase, HelperCodes.Cancelled, Plan, null, []);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<HelperDeploymentResult> RollbackAsync(
        HelperReceiptKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var rolled = await _receipts.RollbackAsync(key, cancellationToken).ConfigureAwait(false);
            if (rolled.Code is not null)
            {
                Phase = HelperDeploymentPhase.Failed;
                WriteDiagnostic("rollback", DiagnosticOutcome.Failure, rolled.Code, key.Device, null);
                return Result(Phase, rolled.Code, Plan, null, []);
            }

            Phase = HelperDeploymentPhase.RolledBack;
            WriteDiagnostic("rollback", DiagnosticOutcome.Success, null, key.Device, null);
            return Result(Phase, null, Plan, rolled.Receipt, []);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal HelperDeploymentReceiptStore Receipts => _receipts;

    private CancellationTokenSource StartRun(CancellationToken cancellationToken)
    {
        if (_run is { } previous)
        {
            try
            {
                previous.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            previous.Dispose();
        }

        var next = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _run = next;
        return next;
    }

    private void CompleteRun(CancellationTokenSource run)
    {
        if (ReferenceEquals(_run, run))
            _run = null;
        run.Dispose();
    }

    private async ValueTask CleanupStagingAsync()
    {
        if (Plan is null || _settings is null || _stagingId is null)
            return;
        try
        {
            await _publisher.CleanupAsync(Plan, _settings, _stagingId).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }

        _stagingId = null;
    }

    private HelperDeploymentResult Fail(string code)
    {
        Phase = code == HelperCodes.Cancelled ? HelperDeploymentPhase.Cancelled : HelperDeploymentPhase.Failed;
        if (code == HelperCodes.ConsentRequired)
            Phase = HelperDeploymentPhase.Failed;
        WriteDiagnostic("confirm", DiagnosticOutcome.Failure, code, Plan?.Device, Plan?.Session);
        return Result(Phase, code, Plan, null, []);
    }

    private static HelperDeploymentPhase MapPublishPhase(string code) => code switch
    {
        HelperCodes.HashMismatch => HelperDeploymentPhase.Verifying,
        HelperCodes.SelftestFailed => HelperDeploymentPhase.Verifying,
        HelperCodes.VersionCollision => HelperDeploymentPhase.Verifying,
        _ => HelperDeploymentPhase.Uploading
    };

    private static HelperStageResult Stage(HelperDeploymentPhase phase, string code, long started) =>
        new(phase, code, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);

    private static HelperDeploymentResult Result(
        HelperDeploymentPhase phase,
        string? code,
        DeploymentPlan? plan,
        DeploymentReceipt? receipt,
        IReadOnlyList<HelperStageResult> stages) =>
        new(phase, code, plan, receipt, stages);

    private void WriteDiagnostic(
        string operation,
        DiagnosticOutcome outcome,
        string? code,
        DeviceId? device,
        SessionKey? session)
    {
        if (_diagnostics is null)
            return;
        string? deviceAlias = null;
        string? sessionAlias = null;
        if (_aliases is not null && device is { } id)
            deviceAlias = _aliases.Alias("device", id.Value.ToString("D"));
        if (_aliases is not null && session is { } key)
            sessionAlias = _aliases.Alias("session", key.EndpointKey);
        _diagnostics.TryWrite(new DiagnosticEvent(
            DateTimeOffset.UtcNow,
            "helper",
            operation,
            outcome,
            code,
            null,
            null,
            null,
            deviceAlias,
            sessionAlias));
    }
}
