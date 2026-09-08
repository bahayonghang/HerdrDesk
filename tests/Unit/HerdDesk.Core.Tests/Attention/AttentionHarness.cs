using System.Collections.Frozen;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class AttentionHarness
{
    public static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    public static DeviceId DeviceA { get; } = new(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
    public static DeviceId DeviceB { get; } = new(Guid.Parse("11111111-2222-3333-4444-555555555555"));

    public static SessionKey SessionOf(DeviceId device, string name = "dev") =>
        new(device, "named-session", name);

    public static PaneKey Pane(DeviceId device, string paneId = "p1", string workspace = "ws", string name = "dev") =>
        new(SessionOf(device, name), workspace, paneId);

    public static WireEnum<AgentStatusKind> Status(AgentStatusKind kind, string? raw = null) =>
        new(raw ?? kind.ToString().ToLowerInvariant(), kind);

    public static WireEnum<AgentStatusKind> Unknown(string raw) => new(raw, AgentStatusKind.Unknown);

    public static ProjectionStamp Stamp(long epoch, long generation = 0) =>
        new(new ConnectionEpoch(epoch), generation, DateTimeOffset.UnixEpoch);

    public static DeviceProjectionSnapshot One(
        WireEnum<AgentStatusKind> status,
        long epoch = 1,
        DeviceId? device = null,
        string paneId = "p1",
        string terminal = "term-1",
        ConnectionPhase phase = ConnectionPhase.Ready)
    {
        device ??= DeviceA;
        var session = SessionOf(device.Value);
        var pane = new PaneKey(session, "ws", paneId);
        return Pack(epoch, phase, [Entity(device.Value, session, pane, status, terminal)]);
    }

    public static DeviceProjectionSnapshot Many(long epoch, params AgentRow[] rows) =>
        Pack(epoch, ConnectionPhase.Ready, rows);

    public static AgentRow Entity(
        DeviceId device,
        SessionKey session,
        PaneKey pane,
        WireEnum<AgentStatusKind> status,
        string terminal = "term-1") =>
        new(device, session, pane, status, terminal);

    public static DeviceProjectionSnapshot Pack(long epoch, ConnectionPhase phase, IReadOnlyList<AgentRow> rows)
    {
        var devices = new List<ProjectedDevice>();
        foreach (var deviceGroup in rows.GroupBy(item => item.Device))
        {
            var sessions = new List<SessionProjection>();
            foreach (var sessionGroup in deviceGroup.GroupBy(item => item.Session))
            {
                var session = sessionGroup.Key;
                var panes = new List<PaneProjection>();
                var agents = new List<AgentProjection>();
                foreach (var row in sessionGroup)
                {
                    panes.Add(new PaneProjection(
                        row.Pane, row.Terminal, "t1", false, "main", "claude", KnownAgentKind.Claude, "Claude",
                        row.Status, 1));
                    agents.Add(new AgentProjection(
                        session, row.Terminal, "t1", row.Pane, "coder", "claude", KnownAgentKind.Claude, "Claude",
                        row.Status, true, 1));
                }

                var workspace = new WorkspaceProjection(
                    session, "ws", 1, "lab", false, (ulong)panes.Count, 1, "t1", sessionGroup.First().Status, null);
                sessions.Add(new SessionProjection(
                    session, "0.9.0", 22, "ws", "t1", panes[0].Key.PaneId,
                    [workspace], [], panes, [], agents));
            }

            devices.Add(new ProjectedDevice(deviceGroup.Key, Caps(), sessions));
        }

        return new DeviceProjectionSnapshot(new ConnectionEpoch(epoch), epoch, phase, devices);
    }

    public static CapabilityProfile Caps() =>
        new("0.9.0", "0.9.0", 22, 1, "sha", "UNVERIFIED", FrozenSet<string>.Empty);

    public sealed record AgentRow(
        DeviceId Device,
        SessionKey Session,
        PaneKey Pane,
        WireEnum<AgentStatusKind> Status,
        string Terminal);
}
