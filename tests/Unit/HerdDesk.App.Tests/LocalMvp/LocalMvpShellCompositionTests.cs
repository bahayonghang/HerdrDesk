using System.Collections.Frozen;
using System.Text;
using System.Threading.Channels;
using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Configuration;
using HerdDesk.Terminal.Web;

internal static class LocalMvpShellCompositionTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("default observe stays readonly", DefaultObserve),
        ("request control does not authorize takeover", RequestControlNoTakeover),
        ("disconnect does not replay input", NoReplayAfterDisconnect),
        ("gui close kills only owned pids", GuiCloseOwnedOnly),
        ("namesake panes stay isolated", NamesakeIsolated),
        ("claude codex opencode profiles only", AgentProfilesOnly)
    ];

    static void DefaultObserve()
    {
        using var env = new LocalMvpShellEnv();
        env.Start();
        AppTestHost.Check(env.Shell.Lifecycle == ShellLifecycle.Ready);
        AppTestHost.Check(env.Control.AccessLabel == ShellStrings.Observing);
        AppTestHost.Check(!env.Lease.Current.ControlVerified);
        AppTestHost.Check(env.Host.TakeoverCount == 0);
        var ctx = env.Input.Context;
        var decision = InputPolicy.Evaluate(
            ctx, new RendererInput(ctx.ActivePane, ctx.Epoch, InputOrigin.UserKey, new byte[] { (byte)'x' }));
        AppTestHost.Check(!decision.Allowed);
        AppTestHost.Check(decision.Code == "control_not_verified");
        var request = env.InputVm.RequestControl();
        AppTestHost.Check(!request.Allowed);
        AppTestHost.Check(request.Code == HostInputCodes.LeaseNotGranted);
        AppTestHost.Check(env.Input.TransportByteCount == 0);
        AppTestHost.Check(!env.Input.TryRequestResize(80, 24));
        AppTestHost.Check(env.Input.UpstreamResizeCount == 0);
        AppTestHost.Check(env.Attention.UnreadForPane(env.PaneA) == 0);
        AppTestHost.Check(env.Bindings.DaemonStartCalls == 0);
        AppTestHost.Check(env.Bindings.RecoverControlCalls == 0);
    }

    static void RequestControlNoTakeover()
    {
        using var env = new LocalMvpShellEnv();
        env.Start();
        env.Control.RequestControlFromScreenReader();
        env.WaitLease(state => state.Access == TerminalAccess.Acquiring && state.CandidateBinding is not null);
        AppTestHost.Check(env.Host.NoTakeoverCount == 1);
        AppTestHost.Check(env.Host.TakeoverCount == 0);
        AppTestHost.Check(!env.Control.State.ControlVerified);
        AppTestHost.Check(env.Control.AlwaysTakeoverEnabled is false);
        AppTestHost.Check(env.Input.ControlVerified is false);
    }

    static void NoReplayAfterDisconnect()
    {
        using var env = new LocalMvpShellEnv();
        env.Start();
        var epoch = env.Lease.Current.ObserveBinding!.Epoch;
        env.Bindings.Select(env.PaneA, "main");
        env.Bindings.Apply(env.StaleSession(), env.Lease.Current);
        env.WaitLease(state => state.ObserveBinding is null);
        AppTestHost.Check(env.Bindings.Trace.Contains("stale"));
        var replay = env.Lease.SubmitInputAsync(
            new RendererInput(env.PaneA, epoch, InputOrigin.UserKey, new byte[] { (byte)'z' }))
            .AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(replay.Disposition == TerminalWriteDisposition.NotSent);
        env.Input.Suspend("stale_epoch");
        var key = env.Input.HandleKey(new PhysicalKeyEvent("a"));
        AppTestHost.Check(!key.Allowed);
        AppTestHost.Check(env.Input.TransportByteCount == 0);
        AppTestHost.Check(env.Bindings.RecoverControlCalls == 0);
        AppTestHost.Check(env.Bindings.DaemonStartCalls == 0);
    }

    static void GuiCloseOwnedOnly()
    {
        using var env = new LocalMvpShellEnv();
        env.Start();
        var bridge = new FakeOwnedChild();
        var cli = new FakeOwnedChild();
        var daemon = new FakeDaemon();
        env.Exit.RegisterOwned(11, bridge, OwnedChildKind.RpcBridge);
        env.Exit.RegisterOwned(12, cli, OwnedChildKind.TerminalCli);
        AppTestHost.Check(!env.Exit.TryRegisterForeign(99, ForeignProcessKind.Daemon));
        AppTestHost.Check(!env.Exit.TryRegisterForeign(98, ForeignProcessKind.Agent));
        AppTestHost.Check(!env.Exit.TryRegisterForeign(97, ForeignProcessKind.Pane));
        env.Bindings.NotifyAppStopping();
        env.Shell.ExitAsync().AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(bridge.Disposed);
        AppTestHost.Check(cli.Disposed);
        AppTestHost.Check(env.Exit.KillLedger.Contains(11));
        AppTestHost.Check(env.Exit.KillLedger.Contains(12));
        AppTestHost.Check(!env.Exit.KillLedger.Contains(99));
        AppTestHost.Check(!env.Exit.KillLedger.Contains(98));
        AppTestHost.Check(!env.Exit.KillLedger.Contains(97));
        AppTestHost.Check(!daemon.Stopped);
        AppTestHost.Check(!env.Exit.StoppedDaemon);
        AppTestHost.Check(!env.Exit.StoppedAgent);
        AppTestHost.Check(!env.Exit.ClosedRemotePane);
        AppTestHost.Check(env.Bindings.DaemonStartCalls == 0);
    }

    static void NamesakeIsolated()
    {
        using var env = new LocalMvpShellEnv();
        env.Start();
        env.Shell.ExpandAll();
        var panes = env.Shell.VisibleItems.Where(item => item.Kind == NavigationKind.Pane).ToArray();
        AppTestHost.Check(panes.Length == 2);
        AppTestHost.Check(panes[0].Label == panes[1].Label);
        AppTestHost.Check(panes[0].Pane != panes[1].Pane);
        env.Shell.Select(panes[0]);
        AppTestHost.Check(env.Shell.Selection.Pane == env.PaneA);
        env.Input.SwitchPane(env.PaneB, env.Snapshot.Epoch);
        var crossed = env.Input.HandleKey(new PhysicalKeyEvent("a"));
        AppTestHost.Check(!crossed.Allowed);
        AppTestHost.Check(env.Input.TransportByteCount == 0);
        var remaining = env.Snapshot.Devices.First(item => item.Device == AppTestHost.DeviceB);
        env.Catalog.Snapshot = AppTestHost.Snapshot(env.Snapshot.Epoch, ConnectionPhase.Ready, remaining);
        env.Shell.RefreshFromCatalog();
        AppTestHost.Check(env.Shell.Selection.IsExpired);
        AppTestHost.Check(env.Shell.Selection.Pane == env.PaneA);
        var visible = env.Shell.VisibleItems.Where(item => item.Kind == NavigationKind.Pane).ToArray();
        AppTestHost.Check(visible.Length == 1);
        AppTestHost.Check(visible[0].Pane == env.PaneB);
        AppTestHost.Check(!visible[0].IsSelected);
    }

    static void AgentProfilesOnly()
    {
        using var env = new LocalMvpShellEnv();
        env.Start();
        AppTestHost.Check(!AgentInputProfiles.Resolve("muse").LiveVerified);
        AppTestHost.Check(AgentInputProfiles.Resolve("muse").IsUnknown);
        AppTestHost.Check(!AgentInputProfiles.Resolve("claude-code").IsUnknown);
        AppTestHost.Check(!AgentInputProfiles.Resolve("codex").LiveVerified);
        AppTestHost.Check(!AgentInputProfiles.Resolve("opencode").LiveVerified);
        AppTestHost.Check(!Enum.GetNames<KnownAgentKind>().Contains("Muse"));
        AppTestHost.Check(env.Resources.AgentKinds.Length == 3);
        AppTestHost.Check(env.Resources.AgentKinds.Contains(KnownAgentKind.Claude));
        AppTestHost.Check(env.Resources.AgentKinds.Contains(KnownAgentKind.Codex));
        AppTestHost.Check(env.Resources.AgentKinds.Contains(KnownAgentKind.OpenCode));
        var claude = new TerminalInputController(
            new InputContext(env.PaneA, env.Snapshot.Epoch, TerminalAccess.Controlling, true),
            CompositionPolicy.Evaluate, InputPolicy.Evaluate, AgentInputProfiles.ClaudeCode);
        claude.Bind(env.PaneA, env.Snapshot.Epoch);
        claude.SetReadOnly(false);
        claude.SetRendererReady(true);
        var preedit = claude.StartComposition();
        AppTestHost.Check(!preedit.Allowed);
        AppTestHost.Check(preedit.Code == HostInputCodes.PreeditNotSent);
        AppTestHost.Check(claude.TransportByteCount == 0);
        var token = claude.ActiveCommitToken!;
        var commit = claude.Commit(token, "词");
        AppTestHost.Check(commit.Allowed);
        AppTestHost.Check(commit.TransportBytes.SequenceEqual(Encoding.UTF8.GetBytes("词")));
        AppTestHost.Check(claude.Commit(token, "词").Code == HostInputCodes.CommitAlreadyAccepted);
    }
}

file sealed class LocalMvpShellEnv : IDisposable
{
    public LocalMvpShellEnv()
    {
        Snapshot = AppTestHost.TwoNamedPanes();
        PaneA = Snapshot.Devices[0].Sessions[0].Panes[0].Key;
        PaneB = Snapshot.Devices[1].Sessions[0].Panes[0].Key;
        Catalog = new ProjectionCatalog
        {
            Snapshot = Snapshot,
            DaemonAvailable = true,
            RendererReadyDefault = true,
            Freshness = DeviceFreshness.Current
        };
        Exit = new AppExitCoordinator();
        Attention = new AttentionReducer();
        Input = new TerminalInputController(
            new InputContext(PaneA, Snapshot.Epoch, TerminalAccess.Observing, false),
            CompositionPolicy.Evaluate, InputPolicy.Evaluate, AgentInputProfiles.ClaudeCode);
        Input.Bind(PaneA, Snapshot.Epoch);
        Input.SetReadOnly(true);
        Input.SetRendererReady(true);
        InputVm = new TerminalInputViewModel(Input);
        Store = new MvpLeaseStore(PaneA);
        Host = new MvpLeaseHost();
        Renderer = new MvpLeaseRenderer();
        Lease = new ControlLeaseCoordinator(Store, Host, Renderer);
        Control = new TerminalControlViewModel(Lease);
        ResourceStore = new MvpResourceStore();
        ResourceTransport = new MvpResourceTransport();
        var session = PaneA.Session;
        var workspace = AppTestHost.Workspace(session, "ws", "lab", 1);
        var pane = AppTestHost.Pane(PaneA);
        ResourceStore.Snapshot = new ResourceStoreSnapshot(
            AppTestHost.Snapshot(
                Snapshot.Epoch, ConnectionPhase.Ready,
                AppTestHost.Projected(session.Device, AppTestHost.Compatible(),
                    AppTestHost.SessionState(session, [workspace], [pane]))),
            DeviceFreshness.Current);
        Commands = new ResourceCommandCoordinator(ResourceStore, ResourceTransport, ResourceTransport);
        Resources = new ResourceCommandViewModel(Commands, Catalog);
        Bindings = new RecoveryBindings(lease: Lease, exit: Exit);
        Shell = new ShellViewModel(new ShellDependencies
        {
            Profiles = new MemoryDeviceProfileStore
            {
                Snapshot = new ConfigurationSnapshot(
                    1, 1,
                    [
                        new DeviceProfile(
                            AppTestHost.DeviceA, "lab", ConnectionKinds.Local, "/tmp/herdr",
                            [SessionProfile.Named("dev")]),
                        new DeviceProfile(
                            AppTestHost.DeviceB, "lab", ConnectionKinds.Local, "/tmp/herdr",
                            [SessionProfile.Named("dev")])
                    ])
            },
            Catalog = Catalog,
            Exit = Exit,
            Attention = Attention,
            TerminalInput = InputVm,
            TerminalControl = Control,
            ResourceCommands = Resources
        });
    }

    public DeviceProjectionSnapshot Snapshot { get; }
    public PaneKey PaneA { get; }
    public PaneKey PaneB { get; }
    public ProjectionCatalog Catalog { get; }
    public AppExitCoordinator Exit { get; }
    public AttentionReducer Attention { get; }
    public TerminalInputController Input { get; }
    public TerminalInputViewModel InputVm { get; }
    public MvpLeaseStore Store { get; }
    public MvpLeaseHost Host { get; }
    public MvpLeaseRenderer Renderer { get; }
    public ControlLeaseCoordinator Lease { get; }
    public TerminalControlViewModel Control { get; }
    public MvpResourceStore ResourceStore { get; }
    public MvpResourceTransport ResourceTransport { get; }
    public ResourceCommandCoordinator Commands { get; }
    public ResourceCommandViewModel Resources { get; }
    public RecoveryBindings Bindings { get; }
    public ShellViewModel Shell { get; }

    public void Start()
    {
        Shell.StartAsync().AsTask().GetAwaiter().GetResult();
        Lease.OpenPaneAsync(PaneA).AsTask().GetAwaiter().GetResult();
        WaitLease(state => state.ObserveBinding is not null && state.Access == TerminalAccess.Observing);
        Bindings.Select(PaneA, "main");
        Bindings.Apply(FreshSession(), Lease.Current);
    }

    public DeviceSessionState FreshSession() => SessionState(ConnectionPhase.Ready, DeviceFreshness.Current);

    public DeviceSessionState StaleSession() => SessionState(ConnectionPhase.Stale, DeviceFreshness.Stale);

    DeviceSessionState SessionState(ConnectionPhase phase, DeviceFreshness freshness) =>
        new(
            PaneA.Session,
            Snapshot.Epoch,
            phase,
            freshness,
            AppTestHost.Compatible(),
            Catalog.Snapshot,
            0, 0,
            phase == ConnectionPhase.Stale ? RpcCodes.RequestLost : null,
            phase == ConnectionPhase.Ready,
            0,
            phase == ConnectionPhase.Stale ? 1 : 0,
            0, 0,
            phase == ConnectionPhase.Stale
                ? new SessionRecoveryProgress(1, DateTimeOffset.UnixEpoch.AddSeconds(1), RecoveryCodes.RequestEof,
                    RecoveryCodes.RetryAfter, 1, 1)
                : SessionRecoveryProgress.None);

    public void WaitLease(Func<ControlLeaseState, bool> pred)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        WaitLeaseAsync(pred, cts.Token).GetAwaiter().GetResult();
    }

    async Task WaitLeaseAsync(Func<ControlLeaseState, bool> pred, CancellationToken cancellationToken)
    {
        if (pred(Lease.Current))
            return;
        await foreach (var _ in Lease.ReadStatesAsync(cancellationToken).ConfigureAwait(false))
        {
            if (pred(Lease.Current))
                return;
        }

        throw new Exception("wait_timeout access=" + Lease.Current.Access + " code=" + Lease.Current.LastCode);
    }

    public void Dispose()
    {
        Commands.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Exit.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

file sealed class MvpLeaseStore : ILeaseTargetStore
{
    public MvpLeaseStore(PaneKey pane) =>
        Snapshot = new LeaseTargetSnapshot(
            pane, true, new ConnectionEpoch(1), 1, DeviceFreshness.Current,
            new CapabilityProfile("0.9.0", "0.9.0", 22, 1, "sha", "sha",
                FrozenSet.ToFrozenSet(["pane.send_input"], StringComparer.Ordinal)),
            "dev / ws / p1", "p1");

    public LeaseTargetSnapshot Snapshot { get; set; }

    public LeaseTargetSnapshot Read(PaneKey pane) => Snapshot.Pane == pane
        ? Snapshot
        : Snapshot with { Exists = false, Pane = pane };
}

file sealed class MvpLeaseHost : IControlBindingHost
{
    public List<MvpLeaseTransport> Observes { get; } = [];
    public List<MvpLeaseTransport> Candidates { get; } = [];
    public int TakeoverCount => Candidates.Count(item => item.TakeoverAuthorized);
    public int NoTakeoverCount => Candidates.Count(item => !item.TakeoverAuthorized);

    public ValueTask<ITerminalTransport> OpenObserveAsync(
        PaneKey pane, ConnectionEpoch epoch, CancellationToken cancellationToken = default)
    {
        var transport = new MvpLeaseTransport(pane, epoch, TerminalMode.Observe, false);
        Observes.Add(transport);
        return ValueTask.FromResult<ITerminalTransport>(transport);
    }

    public ValueTask<ITerminalTransport> OpenControlCandidateAsync(
        PaneKey pane, ConnectionEpoch epoch, string attemptId, TerminalTakeoverAuthorization? takeover,
        CancellationToken cancellationToken = default)
    {
        _ = attemptId;
        var transport = new MvpLeaseTransport(pane, epoch, TerminalMode.Control, takeover?.Confirmed == true);
        Candidates.Add(transport);
        return ValueTask.FromResult<ITerminalTransport>(transport);
    }
}

file sealed class MvpLeaseRenderer : ITerminalRenderer
{
    public bool ReadOnly { get; private set; } = true;
    public PaneKey? Pane { get; private set; }
    public ConnectionEpoch? Epoch { get; private set; }
    public RendererSurfaceState SurfaceState => RendererSurfaceState.Bound;

    public ValueTask BindAsync(PaneKey pane, ConnectionEpoch epoch, CancellationToken cancellationToken = default)
    {
        Pane = pane;
        Epoch = epoch;
        return ValueTask.CompletedTask;
    }

    public ValueTask<RenderApplyResult> ApplyAsync(TerminalFrame frame, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new RenderApplyResult(
            true, new RenderConsumption(Epoch ?? new ConnectionEpoch(1), frame.Sequence),
            RendererQueueState.Ready, RendererSurfaceState.Observing, "parse_consumed"));

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

file sealed class MvpLeaseTransport : ITerminalTransport
{
    private readonly Channel<TerminalTransportEvent> _events = Channel.CreateUnbounded<TerminalTransportEvent>();

    public MvpLeaseTransport(PaneKey pane, ConnectionEpoch epoch, TerminalMode mode, bool takeover)
    {
        Pane = pane;
        Epoch = epoch;
        Mode = mode;
        TakeoverAuthorized = takeover;
    }

    public PaneKey Pane { get; }
    public ConnectionEpoch Epoch { get; }
    public TerminalMode Mode { get; }
    public bool TakeoverAuthorized { get; }
    public List<byte[]> Inputs { get; } = [];

    public async IAsyncEnumerable<TerminalTransportEvent> ReadEventsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return item;
    }

    public ValueTask<TerminalWriteReceipt> SendInputAsync(
        TerminalInputCommand input, CancellationToken cancellationToken = default)
    {
        if (Mode != TerminalMode.Control)
            return new(new TerminalWriteReceipt(1, TerminalWriteDisposition.NotSent, ControlLeaseCodes.InputNotSent));
        Inputs.Add((input.Bytes ?? []).ToArray());
        return new(new TerminalWriteReceipt(1, TerminalWriteDisposition.WrittenUnacknowledged,
            TerminalTransportCodes.WrittenUnacknowledged));
    }

    public ValueTask<TerminalWriteReceipt> ResizeAsync(
        TerminalResizeCommand size, CancellationToken cancellationToken = default) =>
        new(new TerminalWriteReceipt(1, TerminalWriteDisposition.NotSent, ControlLeaseCodes.ObserveNoResize));

    public ValueTask<TerminalWriteReceipt> ScrollAsync(
        TerminalScrollCommand request, CancellationToken cancellationToken = default) =>
        new(new TerminalWriteReceipt(1, TerminalWriteDisposition.NotSent, ControlLeaseCodes.ObserveScrollDenied));

    public ValueTask<TerminalWriteReceipt> ReleaseAsync(CancellationToken cancellationToken = default) =>
        new(new TerminalWriteReceipt(1, TerminalWriteDisposition.WrittenUnacknowledged,
            TerminalTransportCodes.WrittenUnacknowledged));

    public ValueTask DisposeAsync()
    {
        _events.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}

file sealed class MvpResourceStore : IResourceProjectionStore
{
    public ResourceStoreSnapshot Snapshot { get; set; } = new(DeviceProjectionSnapshot.Empty, DeviceFreshness.Unknown);

    public ResourceStoreSnapshot Read() => Snapshot;
}

file sealed class MvpResourceTransport : IResourceCommandTransport, IResourceQueryTransport
{
    public List<ResourceIntent> Intents { get; } = [];

    public ValueTask<ResourceTransportReceipt> SubmitAsync(
        ResourceIntent intent, CancellationToken cancellationToken = default)
    {
        Intents.Add(intent);
        return ValueTask.FromResult(new ResourceTransportReceipt(ResourceTransportKind.Result));
    }

    public ValueTask<ResourceQueryReceipt> QueryAsync(
        ResourceQueryRequest query, CancellationToken cancellationToken = default)
    {
        _ = (query, cancellationToken);
        return ValueTask.FromResult(new ResourceQueryReceipt(ResourceTransportKind.Result, false));
    }
}
