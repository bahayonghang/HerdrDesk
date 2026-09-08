using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Configuration;
using HerdDesk.Infrastructure.Ssh;

namespace HerdDesk.Infrastructure.SshTransports;

internal sealed record RemoteSessionLaunch(
    SessionKey Session,
    ConnectionEpoch Epoch,
    SshDeviceSettings Settings,
    string SocketPath,
    string KnownHostsFile,
    DeploymentReceipt Receipt,
    long HostKeyRevision,
    IDiagnosticSink? Diagnostics = null);

internal sealed class RemoteSessionTransportSet : IAsyncDisposable
{
    private static readonly JsonDocument EmptyObject = JsonDocument.Parse("{}");
    private readonly RemoteSessionLaunch _launch;
    private readonly RemoteRpcConnectionFactory _rpc;
    private readonly RemoteTerminalTransportFactory _terminals;
    private readonly ConcurrentDictionary<PaneKey, ITerminalTransport> _terminalChildren = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private IRpcRequestConnection? _request;
    private IRpcSubscriptionConnection? _events;
    private SshProcessSpec? _requestSpec;
    private SshProcessSpec? _eventSpec;
    private int _requestSlot;
    private int _eventSlot;
    private int _disposed;
    private bool _ready;

    private RemoteSessionTransportSet(
        RemoteSessionLaunch launch,
        RemoteRpcConnectionFactory rpc,
        RemoteTerminalTransportFactory terminals)
    {
        _launch = launch;
        _rpc = rpc;
        _terminals = terminals;
        Session = launch.Session;
        Epoch = launch.Epoch;
    }

    public SessionKey Session { get; }
    public ConnectionEpoch Epoch { get; }
    public bool Ready => _ready;
    public IRpcRequestConnection? Request => _request;
    public IRpcSubscriptionConnection? Events => _events;
    public SshProcessSpec? RequestSpec => _requestSpec;
    public SshProcessSpec? EventSpec => _eventSpec;
    public SshTransportOutcome? Outcome { get; private set; }
    public RemoteHostProbeResult? HostProbe { get; private set; }
    public SessionPingResult? Ping { get; private set; }
    public IReadOnlyDictionary<PaneKey, ITerminalTransport> Terminals => _terminalChildren;

    public static async ValueTask<RemoteSessionTransportSet> OpenAsync(
        RemoteSessionLaunch launch,
        OpenSshLocator locator,
        RemoteCompatibilityProbe probe,
        CancellationToken cancellationToken = default,
        ISshChildProcessStarter? starter = null)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(probe);
        if (launch.Epoch.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(launch));
        var childStarter = starter ?? OwnedSshChildProcessStarter.Instance;
        var rpc = new RemoteRpcConnectionFactory(locator, launch.Diagnostics, childStarter);
        var terminals = new RemoteTerminalTransportFactory(locator, launch.Diagnostics, starter: childStarter);
        var set = new RemoteSessionTransportSet(launch, rpc, terminals);
        try
        {
            await set.StartAsync(probe, cancellationToken).ConfigureAwait(false);
            return set;
        }
        catch (OperationCanceledException)
        {
            set.Fail(SshTransportCodes.TransportCancelled, SshChannelKind.RequestRpc);
            await set.TeardownAsync().ConfigureAwait(false);
            return set;
        }
        catch
        {
            await set.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public bool TryOpenTerminal(
        TerminalOpenRequest request,
        out ITerminalTransport? transport,
        out SshProcessSpec? spec,
        out string code)
    {
        transport = null;
        spec = null;
        if (!_ready)
        {
            code = SshTransportCodes.DaemonUnavailable;
            return false;
        }

        if (request.Pane.Session != Session || request.Epoch != Epoch)
        {
            code = TerminalTransportCodes.StaleEpoch;
            return false;
        }

        if (_terminalChildren.TryGetValue(request.Pane, out var existing))
        {
            transport = existing;
            code = SshCodes.Ok;
            return true;
        }

        if (!_terminals.TryOpen(_launch, request, out transport, out spec, out code) || transport is null)
            return false;
        if (!_terminalChildren.TryAdd(request.Pane, transport))
        {
            transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
            transport = _terminalChildren[request.Pane];
        }

        code = SshCodes.Ok;
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        await TeardownAsync().ConfigureAwait(false);
    }

    private async ValueTask StartAsync(RemoteCompatibilityProbe probe, CancellationToken cancellationToken)
    {
        if (!TryValidateLaunch(out var trustCode))
        {
            Fail(trustCode, SshChannelKind.CompatibilityProbe);
            return;
        }

        var host = await probe.ProbeHostAsync(_launch, cancellationToken).ConfigureAwait(false);
        HostProbe = host;
        if (!host.Succeeded)
        {
            Fail(host.Code, SshChannelKind.CompatibilityProbe);
            return;
        }

        if (!host.CliCompatible)
        {
            Fail(SshTransportCodes.SchemaIncompatible, SshChannelKind.CompatibilityProbe);
            return;
        }

        if (Interlocked.Exchange(ref _requestSlot, 1) != 0 || Interlocked.Exchange(ref _eventSlot, 1) != 0)
        {
            Fail(SshTransportCodes.ChildExited, SshChannelKind.RequestRpc);
            return;
        }

        if (!_rpc.TryOpenRequest(_launch, out _request, out _requestSpec, out var requestCode) || _request is null)
        {
            Fail(SshTransportMapper.FromRpc(requestCode), SshChannelKind.RequestRpc);
            await TeardownAsync().ConfigureAwait(false);
            return;
        }

        if (!_rpc.TryOpenSubscription(
                _launch, EmptyObject.RootElement.Clone(), out _events, out _eventSpec, out var eventCode) ||
            _events is null)
        {
            Fail(SshTransportMapper.FromRpc(eventCode), SshChannelKind.EventRpc);
            await TeardownAsync().ConfigureAwait(false);
            return;
        }

        var ping = await probe.PingAsync(_request, Session, cancellationToken).ConfigureAwait(false);
        Ping = ping;
        if (!ping.Succeeded)
        {
            Fail(ping.Code, SshChannelKind.RequestRpc);
            await TeardownAsync().ConfigureAwait(false);
            return;
        }

        await _events.WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (_events.Failure is not null)
        {
            Fail(SshTransportMapper.FromRpc(_events.Failure.Code), SshChannelKind.EventRpc);
            await TeardownAsync().ConfigureAwait(false);
            return;
        }

        _ready = true;
        Outcome = SshTransportMapper.Outcome(
            SshChannelKind.RequestRpc,
            SshTransportStage.Ready,
            Epoch,
            SshCodes.Ok,
            _clock.ElapsedMilliseconds,
            0,
            0,
            null,
            _request.ChildProcessId);
        WriteDiagnostic("ready", DiagnosticOutcome.Success, null);
    }

    private bool TryValidateLaunch(out string code)
    {
        code = SshTransportCodes.TrustRequired;
        if (_launch.Receipt.Key.Device != Session.Device)
            return false;
        if (!HelperRemoteScripts.IsSha256(_launch.Receipt.Sha256) ||
            !HelperRemoteScripts.IsSafeVersion(_launch.Receipt.Version))
            return false;
        if (_launch.Settings.RemoteHelperPath is null ||
            !AtomicConfigurationStore.IsSafePosixAbsolute(_launch.Settings.RemoteHelperPath) ||
            !AtomicConfigurationStore.IsSafePosixAbsolute(_launch.Settings.RemoteHerdrPath))
            return false;
        var sshError = AtomicConfigurationStore.ValidateSsh(_launch.Settings);
        if (sshError is not null)
        {
            code = sshError == ConfigurationCodes.ForbiddenField
                ? ConfigurationCodes.ForbiddenField
                : SshCodes.ProfileInvalid;
            return false;
        }

        code = SshCodes.Ok;
        return true;
    }

    private void Fail(string code, SshChannelKind channel)
    {
        _ready = false;
        Outcome = SshTransportMapper.Outcome(
            channel,
            SshTransportStage.Failed,
            Epoch,
            code,
            _clock.ElapsedMilliseconds,
            0,
            0,
            null,
            _request?.ChildProcessId);
        WriteDiagnostic("fail", DiagnosticOutcome.Failure, code);
    }

    private async ValueTask TeardownAsync()
    {
        _ready = false;
        var terminals = _terminalChildren.ToArray();
        _terminalChildren.Clear();
        foreach (var pair in terminals)
        {
            try
            {
                await pair.Value.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }

        var events = Interlocked.Exchange(ref _events, null);
        if (events is not null)
        {
            try
            {
                await events.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }

        var request = Interlocked.Exchange(ref _request, null);
        if (request is not null)
        {
            try
            {
                await request.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }

        if (Outcome is null || Outcome.Stage == SshTransportStage.Ready)
        {
            Outcome = SshTransportMapper.Outcome(
                SshChannelKind.RequestRpc,
                SshTransportStage.Teardown,
                Epoch,
                Outcome?.Code ?? SshTransportCodes.TransportCancelled,
                _clock.ElapsedMilliseconds,
                0,
                0,
                null,
                null);
        }

        WriteDiagnostic("teardown", DiagnosticOutcome.Failure, Outcome?.Code);
    }

    private void WriteDiagnostic(string operation, DiagnosticOutcome outcome, string? code)
    {
        _launch.Diagnostics?.TryWrite(new DiagnosticEvent(
            DateTimeOffset.UtcNow,
            "ssh-transport",
            operation,
            outcome,
            code,
            Epoch.Value,
            _clock.ElapsedMilliseconds,
            null,
            null,
            null));
    }
}
