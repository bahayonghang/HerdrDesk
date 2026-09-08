using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class RenderFlowControllerTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("ack matches oldest frame and does not mean presented", OldestAck),
        ("slow ack backpressures without dropping", SlowAck),
        ("cancel invalidates old tokens", CancelTokens),
        ("old epoch enqueue faults", OldEpoch),
        ("reset rejects a lower epoch and drops old tokens", ResetEpoch),
        ("seq gap and missing full baseline fault", SeqAndFull),
        ("delta drop classification requires a full reset", DeltaDrop)
    ];

    static void OldestAck()
    {
        var flow = WebTestHost.Flow(8, 2);
        var epoch = WebTestHost.Epoch();
        WebTestHost.Check(flow.TryEnqueue(epoch, 1, true, 4, out var first).Accepted);
        WebTestHost.Check(flow.TryEnqueue(epoch, 2, false, 3, out var second).Accepted);
        var mismatch = flow.AcknowledgeParseConsumed(first with { Bytes = 3 });
        WebTestHost.Check(!mismatch.Accepted);
        WebTestHost.Check(mismatch.Code == "queue_ack_mismatch");
        WebTestHost.Check(flow.InFlightBytes == 7);
        var ack = flow.AcknowledgeParseConsumed(first);
        WebTestHost.Check(ack.Accepted);
        WebTestHost.Check(!ack.ParseConsumedIsPresented);
        WebTestHost.Check(flow.InFlightBytes == 3);
        WebTestHost.Check(flow.AcknowledgeParseConsumed(second).Accepted);
        WebTestHost.Check(flow.InFlightBytes == 0);
    }

    static void SlowAck()
    {
        var flow = WebTestHost.Flow(8, 2);
        var epoch = WebTestHost.Epoch();
        WebTestHost.Check(flow.TryEnqueue(epoch, 1, true, 4, out _).Accepted);
        WebTestHost.Check(flow.TryEnqueue(epoch, 2, false, 3, out _).Accepted);
        var blocked = flow.TryEnqueue(epoch, 3, false, 2, out _);
        WebTestHost.Check(!blocked.Accepted);
        WebTestHost.Check(blocked.State == RendererQueueState.Backpressured);
        WebTestHost.Check(blocked.Code == "queue_bytes_limit");
        WebTestHost.Check(!blocked.RequiresFullReset);
        WebTestHost.Check(flow.State != RendererQueueState.Ready);
        WebTestHost.Check(flow.InFlightBytes == 7);
    }

    static void CancelTokens()
    {
        var flow = WebTestHost.Flow(8, 2);
        var epoch = WebTestHost.Epoch();
        WebTestHost.Check(flow.TryEnqueue(epoch, 1, true, 4, out var token).Accepted);
        flow.CancelInFlight();
        var late = flow.AcknowledgeParseConsumed(token);
        WebTestHost.Check(!late.Accepted);
        WebTestHost.Check(late.Code == "stale_epoch");
        WebTestHost.Check(flow.InFlightBytes == 0);
        WebTestHost.Check(flow.TryEnqueue(epoch, 1, true, 4, out var next).Accepted);
        WebTestHost.Check(flow.AcknowledgeParseConsumed(next).Accepted);
    }

    static void OldEpoch()
    {
        var flow = WebTestHost.Flow();
        var decision = flow.TryEnqueue(WebTestHost.Epoch(2), 1, true, 4, out _);
        WebTestHost.Check(!decision.Accepted);
        WebTestHost.Check(decision.Code == "stale_epoch");
        WebTestHost.Check(decision.RequiresFullReset);
        WebTestHost.Check(flow.State == RendererQueueState.Faulted);
    }

    static void ResetEpoch()
    {
        var flow = WebTestHost.Flow(8, 2);
        var epoch = WebTestHost.Epoch();
        WebTestHost.Check(flow.TryEnqueue(epoch, 1, true, 4, out var token).Accepted);
        var rejected = flow.Reset(WebTestHost.Epoch(0));
        WebTestHost.Check(!rejected.Accepted);
        WebTestHost.Check(rejected.Code == "stale_epoch");
        WebTestHost.Check(flow.InFlightBytes == 4);
        flow.Reset(WebTestHost.Epoch(2));
        WebTestHost.Check(flow.InFlightBytes == 0);
        WebTestHost.Check(!flow.AcknowledgeParseConsumed(token).Accepted);
        WebTestHost.Check(flow.AcknowledgeParseConsumed(token).Code == "stale_epoch");
        WebTestHost.Check(flow.TryEnqueue(WebTestHost.Epoch(2), 1, true, 4, out var next).Accepted);
        WebTestHost.Check(flow.AcknowledgeParseConsumed(next).Accepted);
    }

    static void SeqAndFull()
    {
        var missing = WebTestHost.Flow();
        var delta = missing.TryEnqueue(WebTestHost.Epoch(), 1, false, 4, out _);
        WebTestHost.Check(!delta.Accepted);
        WebTestHost.Check(delta.Code == "initial_full_frame_required");
        WebTestHost.Check(delta.RequiresFullReset);
        var flow = WebTestHost.Flow();
        WebTestHost.Check(flow.TryEnqueue(WebTestHost.Epoch(), 1, true, 4, out _).Accepted);
        var gap = flow.TryEnqueue(WebTestHost.Epoch(), 3, false, 1, out _);
        WebTestHost.Check(!gap.Accepted);
        WebTestHost.Check(gap.Code == "sequence_gap_or_replay");
        WebTestHost.Check(gap.RequiresFullReset);
        WebTestHost.Check(flow.State == RendererQueueState.Faulted);
    }

    static void DeltaDrop()
    {
        var flow = WebTestHost.Flow();
        var drop = flow.ClassifyDeltaDrop();
        WebTestHost.Check(!drop.Accepted);
        WebTestHost.Check(drop.Code == "delta_drop_forbidden");
        WebTestHost.Check(drop.RequiresFullReset);
        WebTestHost.Check(flow.State == RendererQueueState.Faulted);
        WebTestHost.Check(!flow.TryEnqueue(WebTestHost.Epoch(), 1, true, 4, out _).Accepted);
    }
}
