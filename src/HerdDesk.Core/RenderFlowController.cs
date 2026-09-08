using HerdDesk.Contracts;

namespace HerdDesk.Core;

/// <summary>
/// End-to-end in-flight window over RendererByteWindow. Ack is parse-consumed,
/// not GPU presentation. Cancel and epoch reset invalidate old tokens.
/// </summary>
public sealed class RenderFlowController : IRenderFlowController
{
    private readonly RendererByteWindow window;
    private readonly Queue<RenderToken> queued = new();
    private ConnectionEpoch epoch;
    private long generation = 1;
    private ulong? lastEnqueued;
    private ulong? lastAcked;
    private bool hasFullBaseline;
    private bool faulted;

    public RenderFlowController(
        ConnectionEpoch epoch,
        int maxInFlightBytes = RendererByteWindow.DefaultMaxInFlightBytes,
        int maxQueuedFrames = RendererByteWindow.DefaultMaxQueuedFrames)
    {
        window = new RendererByteWindow(epoch, maxInFlightBytes, maxQueuedFrames);
        this.epoch = epoch;
    }

    public ConnectionEpoch Epoch => epoch;
    public RendererQueueState State =>
        faulted ? RendererQueueState.Faulted : window.State;
    public int InFlightBytes => window.InFlightBytes;
    public bool HasFullBaseline => hasFullBaseline;

    public RendererQueueDecision TryEnqueue(
        ConnectionEpoch incoming,
        ulong sequence,
        bool full,
        int byteCount,
        out RenderToken token)
    {
        token = default;
        if (faulted)
            return Decision(false, RendererQueueState.Faulted, "terminal_stream_not_active", true);
        if (incoming != epoch)
        {
            Fault();
            return Decision(false, RendererQueueState.Faulted, "stale_epoch", true);
        }
        if (lastEnqueued is null)
        {
            if (!full)
            {
                Fault();
                return Decision(false, RendererQueueState.Faulted, "initial_full_frame_required", true);
            }
        }
        else if (lastEnqueued == ulong.MaxValue || sequence != lastEnqueued.Value + 1)
        {
            Fault();
            return Decision(false, RendererQueueState.Faulted, "sequence_gap_or_replay", true);
        }

        var queuedDecision = window.TryEnqueue(incoming, byteCount);
        if (!queuedDecision.Accepted)
            return queuedDecision;

        lastEnqueued = sequence;
        if (full)
            hasFullBaseline = true;
        token = new RenderToken(epoch, generation, sequence, byteCount);
        queued.Enqueue(token);
        return queuedDecision;
    }

    public RendererQueueDecision AcknowledgeParseConsumed(RenderToken token)
    {
        if (faulted)
            return Decision(false, RendererQueueState.Faulted, "terminal_stream_not_active", true);
        if (token.Epoch != epoch || token.Generation != generation)
            return Decision(false, State, "stale_epoch", false);
        if (queued.Count < 1 || queued.Peek() != token)
            return Decision(false, State, "queue_ack_mismatch", false);
        var ack = window.AcknowledgeParseConsumed(token.Epoch, token.Bytes);
        if (!ack.Accepted)
            return ack;
        queued.Dequeue();
        lastAcked = token.Sequence;
        return ack;
    }

    public RendererQueueDecision ClassifyDeltaDrop()
    {
        Fault();
        return window.ClassifyDeltaDrop();
    }

    public void CancelInFlight()
    {
        generation++;
        queued.Clear();
        window.Reset(epoch);
        lastEnqueued = lastAcked;
        if (lastAcked is null)
            hasFullBaseline = false;
    }

    public RendererQueueDecision Reset(ConnectionEpoch next)
    {
        if (next.Value <= 0 || next.Value < epoch.Value)
            return Decision(false, State, "stale_epoch", true);
        window.Reset(next);
        epoch = next;
        generation++;
        queued.Clear();
        lastEnqueued = null;
        lastAcked = null;
        hasFullBaseline = false;
        faulted = false;
        return Decision(true, State, "reset", false);
    }

    private void Fault() => faulted = true;

    private static RendererQueueDecision Decision(
        bool accepted, RendererQueueState state, string code, bool requiresFullReset) =>
        new(accepted, state, code, ParseConsumedIsPresented: false, requiresFullReset);
}
