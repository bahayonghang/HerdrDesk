using System.Collections.Frozen;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class AggregationHarness
{
    public static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    public static DeviceId DeviceA { get; } = new(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
    public static DeviceId DeviceB { get; } = new(Guid.Parse("11111111-2222-3333-4444-555555555555"));
    public static DeviceId DeviceC { get; } = new(Guid.Parse("22222222-3333-4444-5555-666666666666"));

    public static SessionKey SessionOf(DeviceId device, string name = "dev") =>
        new(device, "named-session", name);

    public static PaneKey PaneKeyOf(DeviceId device, string workspace = "w1", string pane = "p1") =>
        new(SessionOf(device), workspace, pane);

    public static CapabilityProfile Compatible() =>
        CapabilityGate.Evaluate(SchemaCompatibilityBinding.PinnedMatchingRuntimeForTests("0.9.0"), 22, "0.9.0");

    public static CapabilityProfile Incompatible() =>
        CapabilityGate.Evaluate(SchemaCompatibilityBinding.PinnedUnverified("0.9.0"), 22, "0.9.0");

    public static WireEnum<AgentStatusKind> Idle() => new("idle", AgentStatusKind.Idle);

    public static WorkspaceProjection Workspace(SessionKey session, string id, string label, ulong panes) =>
        new(session, id, 1, label, false, panes, 1, "t1", Idle(), null);

    public static PaneProjection Pane(PaneKey key, string? label = null) =>
        new(key, "term-" + key.PaneId, "t1", false, label ?? key.PaneId, "claude", KnownAgentKind.Claude,
            "Claude", Idle(), 1);

    public static AgentProjection Agent(PaneProjection pane) =>
        new(pane.Key.Session, pane.TerminalId, pane.TabId, pane.Key, pane.Label, pane.AgentRaw, pane.AgentKind,
            pane.DisplayAgent, pane.AgentStatus, pane.Focused, pane.Revision);

    public static SessionProjection SessionState(
        SessionKey session,
        IReadOnlyList<WorkspaceProjection> workspaces,
        IReadOnlyList<PaneProjection> panes)
    {
        var agents = panes.Select(Agent).ToArray();
        return new(session, "0.9.0", 22, workspaces.FirstOrDefault()?.WorkspaceId, "t1",
            panes.FirstOrDefault()?.Key.PaneId, workspaces, [], panes, [], agents);
    }

    public static DeviceSessionState DeviceState(
        DeviceId device,
        long epoch,
        ConnectionPhase phase,
        DeviceFreshness freshness,
        SessionProjection? session = null,
        string? error = null,
        CapabilityProfile? capabilities = null)
    {
        var key = session?.Session ?? SessionOf(device);
        var sessions = session is null ? Array.Empty<SessionProjection>() : new[] { session };
        var caps = capabilities ?? Compatible();
        var snapshot = new DeviceProjectionSnapshot(
            new ConnectionEpoch(epoch), epoch, phase, [new ProjectedDevice(device, caps, sessions)]);
        return new DeviceSessionState(
            key, new ConnectionEpoch(epoch), phase, freshness, caps, snapshot,
            0, 0, error, phase == ConnectionPhase.Ready, 0, 0, 0, 0);
    }

    public static SessionProjection NamesakeSession(DeviceId device, string label = "main")
    {
        var session = SessionOf(device);
        var pane = Pane(PaneKeyOf(device), label);
        return SessionState(session, [Workspace(session, "w1", "lab", 1)], [pane]);
    }

    public static SessionProjection NamedPane(
        DeviceId device,
        string sessionName,
        string paneId,
        string label)
    {
        var session = SessionOf(device, sessionName);
        var pane = Pane(new PaneKey(session, "w1", paneId), label);
        return SessionState(session, [Workspace(session, "w1", "lab", 1)], [pane]);
    }

    public static DevicePartition PartitionFrom(DeviceSessionState state, int order, string label)
    {
        var store = new GlobalProjectionStore();
        var applied = store.ApplySession(state, order, label);
        Check(applied.Succeeded);
        Check(store.TryGetPartition(state.Session.Device, out var partition));
        return partition;
    }

    public static GlobalEntityRef PaneRef(DeviceId device, long epoch, string workspace = "w1", string pane = "p1")
    {
        var key = PaneKeyOf(device, workspace, pane);
        return new GlobalEntityRef(
            GlobalEntityKind.Pane, device, key.Session, workspace, key, null,
            new FreshnessStamp(new ConnectionEpoch(epoch), 0));
    }

    public static IReadOnlyList<SearchDocument> MixedHundred(GlobalProjectionStore store)
    {
        var devices = new[] { DeviceA, DeviceB, DeviceC };
        var remaining = 100;
        var index = 0;
        for (var d = 0; d < devices.Length; d++)
        {
            var device = devices[d];
            var session = SessionOf(device);
            var workspaces = new[]
            {
                Workspace(session, "w1", "one", 0),
                Workspace(session, "w2", "two", 0)
            };
            var paneBudget = d == devices.Length - 1
                ? Math.Max(1, remaining - 4)
                : 30;
            var panes = new List<PaneProjection>(paneBudget);
            for (var i = 0; i < paneBudget; i++)
            {
                var workspace = i % 2 == 0 ? "w1" : "w2";
                var key = new PaneKey(session, workspace, "p" + index.ToString("D3"));
                panes.Add(Pane(key, "pane-" + index.ToString("D3")));
                index++;
            }

            workspaces =
            [
                Workspace(session, "w1", "one", (ulong)panes.Count(item => item.Key.WorkspaceId == "w1")),
                Workspace(session, "w2", "two", (ulong)panes.Count(item => item.Key.WorkspaceId == "w2"))
            ];
            var projected = SessionState(session, workspaces, panes);
            var state = DeviceState(device, d + 1, ConnectionPhase.Ready, DeviceFreshness.Current, projected);
            Check(store.ApplySession(state, d, "lab-" + d).Succeeded);
            remaining = 100 - store.DocumentCount;
        }

        var documents = store.Search(new GlobalSearchQuery("", null, 1)).Documents;
        Check(documents.Count >= 100);
        return documents;
    }
}
