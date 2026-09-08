using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class StaleAndUnknownTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("stale device does not emit running or success toasts", StaleIsolated),
        ("unknown raw stays unknown and not idle", UnknownPreserved),
        ("old epoch after reconnect is suppressed", OldEpochSuppressed),
        ("old epoch snapshot does not expire current entries", OldEpochDoesNotExpireCurrent)
    ];

    static void StaleIsolated()
    {
        var reducer = new AttentionReducer();
        var sessionA = AttentionHarness.SessionOf(AttentionHarness.DeviceA);
        var sessionB = AttentionHarness.SessionOf(AttentionHarness.DeviceB);
        var paneA = AttentionHarness.Pane(AttentionHarness.DeviceA);
        var paneB = AttentionHarness.Pane(AttentionHarness.DeviceB);
        var working = AttentionHarness.Status(AgentStatusKind.Working);
        reducer.Apply(
            AttentionHarness.Many(1,
                AttentionHarness.Entity(AttentionHarness.DeviceA, sessionA, paneA, working, "term-a"),
                AttentionHarness.Entity(AttentionHarness.DeviceB, sessionB, paneB, working, "term-b")),
            AttentionHarness.Stamp(1),
            AttentionSyncKind.Baseline);
        reducer.MarkDeviceStale(AttentionHarness.DeviceA);
        var result = reducer.Apply(
            AttentionHarness.Many(1,
                AttentionHarness.Entity(AttentionHarness.DeviceA, sessionA, paneA,
                    AttentionHarness.Status(AgentStatusKind.Done), "term-a"),
                AttentionHarness.Entity(AttentionHarness.DeviceB, sessionB, paneB,
                    AttentionHarness.Status(AgentStatusKind.Blocked), "term-b")),
            AttentionHarness.Stamp(1),
            AttentionSyncKind.Live);
        var a = result.Decisions.Where(item => item.Transition.Key.Pane.Session.Device == AttentionHarness.DeviceA)
            .ToArray();
        var b = result.Decisions.Where(item => item.Transition.Key.Pane.Session.Device == AttentionHarness.DeviceB)
            .ToArray();
        AttentionHarness.Check(a.Length == 1);
        AttentionHarness.Check(a[0].Action == NotificationAction.Suppress);
        AttentionHarness.Check(a[0].Reason == AttentionCodes.Stale);
        AttentionHarness.Check(a[0].Transition.To.Kind == BusinessStateKind.Done);
        AttentionHarness.Check(b.Length == 1);
        AttentionHarness.Check(b[0].Action == NotificationAction.Deliver);
        AttentionHarness.Check(b[0].Transition.To.Kind == BusinessStateKind.Blocked);
        foreach (var entry in reducer.Entries.Where(item => item.Key.Pane.Session.Device == AttentionHarness.DeviceA))
            AttentionHarness.Check(entry.Freshness is AttentionFreshness.Stale or AttentionFreshness.Expired);
    }

    static void UnknownPreserved()
    {
        var reducer = new AttentionReducer();
        reducer.Apply(
            AttentionHarness.One(AttentionHarness.Status(AgentStatusKind.Working)),
            AttentionHarness.Stamp(1),
            AttentionSyncKind.Baseline);
        var raw = "muse-spin";
        var result = reducer.Apply(
            AttentionHarness.One(AttentionHarness.Unknown(raw)),
            AttentionHarness.Stamp(1),
            AttentionSyncKind.Live);
        AttentionHarness.Check(result.Decisions.Count == 1);
        AttentionHarness.Check(result.Decisions[0].Transition.To.Kind == BusinessStateKind.Unknown);
        AttentionHarness.Check(result.Decisions[0].Transition.To.Raw == raw);
        AttentionHarness.Check(result.Decisions[0].Transition.To.Kind != BusinessStateKind.Idle);
        AttentionHarness.Check(result.Decisions[0].Action == NotificationAction.CenterOnly);
        AttentionHarness.Check(BusinessState.From(new WireEnum<AgentStatusKind>(raw, null)).Kind ==
                               BusinessStateKind.Unknown);
    }

    static void OldEpochSuppressed()
    {
        var reducer = new AttentionReducer();
        reducer.Apply(
            AttentionHarness.One(AttentionHarness.Status(AgentStatusKind.Working), epoch: 2),
            AttentionHarness.Stamp(2),
            AttentionSyncKind.Baseline);
        var old = reducer.Apply(
            AttentionHarness.One(AttentionHarness.Status(AgentStatusKind.Done), epoch: 1),
            AttentionHarness.Stamp(1),
            AttentionSyncKind.Live);
        AttentionHarness.Check(old.DeliverCount == 0);
        AttentionHarness.Check(old.Decisions.Count >= 1);
        AttentionHarness.Check(old.Decisions.All(item => item.Reason == AttentionCodes.OldEpoch));
        var live = reducer.Apply(
            AttentionHarness.One(AttentionHarness.Status(AgentStatusKind.Blocked), epoch: 2),
            AttentionHarness.Stamp(2),
            AttentionSyncKind.Live);
        AttentionHarness.Check(live.DeliverCount == 1);
    }

    static void OldEpochDoesNotExpireCurrent()
    {
        var reducer = new AttentionReducer();
        reducer.Apply(
            AttentionHarness.One(AttentionHarness.Status(AgentStatusKind.Working), epoch: 2),
            AttentionHarness.Stamp(2),
            AttentionSyncKind.Baseline);
        var live = reducer.Apply(
            AttentionHarness.One(AttentionHarness.Status(AgentStatusKind.Blocked), epoch: 2),
            AttentionHarness.Stamp(2),
            AttentionSyncKind.Live);
        AttentionHarness.Check(live.DeliverCount == 1);
        AttentionHarness.Check(reducer.Entries.Count == 1);
        AttentionHarness.Check(!reducer.Entries[0].IsExpired);
        var old = reducer.Apply(
            AttentionHarness.One(AttentionHarness.Status(AgentStatusKind.Done), epoch: 1),
            AttentionHarness.Stamp(1),
            AttentionSyncKind.Live);
        AttentionHarness.Check(old.DeliverCount == 0);
        AttentionHarness.Check(old.Decisions.All(item => item.Reason == AttentionCodes.OldEpoch));
        AttentionHarness.Check(reducer.Entries.Count == 1);
        AttentionHarness.Check(!reducer.Entries[0].IsExpired);
        AttentionHarness.Check(reducer.Entries[0].To.Kind == BusinessStateKind.Blocked);
        AttentionHarness.Check(reducer.UnreadTotal == 1);
        var again = reducer.Apply(
            AttentionHarness.One(AttentionHarness.Status(AgentStatusKind.Done), epoch: 2),
            AttentionHarness.Stamp(2),
            AttentionSyncKind.Live);
        AttentionHarness.Check(again.DeliverCount == 1);
        AttentionHarness.Check(again.Decisions[0].Transition.From.Kind == BusinessStateKind.Blocked);
    }
}
