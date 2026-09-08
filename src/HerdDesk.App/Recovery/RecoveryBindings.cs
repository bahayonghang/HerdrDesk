using HerdDesk.Contracts;
using HerdDesk.Core;

namespace HerdDesk.App;

public sealed class RecoveryBindings
{
    private readonly IDeviceSession? _session;
    private readonly IControlLeaseCoordinator? _lease;
    private readonly AppExitCoordinator? _exit;
    private readonly List<string> _trace = [];
    private ConnectionPhase _lastPhase = ConnectionPhase.Offline;
    private PaneKey? _selected;
    private string? _lastKnownLabel;
    private bool _appStopping;
    private bool _awaitingObserveRecovery;

    public RecoveryBindings(
        IDeviceSession? session = null,
        IControlLeaseCoordinator? lease = null,
        AppExitCoordinator? exit = null)
    {
        _session = session;
        _lease = lease;
        _exit = exit;
        Current = RecoveryViewState.Offline;
    }

    public RecoveryViewState Current { get; private set; }
    public IReadOnlyList<string> Trace => _trace;
    public int DaemonStartCalls { get; } = 0;
    public int RecoverControlCalls { get; } = 0;

    public void Select(PaneKey pane, string? lastKnownLabel = null)
    {
        _selected = pane;
        if (!string.IsNullOrWhiteSpace(lastKnownLabel))
            _lastKnownLabel = lastKnownLabel;
    }

    public void Apply(DeviceSessionState session, ControlLeaseState? lease = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (lease?.Target is { } target && !string.IsNullOrWhiteSpace(target.PaneId))
            _lastKnownLabel ??= target.PaneId;
        if (session.Phase == ConnectionPhase.Stale && _lastPhase != ConnectionPhase.Stale)
            NoteProjectionStale();
        else if (session.Phase == ConnectionPhase.Connecting && _lastPhase == ConnectionPhase.Ready)
            NoteProjectionStale();

        if (session.Phase == ConnectionPhase.Connecting && _lastPhase != ConnectionPhase.Connecting)
            _trace.Add("rpc-connecting");
        if (session.Phase == ConnectionPhase.Synchronizing && _lastPhase != ConnectionPhase.Synchronizing)
            _trace.Add("subscribe-ack");
        if (session.Phase == ConnectionPhase.Ready && _lastPhase != ConnectionPhase.Ready)
        {
            _trace.Add("authoritative-convergence");
            if (_awaitingObserveRecovery)
            {
                _awaitingObserveRecovery = false;
                _lease?.NoteRecoverySignalAsync(LeaseRecoverySignal.ProjectionReady).AsTask()
                    .GetAwaiter().GetResult();
                _trace.Add("target-revalidate");
                if (PaneMissing(session, _selected))
                    _trace.Add("pane-closed");
                else
                    _trace.Add("new-observe");
            }
        }

        Current = RecoveryProjection.Project(session, _lease?.Current ?? lease, _lastKnownLabel);
        _lastPhase = session.Phase;
    }

    public void NotifyAppStopping()
    {
        if (_appStopping)
            return;
        _appStopping = true;
        _session?.NotifyAppStoppingAsync().AsTask().GetAwaiter().GetResult();
        _lease?.NoteRecoverySignalAsync(LeaseRecoverySignal.AppStopping).AsTask().GetAwaiter().GetResult();
        _exit?.ExitAsync().AsTask().GetAwaiter().GetResult();
    }

    public ValueTask RetryNowAsync(CancellationToken cancellationToken = default)
    {
        if (_appStopping || _session is null)
            return ValueTask.CompletedTask;
        return _session.RetryNowAsync(cancellationToken);
    }

    private void NoteProjectionStale()
    {
        _awaitingObserveRecovery = true;
        if (_trace.Count == 0 || _trace[^1] != "stale")
            _trace.Add("stale");
        _lease?.NoteRecoverySignalAsync(LeaseRecoverySignal.ProjectionStale).AsTask()
            .GetAwaiter().GetResult();
    }

    private static bool PaneMissing(DeviceSessionState session, PaneKey? selected)
    {
        if (selected is null)
            return false;
        foreach (var device in session.Projection.Devices)
        {
            foreach (var item in device.Sessions)
            {
                if (item.Panes.Any(pane => pane.Key == selected))
                    return false;
            }
        }

        return true;
    }
}
