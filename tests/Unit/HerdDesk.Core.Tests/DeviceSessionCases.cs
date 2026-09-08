using System.Collections.Frozen;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class DeviceSessionCases
{
    public static (string Name, Action Run)[] All =>
    [
        ("reconcile planner maps create close to full snapshot and status to getter", PlannerScopes),
        ("device session waits for subscribe ack before snapshot and ready", AckBeforeSnapshot),
        ("create close and status around snapshot converge to authority", InterleavedEventsConverge),
        ("old epoch snapshot event timer and error do not rewrite store", EpochRolloverIgnoresOld),
        ("unknown event and getter failure upgrade to full snapshot", UnknownAndGetterFailure),
        ("verified getter reads only the target entity", VerifiedGetterIsTargeted),
        ("event burst coalesces without dropping invalidations or leaking timers", BurstCoalesces),
        ("request eof and subscription eof are distinct stale codes", DistinctEofCodes),
        ("baseline install does not call the notification sink", BaselineDoesNotNotify),
        ("schema incompatible snapshot is not ready", IncompatibleSnapshot),
        ("ui state drop oldest does not block actor processing", UiMergeDoesNotBlock),
        ("manual disconnect is offline and readonly", ManualDisconnectOffline),
        ("l2 live subscribe interleave remains unverified", L2Unverified)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static CapabilityProfile Compatible() =>
        CapabilityGate.Evaluate(SchemaCompatibilityBinding.PinnedMatchingRuntimeForTests("0.9.0"), 22, "0.9.0");

    static CapabilityProfile NoGetters() =>
        new("0.9.0", "0.9.0", 22, 1, SchemaCompatibilityBinding.PinnedDocumentSha256,
            SchemaCompatibilityBinding.PinnedDocumentSha256,
            FrozenSet.ToFrozenSet(["session.snapshot", "events.subscribe"], StringComparer.Ordinal));

    static void PlannerScopes()
    {
        var session = DeviceSessionGraphs.DefaultSession();
        var create = ReconcilePlanner.Plan(DeviceSessionGraphs.CreateWorkspace(session, 1), Compatible(), false);
        Check(create.WholeSession);
        var close = ReconcilePlanner.Plan(DeviceSessionGraphs.ClosePane(session, 1), Compatible(), false);
        Check(close.WholeSession);
        var status = ReconcilePlanner.Plan(DeviceSessionGraphs.StatusPane(session, 1), Compatible(), false);
        Check(!status.WholeSession);
        Check(status.PaneId == "p1");
        var unknown = ReconcilePlanner.Plan(DeviceSessionGraphs.Unknown(session, 1), Compatible(), false);
        Check(unknown.WholeSession);
        var noGetter = ReconcilePlanner.Plan(DeviceSessionGraphs.StatusPane(session, 1), NoGetters(), false);
        Check(noGetter.WholeSession);
        var degraded = ReconcilePlanner.Plan(DeviceSessionGraphs.StatusPane(session, 1), Compatible(), true);
        Check(degraded.WholeSession);
    }

    static void AckBeforeSnapshot()
    {
        var env = new DeviceSessionHarness();
        try
        {
            env.Factory.AutoAck = false;
            env.Connect();
            Check(env.Actor.Current.Phase == ConnectionPhase.Connecting);
            Check(!env.Factory.Methods.Contains("session.snapshot"));
            env.Emit(DeviceSessionGraphs.StatusPane(env.Session, 1));
            Check(env.Actor.Current.AcceptedInvalidations == 0);
            env.Factory.LastSubscription!.Acknowledge();
            var ready = env.WaitFor(state => state.Phase == ConnectionPhase.Ready);
            Check(ready.BaselineInstalled);
            Check(env.Factory.Methods.Contains("session.snapshot"));
            var ackIndex = env.Factory.Opens.IndexOf("subscription");
            Check(ackIndex >= 0);
            Check(env.Factory.Methods[0] == "session.snapshot");
        }
        finally
        {
            env.DisposeActor();
        }
    }

    static void InterleavedEventsConverge()
    {
        var env = new DeviceSessionHarness();
        try
        {
            env.Factory.AutoAck = false;
            env.Connect();
            env.Factory.LastRequest!.HoldSnapshot();
            env.Factory.LastSubscription!.Acknowledge();
            DeviceSessionWait.Gate(env.Factory.LastRequest.SnapshotRequested);
            Check(env.Actor.Current.Phase == ConnectionPhase.Synchronizing);
            Check(!env.Factory.Methods.Contains("session.snapshot") || env.Actor.Current.Phase != ConnectionPhase.Ready);
            env.Emit(DeviceSessionGraphs.CreateWorkspace(env.Session, 1));
            env.Emit(DeviceSessionGraphs.ClosePane(env.Session, 1));
            env.Emit(DeviceSessionGraphs.StatusPane(env.Session, 1, "p2"));
            env.Decoder.Snapshot = DeviceSessionGraphs.AfterCreateCloseStatus(env.Session, 1);
            env.Factory.LastRequest.ReleaseSnapshot();
            env.WaitFor(state => state.AcceptedInvalidations >= 3);
            env.Time.Advance(env.Options.CoalesceWindow);
            var ready = env.WaitFor(state =>
                state.Phase == ConnectionPhase.Ready && state.DirtyScopeCount == 0 &&
                state.InFlightReadEffects == 0);
            DeviceSessionGraphs.CheckEquivalent(ready, env.Decoder.Snapshot);
            Check(env.Notifications.Calls == 0);
        }
        finally
        {
            env.DisposeActor();
        }
    }

    static void EpochRolloverIgnoresOld()
    {
        var env = new DeviceSessionHarness();
        try
        {
            env.Factory.AutoAck = false;
            env.Connect();
            env.Factory.LastRequest!.HoldSnapshot();
            env.Factory.LastSubscription!.Acknowledge();
            DeviceSessionWait.Gate(env.Factory.LastRequest.SnapshotRequested);
            var firstRequest = env.Factory.LastRequest;
            var firstSub = env.Factory.LastSubscription;
            env.Factory.AutoAck = true;
            env.Decoder.Snapshot = DeviceSessionGraphs.Baseline(env.Session, 2, "new-a");
            env.Connect();
            var epoch2 = env.WaitFor(state => state.Epoch.Value == 2);
            Check(epoch2.Epoch.Value == 2);
            firstSub.Emit(JsonEvent());
            env.Emit(DeviceSessionGraphs.StatusPane(env.Session, 2, "new-a"));
            firstRequest.ReleaseSnapshot();
            firstRequest.FailEof();
            var ready = env.WaitFor(state =>
                state.Epoch.Value == 2 && state.Phase == ConnectionPhase.Ready);
            Check(ready.Epoch.Value == 2);
            Check(ready.Projection.Epoch.Value == 2);
            Check(ready.Projection.Devices[0].Sessions[0].Panes[0].Key.PaneId == "new-a");
            var revision = ready.Projection.Revision;
            env.Time.Advance(env.Options.CoalesceWindow);
            env.Time.Advance(env.Options.CalibrationPeriod);
            Check(env.Actor.Current.Epoch.Value == 2);
            Check(env.Actor.Current.Projection.Devices[0].Sessions[0].Panes[0].Key.PaneId == "new-a");
            Check(env.Actor.Current.Projection.Revision >= revision);
        }
        finally
        {
            env.DisposeActor();
        }
    }

    static void UnknownAndGetterFailure()
    {
        var env = new DeviceSessionHarness();
        try
        {
            env.Connect();
            env.WaitFor(state => state.Phase == ConnectionPhase.Ready);
            env.Factory.Methods.Clear();
            var accepted = env.Actor.Current.AcceptedInvalidations;
            env.Emit(DeviceSessionGraphs.Unknown(env.Session, 1));
            env.WaitFor(state => state.AcceptedInvalidations == accepted + 1);
            env.Time.Advance(env.Options.CoalesceWindow);
            env.WaitFor(state =>
                state.Phase == ConnectionPhase.Ready && state.DirtyScopeCount == 0 &&
                env.Factory.Methods.Contains("session.snapshot"));
            env.Factory.Methods.Clear();
            env.Decoder.EntityFailCode = ProjectionCodes.FullSnapshotRequired;
            accepted = env.Actor.Current.AcceptedInvalidations;
            env.Emit(DeviceSessionGraphs.StatusPane(env.Session, 1));
            env.WaitFor(state => state.AcceptedInvalidations == accepted + 1);
            env.Time.Advance(env.Options.CoalesceWindow);
            env.WaitFor(state =>
                state.Phase == ConnectionPhase.Ready && state.DirtyScopeCount == 0);
            Check(env.Factory.Methods.Contains("session.snapshot"));
        }
        finally
        {
            env.DisposeActor();
        }
    }

    static void VerifiedGetterIsTargeted()
    {
        var env = new DeviceSessionHarness();
        try
        {
            env.Connect();
            env.WaitFor(state => state.Phase == ConnectionPhase.Ready);
            env.Factory.Methods.Clear();
            env.Decoder.Snapshot = DeviceSessionGraphs.Baseline(env.Session, 1) with
            {
                Panes = [DeviceSessionGraphs.Pane(label: "busy")]
            };
            var accepted = env.Actor.Current.AcceptedInvalidations;
            env.Emit(DeviceSessionGraphs.StatusPane(env.Session, 1));
            env.WaitFor(state => state.AcceptedInvalidations == accepted + 1);
            env.Time.Advance(env.Options.CoalesceWindow);
            env.WaitFor(state =>
                state.Phase == ConnectionPhase.Ready && state.DirtyScopeCount == 0 &&
                state.InFlightReadEffects == 0);
            Check(env.Factory.Methods.Contains("pane.get"));
            Check(!env.Factory.Methods.Contains("session.snapshot"));
            Check(env.Actor.Current.Projection.Devices[0].Sessions[0].Panes[0].Label == "busy");
        }
        finally
        {
            env.DisposeActor();
        }
    }

    static void BurstCoalesces()
    {
        var env = new DeviceSessionHarness();
        try
        {
            env.Connect();
            env.WaitFor(state => state.Phase == ConnectionPhase.Ready);
            for (var i = 0; i < 10_000; i++)
                env.Emit(DeviceSessionGraphs.StatusPane(env.Session, 1));
            env.WaitFor(state => state.AcceptedInvalidations == 10_000);
            Check(env.Actor.Current.ActiveTimerCount <= 2);
            Check(env.Actor.Current.InFlightReadEffects <= 1);
            env.Time.Advance(env.Options.CoalesceWindow);
            var quiet = env.WaitFor(state =>
                state.DirtyScopeCount == 0 && state.Phase == ConnectionPhase.Ready &&
                state.InFlightReadEffects == 0);
            Check(quiet.AcceptedInvalidations == 10_000);
            Check(quiet.ActiveTimerCount <= 2);
            env.DisposeActor();
            Check(env.Actor.Current.PendingEffectTasks == 0);
            Check(env.Actor.Current.InFlightReadEffects == 0);
            Check(env.Actor.Current.ActiveTimerCount == 0);
        }
        finally
        {
            if (env.Actor.Current.Phase != ConnectionPhase.Offline)
                env.DisposeActor();
        }
    }

    static void DistinctEofCodes()
    {
        var requestEof = new DeviceSessionHarness();
        try
        {
            requestEof.Connect();
            requestEof.WaitFor(state => state.Phase == ConnectionPhase.Ready);
            requestEof.Factory.LastRequest!.FailEof();
            var stale = requestEof.WaitFor(state => state.Phase == ConnectionPhase.Stale);
            Check(stale.LastErrorCode == RpcCodes.RequestLost);
            Check(!stale.Capabilities.HasMutationControl);
            Check(stale.Freshness == DeviceFreshness.Stale);
        }
        finally
        {
            requestEof.DisposeActor();
        }

        var subEof = new DeviceSessionHarness();
        try
        {
            subEof.Connect();
            subEof.WaitFor(state => state.Phase == ConnectionPhase.Ready);
            subEof.Factory.LastSubscription!.CompleteEof();
            var stale = subEof.WaitFor(state => state.Phase == ConnectionPhase.Stale);
            Check(stale.LastErrorCode == RpcCodes.SubscriptionLost);
            Check(stale.LastErrorCode != RpcCodes.RequestLost);
            Check(!stale.Capabilities.HasMutationControl);
        }
        finally
        {
            subEof.DisposeActor();
        }
    }

    static void BaselineDoesNotNotify()
    {
        var env = new DeviceSessionHarness();
        try
        {
            env.Connect();
            env.WaitFor(state => state.Phase == ConnectionPhase.Ready && state.BaselineInstalled);
            Check(env.Notifications.Calls == 0);
        }
        finally
        {
            env.DisposeActor();
        }
    }

    static void IncompatibleSnapshot()
    {
        var env = new DeviceSessionHarness();
        try
        {
            env.Decoder.Snapshot = DeviceSessionGraphs.Baseline(env.Session, 1) with { Protocol = 23 };
            env.Connect();
            var state = env.WaitFor(item =>
                item.Phase is ConnectionPhase.Incompatible or ConnectionPhase.Stale);
            Check(state.Phase == ConnectionPhase.Incompatible);
            Check(!state.Capabilities.HasMutationControl);
            Check(state.Capabilities.VerifiedOperations.Count == 0);
            Check(state.Phase != ConnectionPhase.Ready);
        }
        finally
        {
            env.DisposeActor();
        }
    }

    static void UiMergeDoesNotBlock()
    {
        var env = new DeviceSessionHarness();
        try
        {
            env.Connect();
            env.WaitFor(state => state.Phase == ConnectionPhase.Ready);
            for (var i = 0; i < 32; i++)
                env.Emit(DeviceSessionGraphs.StatusPane(env.Session, 1));
            env.WaitFor(state => state.AcceptedInvalidations == 32);
            env.Time.Advance(env.Options.CoalesceWindow);
            env.WaitFor(state => state.DirtyScopeCount == 0 && state.Phase == ConnectionPhase.Ready);
        }
        finally
        {
            env.DisposeActor();
        }
    }

    static void ManualDisconnectOffline()
    {
        var env = new DeviceSessionHarness();
        try
        {
            env.Connect();
            env.WaitFor(state => state.Phase == ConnectionPhase.Ready);
            env.Disconnect();
            var offline = env.WaitFor(state => state.Phase == ConnectionPhase.Offline);
            Check(offline.Phase == ConnectionPhase.Offline);
            Check(!offline.Capabilities.HasMutationControl);
        }
        finally
        {
            env.DisposeActor();
        }
    }

    static void L2Unverified()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HerdDesk.slnx")))
            dir = dir.Parent;
        Check(dir is not null);
        var json = File.ReadAllText(
            Path.Combine(dir!.FullName, "implementation", "hd-010-l2.json"),
            System.Text.Encoding.UTF8);
        Check(json.Contains("UNVERIFIED", StringComparison.Ordinal));
        Check(json.Contains("\"ac12_passed\": false", StringComparison.Ordinal));
        Check(json.Contains("\"phase_gate\": \"not_passed\"", StringComparison.Ordinal));
        Check(json.Contains("l2_live_subscribe_interleave", StringComparison.Ordinal));
    }

    static System.Text.Json.JsonElement JsonEvent()
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            """{"event":"pane.updated","data":{"pane_id":"p1"}}""");
        return document.RootElement.Clone();
    }
}
