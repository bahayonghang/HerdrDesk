using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class BaselineSuppressionTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("initial snapshot does not replay historical done", InitialSnapshot),
        ("one hundred reconnects do not replay done or blocked", HundredReconnects),
        ("begin baseline then live blocked delivers once", BaselineThenLive)
    ];

    static void InitialSnapshot()
    {
        var reducer = new AttentionReducer();
        var done = AttentionHarness.One(AttentionHarness.Status(AgentStatusKind.Done));
        var result = reducer.Apply(done, AttentionHarness.Stamp(1), AttentionSyncKind.Baseline);
        AttentionHarness.Check(result.DeliverCount == 0);
        AttentionHarness.Check(reducer.UnreadTotal == 0);
        AttentionHarness.Check(reducer.Entries.Count == 0);
        foreach (var decision in result.Decisions)
            AttentionHarness.Check(decision.Action == NotificationAction.Suppress);
    }

    static void HundredReconnects()
    {
        var reducer = new AttentionReducer();
        var session = AttentionHarness.SessionOf(AttentionHarness.DeviceA);
        var delivers = 0;
        for (var i = 1; i <= 100; i++)
        {
            reducer.BeginBaseline(session, new ConnectionEpoch(i));
            var snapshot = AttentionHarness.One(
                i % 2 == 0
                    ? AttentionHarness.Status(AgentStatusKind.Done)
                    : AttentionHarness.Status(AgentStatusKind.Blocked),
                epoch: i);
            var kind = i == 1 ? AttentionSyncKind.Baseline : AttentionSyncKind.ReconnectBaseline;
            var result = reducer.Apply(snapshot, AttentionHarness.Stamp(i), kind);
            delivers += result.DeliverCount;
            foreach (var decision in result.Decisions)
            {
                AttentionHarness.Check(decision.Action == NotificationAction.Suppress);
                AttentionHarness.Check(i == 1
                    ? decision.Reason == AttentionCodes.Baseline
                    : decision.Reason == AttentionCodes.Reconnect);
            }
        }

        AttentionHarness.Check(delivers == 0);
        AttentionHarness.Check(reducer.UnreadTotal == 0);
    }

    static void BaselineThenLive()
    {
        var reducer = new AttentionReducer();
        var session = AttentionHarness.SessionOf(AttentionHarness.DeviceA);
        reducer.BeginBaseline(session, new ConnectionEpoch(1));
        var idle = AttentionHarness.One(AttentionHarness.Status(AgentStatusKind.Idle));
        var baseline = reducer.Apply(idle, AttentionHarness.Stamp(1), AttentionSyncKind.Baseline);
        AttentionHarness.Check(baseline.DeliverCount == 0);
        var blocked = AttentionHarness.One(AttentionHarness.Status(AgentStatusKind.Blocked));
        var live = reducer.Apply(blocked, AttentionHarness.Stamp(1), AttentionSyncKind.Live);
        AttentionHarness.Check(live.DeliverCount == 1);
        AttentionHarness.Check(live.Decisions[0].Transition.To.Kind == BusinessStateKind.Blocked);
        AttentionHarness.Check(live.Decisions[0].Transition.IsBaseline is false);
        AttentionHarness.Check(reducer.UnreadTotal == 1);
    }
}
