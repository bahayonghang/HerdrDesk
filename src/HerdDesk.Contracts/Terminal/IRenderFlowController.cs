namespace HerdDesk.Contracts;

public interface IRenderFlowController
{
    ConnectionEpoch Epoch { get; }
    RendererQueueState State { get; }
    int InFlightBytes { get; }
    bool HasFullBaseline { get; }

    RendererQueueDecision TryEnqueue(
        ConnectionEpoch epoch,
        ulong sequence,
        bool full,
        int byteCount,
        out RenderToken token);

    RendererQueueDecision AcknowledgeParseConsumed(RenderToken token);

    RendererQueueDecision ClassifyDeltaDrop();

    void CancelInFlight();

    RendererQueueDecision Reset(ConnectionEpoch next);
}
