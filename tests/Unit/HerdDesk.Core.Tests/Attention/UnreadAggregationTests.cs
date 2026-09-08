using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class UnreadAggregationTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("unread aggregates pane session device without namesake leak", PreciseClear),
        ("attention key uses pane plus entity not title", KeyIsNotTitle),
        ("removed device expires without clearing the other device", RemovedDeviceExpires)
    ];

    static void PreciseClear()
    {
        var reducer = new AttentionReducer();
        var sessionA = AttentionHarness.SessionOf(AttentionHarness.DeviceA);
        var sessionB = AttentionHarness.SessionOf(AttentionHarness.DeviceB);
        var paneA1 = AttentionHarness.Pane(AttentionHarness.DeviceA, "p1");
        var paneA2 = AttentionHarness.Pane(AttentionHarness.DeviceA, "p2");
        var paneB1 = AttentionHarness.Pane(AttentionHarness.DeviceB, "p1");
        var idle = AttentionHarness.Status(AgentStatusKind.Idle);
        var blocked = AttentionHarness.Status(AgentStatusKind.Blocked);
        reducer.Apply(
            AttentionHarness.Many(1,
                AttentionHarness.Entity(AttentionHarness.DeviceA, sessionA, paneA1, idle, "term-a1"),
                AttentionHarness.Entity(AttentionHarness.DeviceA, sessionA, paneA2, idle, "term-a2"),
                AttentionHarness.Entity(AttentionHarness.DeviceB, sessionB, paneB1, idle, "term-b1")),
            AttentionHarness.Stamp(1),
            AttentionSyncKind.Baseline);
        reducer.Apply(
            AttentionHarness.Many(1,
                AttentionHarness.Entity(AttentionHarness.DeviceA, sessionA, paneA1, blocked, "term-a1"),
                AttentionHarness.Entity(AttentionHarness.DeviceA, sessionA, paneA2, blocked, "term-a2"),
                AttentionHarness.Entity(AttentionHarness.DeviceB, sessionB, paneB1, blocked, "term-b1")),
            AttentionHarness.Stamp(1),
            AttentionSyncKind.Live);
        AttentionHarness.Check(reducer.UnreadForPane(paneA1) == 1);
        AttentionHarness.Check(reducer.UnreadForPane(paneA2) == 1);
        AttentionHarness.Check(reducer.UnreadForSession(sessionA) == 2);
        AttentionHarness.Check(reducer.UnreadForDevice(AttentionHarness.DeviceA) == 2);
        AttentionHarness.Check(reducer.UnreadForDevice(AttentionHarness.DeviceB) == 1);
        AttentionHarness.Check(reducer.UnreadTotal == 3);
        reducer.MarkScopeRead(new AttentionScope(AttentionScopeKind.Pane, Pane: paneA1));
        AttentionHarness.Check(reducer.UnreadForPane(paneA1) == 0);
        AttentionHarness.Check(reducer.UnreadForPane(paneA2) == 1);
        AttentionHarness.Check(reducer.UnreadForSession(sessionA) == 1);
        AttentionHarness.Check(reducer.UnreadForDevice(AttentionHarness.DeviceB) == 1);
        reducer.MarkScopeRead(new AttentionScope(AttentionScopeKind.Session, Session: sessionA));
        AttentionHarness.Check(reducer.UnreadForSession(sessionA) == 0);
        AttentionHarness.Check(reducer.UnreadForDevice(AttentionHarness.DeviceB) == 1);
        reducer.MarkScopeRead(new AttentionScope(AttentionScopeKind.Device, Device: AttentionHarness.DeviceB));
        AttentionHarness.Check(reducer.UnreadTotal == 0);
    }

    static void KeyIsNotTitle()
    {
        var reducer = new AttentionReducer();
        var session = AttentionHarness.SessionOf(AttentionHarness.DeviceA);
        var paneA = AttentionHarness.Pane(AttentionHarness.DeviceA, "p1");
        var paneB = AttentionHarness.Pane(AttentionHarness.DeviceA, "p2");
        var idle = AttentionHarness.Status(AgentStatusKind.Idle);
        reducer.Apply(
            AttentionHarness.Many(1,
                AttentionHarness.Entity(AttentionHarness.DeviceA, session, paneA, idle, "term-a"),
                AttentionHarness.Entity(AttentionHarness.DeviceA, session, paneB, idle, "term-b")),
            AttentionHarness.Stamp(1),
            AttentionSyncKind.Baseline);
        reducer.Apply(
            AttentionHarness.Many(1,
                AttentionHarness.Entity(AttentionHarness.DeviceA, session, paneA,
                    AttentionHarness.Status(AgentStatusKind.Blocked), "term-a"),
                AttentionHarness.Entity(AttentionHarness.DeviceA, session, paneB,
                    AttentionHarness.Status(AgentStatusKind.Done), "term-b")),
            AttentionHarness.Stamp(1),
            AttentionSyncKind.Live);
        var keys = reducer.Entries.Select(item => item.Key).ToArray();
        AttentionHarness.Check(keys.Length == 2);
        AttentionHarness.Check(keys[0] != keys[1]);
        AttentionHarness.Check(keys.All(item => item.EntityKind == AttentionKey.AgentEntityKind));
        AttentionHarness.Check(keys.All(item => item.EntityId.StartsWith("term-", StringComparison.Ordinal)));
        AttentionHarness.Check(keys.All(item => item.Pane.PaneId is "p1" or "p2"));
        var title = typeof(AttentionKey).GetProperty("Title");
        AttentionHarness.Check(title is null);
    }

    static void RemovedDeviceExpires()
    {
        var reducer = new AttentionReducer();
        var sessionA = AttentionHarness.SessionOf(AttentionHarness.DeviceA);
        var sessionB = AttentionHarness.SessionOf(AttentionHarness.DeviceB);
        var paneA = AttentionHarness.Pane(AttentionHarness.DeviceA, "p1");
        var paneB = AttentionHarness.Pane(AttentionHarness.DeviceB, "p1");
        var idle = AttentionHarness.Status(AgentStatusKind.Idle);
        var blocked = AttentionHarness.Status(AgentStatusKind.Blocked);
        reducer.Apply(
            AttentionHarness.Many(1,
                AttentionHarness.Entity(AttentionHarness.DeviceA, sessionA, paneA, idle, "term-a"),
                AttentionHarness.Entity(AttentionHarness.DeviceB, sessionB, paneB, idle, "term-b")),
            AttentionHarness.Stamp(1),
            AttentionSyncKind.Baseline);
        reducer.Apply(
            AttentionHarness.Many(1,
                AttentionHarness.Entity(AttentionHarness.DeviceA, sessionA, paneA, blocked, "term-a"),
                AttentionHarness.Entity(AttentionHarness.DeviceB, sessionB, paneB, blocked, "term-b")),
            AttentionHarness.Stamp(1),
            AttentionSyncKind.Live);
        AttentionHarness.Check(reducer.UnreadForDevice(AttentionHarness.DeviceA) == 1);
        AttentionHarness.Check(reducer.UnreadForDevice(AttentionHarness.DeviceB) == 1);
        reducer.Apply(
            AttentionHarness.Many(1,
                AttentionHarness.Entity(AttentionHarness.DeviceB, sessionB, paneB, blocked, "term-b")),
            AttentionHarness.Stamp(1),
            AttentionSyncKind.Live);
        var fromA = reducer.Entries.Where(item => item.Key.Pane.Session.Device == AttentionHarness.DeviceA)
            .ToArray();
        var fromB = reducer.Entries.Where(item => item.Key.Pane.Session.Device == AttentionHarness.DeviceB)
            .ToArray();
        AttentionHarness.Check(fromA.Length == 1);
        AttentionHarness.Check(fromA[0].IsExpired);
        AttentionHarness.Check(fromA[0].Key.Pane == paneA);
        AttentionHarness.Check(fromA[0].Key.Pane != paneB);
        AttentionHarness.Check(fromB.Length == 1);
        AttentionHarness.Check(!fromB[0].IsExpired);
        AttentionHarness.Check(fromB[0].Key.Pane == paneB);
        AttentionHarness.Check(reducer.UnreadForDevice(AttentionHarness.DeviceB) == 1);
        reducer.MarkScopeRead(new AttentionScope(AttentionScopeKind.Device, Device: AttentionHarness.DeviceA));
        AttentionHarness.Check(reducer.UnreadForDevice(AttentionHarness.DeviceB) == 1);
    }
}
