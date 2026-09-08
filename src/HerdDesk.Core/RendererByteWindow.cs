using HerdDesk.Contracts;

namespace HerdDesk.Core;

/// <summary>
/// Bounded in-flight FIFO of frame sizes. Parse-consumed is not GPU
/// presentation. Overflow must not drop a delta and stay Ready. Recovery
/// is a full reset. A lower epoch is stale; the same epoch may rebuild.
/// </summary>
public sealed class RendererByteWindow
{
    public const int DefaultMaxInFlightBytes = 256 * 1024;
    public const int DefaultMaxQueuedFrames = 8;

    private readonly int maxBytes;
    private readonly int maxFrames;
    private readonly Queue<int> queued = new();
    private ConnectionEpoch epoch;
    private int inFlightBytes;
    private bool faulted;

    public RendererByteWindow(
        ConnectionEpoch epoch,
        int maxInFlightBytes = DefaultMaxInFlightBytes,
        int maxQueuedFrames = DefaultMaxQueuedFrames)
    {
        if (epoch.Value <= 0)
            throw new TerminalProtocolException("stale_epoch");
        if (maxInFlightBytes < 1 || maxQueuedFrames < 1)
            throw new ArgumentOutOfRangeException(nameof(maxInFlightBytes));
        this.epoch = epoch;
        maxBytes = maxInFlightBytes;
        maxFrames = maxQueuedFrames;
    }

    public int InFlightBytes => inFlightBytes;
    public RendererQueueState State =>
        faulted ? RendererQueueState.Faulted
        : AtCapacity ? RendererQueueState.Backpressured
        : RendererQueueState.Ready;

    public RendererQueueDecision TryEnqueue(ConnectionEpoch incoming, int byteCount)
    {
        if (faulted)
            return Decision(false, RendererQueueState.Faulted, "terminal_stream_not_active", true);
        if (incoming != epoch)
        {
            faulted = true;
            return Decision(false, RendererQueueState.Faulted, "stale_epoch", true);
        }
        if (byteCount is <= 0 or > TerminalFrameParser.MaxFrameBytes)
            return Decision(false, State, "decoded_bytes_limit", false);
        if (inFlightBytes + byteCount > maxBytes || queued.Count + 1 > maxFrames)
            return Decision(false, RendererQueueState.Backpressured, "queue_bytes_limit", false);
        queued.Enqueue(byteCount);
        inFlightBytes += byteCount;
        return Decision(true, State, "queued", false);
    }

    public RendererQueueDecision AcknowledgeParseConsumed(ConnectionEpoch incoming, int byteCount)
    {
        if (faulted)
            return Decision(false, RendererQueueState.Faulted, "terminal_stream_not_active", true);
        if (incoming != epoch)
            return Decision(false, State, "stale_epoch", false);
        if (queued.Count < 1 || byteCount != queued.Peek())
            return Decision(false, State, "queue_ack_mismatch", false);
        queued.Dequeue();
        inFlightBytes -= byteCount;
        return Decision(true, State, "parse_consumed", false);
    }

    public RendererQueueDecision ClassifyDeltaDrop()
    {
        faulted = true;
        return Decision(false, RendererQueueState.Faulted, "delta_drop_forbidden", true);
    }

    public void Reset(ConnectionEpoch next)
    {
        if (next.Value <= 0 || next.Value < epoch.Value)
            throw new TerminalProtocolException("stale_epoch");
        epoch = next;
        queued.Clear();
        inFlightBytes = 0;
        faulted = false;
    }

    private bool AtCapacity => inFlightBytes >= maxBytes || queued.Count >= maxFrames;

    private static RendererQueueDecision Decision(
        bool accepted, RendererQueueState state, string code, bool requiresFullReset) =>
        new(accepted, state, code, ParseConsumedIsPresented: false, requiresFullReset);
}
