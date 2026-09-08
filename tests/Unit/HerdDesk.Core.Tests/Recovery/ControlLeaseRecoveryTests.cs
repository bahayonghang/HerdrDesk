using System.Text;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class ControlLeaseRecoveryTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("projection stale is readonly and clears control", ProjectionStaleReadonly),
        ("projection ready reopens observe only for the same pane", ProjectionReadySamePane),
        ("missing pane does not reconnect a namesake", MissingPaneNoNamesake),
        ("terminal closed eof and exit stay distinct", DistinctTerminalFaults),
        ("renderer failure reobserves without control", RendererFailureReobserve),
        ("input breakpoints map to not-sent or unknown without payload replay", InputBreakpoints),
        ("old lease generation completions are no-ops", OldGenerationNoOp)
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

    static RendererInput Input(PaneKey pane, ConnectionEpoch epoch, byte[] bytes) =>
        new(pane, epoch, InputOrigin.UserKey, bytes);

    static void Promote(ControlLeaseHarness env)
    {
        env.Open();
        env.WaitObserve();
        env.Request();
        env.WaitCandidate();
        env.Host.LastCandidate!.EmitFrame(1, true);
        env.Host.LastCandidate.EmitOwnership(Verified());
        env.Wait(state => state.Access == TerminalAccess.Controlling && state.ControlVerified);
    }

    static void ProjectionStaleReadonly()
    {
        var env = new ControlLeaseHarness();
        try
        {
            Promote(env);
            var generation = env.Coordinator.Current.LeaseGeneration;
            env.Coordinator.NoteRecoverySignalAsync(LeaseRecoverySignal.ProjectionStale).AsTask()
                .GetAwaiter().GetResult();
            env.Wait(state => state.Access != TerminalAccess.Controlling && !state.ControlVerified);
            Check(env.Renderer.ReadOnly);
            Check(env.Coordinator.Current.Challenge is null);
            Check(env.Coordinator.Current.LeaseGeneration > generation);
            Check(env.Coordinator.Current.ControlBinding is null);
            Check(env.Host.TakeoverCount == 0);
        }
        finally
        {
            env.Dispose();
        }
    }

    static void ProjectionReadySamePane()
    {
        var env = new ControlLeaseHarness();
        try
        {
            env.Open();
            env.WaitObserve();
            var first = env.Coordinator.Current.ObserveBinding!.Epoch;
            env.Coordinator.NoteRecoverySignalAsync(LeaseRecoverySignal.ProjectionStale).AsTask()
                .GetAwaiter().GetResult();
            env.Wait(state => state.ObserveBinding is null);
            env.Coordinator.NoteRecoverySignalAsync(LeaseRecoverySignal.ProjectionReady).AsTask()
                .GetAwaiter().GetResult();
            env.WaitObserve();
            Check(env.Coordinator.Current.ObserveBinding!.Epoch != first);
            Check(env.Coordinator.Current.ObserveBinding.Mode == TerminalMode.Observe);
            Check(!env.Coordinator.Current.ControlVerified);
            Check(env.Host.TakeoverCount == 0);
            Check(env.Host.LastObserve!.TakeoverAuthorized is false);
            env.Host.LastObserve.EmitFrame(1, true);
            env.Wait(state => env.Renderer.Applied.Any(item =>
                item.Epoch == env.Coordinator.Current.ObserveBinding!.Epoch && item.Full));
        }
        finally
        {
            env.Dispose();
        }
    }

    static void MissingPaneNoNamesake()
    {
        var env = new ControlLeaseHarness();
        try
        {
            env.Open();
            env.WaitObserve();
            env.Coordinator.NoteRecoverySignalAsync(LeaseRecoverySignal.ProjectionStale).AsTask()
                .GetAwaiter().GetResult();
            var opens = env.Host.Observes.Count;
            var other = env.Pane with { PaneId = "other" };
            env.Store.Snapshot = env.Store.Snapshot with
            {
                Pane = other,
                Exists = false,
                Breadcrumb = "dev / ws / other"
            };
            env.Coordinator.NoteRecoverySignalAsync(LeaseRecoverySignal.ProjectionReady).AsTask()
                .GetAwaiter().GetResult();
            Check(env.Coordinator.Current.LastCode is ControlLeaseCodes.PaneClosed
                or ControlLeaseCodes.TargetStale);
            Check(env.Coordinator.Current.Access != TerminalAccess.Controlling);
            Check(env.Host.Observes.Count == opens);
            Check(env.Host.TakeoverCount == 0);
        }
        finally
        {
            env.Dispose();
        }
    }

    static void DistinctTerminalFaults()
    {
        foreach (var (emit, code) in new (Action<FakeLeaseTransport>, string)[]
                 {
                     (t => t.EmitClosed(), ControlLeaseCodes.TerminalClosed),
                     (t => t.EmitStdoutEnded(), ControlLeaseCodes.TerminalStdoutEof),
                     (t => t.EmitProcessExited(), ControlLeaseCodes.TerminalClientExit)
                 })
        {
            var env = new ControlLeaseHarness();
            try
            {
                env.Open();
                env.WaitObserve();
                env.Store.MakeStale();
                emit(env.Host.LastObserve!);
                env.Wait(state => state.LastCode == code || state.Access == TerminalAccess.Disconnected);
                Check(env.Coordinator.Current.LastCode == code);
                Check(!env.Coordinator.Current.ControlVerified);
                Check(env.Renderer.ReadOnly);
            }
            finally
            {
                env.Dispose();
            }
        }
    }

    static void RendererFailureReobserve()
    {
        var env = new ControlLeaseHarness();
        try
        {
            env.Open();
            env.WaitObserve();
            var first = env.Coordinator.Current.ObserveBinding!.Epoch;
            env.Coordinator.NoteRecoverySignalAsync(LeaseRecoverySignal.RendererFailed).AsTask()
                .GetAwaiter().GetResult();
            env.Wait(state => state.ObserveBinding is not null && state.ObserveBinding.Epoch != first);
            Check(!env.Coordinator.Current.ControlVerified);
            Check(env.Host.TakeoverCount == 0);
            Check(env.Renderer.ReadOnly);
        }
        finally
        {
            env.Dispose();
        }
    }

    static void InputBreakpoints()
    {
        var before = new ControlLeaseHarness();
        try
        {
            before.Open();
            before.WaitObserve();
            before.Coordinator.NoteRecoverySignalAsync(LeaseRecoverySignal.ProjectionStale).AsTask()
                .GetAwaiter().GetResult();
            var denied = before.Coordinator.SubmitInputAsync(
                Input(before.Pane, new ConnectionEpoch(1), "secret-before"u8.ToArray()))
                .AsTask().GetAwaiter().GetResult();
            Check(denied.Disposition == TerminalWriteDisposition.NotSent);
            Check(before.Host.Observes.SelectMany(item => item.Inputs).Count() == 0);
        }
        finally
        {
            before.Dispose();
        }

        var writing = new ControlLeaseHarness();
        try
        {
            Promote(writing);
            var epoch = writing.Coordinator.Current.ControlBinding!.Epoch;
            writing.Host.LastCandidate!.HoldWrite = true;
            var pending = writing.Coordinator.SubmitInputAsync(
                Input(writing.Pane, epoch, "secret-write"u8.ToArray())).AsTask();
            DeviceSessionWait.Gate(writing.Host.LastCandidate.WriteStarted);
            writing.Coordinator.NoteRecoverySignalAsync(LeaseRecoverySignal.ProjectionStale).AsTask()
                .GetAwaiter().GetResult();
            writing.Host.LastCandidate.ReleaseWrite();
            var outcome = pending.GetAwaiter().GetResult();
            Check(outcome.Disposition is TerminalWriteDisposition.NotSent
                or TerminalWriteDisposition.UnknownAfterDisconnect);
            if (outcome.Disposition != TerminalWriteDisposition.NotSent)
                Check(outcome.Code == ControlLeaseCodes.InputOutcomeUnknown);
            writing.Coordinator.NoteRecoverySignalAsync(LeaseRecoverySignal.ProjectionReady).AsTask()
                .GetAwaiter().GetResult();
            writing.WaitObserve();
            Check(writing.Host.LastObserve!.Inputs.Count == 0);
            foreach (var captured in writing.Host.Candidates.SelectMany(item => item.Inputs))
                Check(!Encoding.UTF8.GetString(captured).Contains("secret-write", StringComparison.Ordinal));
        }
        finally
        {
            writing.Dispose();
        }

        var flushed = new ControlLeaseHarness();
        try
        {
            Promote(flushed);
            var epoch = flushed.Coordinator.Current.ControlBinding!.Epoch;
            var sent = flushed.Coordinator.SubmitInputAsync(
                Input(flushed.Pane, epoch, "secret-flush"u8.ToArray())).AsTask().GetAwaiter().GetResult();
            Check(sent.Disposition == TerminalWriteDisposition.WrittenUnacknowledged);
            flushed.Coordinator.NoteRecoverySignalAsync(LeaseRecoverySignal.ProjectionStale).AsTask()
                .GetAwaiter().GetResult();
            var last = flushed.Coordinator.Current.LastInput;
            Check(last is not null);
            Check(last!.Disposition is TerminalWriteDisposition.UnknownAfterDisconnect
                or TerminalWriteDisposition.WrittenUnacknowledged);
            flushed.Coordinator.NoteRecoverySignalAsync(LeaseRecoverySignal.ProjectionReady).AsTask()
                .GetAwaiter().GetResult();
            flushed.WaitObserve();
            Check(flushed.Host.LastObserve!.Inputs.Count == 0);
        }
        finally
        {
            flushed.Dispose();
        }
    }

    static void OldGenerationNoOp()
    {
        var env = new ControlLeaseHarness();
        try
        {
            env.Open();
            env.WaitObserve();
            var first = env.Host.LastObserve!;
            var firstEpoch = first.Epoch;
            env.Coordinator.NoteRecoverySignalAsync(LeaseRecoverySignal.ProjectionStale).AsTask()
                .GetAwaiter().GetResult();
            env.Coordinator.NoteRecoverySignalAsync(LeaseRecoverySignal.ProjectionReady).AsTask()
                .GetAwaiter().GetResult();
            env.WaitObserve();
            var generation = env.Coordinator.Current.LeaseGeneration;
            var applied = env.Renderer.Applied.Count;
            first.EmitFrame(9, true, [(byte)'x']);
            env.Coordinator.ConfirmTakeoverAsync("stale-handle").AsTask().GetAwaiter().GetResult();
            Check(env.Coordinator.Current.LeaseGeneration == generation);
            Check(env.Coordinator.Current.ObserveBinding!.Epoch != firstEpoch);
            Check(env.Renderer.Applied.Count == applied);
            Check(!env.Coordinator.Current.ControlVerified);
            Check(env.Host.TakeoverCount == 0);
        }
        finally
        {
            env.Dispose();
        }
    }
}
