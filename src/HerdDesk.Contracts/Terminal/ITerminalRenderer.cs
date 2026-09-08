namespace HerdDesk.Contracts;

public interface ITerminalRenderer : IAsyncDisposable
{
    PaneKey? Pane { get; }
    ConnectionEpoch? Epoch { get; }
    RendererSurfaceState SurfaceState { get; }

    ValueTask BindAsync(PaneKey pane, ConnectionEpoch epoch, CancellationToken cancellationToken = default);

    // Completion is parser consumption, not GPU presentation or remote execution.
    ValueTask<RenderApplyResult> ApplyAsync(
        TerminalFrame frame,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<RendererInput> ReadInputsAsync(CancellationToken cancellationToken = default);

    ValueTask SetReadOnlyAsync(bool readOnly, CancellationToken cancellationToken = default);

    ValueTask FocusAsync(CancellationToken cancellationToken = default);
}
