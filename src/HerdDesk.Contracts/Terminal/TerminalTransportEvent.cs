namespace HerdDesk.Contracts;

public abstract record TerminalTransportEvent(
    long EventId,
    PaneKey Pane,
    ConnectionEpoch Epoch);

public sealed class TerminalOwnedFrame : IDisposable
{
    private readonly byte[] _buffer;
    private Action<int>? _release;

    public TerminalOwnedFrame(
        PaneKey pane,
        ConnectionEpoch epoch,
        ulong sequence,
        ushort columns,
        ushort rows,
        bool full,
        byte[] buffer,
        Action<int>? release = null)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Pane = pane;
        Epoch = epoch;
        Sequence = sequence;
        Columns = columns;
        Rows = rows;
        Full = full;
        _buffer = buffer;
        _release = release;
    }

    public PaneKey Pane { get; }
    public ConnectionEpoch Epoch { get; }
    public ulong Sequence { get; }
    public ushort Columns { get; }
    public ushort Rows { get; }
    public bool Full { get; }
    public ReadOnlyMemory<byte> Bytes => _buffer;

    public void Dispose()
    {
        var release = Interlocked.Exchange(ref _release, null);
        release?.Invoke(_buffer.Length);
    }
}

public sealed record TerminalFrameArrived(
    long EventId,
    PaneKey Pane,
    ConnectionEpoch Epoch,
    TerminalOwnedFrame Frame) : TerminalTransportEvent(EventId, Pane, Epoch), IDisposable
{
    public void Dispose() => Frame.Dispose();
}

public sealed record TerminalClosedObserved(
    long EventId,
    PaneKey Pane,
    ConnectionEpoch Epoch,
    bool ReasonPresent,
    string Classification) : TerminalTransportEvent(EventId, Pane, Epoch);

public sealed record TerminalStdoutEnded(
    long EventId,
    PaneKey Pane,
    ConnectionEpoch Epoch,
    bool Truncated) : TerminalTransportEvent(EventId, Pane, Epoch);

public sealed record TerminalProcessExited(
    long EventId,
    PaneKey Pane,
    ConnectionEpoch Epoch,
    int? ExitCode) : TerminalTransportEvent(EventId, Pane, Epoch);

public sealed record TerminalProtocolFailed(
    long EventId,
    PaneKey Pane,
    ConnectionEpoch Epoch,
    string Code) : TerminalTransportEvent(EventId, Pane, Epoch);

public sealed record TerminalConsumerBackpressure(
    long EventId,
    PaneKey Pane,
    ConnectionEpoch Epoch,
    string Code) : TerminalTransportEvent(EventId, Pane, Epoch);

public sealed record TerminalOwnershipObserved(
    long EventId,
    PaneKey Pane,
    ConnectionEpoch Epoch,
    string? ControlAttemptId,
    TerminalLeaseResult Result) : TerminalTransportEvent(EventId, Pane, Epoch);

public sealed record TerminalTransportEnded(
    long EventId,
    PaneKey Pane,
    ConnectionEpoch Epoch,
    string Code,
    int? ExitCode,
    bool SawClosedEnvelope) : TerminalTransportEvent(EventId, Pane, Epoch);
