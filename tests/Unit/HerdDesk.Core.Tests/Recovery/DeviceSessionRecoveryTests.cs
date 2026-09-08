using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class DeviceSessionRecoveryTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("request eof publishes stale before any retry timer", RequestEofStaleFirst),
        ("subscription eof is a distinct stale cause", SubscriptionEofDistinct),
        ("bridge exit daemon unreachable and protocol stay classified", ClassifiedRpcFaults),
        ("incompatible and manual disconnect schedule zero retries", NoRetryPaths),
        ("single timer backoff and coalesced manual retry", BackoffAndManualRetry),
        ("old epoch completions stay no-op across 100 reconnects", HundredEpochRollover),
        ("recovery snapshot notifies baseline-established", RecoveryBaselineNotify),
        ("app stopping forbids retry and late connect", AppStoppingLatch),
        ("l2 live disconnect remains unverified", L2Unverified)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void RequestEofStaleFirst()
    {
        var env = new DeviceSessionHarness();
        try
        {
            env.Connect();
            env.WaitFor(state => state.Phase == ConnectionPhase.Ready);
            var opens = env.Factory.Opens.Count;
            env.Factory.LastRequest!.FailEof();
            var stale = env.WaitFor(state => state.Phase == ConnectionPhase.Stale);
            Check(stale.Freshness == DeviceFreshness.Stale);
            Check(!stale.Capabilities.HasMutationControl);
            Check(stale.LastErrorCode == RpcCodes.RequestLost);
            Check(stale.Recovery.Cause == RecoveryCodes.RequestEof);
            Check(env.Factory.Opens.Count == opens);
            Check(stale.Phase != ConnectionPhase.Ready);
            env.WaitFor(state => state.Recovery.RetryTimerCount == 1);
            Check(env.Factory.Opens.Count == opens);
            env.Time.Advance(TimeSpan.FromSeconds(1));
            env.WaitFor(state => state.Epoch.Value == 2);
            Check(env.Factory.Opens.Count > opens);
        }
        finally
        {
            env.DisposeActor();
        }
    }

    static void SubscriptionEofDistinct()
    {
        var env = new DeviceSessionHarness();
        try
        {
            env.Connect();
            env.WaitFor(state => state.Phase == ConnectionPhase.Ready);
            env.Factory.LastSubscription!.CompleteEof();
            var stale = env.WaitFor(state => state.Phase == ConnectionPhase.Stale);
            Check(stale.LastErrorCode == RpcCodes.SubscriptionLost);
            Check(stale.Recovery.Cause == RecoveryCodes.SubscriptionEof);
            Check(stale.Recovery.Cause != RecoveryCodes.RequestEof);
        }
        finally
        {
            env.DisposeActor();
        }
    }

    static void ClassifiedRpcFaults()
    {
        var bridge = new DeviceSessionHarness();
        try
        {
            bridge.Connect();
            bridge.WaitFor(state => state.Phase == ConnectionPhase.Ready);
            bridge.Factory.LastRequest!.FailChildExit();
            var stale = bridge.WaitFor(state =>
                state.Phase == ConnectionPhase.Stale && state.Recovery.Cause == RecoveryCodes.RpcBridgeExit);
            Check(stale.Recovery.Cause == RecoveryCodes.RpcBridgeExit);
        }
        finally
        {
            bridge.DisposeActor();
        }

        var down = new DeviceSessionHarness();
        try
        {
            down.Connect();
            down.WaitFor(state => state.Phase == ConnectionPhase.Ready);
            down.Factory.LastRequest!.FailUnavailable();
            var stale = down.WaitFor(state =>
                state.Phase == ConnectionPhase.Stale && state.Recovery.Cause == RecoveryCodes.DaemonUnreachable);
            Check(stale.Recovery.Cause == RecoveryCodes.DaemonUnreachable);
        }
        finally
        {
            down.DisposeActor();
        }

        var proto = new DeviceSessionHarness();
        try
        {
            proto.Connect();
            proto.WaitFor(state => state.Phase == ConnectionPhase.Ready);
            proto.Factory.LastRequest!.FailProtocol();
            var stale = proto.WaitFor(state =>
                state.Phase == ConnectionPhase.Stale && state.Recovery.Cause == RecoveryCodes.ProtocolError);
            Check(stale.Recovery.Cause == RecoveryCodes.ProtocolError);
            Check(stale.Recovery.Decision == RecoveryCodes.ProtocolError ||
                  stale.Recovery.Decision == RecoveryCodes.AwaitUser);
            Check(stale.Recovery.RetryTimerCount == 0);
            Check(proto.Actor.Current.Recovery.RetryTimerCount == 0);
            proto.Time.Advance(TimeSpan.FromSeconds(30));
            Check(proto.Actor.Current.Phase == ConnectionPhase.Stale);
            Check(proto.Actor.Current.Recovery.RetryTimerCount == 0);
        }
        finally
        {
            proto.DisposeActor();
        }
    }

    static void NoRetryPaths()
    {
        var incompatible = new DeviceSessionHarness();
        try
        {
            incompatible.Decoder.Snapshot = DeviceSessionGraphs.Baseline(incompatible.Session, 1) with
            {
                Protocol = 23
            };
            incompatible.Connect();
            var state = incompatible.WaitFor(item => item.Phase == ConnectionPhase.Incompatible);
            Check(state.Recovery.RetryTimerCount == 0);
            var opens = incompatible.Factory.Opens.Count;
            incompatible.Time.Advance(TimeSpan.FromSeconds(30));
            Check(incompatible.Actor.Current.Phase == ConnectionPhase.Incompatible);
            Check(incompatible.Factory.Opens.Count == opens);
        }
        finally
        {
            incompatible.DisposeActor();
        }

        var manual = new DeviceSessionHarness();
        try
        {
            manual.Connect();
            manual.WaitFor(state => state.Phase == ConnectionPhase.Ready);
            var opens = manual.Factory.Opens.Count;
            manual.Disconnect();
            var offline = manual.WaitFor(state => state.Phase == ConnectionPhase.Offline);
            Check(offline.Recovery.Cause == RecoveryCodes.ManualDisconnect);
            Check(offline.Recovery.RetryTimerCount == 0);
            manual.Time.Advance(TimeSpan.FromSeconds(30));
            Check(manual.Factory.Opens.Count == opens);
        }
        finally
        {
            manual.DisposeActor();
        }
    }

    static void BackoffAndManualRetry()
    {
        var env = new DeviceSessionHarness();
        try
        {
            env.Connect();
            env.WaitFor(state => state.Phase == ConnectionPhase.Ready);
            env.Factory.LastRequest!.FailEof();
            env.WaitFor(state => state.Phase == ConnectionPhase.Stale && state.Recovery.RetryTimerCount == 1);
            Check(env.Actor.Current.Recovery.Attempt == 1);
            Check(env.Actor.Current.ActiveTimerCount == 1);
            env.Factory.NextOpenFails = true;
            env.Time.Advance(TimeSpan.FromSeconds(1));
            env.WaitFor(state => state.Recovery.Attempt == 2 && state.Phase == ConnectionPhase.Stale);
            Check(env.Actor.Current.Recovery.RetryTimerCount == 1);
            env.Time.Advance(TimeSpan.FromSeconds(1));
            Check(env.Actor.Current.Recovery.Attempt == 2);
            env.Time.Advance(TimeSpan.FromSeconds(1));
            env.WaitFor(state => state.Recovery.Attempt == 3 && state.Phase == ConnectionPhase.Stale);
            env.RetryNow();
            env.RetryNow();
            env.WaitFor(state =>
                state.Recovery.Attempt >= 4 && state.Phase == ConnectionPhase.Stale);
            Check(env.Actor.Current.Recovery.RetryTimerCount <= 1);
            Check(env.Actor.Current.Recovery.ReconnectEffectCount >= 1);
        }
        finally
        {
            env.DisposeActor();
        }
    }

    static void HundredEpochRollover()
    {
        var env = new DeviceSessionHarness();
        try
        {
            env.Factory.AutoAck = false;
            env.Connect();
            env.Factory.LastSubscription!.Acknowledge();
            env.WaitFor(state => state.Phase == ConnectionPhase.Ready);
            for (var i = 0; i < 100; i++)
            {
                var previousRequest = env.Factory.LastRequest!;
                var previousSub = env.Factory.LastSubscription!;
                var previousEpoch = env.Actor.Current.Epoch;
                env.Factory.AutoAck = true;
                env.Decoder.Snapshot = DeviceSessionGraphs.Baseline(env.Session, previousEpoch.Value + 1, "new-a");
                env.Connect();
                var ready = env.WaitFor(state =>
                    state.Epoch.Value == previousEpoch.Value + 1 && state.Phase == ConnectionPhase.Ready);
                var revision = ready.Projection.Revision;
                previousSub.Emit(JsonEvent());
                previousRequest.FailEof();
                env.Time.Advance(env.Options.CoalesceWindow);
                Check(env.Actor.Current.Epoch.Value == previousEpoch.Value + 1);
                Check(env.Actor.Current.Phase == ConnectionPhase.Ready);
                Check(env.Actor.Current.Projection.Devices[0].Sessions[0].Panes[0].Key.PaneId == "new-a");
                Check(env.Actor.Current.Projection.Revision >= revision);
            }

            Check(env.Actor.Current.Recovery.RetryTimerCount <= 1);
        }
        finally
        {
            env.DisposeActor();
        }
    }

    static void RecoveryBaselineNotify()
    {
        var env = new DeviceSessionHarness();
        try
        {
            env.Connect();
            env.WaitFor(state => state.Phase == ConnectionPhase.Ready && state.BaselineInstalled);
            Check(env.Notifications.Calls == 0);
            env.Factory.LastRequest!.FailEof();
            env.WaitFor(state =>
                state.Phase == ConnectionPhase.Stale && state.Recovery.RetryTimerCount == 1);
            env.Time.Advance(TimeSpan.FromSeconds(1));
            env.WaitFor(state => state.Phase == ConnectionPhase.Ready && state.Epoch.Value == 2);
            Check(env.Notifications.Calls == 1);
            Check(env.Notifications.Kinds.Contains(SessionLifecycleKinds.BaselineEstablished));
        }
        finally
        {
            env.DisposeActor();
        }
    }

    static void AppStoppingLatch()
    {
        var env = new DeviceSessionHarness();
        try
        {
            env.Connect();
            env.WaitFor(state => state.Phase == ConnectionPhase.Ready);
            var opens = env.Factory.Opens.Count;
            env.NotifyAppStopping();
            var offline = env.WaitFor(state => state.Phase == ConnectionPhase.Offline);
            Check(offline.Recovery.Cause == RecoveryCodes.AppStopping);
            Check(offline.Recovery.RetryTimerCount == 0);
            env.RetryNow();
            env.Connect();
            env.Time.Advance(TimeSpan.FromSeconds(30));
            Check(env.Factory.Opens.Count == opens);
            Check(env.Actor.Current.Phase == ConnectionPhase.Offline);
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
            Path.Combine(dir!.FullName, "implementation", "hd-018-l2.json"),
            System.Text.Encoding.UTF8);
        Check(json.Contains("UNVERIFIED", StringComparison.Ordinal));
        Check(json.Contains("\"ac13_passed\": false", StringComparison.Ordinal));
        Check(json.Contains("\"ac14_passed\": false", StringComparison.Ordinal));
        Check(json.Contains("\"ac15_passed\": false", StringComparison.Ordinal));
        Check(json.Contains("\"g0_passed\": false", StringComparison.Ordinal));
        Check(json.Contains("\"phase_gate\": \"not_passed\"", StringComparison.Ordinal));
    }

    static System.Text.Json.JsonElement JsonEvent()
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            """{"event":"pane.updated","data":{"pane_id":"p1"}}""");
        return document.RootElement.Clone();
    }
}
