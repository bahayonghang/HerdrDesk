using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Ssh;

namespace HerdDesk.Infrastructure.SshTransports;

internal sealed class SshConnectionLease : IAsyncDisposable
{
    private readonly ConnectionAdmissionPolicy _policy;
    private readonly Dictionary<PaneKey, ConnectionLease> _terminals = [];
    private readonly object _gate = new();
    private RpcPairLease? _pair;
    private RemoteSessionTransportSet? _set;
    private int _disposed;

    private SshConnectionLease(
        ConnectionAdmissionPolicy policy,
        RemoteSessionLaunch launch,
        RpcPairLease? pair,
        RemoteSessionTransportSet? set,
        string code,
        ConnectionPhase phase)
    {
        _policy = policy;
        Launch = launch;
        _pair = pair;
        _set = set;
        Session = launch.Session;
        Epoch = launch.Epoch;
        Code = code;
        Phase = phase;
        Ready = set is { Ready: true } && pair is not null;
        Outcome = set?.Outcome;
    }

    public RemoteSessionLaunch Launch { get; }
    public SessionKey Session { get; }
    public ConnectionEpoch Epoch { get; }
    public bool Ready { get; private set; }
    public string Code { get; private set; }
    public ConnectionPhase Phase { get; private set; }
    public SshTransportOutcome? Outcome { get; private set; }
    public RemoteSessionTransportSet? Transport => _set;
    public IRpcRequestConnection? Request => _set?.Request;
    public IRpcSubscriptionConnection? Events => _set?.Events;

    public static async ValueTask<SshConnectionLease> OpenAsync(
        ConnectionAdmissionPolicy policy,
        RemoteSessionLaunch launch,
        OpenSshLocator locator,
        RemoteCompatibilityProbe probe,
        CancellationToken cancellationToken = default,
        ISshChildProcessStarter? starter = null)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(probe);
        var pair = policy.TryAcquireRpcPair(launch.Session, launch.Epoch);
        if (!pair.Admitted || pair.Pair is null)
        {
            return new SshConnectionLease(
                policy,
                launch,
                null,
                null,
                pair.Code,
                pair.Phase);
        }

        RemoteSessionTransportSet? set = null;
        try
        {
            set = await RemoteSessionTransportSet.OpenAsync(
                    launch, locator, probe, cancellationToken, starter)
                .ConfigureAwait(false);
            if (!set.Ready)
            {
                pair.Pair.Dispose();
                var code = MapStartFailure(set.Outcome?.Code);
                await set.DisposeAsync().ConfigureAwait(false);
                return new SshConnectionLease(
                    policy, launch, null, null, code, ConnectionPhase.Stale);
            }

            return new SshConnectionLease(
                policy, launch, pair.Pair, set, SshCodes.Ok, ConnectionPhase.Connecting);
        }
        catch (OperationCanceledException)
        {
            pair.Pair.Dispose();
            if (set is not null)
                await set.DisposeAsync().ConfigureAwait(false);
            return new SshConnectionLease(
                policy,
                launch,
                null,
                null,
                ResourceBudgetCodes.TransportCancelTimeout,
                ConnectionPhase.Stale);
        }
        catch
        {
            pair.Pair.Dispose();
            if (set is not null)
                await set.DisposeAsync().ConfigureAwait(false);
            return new SshConnectionLease(
                policy,
                launch,
                null,
                null,
                ResourceBudgetCodes.ProcessStartFailed,
                ConnectionPhase.Stale);
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
        code = ResourceBudgetCodes.ProcessStartFailed;
        if (!Ready || _set is null)
        {
            code = ResourceBudgetCodes.ConnectionBudgetExhausted;
            return false;
        }

        if (request.Pane.Session != Session || request.Epoch != Epoch)
        {
            code = ResourceBudgetCodes.StaleEpoch;
            return false;
        }

        ConnectionLease? acquired;
        lock (_gate)
        {
            if (_terminals.ContainsKey(request.Pane))
                acquired = null;
            else
            {
                var admitted = _policy.TryAcquireTerminal(request.Pane, request.Epoch);
                if (!admitted.Admitted || admitted.Lease is null)
                {
                    code = admitted.Code;
                    return false;
                }

                acquired = admitted.Lease;
            }
        }

        if (!_set.TryOpenTerminal(request, out transport, out spec, out code) || transport is null)
        {
            acquired?.Dispose();
            if (code == SshTransportCodes.ChildExited)
                code = ResourceBudgetCodes.ProcessStartFailed;
            return false;
        }

        if (acquired is not null)
        {
            lock (_gate)
                _terminals[request.Pane] = acquired;
        }

        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        ConnectionLease[] terminals;
        lock (_gate)
        {
            Ready = false;
            terminals = [.. _terminals.Values];
            _terminals.Clear();
        }

        foreach (var lease in terminals)
            lease.Dispose();

        var set = Interlocked.Exchange(ref _set, null);
        if (set is not null)
        {
            try
            {
                await set.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
            }

            Outcome = set.Outcome;
        }

        var pair = Interlocked.Exchange(ref _pair, null);
        pair?.Dispose();
        Phase = ConnectionPhase.Offline;
    }

    private static string MapStartFailure(string? transportCode)
    {
        if (string.IsNullOrEmpty(transportCode) || transportCode == SshTransportCodes.ChildExited)
            return ResourceBudgetCodes.ProcessStartFailed;
        return transportCode;
    }
}
