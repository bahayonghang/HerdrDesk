using System.Collections.Frozen;
using System.Threading.Channels;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal sealed class FakeLeaseStore : ILeaseTargetStore
{
    public LeaseTargetSnapshot Snapshot { get; set; } = default!;

    public LeaseTargetSnapshot Read(PaneKey pane)
    {
        if (Snapshot is null || Snapshot.Pane != pane)
        {
            return new LeaseTargetSnapshot(
                pane, false, new ConnectionEpoch(0), 0, DeviceFreshness.Unknown,
                new CapabilityProfile("", null, 0, 0, "", "UNVERIFIED", FrozenSet<string>.Empty),
                "", "");
        }

        return Snapshot;
    }

    public void MakeStale() =>
        Snapshot = Snapshot with { Freshness = DeviceFreshness.Stale };
}

internal sealed class FakeLeaseRenderer : ITerminalRenderer
{
    public List<string> Log { get; } = [];
    public List<(ConnectionEpoch Epoch, ulong Sequence, bool Full)> Applied { get; } = [];
    public bool HoldApply { get; set; }
    public CancellationTokenSource ApplyHold { get; } = new();
    public bool ReadOnly { get; private set; } = true;
    public PaneKey? Pane { get; private set; }
    public ConnectionEpoch? Epoch { get; private set; }
    public RendererSurfaceState SurfaceState { get; private set; } = RendererSurfaceState.Uninitialized;

    public ValueTask BindAsync(PaneKey pane, ConnectionEpoch epoch, CancellationToken cancellationToken = default)
    {
        Pane = pane;
        Epoch = epoch;
        SurfaceState = RendererSurfaceState.Bound;
        Log.Add("bind:" + epoch.Value);
        return ValueTask.CompletedTask;
    }

    public async ValueTask<RenderApplyResult> ApplyAsync(TerminalFrame frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (HoldApply)
            await Task.Delay(Timeout.Infinite, ApplyHold.Token).ConfigureAwait(false);
        var epoch = Epoch ?? new ConnectionEpoch(0);
        Applied.Add((epoch, frame.Sequence, frame.Full));
        Log.Add("apply:" + epoch.Value + ":" + frame.Sequence + ":" + (frame.Full ? "full" : "delta"));
        SurfaceState = ReadOnly ? RendererSurfaceState.Observing : RendererSurfaceState.Ready;
        return new RenderApplyResult(
            true,
            new RenderConsumption(epoch, frame.Sequence),
            RendererQueueState.Ready,
            SurfaceState,
            "parse_consumed");
    }

    public async IAsyncEnumerable<RendererInput> ReadInputsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        yield break;
    }

    public ValueTask SetReadOnlyAsync(bool readOnly, CancellationToken cancellationToken = default)
    {
        ReadOnly = readOnly;
        Log.Add("readonly:" + (readOnly ? "true" : "false"));
        return ValueTask.CompletedTask;
    }

    public ValueTask FocusAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync()
    {
        ApplyHold.Cancel();
        ApplyHold.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeLeaseTransport : ITerminalTransport
{
    private readonly Channel<TerminalTransportEvent> _events = Channel.CreateUnbounded<TerminalTransportEvent>();
    private readonly TaskCompletionSource _writeStarted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _writeRelease =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _eventId;
    private ulong _commandId;
    private int _closed;

    public FakeLeaseTransport(
        PaneKey pane,
        ConnectionEpoch epoch,
        TerminalMode mode,
        string? attemptId,
        bool takeover)
    {
        Pane = pane;
        Epoch = epoch;
        Mode = mode;
        AttemptId = attemptId;
        TakeoverAuthorized = takeover;
    }

    public PaneKey Pane { get; }
    public ConnectionEpoch Epoch { get; }
    public TerminalMode Mode { get; }
    public string? AttemptId { get; }
    public bool TakeoverAuthorized { get; }
    public List<byte[]> Inputs { get; } = [];
    public bool HoldWrite { get; set; }
    public Task WriteStarted => _writeStarted.Task;
    public int ResizeCount { get; private set; }
    public int ScrollCount { get; private set; }
    public int ReleaseCount { get; private set; }

    public async IAsyncEnumerable<TerminalTransportEvent> ReadEventsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return item;
    }

    public async ValueTask<TerminalWriteReceipt> SendInputAsync(
        TerminalInputCommand input,
        CancellationToken cancellationToken = default)
    {
        if (HoldWrite)
        {
            _writeStarted.TrySetResult();
            await _writeRelease.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (Volatile.Read(ref _closed) != 0 || Mode != TerminalMode.Control)
            return new TerminalWriteReceipt(NextCommand(), TerminalWriteDisposition.NotSent,
                ControlLeaseCodes.InputNotSent);
        Inputs.Add((input.Bytes ?? []).ToArray());
        return new TerminalWriteReceipt(
            NextCommand(), TerminalWriteDisposition.WrittenUnacknowledged,
            TerminalTransportCodes.WrittenUnacknowledged);
    }

    public void ReleaseWrite() => _writeRelease.TrySetResult();

    public void EmitClosed() =>
        Emit(new TerminalClosedObserved(NextEvent(), Pane, Epoch, false, "closed"));

    public void EmitStdoutEnded() =>
        Emit(new TerminalStdoutEnded(NextEvent(), Pane, Epoch, false));

    public void EmitProcessExited() =>
        Emit(new TerminalProcessExited(NextEvent(), Pane, Epoch, 1));

    public ValueTask<TerminalWriteReceipt> ResizeAsync(
        TerminalResizeCommand size,
        CancellationToken cancellationToken = default)
    {
        _ = size;
        if (Volatile.Read(ref _closed) != 0 || Mode != TerminalMode.Control)
            return new(new TerminalWriteReceipt(NextCommand(), TerminalWriteDisposition.NotSent,
                ControlLeaseCodes.ObserveNoResize));
        ResizeCount++;
        return new(new TerminalWriteReceipt(
            NextCommand(), TerminalWriteDisposition.WrittenUnacknowledged,
            TerminalTransportCodes.WrittenUnacknowledged));
    }

    public ValueTask<TerminalWriteReceipt> ScrollAsync(
        TerminalScrollCommand request,
        CancellationToken cancellationToken = default)
    {
        _ = request;
        if (Volatile.Read(ref _closed) != 0 || Mode != TerminalMode.Control)
            return new(new TerminalWriteReceipt(NextCommand(), TerminalWriteDisposition.NotSent,
                ControlLeaseCodes.ObserveScrollDenied));
        ScrollCount++;
        return new(new TerminalWriteReceipt(
            NextCommand(), TerminalWriteDisposition.WrittenUnacknowledged,
            TerminalTransportCodes.WrittenUnacknowledged));
    }

    public ValueTask<TerminalWriteReceipt> ReleaseAsync(CancellationToken cancellationToken = default)
    {
        ReleaseCount++;
        return new(new TerminalWriteReceipt(
            NextCommand(), TerminalWriteDisposition.WrittenUnacknowledged,
            TerminalTransportCodes.WrittenUnacknowledged));
    }

    public void EmitFrame(ulong sequence, bool full, byte[]? bytes = null)
    {
        var buffer = bytes ?? [(byte)'A'];
        var frame = new TerminalOwnedFrame(Pane, Epoch, sequence, 80, 24, full, buffer);
        Emit(new TerminalFrameArrived(NextEvent(), Pane, Epoch, frame));
    }

    public void EmitOwnership(TerminalLeaseResult result, string? attemptId = null) =>
        Emit(new TerminalOwnershipObserved(NextEvent(), Pane, Epoch, attemptId ?? AttemptId, result));

    public void EmitEnded() =>
        Emit(new TerminalTransportEnded(NextEvent(), Pane, Epoch, ControlLeaseCodes.TerminalDisconnected, 0, false));

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return ValueTask.CompletedTask;
        _writeRelease.TrySetResult();
        _events.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    private void Emit(TerminalTransportEvent item) => _events.Writer.TryWrite(item);

    private long NextEvent() => Interlocked.Increment(ref _eventId);

    private ulong NextCommand() => ++_commandId;
}

internal sealed class FakeLeaseHost : IControlBindingHost
{
    public List<FakeLeaseTransport> Observes { get; } = [];
    public List<FakeLeaseTransport> Candidates { get; } = [];

    public FakeLeaseTransport? LastObserve => Observes.Count == 0 ? null : Observes[^1];
    public FakeLeaseTransport? LastCandidate => Candidates.Count == 0 ? null : Candidates[^1];
    public int TakeoverCount => Candidates.Count(item => item.TakeoverAuthorized);
    public int NoTakeoverCount => Candidates.Count(item => !item.TakeoverAuthorized);

    public ValueTask<ITerminalTransport> OpenObserveAsync(
        PaneKey pane,
        ConnectionEpoch epoch,
        CancellationToken cancellationToken = default)
    {
        var transport = new FakeLeaseTransport(pane, epoch, TerminalMode.Observe, null, false);
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
        var transport = new FakeLeaseTransport(
            pane, epoch, TerminalMode.Control, attemptId, takeover?.Confirmed == true);
        Candidates.Add(transport);
        return ValueTask.FromResult<ITerminalTransport>(transport);
    }
}

internal sealed class ControlLeaseHarness
{
    public ControlLeaseHarness()
    {
        Pane = new PaneKey(
            new SessionKey(new DeviceId(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")), "endpoint", "dev"),
            "ws", "p1");
        Store = new FakeLeaseStore
        {
            Snapshot = new LeaseTargetSnapshot(
                Pane,
                true,
                new ConnectionEpoch(1),
                1,
                DeviceFreshness.Current,
                new CapabilityProfile(
                    "0.9.0", "0.9.0", 22, 1, "sha", "sha",
                    FrozenSet.ToFrozenSet(["pane.send_input"], StringComparer.Ordinal)),
                "dev / ws / p1",
                "p1")
        };
        Host = new FakeLeaseHost();
        Renderer = new FakeLeaseRenderer();
        Coordinator = new ControlLeaseCoordinator(Store, Host, Renderer);
    }

    public PaneKey Pane { get; }
    public FakeLeaseStore Store { get; }
    public FakeLeaseHost Host { get; }
    public FakeLeaseRenderer Renderer { get; }
    public ControlLeaseCoordinator Coordinator { get; }

    public void Open() => Coordinator.OpenPaneAsync(Pane).AsTask().GetAwaiter().GetResult();

    public void Request() => Coordinator.RequestControlAsync().AsTask().GetAwaiter().GetResult();

    public void WaitObserve() =>
        Wait(state => state.ObserveBinding is not null && state.Access == TerminalAccess.Observing);

    public void WaitCandidate() =>
        Wait(state => state.CandidateBinding is not null && state.Access == TerminalAccess.Acquiring);

    public ControlLeaseState Wait(Func<ControlLeaseState, bool> pred, TimeSpan? timeout = null) =>
        ControlLeaseWait.Until(Coordinator, pred, timeout);

    public void Dispose()
    {
        Renderer.ApplyHold.Cancel();
        Coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

internal static class ControlLeaseWait
{
    public static ControlLeaseState Until(
        ControlLeaseCoordinator coordinator,
        Func<ControlLeaseState, bool> pred,
        TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        return UntilAsync(coordinator, pred, cts.Token).GetAwaiter().GetResult();
    }

    static async Task<ControlLeaseState> UntilAsync(
        ControlLeaseCoordinator coordinator,
        Func<ControlLeaseState, bool> pred,
        CancellationToken cancellationToken)
    {
        try
        {
            if (pred(coordinator.Current))
                return coordinator.Current;
            await foreach (var state in coordinator.ReadStatesAsync(cancellationToken).ConfigureAwait(false))
            {
                if (pred(state) || pred(coordinator.Current))
                    return coordinator.Current;
            }
        }
        catch (OperationCanceledException)
        {
        }

        throw new Exception("wait_timeout access=" + coordinator.Current.Access + " code=" +
                            coordinator.Current.LastCode);
    }
}
