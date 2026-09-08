using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class TransitionDedupTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("event dirty refresh and snapshot replay one blocked", InterleavedDedup),
        ("blocked working done keep independent transitions", SequenceSemantics),
        ("muted and rate limited stay in center", MuteAndRateLimit),
        ("device mute does not suppress the other device", DeviceMuteIsolated)
    ];

    static void InterleavedDedup()
    {
        var reducer = new AttentionReducer();
        reducer.Apply(
            AttentionHarness.One(AttentionHarness.Status(AgentStatusKind.Working)),
            AttentionHarness.Stamp(1),
            AttentionSyncKind.Baseline);
        var blocked = AttentionHarness.One(AttentionHarness.Status(AgentStatusKind.Blocked));
        var live = reducer.Apply(blocked, AttentionHarness.Stamp(1), AttentionSyncKind.Live);
        var dirty = reducer.Apply(blocked, AttentionHarness.Stamp(1), AttentionSyncKind.DirtyRefresh);
        var snapshot = reducer.Apply(blocked, AttentionHarness.Stamp(1), AttentionSyncKind.SnapshotRepeat);
        AttentionHarness.Check(live.DeliverCount == 1);
        AttentionHarness.Check(dirty.DeliverCount == 0);
        AttentionHarness.Check(snapshot.DeliverCount == 0);
        AttentionHarness.Check(dirty.Decisions.All(item => item.Reason == AttentionCodes.Duplicate));
        AttentionHarness.Check(snapshot.Decisions.All(item => item.Reason == AttentionCodes.Duplicate));
        AttentionHarness.Check(reducer.Entries.Count == 1);
        AttentionHarness.Check(reducer.UnreadTotal == 1);
    }

    static void SequenceSemantics()
    {
        var reducer = new AttentionReducer();
        reducer.Apply(
            AttentionHarness.One(AttentionHarness.Status(AgentStatusKind.Idle)),
            AttentionHarness.Stamp(1),
            AttentionSyncKind.Baseline);
        var blocked = reducer.Apply(
            AttentionHarness.One(AttentionHarness.Status(AgentStatusKind.Blocked)),
            AttentionHarness.Stamp(1),
            AttentionSyncKind.Live);
        var working = reducer.Apply(
            AttentionHarness.One(AttentionHarness.Status(AgentStatusKind.Working)),
            AttentionHarness.Stamp(1),
            AttentionSyncKind.Live);
        var done = reducer.Apply(
            AttentionHarness.One(AttentionHarness.Status(AgentStatusKind.Done)),
            AttentionHarness.Stamp(1),
            AttentionSyncKind.Live);
        AttentionHarness.Check(blocked.DeliverCount == 1);
        AttentionHarness.Check(blocked.Decisions[0].Transition.To.Kind == BusinessStateKind.Blocked);
        AttentionHarness.Check(working.DeliverCount == 0);
        AttentionHarness.Check(working.Decisions[0].Action == NotificationAction.CenterOnly);
        AttentionHarness.Check(working.Decisions[0].Transition.To.Kind == BusinessStateKind.Working);
        AttentionHarness.Check(working.Decisions[0].Transition.From.Kind == BusinessStateKind.Blocked);
        AttentionHarness.Check(done.DeliverCount == 1);
        AttentionHarness.Check(done.Decisions[0].Transition.To.Kind == BusinessStateKind.Done);
        AttentionHarness.Check(done.Decisions[0].Transition.From.Kind == BusinessStateKind.Working);
        AttentionHarness.Check(reducer.Entries.Select(item => item.TransitionId).Distinct().Count() == 3);
        AttentionHarness.Check(typeof(NotificationTarget).GetProperty("Command") is null);
        AttentionHarness.Check(typeof(NotificationTarget).GetProperty("Takeover") is null);
        AttentionHarness.Check(typeof(NotificationTarget).GetProperty("Input") is null);
    }

    static void MuteAndRateLimit()
    {
        var policy = new NotificationPolicy { MaxDeliversPerSession = 1, RateLimitWindow = TimeSpan.FromMinutes(1) };
        var reducer = new AttentionReducer(policy);
        var session = AttentionHarness.SessionOf(AttentionHarness.DeviceA);
        var paneA = AttentionHarness.Pane(AttentionHarness.DeviceA, "p1");
        var paneB = AttentionHarness.Pane(AttentionHarness.DeviceA, "p2");
        var workingA = AttentionHarness.Status(AgentStatusKind.Working);
        var workingB = AttentionHarness.Status(AgentStatusKind.Working);
        reducer.Apply(
            AttentionHarness.Many(1,
                AttentionHarness.Entity(AttentionHarness.DeviceA, session, paneA, workingA, "term-a"),
                AttentionHarness.Entity(AttentionHarness.DeviceA, session, paneB, workingB, "term-b")),
            AttentionHarness.Stamp(1),
            AttentionSyncKind.Baseline);
        reducer.SetGlobalMute(true);
        var muted = reducer.Apply(
            AttentionHarness.Many(1,
                AttentionHarness.Entity(AttentionHarness.DeviceA, session, paneA,
                    AttentionHarness.Status(AgentStatusKind.Blocked), "term-a"),
                AttentionHarness.Entity(AttentionHarness.DeviceA, session, paneB, workingB, "term-b")),
            AttentionHarness.Stamp(1),
            AttentionSyncKind.Live);
        AttentionHarness.Check(muted.DeliverCount == 0);
        AttentionHarness.Check(muted.Decisions.Any(item => item.Reason == AttentionCodes.Muted));
        AttentionHarness.Check(reducer.UnreadTotal == 1);
        reducer.SetGlobalMute(false);
        var delivered = reducer.Apply(
            AttentionHarness.Many(1,
                AttentionHarness.Entity(AttentionHarness.DeviceA, session, paneA,
                    AttentionHarness.Status(AgentStatusKind.Blocked), "term-a"),
                AttentionHarness.Entity(AttentionHarness.DeviceA, session, paneB,
                    AttentionHarness.Status(AgentStatusKind.Done), "term-b")),
            AttentionHarness.Stamp(1),
            AttentionSyncKind.Live);
        AttentionHarness.Check(delivered.DeliverCount == 1);
        AttentionHarness.Check(delivered.Decisions.Any(item =>
            item.Action == NotificationAction.Deliver && item.Transition.To.Kind == BusinessStateKind.Done));
        var limited = reducer.Apply(
            AttentionHarness.Many(1,
                AttentionHarness.Entity(AttentionHarness.DeviceA, session, paneA,
                    AttentionHarness.Status(AgentStatusKind.Done), "term-a"),
                AttentionHarness.Entity(AttentionHarness.DeviceA, session, paneB,
                    AttentionHarness.Status(AgentStatusKind.Done), "term-b")),
            AttentionHarness.Stamp(1),
            AttentionSyncKind.Live);
        AttentionHarness.Check(limited.DeliverCount == 0);
        AttentionHarness.Check(limited.Decisions.Any(item => item.Reason == AttentionCodes.RateLimited));
        AttentionHarness.Check(reducer.Entries.Count >= 2);
        foreach (var entry in reducer.Entries)
            AttentionHarness.Check(!entry.TransitionId.Contains("coder", StringComparison.Ordinal));
    }

    static void DeviceMuteIsolated()
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
        reducer.SetDeviceMute(AttentionHarness.DeviceA, true);
        var result = reducer.Apply(
            AttentionHarness.Many(1,
                AttentionHarness.Entity(AttentionHarness.DeviceA, sessionA, paneA,
                    AttentionHarness.Status(AgentStatusKind.Blocked), "term-a"),
                AttentionHarness.Entity(AttentionHarness.DeviceB, sessionB, paneB,
                    AttentionHarness.Status(AgentStatusKind.Done), "term-b")),
            AttentionHarness.Stamp(1),
            AttentionSyncKind.Live);
        var a = result.Decisions.Single(item => item.Transition.Key.Pane.Session.Device == AttentionHarness.DeviceA);
        var b = result.Decisions.Single(item => item.Transition.Key.Pane.Session.Device == AttentionHarness.DeviceB);
        AttentionHarness.Check(a.Action == NotificationAction.CenterOnly);
        AttentionHarness.Check(a.Reason == AttentionCodes.Muted);
        AttentionHarness.Check(b.Action == NotificationAction.Deliver);
        AttentionHarness.Check(b.Transition.To.Kind == BusinessStateKind.Done);
        AttentionHarness.Check(reducer.UnreadForDevice(AttentionHarness.DeviceA) == 1);
        AttentionHarness.Check(reducer.UnreadForDevice(AttentionHarness.DeviceB) == 1);
    }
}
