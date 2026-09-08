using System.Collections.Frozen;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class LocalMvpCompositionTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("default observe does not grant control", DefaultObserve),
        ("request control never authorizes takeover", RequestControlNoTakeover),
        ("disconnect does not replay input", NoReplayAfterDisconnect),
        ("namesake panes stay isolated", NamesakePanesIsolated),
        ("claude codex opencode kinds only", AgentKindsOnly)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static TerminalLeaseResult Verified() =>
        TerminalLeaseProbe.Map(new TerminalLeaseObservation(
            TerminalLeaseOperation.RequestControl,
            TerminalAccess.Observing,
            false,
            AdapterProvedWriteOwnership: true));

    static RendererInput UserInput(PaneKey pane, ConnectionEpoch epoch, byte[] bytes) =>
        new(pane, epoch, InputOrigin.UserKey, bytes);

    static InputContext ContextFrom(ControlLeaseState state, PaneKey pane, ConnectionEpoch epoch) =>
        new(pane, epoch, state.Access, state.ControlVerified);

    static void DefaultObserve()
    {
        using var env = new LocalMvpEnv();
        env.ConnectReady();
        Check(env.Session.Actor.Current.Phase == ConnectionPhase.Ready);
        Check(env.Lease.Current.Access == TerminalAccess.Observing);
        Check(!env.Lease.Current.ControlVerified);
        Check(env.LeaseRenderer.ReadOnly);
        Check(env.LeaseHost.TakeoverCount == 0);
        var epoch = env.Lease.Current.ObserveBinding!.Epoch;
        var decision = InputPolicy.Evaluate(
            ContextFrom(env.Lease.Current, env.Pane, epoch),
            UserInput(env.Pane, epoch, [(byte)'x']));
        Check(!decision.Allowed);
        Check(decision.Code == "control_not_verified");
        Check(env.LeaseHost.LastObserve!.Inputs.Count == 0);
        var resize = env.Lease.SubmitResizeAsync(
            new TerminalResizeCommand(env.Pane, epoch, 80, 24, 8, 16)).AsTask().GetAwaiter().GetResult();
        Check(resize.Code == ControlLeaseCodes.ObserveNoResize);
        var scroll = env.Lease.SubmitScrollAsync(
            new TerminalScrollCommand(env.Pane, epoch, "up", 1, "wheel")).AsTask().GetAwaiter().GetResult();
        Check(scroll.Code == ControlLeaseCodes.ObserveScrollDenied);
        Check(env.Attention.UnreadForPane(env.Pane) == 0);
        Check(env.CommandTransport.Intents.Count == 0);
    }

    static void RequestControlNoTakeover()
    {
        using var env = new LocalMvpEnv();
        env.ConnectReady();
        env.Lease.RequestControlAsync().AsTask().GetAwaiter().GetResult();
        ControlLeaseWait.Until(env.Lease, state => state.CandidateBinding is not null);
        Check(env.LeaseHost.NoTakeoverCount == 1);
        Check(env.LeaseHost.TakeoverCount == 0);
        Check(env.Lease.Current.Access == TerminalAccess.Acquiring);
        Check(!env.Lease.Current.ControlVerified);
        Check(env.LeaseRenderer.ReadOnly);
        var epoch = env.Lease.Current.CandidateBinding!.Epoch;
        var decision = InputPolicy.Evaluate(
            ContextFrom(env.Lease.Current, env.Pane, epoch),
            UserInput(env.Pane, epoch, [(byte)'x']));
        Check(!decision.Allowed);
        Check(decision.Code == "control_not_verified");
    }

    static void NoReplayAfterDisconnect()
    {
        using var env = new LocalMvpEnv();
        env.ConnectReady();
        env.LeaseHost.LastObserve!.EmitFrame(1, true);
        env.Lease.RequestControlAsync().AsTask().GetAwaiter().GetResult();
        ControlLeaseWait.Until(env.Lease, state => state.CandidateBinding is not null);
        env.LeaseHost.LastCandidate!.EmitFrame(1, true);
        env.LeaseHost.LastCandidate.EmitOwnership(Verified());
        var ready = ControlLeaseWait.Until(env.Lease,
            state => state.Access == TerminalAccess.Controlling && state.ControlVerified);
        var epoch = ready.ControlBinding!.Epoch;
        var allowed = InputPolicy.Evaluate(
            ContextFrom(env.Lease.Current, env.Pane, epoch),
            UserInput(env.Pane, epoch, [(byte)'a']));
        Check(allowed.Allowed);
        var written = env.Lease.SubmitInputAsync(UserInput(env.Pane, epoch, [(byte)'a']))
            .AsTask().GetAwaiter().GetResult();
        Check(written.Disposition is TerminalWriteDisposition.WrittenUnacknowledged
            or TerminalWriteDisposition.UnknownAfterDisconnect);
        var captured = env.LeaseHost.LastCandidate.Inputs.Count;
        env.Session.Factory.LastRequest!.FailEof();
        var stale = env.Session.WaitFor(state => state.Phase == ConnectionPhase.Stale);
        Check(stale.Recovery.Cause == RecoveryCodes.RequestEof);
        Check(!stale.Capabilities.HasMutationControl);
        var classified = RecoveryPolicy.ClassifyRpc(
            env.Session.Session, stale.Epoch, stale.LastErrorCode, RpcFailureKind.ConnectionLost,
            true, false, false, false);
        Check(classified.Cause == RecoveryCause.RequestEof);
        env.SyncProjection();
        env.Lease.NoteRecoverySignalAsync(LeaseRecoverySignal.ProjectionStale).AsTask().GetAwaiter().GetResult();
        ControlLeaseWait.Until(env.Lease, state => state.Access != TerminalAccess.Controlling && !state.ControlVerified);
        Check(env.LeaseRenderer.ReadOnly);
        var replay = env.Lease.SubmitInputAsync(UserInput(env.Pane, epoch, [(byte)'b']))
            .AsTask().GetAwaiter().GetResult();
        Check(replay.Disposition == TerminalWriteDisposition.NotSent);
        Check(env.LeaseHost.LastCandidate.Inputs.Count == captured);
        var policy = InputPolicy.Evaluate(
            ContextFrom(env.Lease.Current, env.Pane, epoch),
            UserInput(env.Pane, epoch, [(byte)'b']));
        Check(!policy.Allowed);
        env.Commands.NotifyProjectionAsync().AsTask().GetAwaiter().GetResult();
        env.Commands.OpenCreateAsync(
            ResourceOperationKind.CreateWorkspace, env.Workspace(), "dev / w1")
            .AsTask().GetAwaiter().GetResult();
        Check(env.CommandTransport.Intents.Count == 0);
        Check(env.Commands.Current.State is ResourceOperationState.Failed or ResourceOperationState.Idle
            or ResourceOperationState.StaleTarget);
        env.Session.NotifyAppStopping();
        Check(env.Session.Actor.Current.Recovery.Cause == RecoveryCodes.AppStopping
            || env.Session.Actor.Current.Phase != ConnectionPhase.Ready);
        env.Lease.NoteRecoverySignalAsync(LeaseRecoverySignal.AppStopping).AsTask().GetAwaiter().GetResult();
        var afterStop = env.Lease.SubmitInputAsync(UserInput(env.Pane, epoch, [(byte)'c']))
            .AsTask().GetAwaiter().GetResult();
        Check(afterStop.Disposition == TerminalWriteDisposition.NotSent);
        Check(env.LeaseHost.LastCandidate.Inputs.Count == captured);
    }

    static void NamesakePanesIsolated()
    {
        using var left = new LocalMvpEnv();
        var otherSession = new SessionKey(AttentionHarness.DeviceB, "local-api", "dev");
        using var right = new LocalMvpEnv(otherSession);
        left.ConnectReady();
        right.ConnectReady();
        Check(left.Pane.PaneId == right.Pane.PaneId);
        Check(left.Pane != right.Pane);
        var leftEpoch = left.Lease.Current.ObserveBinding!.Epoch;
        var crossed = InputPolicy.Evaluate(
            ContextFrom(left.Lease.Current, left.Pane, leftEpoch),
            UserInput(right.Pane, leftEpoch, [(byte)'x']));
        Check(!crossed.Allowed);
        Check(crossed.Code == "wrong_pane");
        var js = left.Lease.SubmitInputAsync(
            UserInput(right.Pane, leftEpoch, [(byte)'x'])).AsTask().GetAwaiter().GetResult();
        Check(js.Disposition == TerminalWriteDisposition.NotSent);
        Check(left.LeaseHost.LastObserve!.Inputs.Count == 0);
        Check(right.LeaseHost.LastObserve!.Inputs.Count == 0);
        left.Session.Factory.LastRequest!.FailEof();
        left.Session.WaitFor(state => state.Phase == ConnectionPhase.Stale);
        Check(right.Session.Actor.Current.Phase == ConnectionPhase.Ready);
        var reducer = new AttentionReducer();
        var idle = AttentionHarness.Status(AgentStatusKind.Idle);
        var blocked = AttentionHarness.Status(AgentStatusKind.Blocked);
        reducer.Apply(
            AttentionHarness.Many(1,
                AttentionHarness.Entity(AttentionHarness.DeviceA, left.Session.Session, left.Pane, idle, "term-a"),
                AttentionHarness.Entity(AttentionHarness.DeviceB, right.Session.Session, right.Pane, idle, "term-b")),
            AttentionHarness.Stamp(1),
            AttentionSyncKind.Baseline);
        reducer.Apply(
            AttentionHarness.Many(1,
                AttentionHarness.Entity(AttentionHarness.DeviceA, left.Session.Session, left.Pane, blocked, "term-a"),
                AttentionHarness.Entity(AttentionHarness.DeviceB, right.Session.Session, right.Pane, idle, "term-b")),
            AttentionHarness.Stamp(1),
            AttentionSyncKind.Live);
        Check(reducer.UnreadForPane(left.Pane) == 1);
        Check(reducer.UnreadForPane(right.Pane) == 0);
        Check(reducer.UnreadForDevice(AttentionHarness.DeviceB) == 0);
    }

    static void AgentKindsOnly()
    {
        using var env = new LocalMvpEnv();
        env.ConnectReady();
        Check(!Enum.GetNames<KnownAgentKind>().Contains("Muse"));
        Check(VerifiedAgentWires.IsVerified(KnownAgentKind.Claude));
        Check(VerifiedAgentWires.IsVerified(KnownAgentKind.Codex));
        Check(VerifiedAgentWires.IsVerified(KnownAgentKind.OpenCode));
        Check(!VerifiedAgentWires.IsVerified((KnownAgentKind)42));
        env.Commands.OpenCreateAsync(
            ResourceOperationKind.CreateAgent, env.Agent(), "dev / w1 / p1")
            .AsTask().GetAwaiter().GetResult();
        env.Commands.SubmitCreateAsync("muse", null, (KnownAgentKind)42).AsTask().GetAwaiter().GetResult();
        Check(env.Commands.Current.Code == ResourceCommandCodes.AgentKindUnverified);
        Check(env.CommandTransport.Intents.Count == 0);
        using var agent = new LocalMvpEnv();
        agent.ConnectReady();
        agent.Commands.OpenCreateAsync(
            ResourceOperationKind.CreateAgent, agent.Agent(), "dev / w1 / p1")
            .AsTask().GetAwaiter().GetResult();
        agent.Commands.SubmitCreateAsync("worker", "/tmp/agent", KnownAgentKind.Codex)
            .AsTask().GetAwaiter().GetResult();
        agent.WaitCommand(op => op.State is ResourceOperationState.Observing or ResourceOperationState.Succeeded
            or ResourceOperationState.UnknownOutcome);
        Check(agent.CommandTransport.Intents.Count == 1);
        Check(((CreateAgentIntent)agent.CommandTransport.Intents[0]).AgentKind == KnownAgentKind.Codex);
    }
}

internal sealed class LocalMvpEnv : IDisposable
{
    public LocalMvpEnv(SessionKey? session = null)
    {
        Session = new DeviceSessionHarness(session);
        Pane = new PaneKey(Session.Session, "w1", "p1");
        LeaseStore = new FakeLeaseStore
        {
            Snapshot = new LeaseTargetSnapshot(
                Pane, true, new ConnectionEpoch(1), 1, DeviceFreshness.Current,
                new CapabilityProfile(
                    "0.9.0", "0.9.0", 22, 1, "sha", "sha",
                    FrozenSet.ToFrozenSet(["pane.send_input"], StringComparer.Ordinal)),
                "dev / w1 / p1", "p1")
        };
        LeaseHost = new FakeLeaseHost();
        LeaseRenderer = new FakeLeaseRenderer();
        Lease = new ControlLeaseCoordinator(LeaseStore, LeaseHost, LeaseRenderer);
        CommandStore = new FakeResourceStore();
        CommandTransport = new FakeResourceTransport();
        Commands = new ResourceCommandCoordinator(
            CommandStore, CommandTransport, CommandTransport, null, Session.Time,
            new ResourceCommandOptions
            {
                RpcTimeout = TimeSpan.FromMilliseconds(50),
                ObserveTimeout = TimeSpan.FromMilliseconds(50)
            });
        Attention = new AttentionReducer();
    }

    public DeviceSessionHarness Session { get; }
    public PaneKey Pane { get; }
    public FakeLeaseStore LeaseStore { get; }
    public FakeLeaseHost LeaseHost { get; }
    public FakeLeaseRenderer LeaseRenderer { get; }
    public ControlLeaseCoordinator Lease { get; }
    public FakeResourceStore CommandStore { get; }
    public FakeResourceTransport CommandTransport { get; }
    public ResourceCommandCoordinator Commands { get; }
    public AttentionReducer Attention { get; }

    public ResourceKey Workspace() =>
        new(Session.Session, ResourceKind.Workspace, "w1", "t1", "p1", "term-1");

    public ResourceKey Agent() =>
        new(Session.Session, ResourceKind.Agent, "w1", "t1", "p1", "term-1");

    public void ConnectReady()
    {
        Session.Connect();
        Session.WaitFor(state => state.Phase == ConnectionPhase.Ready);
        SyncProjection();
        Attention.BeginBaseline(Session.Session, Session.Actor.Current.Epoch);
        Attention.Apply(
            Session.Actor.Current.Projection,
            new ProjectionStamp(Session.Actor.Current.Epoch, 0, DateTimeOffset.UnixEpoch),
            AttentionSyncKind.Baseline);
        Lease.OpenPaneAsync(Pane).AsTask().GetAwaiter().GetResult();
        ControlLeaseWait.Until(Lease, state =>
            state.ObserveBinding is not null && state.Access == TerminalAccess.Observing);
    }

    public void SyncProjection()
    {
        var state = Session.Actor.Current;
        CommandStore.Snapshot = new ResourceStoreSnapshot(state.Projection, state.Freshness);
        LeaseStore.Snapshot = LeaseStore.Snapshot with
        {
            ProjectionEpoch = state.Epoch,
            ProjectionRevision = state.Projection.Revision,
            Freshness = state.Freshness,
            Exists = true
        };
    }

    public void WaitCommand(Func<ResourceOperation, bool> pred)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        WaitCommandAsync(pred, cts.Token).GetAwaiter().GetResult();
    }

    async Task WaitCommandAsync(Func<ResourceOperation, bool> pred, CancellationToken cancellationToken)
    {
        if (pred(Commands.Current))
            return;
        await foreach (var _ in Commands.ReadStatesAsync(cancellationToken).ConfigureAwait(false))
        {
            if (pred(Commands.Current))
                return;
        }

        throw new Exception("wait_timeout state=" + Commands.Current.State + " code=" + Commands.Current.Code);
    }

    public void Dispose()
    {
        Commands.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Session.DisposeActor();
    }
}
