namespace HerdDesk.Contracts;

public interface ITerminalTransport : IAsyncDisposable
{
    PaneKey Pane { get; }
    ConnectionEpoch Epoch { get; }
    TerminalMode Mode { get; }
    IAsyncEnumerable<TerminalTransportEvent> ReadEventsAsync(
        CancellationToken cancellationToken = default);
    ValueTask<TerminalWriteReceipt> SendInputAsync(
        TerminalInputCommand input,
        CancellationToken cancellationToken = default);
    ValueTask<TerminalWriteReceipt> ResizeAsync(
        TerminalResizeCommand size,
        CancellationToken cancellationToken = default);
    ValueTask<TerminalWriteReceipt> ScrollAsync(
        TerminalScrollCommand request,
        CancellationToken cancellationToken = default);
    ValueTask<TerminalWriteReceipt> ReleaseAsync(
        CancellationToken cancellationToken = default);
}
