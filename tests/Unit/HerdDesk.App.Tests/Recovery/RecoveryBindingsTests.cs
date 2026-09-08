using System.Collections.Frozen;
using System.Threading.Channels;
using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class RecoveryBindingsTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("stale routes before observe and ready revalidates the same pane", RouteStaleThenReady),
        ("first ready does not reopen observe", FirstReadyDoesNotReobserve),
        ("commands stay disabled until fresh and labels stay grey", ViewStateGates),
        ("app stop kill ledger excludes daemon agent and pane", KillLedger)
    ];

    static PaneKey Pane() =>
        new(new SessionKey(new DeviceId(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")), "endpoint", "dev"),
            "ws", "p1");

    static DeviceSessionState SessionState(
        ConnectionPhase phase,
        DeviceFreshness freshness = DeviceFreshness.Stale,
        SessionRecoveryProgress? recovery = null)
    {
        var pane = Pane();
        var snapshot = DeviceProjectionSnapshot.Empty with
        {
            Epoch = new ConnectionEpoch(2),
            Phase = phase,
            Devices =
            [
                new ProjectedDevice(
                    pane.Session.Device,
                    new CapabilityProfile("0.9.0", "0.9.0", 22, 1, "sha", "sha", FrozenSet<string>.Empty),
                    [
                        new SessionProjection(
                            pane.Session, "0.9.0", 22, "ws", "t1", pane.PaneId, [], [],
                            [
                                new PaneProjection(
                                    pane, "term-1", "t1", true, "shell", null, null, null,
                                    new WireEnum<AgentStatusKind>("idle", AgentStatusKind.Idle), 1)
                            ],
                            [], [])
                    ])
            ]
        };
        return new DeviceSessionState(
            pane.Session,
            new ConnectionEpoch(2),
            phase,
            freshness,
            new CapabilityProfile("0.9.0", "0.9.0", 22, 1, "sha", "sha", FrozenSet<string>.Empty),
            snapshot,
            0,
            0,
            phase == ConnectionPhase.Stale ? RpcCodes.RequestLost : null,
            phase == ConnectionPhase.Ready,
            0,
            phase == ConnectionPhase.Stale ? 1 : 0,
            0,
            0,
            recovery ?? (phase == ConnectionPhase.Stale
                ? new SessionRecoveryProgress(1, DateTimeOffset.UnixEpoch.AddSeconds(1), RecoveryCodes.RequestEof,
                    RecoveryCodes.RetryAfter, 1, 1)
                : SessionRecoveryProgress.None));
    }

    static void RouteStaleThenReady()
    {
        var pane = Pane();
        var store = new RecoveryLeaseStore(pane);
        var host = new RecoveryLeaseHost();
        var renderer = new RecoveryLeaseRenderer();
        var lease = new ControlLeaseCoordinator(store, host, renderer);
        var bindings = new RecoveryBindings(lease: lease);
        try
        {
            lease.OpenPaneAsync(pane).AsTask().GetAwaiter().GetResult();
            Wait(lease, state => state.ObserveBinding is not null);
            var firstEpoch = lease.Current.ObserveBinding!.Epoch;
            bindings.Select(pane, "shell");
            bindings.Apply(SessionState(ConnectionPhase.Stale), lease.Current);
            Wait(lease, state => state.Access != TerminalAccess.Controlling && state.ObserveBinding is null);
            AppTestHost.Check(bindings.Trace[0] == "stale");
            AppTestHost.Check(renderer.ReadOnly);
            AppTestHost.Check(bindings.DaemonStartCalls == 0);
            AppTestHost.Check(bindings.RecoverControlCalls == 0);
            bindings.Apply(SessionState(ConnectionPhase.Connecting));
            bindings.Apply(SessionState(ConnectionPhase.Synchronizing));
            bindings.Apply(SessionState(ConnectionPhase.Ready, DeviceFreshness.Current));
            Wait(lease, state => state.ObserveBinding is not null && state.ObserveBinding.Epoch != firstEpoch);
            AppTestHost.Check(bindings.Trace.Contains("subscribe-ack"));
            AppTestHost.Check(bindings.Trace.Contains("authoritative-convergence"));
            AppTestHost.Check(bindings.Trace.Contains("target-revalidate"));
            AppTestHost.Check(bindings.Trace.Contains("new-observe"));
            AppTestHost.Check(!lease.Current.ControlVerified);
            AppTestHost.Check(host.TakeoverCount == 0);
            AppTestHost.Check(bindings.DaemonStartCalls == 0);
        }
        finally
        {
            lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void FirstReadyDoesNotReobserve()
    {
        var pane = Pane();
        var store = new RecoveryLeaseStore(pane);
        var host = new RecoveryLeaseHost();
        var renderer = new RecoveryLeaseRenderer();
        var lease = new ControlLeaseCoordinator(store, host, renderer);
        var bindings = new RecoveryBindings(lease: lease);
        try
        {
            lease.OpenPaneAsync(pane).AsTask().GetAwaiter().GetResult();
            Wait(lease, state => state.ObserveBinding is not null);
            var firstEpoch = lease.Current.ObserveBinding!.Epoch;
            var opens = host.Observes.Count;
            bindings.Select(pane, "shell");
            bindings.Apply(SessionState(ConnectionPhase.Ready, DeviceFreshness.Current), lease.Current);
            AppTestHost.Check(lease.Current.ObserveBinding!.Epoch == firstEpoch);
            AppTestHost.Check(host.Observes.Count == opens);
            AppTestHost.Check(!bindings.Trace.Contains("new-observe"));
            AppTestHost.Check(bindings.DaemonStartCalls == 0);
            AppTestHost.Check(bindings.RecoverControlCalls == 0);
        }
        finally
        {
            lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void ViewStateGates()
    {
        var bindings = new RecoveryBindings();
        bindings.Select(Pane(), "shell");
        bindings.Apply(SessionState(ConnectionPhase.Stale));
        AppTestHost.Check(bindings.Current.Kind == RecoveryViewKind.Stale);
        AppTestHost.Check(bindings.Current.Retrying);
        AppTestHost.Check(!bindings.Current.CommandsEnabled);
        AppTestHost.Check(bindings.Current.DataMayBeStale);
        AppTestHost.Check(bindings.Current.LastKnownLabel == "shell");
        bindings.Apply(SessionState(
            ConnectionPhase.Stale,
            recovery: new SessionRecoveryProgress(7, null, RecoveryCodes.ProtocolError, RecoveryCodes.AwaitUser, 0, 1)));
        AppTestHost.Check(bindings.Current.Kind == RecoveryViewKind.AwaitingUser);
        AppTestHost.Check(!bindings.Current.CommandsEnabled);
        bindings.Apply(SessionState(ConnectionPhase.Ready, DeviceFreshness.Current));
        AppTestHost.Check(bindings.Current.CommandsEnabled);
        AppTestHost.Check(bindings.Current.Kind is RecoveryViewKind.Fresh or RecoveryViewKind.Recovered
            or RecoveryViewKind.ReobservingTerminal);
        AppTestHost.Check(typeof(RecoveryBindings).GetMethod("StartDaemon") is null);
        AppTestHost.Check(typeof(RecoveryBindings).GetMethod("RecoverControl") is null);
    }

    static void KillLedger()
    {
        var exit = new AppExitCoordinator();
        var bridge = new FakeOwnedChild();
        var cli = new FakeOwnedChild();
        var late = new FakeOwnedChild();
        var daemon = new FakeDaemon();
        exit.RegisterOwned(11, bridge, OwnedChildKind.RpcBridge);
        exit.RegisterOwned(12, cli, OwnedChildKind.TerminalCli);
        AppTestHost.Check(!exit.TryRegisterForeign(99, ForeignProcessKind.Daemon));
        AppTestHost.Check(!exit.TryRegisterForeign(98, ForeignProcessKind.Agent));
        AppTestHost.Check(!exit.TryRegisterForeign(97, ForeignProcessKind.Pane));
        var bindings = new RecoveryBindings(exit: exit);
        bindings.NotifyAppStopping();
        AppTestHost.Check(bridge.Disposed);
        AppTestHost.Check(cli.Disposed);
        AppTestHost.Check(exit.KillLedger.Contains(11));
        AppTestHost.Check(exit.KillLedger.Contains(12));
        AppTestHost.Check(!exit.KillLedger.Contains(99));
        AppTestHost.Check(!exit.KillLedger.Contains(98));
        AppTestHost.Check(!exit.KillLedger.Contains(97));
        AppTestHost.Check(exit.RejectedForeignIds.Contains(99));
        AppTestHost.Check(!daemon.Stopped);
        AppTestHost.Check(!exit.StoppedDaemon);
        AppTestHost.Check(!exit.StoppedAgent);
        AppTestHost.Check(!exit.ClosedRemotePane);
        exit.NoteLateStartAsync(13, late, OwnedChildKind.TerminalCli).AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(late.Disposed);
        AppTestHost.Check(exit.KillLedger.Contains(13));
        AppTestHost.Check(!exit.AllowNewConnections);
    }

    static void Wait(ControlLeaseCoordinator coordinator, Func<ControlLeaseState, bool> pred)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        WaitAsync(coordinator, pred, cts.Token).GetAwaiter().GetResult();
    }

    static async Task WaitAsync(
        ControlLeaseCoordinator coordinator,
        Func<ControlLeaseState, bool> pred,
        CancellationToken cancellationToken)
    {
        if (pred(coordinator.Current))
            return;
        await foreach (var _ in coordinator.ReadStatesAsync(cancellationToken).ConfigureAwait(false))
        {
            if (pred(coordinator.Current))
                return;
        }

        throw new Exception("wait_timeout access=" + coordinator.Current.Access);
    }
}

file sealed class RecoveryLeaseStore : ILeaseTargetStore
{
    public RecoveryLeaseStore(PaneKey pane) =>
        Snapshot = new LeaseTargetSnapshot(
            pane, true, new ConnectionEpoch(1), 1, DeviceFreshness.Current,
            new CapabilityProfile("0.9.0", "0.9.0", 22, 1, "sha", "sha",
                FrozenSet.ToFrozenSet(["pane.send_input"], StringComparer.Ordinal)),
            "dev / ws / p1", "p1");

    public LeaseTargetSnapshot Snapshot { get; set; }

    public LeaseTargetSnapshot Read(PaneKey pane) => Snapshot.Pane == pane
        ? Snapshot
        : Snapshot with { Pane = pane, Exists = false };
}

file sealed class RecoveryLeaseHost : IControlBindingHost
{
    public List<ITerminalTransport> Observes { get; } = [];
    public int TakeoverCount { get; private set; }

    public ValueTask<ITerminalTransport> OpenObserveAsync(
        PaneKey pane,
        ConnectionEpoch epoch,
        CancellationToken cancellationToken = default)
    {
        var transport = new RecoveryLeaseTransport(pane, epoch, TerminalMode.Observe, false);
        Observes.Add(transport);
        return ValueTask.FromResult<ITerminalTransport>(transport);
    }

    public ValueTask<ITerminalTransport> OpenControlCandidateAsync(
        PaneKey pane,
        ConnectionEpoch epoch,
        string attemptId,
        TerminalTakeoverAuthorization? takeover,
        CancellationToken cancellationToken = default)
    {
        if (takeover?.Confirmed == true)
            TakeoverCount++;
        return ValueTask.FromResult<ITerminalTransport>(
            new RecoveryLeaseTransport(pane, epoch, TerminalMode.Control, takeover?.Confirmed == true));
    }
}

file sealed class RecoveryLeaseTransport : ITerminalTransport
{
    private readonly Channel<TerminalTransportEvent> _events = Channel.CreateUnbounded<TerminalTransportEvent>();

    public RecoveryLeaseTransport(PaneKey pane, ConnectionEpoch epoch, TerminalMode mode, bool takeover)
    {
        Pane = pane;
        Epoch = epoch;
        Mode = mode;
        _ = takeover;
    }

    public PaneKey Pane { get; }
    public ConnectionEpoch Epoch { get; }
    public TerminalMode Mode { get; }

    public IAsyncEnumerable<TerminalTransportEvent> ReadEventsAsync(
        CancellationToken cancellationToken = default) =>
        _events.Reader.ReadAllAsync(cancellationToken);

    public ValueTask<TerminalWriteReceipt> SendInputAsync(
        TerminalInputCommand input,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new TerminalWriteReceipt(0, TerminalWriteDisposition.NotSent, "input_not_sent"));

    public ValueTask<TerminalWriteReceipt> ResizeAsync(
        TerminalResizeCommand size,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new TerminalWriteReceipt(0, TerminalWriteDisposition.NotSent, "observe_no_resize"));

    public ValueTask<TerminalWriteReceipt> ScrollAsync(
        TerminalScrollCommand request,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new TerminalWriteReceipt(0, TerminalWriteDisposition.NotSent, "observe_scroll_denied"));

    public ValueTask<TerminalWriteReceipt> ReleaseAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new TerminalWriteReceipt(0, TerminalWriteDisposition.NotSent, "released"));

    public ValueTask DisposeAsync()
    {
        _events.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}

file sealed class RecoveryLeaseRenderer : ITerminalRenderer
{
    public bool ReadOnly { get; private set; } = true;
    public PaneKey? Pane { get; private set; }
    public ConnectionEpoch? Epoch { get; private set; }
    public RendererSurfaceState SurfaceState { get; private set; } = RendererSurfaceState.Uninitialized;

    public ValueTask BindAsync(PaneKey pane, ConnectionEpoch epoch, CancellationToken cancellationToken = default)
    {
        Pane = pane;
        Epoch = epoch;
        SurfaceState = RendererSurfaceState.Bound;
        return ValueTask.CompletedTask;
    }

    public ValueTask<RenderApplyResult> ApplyAsync(TerminalFrame frame, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new RenderApplyResult(
            true, new RenderConsumption(new ConnectionEpoch(1), frame.Sequence), RendererQueueState.Ready,
            RendererSurfaceState.Observing, "parse_consumed"));

    public async IAsyncEnumerable<RendererInput> ReadInputsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        yield break;
    }

    public ValueTask SetReadOnlyAsync(bool readOnly, CancellationToken cancellationToken = default)
    {
        ReadOnly = readOnly;
        return ValueTask.CompletedTask;
    }

    public ValueTask FocusAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
