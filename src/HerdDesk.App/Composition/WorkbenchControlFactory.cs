using System.Collections.Frozen;
using System.Threading.Channels;
using HerdDesk.Contracts;
using HerdDesk.Core;

namespace HerdDesk.App.Composition;

public static class WorkbenchControlFactory
{
    public static TerminalControlViewModel Create()
    {
        var coordinator = new ControlLeaseCoordinator(
            new MissingLeaseStore(),
            new NoOpControlBindingHost(),
            new NoOpTerminalRenderer());
        return new TerminalControlViewModel(coordinator);
    }
}

file sealed class MissingLeaseStore : ILeaseTargetStore
{
    public LeaseTargetSnapshot Read(PaneKey pane) =>
        new(
            pane,
            false,
            new ConnectionEpoch(0),
            0,
            DeviceFreshness.Unknown,
            new CapabilityProfile("", null, 0, 0, "", "UNVERIFIED", FrozenSet<string>.Empty),
            "",
            "");
}

file sealed class NoOpControlBindingHost : IControlBindingHost
{
    public ValueTask<ITerminalTransport> OpenObserveAsync(
        PaneKey pane, ConnectionEpoch epoch, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return ValueTask.FromResult<ITerminalTransport>(
            new NoOpTerminalTransport(pane, epoch, TerminalMode.Observe));
    }

    public ValueTask<ITerminalTransport> OpenControlCandidateAsync(
        PaneKey pane, ConnectionEpoch epoch, string attemptId, TerminalTakeoverAuthorization? takeover,
        CancellationToken cancellationToken = default)
    {
        _ = (attemptId, takeover, cancellationToken);
        return ValueTask.FromResult<ITerminalTransport>(
            new NoOpTerminalTransport(pane, epoch, TerminalMode.Control));
    }
}

file sealed class NoOpTerminalRenderer : ITerminalRenderer
{
    public PaneKey? Pane { get; private set; }
    public ConnectionEpoch? Epoch { get; private set; }
    public RendererSurfaceState SurfaceState => RendererSurfaceState.Bound;

    public ValueTask BindAsync(PaneKey pane, ConnectionEpoch epoch, CancellationToken cancellationToken = default)
    {
        Pane = pane;
        Epoch = epoch;
        return ValueTask.CompletedTask;
    }

    public ValueTask<RenderApplyResult> ApplyAsync(TerminalFrame frame, CancellationToken cancellationToken = default)
    {
        _ = (frame, cancellationToken);
        return ValueTask.FromResult(new RenderApplyResult(
            true,
            new RenderConsumption(Epoch ?? new ConnectionEpoch(1), frame.Sequence),
            RendererQueueState.Ready,
            RendererSurfaceState.Observing,
            "parse_consumed"));
    }

    public async IAsyncEnumerable<RendererInput> ReadInputsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        yield break;
    }

    public ValueTask SetReadOnlyAsync(bool readOnly, CancellationToken cancellationToken = default)
    {
        _ = readOnly;
        return ValueTask.CompletedTask;
    }

    public ValueTask FocusAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

file sealed class NoOpTerminalTransport : ITerminalTransport
{
    private readonly Channel<TerminalTransportEvent> _events = Channel.CreateUnbounded<TerminalTransportEvent>();

    public NoOpTerminalTransport(PaneKey pane, ConnectionEpoch epoch, TerminalMode mode)
    {
        Pane = pane;
        Epoch = epoch;
        Mode = mode;
    }

    public PaneKey Pane { get; }
    public ConnectionEpoch Epoch { get; }
    public TerminalMode Mode { get; }

    public async IAsyncEnumerable<TerminalTransportEvent> ReadEventsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return item;
    }

    public ValueTask<TerminalWriteReceipt> SendInputAsync(
        TerminalInputCommand input, CancellationToken cancellationToken = default)
    {
        _ = (input, cancellationToken);
        return ValueTask.FromResult(new TerminalWriteReceipt(1, TerminalWriteDisposition.NotSent, "input_not_sent"));
    }

    public ValueTask<TerminalWriteReceipt> ResizeAsync(
        TerminalResizeCommand size, CancellationToken cancellationToken = default)
    {
        _ = (size, cancellationToken);
        return ValueTask.FromResult(new TerminalWriteReceipt(1, TerminalWriteDisposition.NotSent, "observe_no_resize"));
    }

    public ValueTask<TerminalWriteReceipt> ScrollAsync(
        TerminalScrollCommand request, CancellationToken cancellationToken = default)
    {
        _ = (request, cancellationToken);
        return ValueTask.FromResult(new TerminalWriteReceipt(1, TerminalWriteDisposition.NotSent, "observe_scroll_denied"));
    }

    public ValueTask<TerminalWriteReceipt> ReleaseAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return ValueTask.FromResult(new TerminalWriteReceipt(
            1, TerminalWriteDisposition.WrittenUnacknowledged, "released"));
    }

    public ValueTask DisposeAsync()
    {
        _events.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
