using System.Reflection;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class TerminalQueueBudgetTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("twenty four mebibytes enqueue and one extra byte is rejected", Boundary),
        ("ack releases only the monotonic next sequence", MonotonicAck),
        ("old epoch rollback and duplicate ack do not release", StaleAck),
        ("overflow does not drop a queued delta", OverflowKeepsQueued),
        ("single frame limit stays with the parser owner", FrameLimit),
        ("queue budget does not host a dirty set", NoDirtySet)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static ConnectionEpoch Epoch(long value) => new(value);

    static void Boundary()
    {
        var budget = new TerminalQueueBudget(Epoch(1));
        var frame = TerminalFrameParser.MaxFrameBytes;
        Check(budget.TryEnqueue(Epoch(1), 1, frame).Accepted);
        Check(budget.TryEnqueue(Epoch(1), 2, frame).Accepted);
        Check(budget.TryEnqueue(Epoch(1), 3, frame).Accepted);
        Check(budget.QueuedBytes == TerminalQueueBudget.MaxQueuedBytes);
        var extra = budget.TryEnqueue(Epoch(1), 4, 1);
        Check(!extra.Accepted);
        Check(extra.Code == ResourceBudgetCodes.TerminalQueueLimit);
        Check(extra.Overloaded);
        Check(extra.Stale);
        Check(extra.RequiresObserveReset);
        Check(budget.QueuedBytes == TerminalQueueBudget.MaxQueuedBytes);
    }

    static void MonotonicAck()
    {
        var budget = new TerminalQueueBudget(Epoch(1));
        Check(budget.TryEnqueue(Epoch(1), 1, 10).Accepted);
        Check(budget.TryEnqueue(Epoch(1), 2, 20).Accepted);
        var first = budget.Acknowledge(Epoch(1), 1);
        Check(first.Accepted);
        Check(budget.QueuedBytes == 20);
        var skip = budget.Acknowledge(Epoch(1), 3);
        Check(!skip.Accepted);
        Check(skip.Code == ResourceBudgetCodes.StaleRenderAck);
        Check(budget.QueuedBytes == 20);
        var second = budget.AcknowledgeParseConsumed(new RenderToken(Epoch(1), 1, 2, 20));
        Check(second.Accepted);
        Check(budget.QueuedBytes == 0);
    }

    static void StaleAck()
    {
        var budget = new TerminalQueueBudget(Epoch(1));
        Check(budget.TryEnqueue(Epoch(1), 1, 8).Accepted);
        Check(budget.TryEnqueue(Epoch(1), 2, 4).Accepted);
        Check(budget.Acknowledge(Epoch(1), 1).Accepted);
        var dup = budget.Acknowledge(Epoch(1), 1);
        Check(!dup.Accepted);
        Check(dup.Code == ResourceBudgetCodes.StaleRenderAck);
        Check(budget.QueuedBytes == 4);
        var rollback = budget.Acknowledge(Epoch(1), 0);
        Check(!rollback.Accepted);
        Check(budget.QueuedBytes == 4);
        budget.Reset(Epoch(2));
        Check(budget.TryEnqueue(Epoch(2), 1, 16).Accepted);
        var oldEpoch = budget.Acknowledge(Epoch(1), 1);
        Check(!oldEpoch.Accepted);
        Check(oldEpoch.Code == ResourceBudgetCodes.StaleRenderAck);
        Check(budget.QueuedBytes == 16);
        try
        {
            budget.Reset(Epoch(2));
            throw new Exception("expected_stale_epoch");
        }
        catch (TerminalProtocolException error) when (error.Message == ResourceBudgetCodes.StaleEpoch)
        {
        }

        Check(budget.QueuedBytes == 16);
        try
        {
            budget.Reset(Epoch(1));
            throw new Exception("expected_stale_epoch");
        }
        catch (TerminalProtocolException error) when (error.Message == ResourceBudgetCodes.StaleEpoch)
        {
        }

        Check(budget.QueuedBytes == 16);
    }

    static void OverflowKeepsQueued()
    {
        var budget = new TerminalQueueBudget(Epoch(1));
        var frame = TerminalFrameParser.MaxFrameBytes;
        Check(budget.TryEnqueue(Epoch(1), 1, frame).Accepted);
        Check(budget.TryEnqueue(Epoch(1), 2, frame).Accepted);
        Check(budget.TryEnqueue(Epoch(1), 3, frame - 1).Accepted);
        var before = budget.QueuedBytes;
        var overflow = budget.TryEnqueue(Epoch(1), 4, 2);
        Check(!overflow.Accepted);
        Check(overflow.Code == ResourceBudgetCodes.TerminalQueueLimit);
        Check(overflow.Overloaded);
        Check(budget.QueuedBytes == before);
        var drop = budget.ClassifyDeltaDrop();
        Check(drop.Code == ResourceBudgetCodes.DeltaDropForbidden);
        Check(drop.RequiresObserveReset);
        Check(budget.QueuedBytes == before);
    }

    static void FrameLimit()
    {
        var budget = new TerminalQueueBudget(Epoch(1));
        var over = budget.TryEnqueue(Epoch(1), 1, TerminalFrameParser.MaxFrameBytes + 1);
        Check(!over.Accepted);
        Check(over.Code == "decoded_bytes_limit");
        Check(!over.Overloaded);
        Check(budget.QueuedBytes == 0);
    }

    static void NoDirtySet()
    {
        foreach (var type in new[] { typeof(TerminalQueueBudget), typeof(RendererByteWindow), typeof(RenderFlowController) })
        {
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                        BindingFlags.NonPublic);
            foreach (var field in fields)
                Check(field.FieldType != typeof(DirtySetBudget));
        }
    }
}
