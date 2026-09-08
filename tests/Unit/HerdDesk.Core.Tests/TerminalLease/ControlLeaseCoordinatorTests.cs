using System.Text;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class ControlLeaseCoordinatorTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("open select focus frame and process stay observing", OpenSelectFocusFrameProcess),
        ("no-takeover request never authorizes takeover", RequestControlNoTakeover),
        ("four gates are required before controlling", FourGatesRequired),
        ("busy keeps observing and one valid confirm opens takeover once", BusyThenOneTakeover),
        ("stale reused and wrong-target confirm open zero takeover", StaleReuseWrongTarget),
        ("acquire keeps observe stream then promote rebinds candidate full", ObserveThenPromote),
        ("input release barrier does not replay", InputReleaseBarrier),
        ("js pane emulator reply and observe resize stay denied", DeniedOrigins),
        ("release and recover observe never restore control", ReleaseRecoverObserve),
        ("l2 live lease remains unverified", L2Unverified)
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

    static TerminalLeaseResult Unconfirmed() =>
        TerminalLeaseProbe.Map(new TerminalLeaseObservation(
            TerminalLeaseOperation.RequestControl,
            TerminalAccess.Observing,
            false,
            FirstFrameSeen: true,
            ProcessAlive: true,
            WindowFocused: true));

    static TerminalLeaseResult Busy() =>
        TerminalLeaseProbe.Map(new TerminalLeaseObservation(
            TerminalLeaseOperation.RequestControl,
            TerminalAccess.Observing,
            false,
            ControlSignal: TerminalControlSignal.Busy));

    static RendererInput Input(PaneKey pane, ConnectionEpoch epoch, byte[] bytes, InputOrigin origin) =>
        new(pane, epoch, origin, bytes);

    static void OpenSelectFocusFrameProcess()
    {
        var env = new ControlLeaseHarness();
        try
        {
            env.Open();
            env.WaitObserve();
            env.Host.LastObserve!.EmitFrame(1, true);
            env.Coordinator.NoteFocusAsync().AsTask().GetAwaiter().GetResult();
            env.Coordinator.NoteProcessAliveAsync().AsTask().GetAwaiter().GetResult();
            var other = env.Pane with { PaneId = "p2" };
            env.Store.Snapshot = env.Store.Snapshot with { Pane = other, Breadcrumb = "dev / ws / p2" };
            env.Coordinator.SelectPaneAsync(other).AsTask().GetAwaiter().GetResult();
            env.Wait(state => state.Target == other && state.ObserveBinding is not null);
            Check(env.Coordinator.Current.Access == TerminalAccess.Observing);
            Check(!env.Coordinator.Current.ControlVerified);
            Check(env.Host.TakeoverCount == 0);
            Check(env.Renderer.ReadOnly);
        }
        finally
        {
            env.Dispose();
        }
    }

    static void RequestControlNoTakeover()
    {
        var env = new ControlLeaseHarness();
        try
        {
            env.Open();
            env.WaitObserve();
            env.Request();
            env.WaitCandidate();
            env.Host.LastObserve!.EmitOwnership(Verified());
            env.Host.LastObserve.EmitFrame(2, false);
            env.Coordinator.NoteFocusAsync().AsTask().GetAwaiter().GetResult();
            env.Coordinator.NoteProcessAliveAsync().AsTask().GetAwaiter().GetResult();
            Check(env.Host.NoTakeoverCount == 1);
            Check(env.Host.TakeoverCount == 0);
            Check(env.Coordinator.Current.Access == TerminalAccess.Acquiring);
            Check(!env.Coordinator.Current.ControlVerified);
            Check(env.Renderer.ReadOnly);
        }
        finally
        {
            env.Dispose();
        }
    }

    static void FourGatesRequired()
    {
        foreach (var skip in new[] { "ownership", "full", "ack", "fresh" })
        {
            var env = new ControlLeaseHarness();
            try
            {
                env.Open();
                env.WaitObserve();
                env.Request();
                env.WaitCandidate();
                if (skip == "fresh")
                    env.Store.MakeStale();
                if (skip == "ack")
                    env.Renderer.HoldApply = true;
                var candidate = env.Host.LastCandidate!;
                if (skip != "full")
                    candidate.EmitFrame(1, true);
                if (skip == "ownership")
                    candidate.EmitOwnership(Unconfirmed());
                else
                    candidate.EmitOwnership(Verified());
                try
                {
                    env.Wait(state => state.Access == TerminalAccess.Controlling, TimeSpan.FromMilliseconds(400));
                    throw new Exception("unexpected_control skip=" + skip);
                }
                catch (Exception error) when (error.Message.StartsWith("wait_timeout", StringComparison.Ordinal))
                {
                }

                if (env.Coordinator.Current.Access == TerminalAccess.Controlling ||
                    env.Coordinator.Current.ControlVerified ||
                    !env.Renderer.ReadOnly)
                    throw new Exception("skip=" + skip + " access=" + env.Coordinator.Current.Access +
                                        " verified=" + env.Coordinator.Current.ControlVerified +
                                        " code=" + env.Coordinator.Current.LastCode +
                                        " readonly=" + env.Renderer.ReadOnly);
            }
            finally
            {
                env.Dispose();
            }
        }

        var ok = new ControlLeaseHarness();
        try
        {
            ok.Open();
            ok.WaitObserve();
            ok.Host.LastObserve!.EmitFrame(1, true);
            ok.Request();
            ok.WaitCandidate();
            ok.Host.LastCandidate!.EmitFrame(1, true);
            ok.Host.LastCandidate.EmitOwnership(Verified());
            var ready = ok.Wait(state =>
                state.Access == TerminalAccess.Controlling && state.ControlVerified && !ok.Renderer.ReadOnly);
            Check(ready.ControlVerified);
            Check(!ok.Renderer.ReadOnly);
            Check(ok.Host.TakeoverCount == 0);
        }
        finally
        {
            ok.Dispose();
        }
    }

    static void BusyThenOneTakeover()
    {
        var env = new ControlLeaseHarness();
        try
        {
            env.Open();
            env.WaitObserve();
            env.Request();
            env.WaitCandidate();
            env.Host.LastCandidate!.EmitOwnership(Busy());
            var busy = env.Wait(state =>
                state.Access == TerminalAccess.Observing && state.LastAttempt == ControlAttemptOutcome.Busy);
            Check(!busy.ControlVerified);
            Check(busy.Challenge is not null);
            Check(env.Host.TakeoverCount == 0);
            env.Coordinator.ConfirmTakeoverAsync(busy.Challenge!.Handle).AsTask().GetAwaiter().GetResult();
            env.Wait(state => env.Host.TakeoverCount == 1 && state.Access == TerminalAccess.Acquiring);
            Check(env.Host.TakeoverCount == 1);
            Check(env.Host.NoTakeoverCount == 1);
            Check(env.Coordinator.Current.Access == TerminalAccess.Acquiring);
            Check(!env.Coordinator.Current.ControlVerified);
        }
        finally
        {
            env.Dispose();
        }
    }

    static void StaleReuseWrongTarget()
    {
        var env = new ControlLeaseHarness();
        try
        {
            env.Open();
            env.WaitObserve();
            env.Request();
            env.WaitCandidate();
            env.Host.LastCandidate!.EmitOwnership(Busy());
            var busy = env.Wait(state => state.Challenge is not null);
            var handle = busy.Challenge!.Handle;
            env.Coordinator.ConfirmTakeoverAsync("missing").AsTask().GetAwaiter().GetResult();
            Check(env.Host.TakeoverCount == 0);
            env.Coordinator.ConfirmTakeoverAsync(handle).AsTask().GetAwaiter().GetResult();
            env.Wait(state => env.Host.TakeoverCount == 1 && state.Access == TerminalAccess.Acquiring);
            env.Coordinator.ConfirmTakeoverAsync(handle).AsTask().GetAwaiter().GetResult();
            Check(env.Host.TakeoverCount == 1);
            env.Coordinator.CancelAcquireAsync().AsTask().GetAwaiter().GetResult();
            env.Wait(state => state.Access == TerminalAccess.Observing);
            env.Request();
            env.WaitCandidate();
            env.Host.LastCandidate!.EmitOwnership(Busy());
            var again = env.Wait(state =>
                state.Challenge is not null && state.LastAttempt == ControlAttemptOutcome.Busy);
            Check(again.Challenge!.Handle != handle);
            var other = env.Pane with { PaneId = "p2" };
            env.Store.Snapshot = env.Store.Snapshot with { Pane = other, Breadcrumb = "dev / ws / p2" };
            env.Coordinator.SelectPaneAsync(other).AsTask().GetAwaiter().GetResult();
            env.Coordinator.ConfirmTakeoverAsync(again.Challenge.Handle).AsTask().GetAwaiter().GetResult();
            Check(env.Host.TakeoverCount == 1);
        }
        finally
        {
            env.Dispose();
        }
    }

    static void ObserveThenPromote()
    {
        var env = new ControlLeaseHarness();
        try
        {
            env.Open();
            env.WaitObserve();
            var observeEpoch = env.Coordinator.Current.ObserveBinding!.Epoch;
            env.Host.LastObserve!.EmitFrame(1, true, [(byte)'o']);
            env.Wait(state => env.Renderer.Applied.Any(item => item.Epoch == observeEpoch));
            env.Request();
            env.WaitCandidate();
            var candidateEpoch = env.Coordinator.Current.CandidateBinding!.Epoch;
            env.Host.LastObserve.EmitFrame(2, false, [(byte)'d']);
            env.Host.LastCandidate!.EmitFrame(1, true, [(byte)'c']);
            env.Host.LastCandidate.EmitOwnership(Verified());
            env.Wait(state => state.Access == TerminalAccess.Controlling && state.ControlVerified);
            Check(env.Renderer.Applied.Any(item => item.Epoch == observeEpoch && item.Full));
            Check(env.Renderer.Applied.Any(item => item.Epoch == candidateEpoch && item.Full));
            var bindCandidate = env.Renderer.Log.FindIndex(item => item == "bind:" + candidateEpoch.Value);
            var writable = env.Renderer.Log.FindIndex(item => item == "readonly:false");
            Check(bindCandidate >= 0);
            Check(writable > bindCandidate);
            Check(env.Renderer.Applied.Last().Epoch == candidateEpoch);
        }
        finally
        {
            env.Dispose();
        }
    }

    static void InputReleaseBarrier()
    {
        var env = new ControlLeaseHarness();
        try
        {
            env.Open();
            env.WaitObserve();
            env.Request();
            env.WaitCandidate();
            env.Host.LastCandidate!.EmitFrame(1, true);
            env.Host.LastCandidate.EmitOwnership(Verified());
            var ready = env.Wait(state => state.Access == TerminalAccess.Controlling && state.ControlVerified);
            var epoch = ready.ControlBinding!.Epoch;
            var tasks = new Task<InputSubmissionOutcome>[32];
            for (var i = 0; i < tasks.Length; i++)
            {
                tasks[i] = env.Coordinator.SubmitInputAsync(
                    Input(env.Pane, epoch, [(byte)i], InputOrigin.UserKey)).AsTask();
            }

            env.Coordinator.ReleaseControlAsync().AsTask().GetAwaiter().GetResult();
            Task.WaitAll(tasks);
            foreach (var task in tasks)
            {
                Check(task.Result.Disposition is TerminalWriteDisposition.NotSent
                    or TerminalWriteDisposition.WrittenUnacknowledged
                    or TerminalWriteDisposition.UnknownAfterDisconnect);
            }

            var captured = env.Host.Candidates.SelectMany(item => item.Inputs).ToArray();
            Check(captured.Length == captured.Distinct(new ByteSeqComparer()).Count());
            var after = captured.Length;
            env.Wait(state => state.Access != TerminalAccess.Controlling);
            Check(!env.Coordinator.Current.ControlVerified);
            env.WaitObserve();
            Check(env.Host.Candidates.SelectMany(item => item.Inputs).Count() == after);
            var replay = env.Coordinator.SubmitInputAsync(
                Input(env.Pane, epoch, [9], InputOrigin.UserKey)).AsTask().GetAwaiter().GetResult();
            Check(replay.Disposition == TerminalWriteDisposition.NotSent);
            Check(env.Host.Candidates.SelectMany(item => item.Inputs).Count() == after);
        }
        finally
        {
            env.Dispose();
        }
    }

    static void DeniedOrigins()
    {
        var env = new ControlLeaseHarness();
        try
        {
            env.Open();
            env.WaitObserve();
            var observeEpoch = env.Coordinator.Current.ObserveBinding!.Epoch;
            var js = env.Coordinator.SubmitInputAsync(
                Input(env.Pane with { PaneId = "js" }, observeEpoch, [1], InputOrigin.UserKey))
                .AsTask().GetAwaiter().GetResult();
            Check(js.Disposition == TerminalWriteDisposition.NotSent);
            Check(js.Code is ControlLeaseCodes.ControlNotVerified or "wrong_pane" or "control_not_verified");
            Check(env.Host.LastObserve!.Inputs.Count == 0);
            var resize = env.Coordinator.SubmitResizeAsync(
                new TerminalResizeCommand(env.Pane, observeEpoch, 80, 24, 8, 16))
                .AsTask().GetAwaiter().GetResult();
            Check(resize.Code == ControlLeaseCodes.ObserveNoResize);
            var scroll = env.Coordinator.SubmitScrollAsync(
                new TerminalScrollCommand(env.Pane, observeEpoch, "up", 1, "wheel"))
                .AsTask().GetAwaiter().GetResult();
            Check(scroll.Code == ControlLeaseCodes.ObserveScrollDenied);
            env.Request();
            env.WaitCandidate();
            env.Host.LastCandidate!.EmitFrame(1, true);
            env.Host.LastCandidate.EmitOwnership(Verified());
            var ready = env.Wait(state => state.Access == TerminalAccess.Controlling);
            var epoch = ready.ControlBinding!.Epoch;
            var emulator = env.Coordinator.SubmitInputAsync(
                Input(env.Pane, epoch, [2], InputOrigin.EmulatorReply)).AsTask().GetAwaiter().GetResult();
            Check(emulator.Code == "input_origin_denied");
            var claimed = env.Coordinator.SubmitInputAsync(
                Input(env.Pane with { PaneId = "js" }, epoch, [3], InputOrigin.UserKey))
                .AsTask().GetAwaiter().GetResult();
            Check(claimed.Code == ControlLeaseCodes.WrongPane);
            Check(env.Host.LastCandidate.Inputs.Count == 0);
            var jsResize = env.Coordinator.SubmitResizeAsync(
                new TerminalResizeCommand(env.Pane with { PaneId = "js" }, epoch, 80, 24, 8, 16))
                .AsTask().GetAwaiter().GetResult();
            Check(jsResize.Disposition == TerminalWriteDisposition.NotSent);
            Check(jsResize.Code == ControlLeaseCodes.WrongPane);
            var staleScroll = env.Coordinator.SubmitScrollAsync(
                new TerminalScrollCommand(env.Pane, new ConnectionEpoch(epoch.Value + 9), "up", 1, "wheel"))
                .AsTask().GetAwaiter().GetResult();
            Check(staleScroll.Disposition == TerminalWriteDisposition.NotSent);
            Check(staleScroll.Code == ControlLeaseCodes.StaleEpoch);
            Check(env.Host.LastCandidate.ResizeCount == 0);
            Check(env.Host.LastCandidate.ScrollCount == 0);
        }
        finally
        {
            env.Dispose();
        }
    }

    static void ReleaseRecoverObserve()
    {
        var env = new ControlLeaseHarness();
        try
        {
            env.Open();
            env.WaitObserve();
            env.Request();
            env.WaitCandidate();
            env.Host.LastCandidate!.EmitFrame(1, true);
            env.Host.LastCandidate.EmitOwnership(Verified());
            env.Wait(state => state.Access == TerminalAccess.Controlling);
            env.Coordinator.ReleaseControlAsync().AsTask().GetAwaiter().GetResult();
            env.Wait(state => state.Access != TerminalAccess.Controlling && !state.ControlVerified);
            Check(env.Renderer.ReadOnly);
            env.WaitObserve();
            Check(env.Coordinator.Current.Access != TerminalAccess.Controlling);
            env.Coordinator.RecoverObserveAsync().AsTask().GetAwaiter().GetResult();
            env.WaitObserve();
            Check(!env.Coordinator.Current.ControlVerified);
            Check(env.Coordinator.Current.Access != TerminalAccess.Controlling);
            Check(env.Coordinator.Current.Challenge is null);
        }
        finally
        {
            env.Dispose();
        }
    }

    static void L2Unverified()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HerdDesk.slnx")))
            dir = dir.Parent;
        Check(dir is not null);
        var json = File.ReadAllText(Path.Combine(dir!.FullName, "implementation", "hd-016-l2.json"), Encoding.UTF8);
        Check(json.Contains("UNVERIFIED", StringComparison.Ordinal));
        Check(json.Contains("\"ac07_passed\": false", StringComparison.Ordinal));
        Check(json.Contains("\"ac14_passed\": false", StringComparison.Ordinal));
        Check(json.Contains("\"ac16_passed\": false", StringComparison.Ordinal));
        Check(json.Contains("\"g0_passed\": false", StringComparison.Ordinal));
    }

    sealed class ByteSeqComparer : IEqualityComparer<byte[]>
    {
        public bool Equals(byte[]? x, byte[]? y) => x is not null && y is not null && x.SequenceEqual(y);

        public int GetHashCode(byte[] obj)
        {
            var hash = new HashCode();
            hash.AddBytes(obj);
            return hash.ToHashCode();
        }
    }
}
