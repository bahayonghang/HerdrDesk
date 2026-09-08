using System.Collections.Frozen;
using System.Text.Json;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class ProjectionCases
{
    public static (string Name, Action Run)[] All =>
    [
        ("projection mapper rejects duplicate pane identity", DuplicatePaneRejected),
        ("projection mapper rejects dangling parent", DanglingParentRejected),
        ("same pane id on two devices does not collide", TwoDevicesDoNotCollide),
        ("stale epoch leaves graph byte equivalent", StaleEpochUnchanged),
        ("new epoch replaces graph without mixing", NewEpochReplacesAtomically),
        ("entity read with wrong session cannot overwrite", WrongSessionEntityRead),
        ("unknown protocol removes mutation capability", UnknownProtocolRemovesMutation),
        ("unverified runtime hash removes mutation capability", UnverifiedHashRemovesMutation),
        ("store rebuilds equivalent graph from the same decoded snapshot", RebuildEquivalent),
        ("store rejects a graph whose epoch does not match the install epoch", GraphEpochMismatchRejected),
        ("incompatible session on the same device empties verified operations", IncompatibleSessionClearsDeviceOperations),
        ("core mapper store types do not reference infrastructure", CoreHasNoInfrastructure)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static FrozenDictionary<string, JsonElement> NoJson() =>
        FrozenDictionary<string, JsonElement>.Empty;

    static SessionKey Session(string device, string endpoint = "local-api") =>
        new(new DeviceId(Guid.Parse(device)), endpoint, "dev");

    static CapabilityProfile Compatible() =>
        CapabilityGate.Evaluate(SchemaCompatibilityBinding.PinnedMatchingRuntimeForTests("0.9.0"), 22, "0.9.0");

    static CapabilityProfile Unverified() =>
        CapabilityGate.Evaluate(SchemaCompatibilityBinding.PinnedUnverified("0.9.0"), 22, "0.9.0");

    static WireEnum<AgentStatusKind> Idle() => new("idle", AgentStatusKind.Idle);

    static DecodedWorkspace Workspace(string id = "w1", string tab = "t1") =>
        new(id, 1, "lab", true, 1, 1, tab, Idle(), null, null, NoJson());

    static DecodedTab Tab(string id = "t1", string workspace = "w1") =>
        new(id, workspace, 1, "main", true, 1, Idle(), NoJson());

    static DecodedPane Pane(string id = "p1", string workspace = "w1", string tab = "t1", string terminal = "term-1") =>
        new(id, terminal, workspace, tab, true, null, null, "shell", "claude", KnownAgentKind.Claude,
            null, null, null, Idle(), null, null, null, 1, NoJson());

    static DecodedLayout Layout(string workspace = "w1", string tab = "t1", string pane = "p1") =>
        new(workspace, tab, false, new DecodedLayoutRect(0, 0, 80, 24), pane,
            [new DecodedLayoutPane(pane, true, new DecodedLayoutRect(0, 0, 80, 24))],
            [], NoJson());

    static DecodedAgent Agent(string terminal = "term-1", string workspace = "w1", string tab = "t1", string pane = "p1") =>
        new(terminal, workspace, tab, pane, null, "claude", KnownAgentKind.Claude, null, null, Idle(),
            true, 1, NoJson());

    static DecodedSessionSnapshot Snapshot(SessionKey session, long epoch = 1, string pane = "p1") =>
        new(session, new ConnectionEpoch(epoch), "0.9.0", 22, "w1", "t1", pane,
            [Workspace()], [Tab()], [Pane(pane)], [Layout(pane: pane)], [Agent(pane: pane)], NoJson());

    static DeviceProjectionGraph Graph(SessionKey session, long epoch = 1, string pane = "p1",
        CapabilityProfile? capabilities = null)
    {
        var mapped = ProjectionMapper.MapSnapshot(Snapshot(session, epoch, pane), capabilities ?? Compatible());
        Check(mapped.Succeeded);
        return mapped.Graph!;
    }

    static void DuplicatePaneRejected()
    {
        var session = Session("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var decoded = Snapshot(session) with
        {
            Panes = [Pane("p1"), Pane("p1", terminal: "term-2")],
            Agents = [Agent(pane: "p1")],
            Layouts = [Layout()]
        };
        var mapped = ProjectionMapper.MapSnapshot(decoded, Compatible());
        Check(!mapped.Succeeded);
        Check(mapped.Code == ProjectionCodes.DuplicateIdentity);
        var store = new DeviceProjectionStore();
        var before = store.Read();
        Check(store.InstallSnapshot(new ConnectionEpoch(1), Graph(session)).Succeeded);
        var installed = store.Read();
        Check(!ReferenceEquals(before, installed));
        var failed = ProjectionMapper.MapSnapshot(decoded, Compatible());
        Check(!failed.Succeeded);
        Check(store.Read().GraphEquivalent(installed));
        Check(store.Read().Revision == installed.Revision);
    }

    static void DanglingParentRejected()
    {
        var session = Session("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var decoded = Snapshot(session) with
        {
            Panes = [Pane("p1", workspace: "missing")]
        };
        var mapped = ProjectionMapper.MapSnapshot(decoded, Compatible());
        Check(!mapped.Succeeded);
        Check(mapped.Code == ProjectionCodes.ParentMissing);
    }

    static void TwoDevicesDoNotCollide()
    {
        var deviceA = Session("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var deviceB = Session("11111111-2222-3333-4444-555555555555");
        var store = new DeviceProjectionStore();
        Check(store.InstallSnapshot(new ConnectionEpoch(1), Graph(deviceA, pane: "p1")).Succeeded);
        Check(store.InstallSnapshot(new ConnectionEpoch(1), Graph(deviceB, pane: "p1")).Succeeded);
        var snapshot = store.Read();
        Check(snapshot.Devices.Count == 2);
        var panes = snapshot.Devices.SelectMany(device => device.Sessions)
            .SelectMany(session => session.Panes).ToArray();
        Check(panes.Length == 2);
        Check(panes[0].Key != panes[1].Key);
        Check(panes[0].Key.PaneId == "p1");
        Check(panes[1].Key.PaneId == "p1");
        Check(panes[0].Key.Session.Device != panes[1].Key.Session.Device);
    }

    static void StaleEpochUnchanged()
    {
        var session = Session("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var store = new DeviceProjectionStore();
        Check(store.InstallSnapshot(new ConnectionEpoch(1), Graph(session)).Succeeded);
        var before = store.Read();
        var stale = store.InstallSnapshot(new ConnectionEpoch(2), Graph(session, epoch: 2));
        Check(!stale.Succeeded);
        Check(stale.Code == ProjectionCodes.StaleEpoch);
        var after = store.Read();
        Check(ReferenceEquals(before, after));
        Check(after.GraphEquivalent(before));
        Check(after.Revision == before.Revision);
    }

    static void NewEpochReplacesAtomically()
    {
        var deviceA = Session("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var deviceB = Session("11111111-2222-3333-4444-555555555555");
        var store = new DeviceProjectionStore();
        Check(store.InstallSnapshot(new ConnectionEpoch(1), Graph(deviceA, pane: "old-a")).Succeeded);
        Check(store.InstallSnapshot(new ConnectionEpoch(1), Graph(deviceB, pane: "old-b")).Succeeded);
        var cleared = store.ClearForNewEpoch(new ConnectionEpoch(2));
        Check(cleared.Succeeded);
        Check(store.Read().Phase == ConnectionPhase.Stale);
        var mixed = store.InstallSnapshot(new ConnectionEpoch(1), Graph(deviceA, epoch: 1));
        Check(!mixed.Succeeded);
        Check(mixed.Code == ProjectionCodes.StaleEpoch);
        Check(store.InstallSnapshot(new ConnectionEpoch(2), Graph(deviceA, epoch: 2, pane: "new-a")).Succeeded);
        var snapshot = store.Read();
        Check(snapshot.Epoch.Value == 2);
        Check(snapshot.Devices.Count == 1);
        Check(snapshot.Devices[0].Sessions[0].Panes[0].Key.PaneId == "new-a");
        Check(snapshot.Devices[0].Device == deviceA.Device);
    }

    static void WrongSessionEntityRead()
    {
        var deviceA = Session("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var deviceB = Session("11111111-2222-3333-4444-555555555555");
        var store = new DeviceProjectionStore();
        Check(store.InstallSnapshot(new ConnectionEpoch(1), Graph(deviceA)).Succeeded);
        Check(store.InstallSnapshot(new ConnectionEpoch(1), Graph(deviceB)).Succeeded);
        var before = store.Read();
        var change = new ProjectionEntityChangeSet(
            deviceB,
            [],
            [],
            [Pane("p1", terminal: "hijack")],
            [],
            []);
        var mapped = ProjectionMapper.MapEntityRead(Graph(deviceA), change, Compatible());
        Check(!mapped.Succeeded);
        Check(mapped.Code == ProjectionCodes.InvalidIdentity);
        var result = store.InstallEntityRead(new ConnectionEpoch(1), change);
        Check(result.Succeeded);
        var after = store.Read();
        var paneA = after.Devices.Single(item => item.Device == deviceA.Device).Sessions[0].Panes[0];
        var paneB = after.Devices.Single(item => item.Device == deviceB.Device).Sessions[0].Panes[0];
        Check(paneA.TerminalId == "term-1");
        Check(paneA.Key.Session.Device == deviceA.Device);
        Check(paneB.TerminalId == "hijack");
        Check(paneB.Key.Session.Device == deviceB.Device);
        Check(after.Revision == before.Revision + 1);
    }

    static void UnknownProtocolRemovesMutation()
    {
        var binding = SchemaCompatibilityBinding.PinnedMatchingRuntimeForTests("0.9.0");
        var profile = CapabilityGate.Evaluate(binding, 23, "0.9.0");
        Check(profile.VerifiedOperations.Count == 0);
        Check(!profile.HasMutationControl);
        Check(!profile.HasOperation("workspace.close"));
        Check(!profile.HasOperation("pane.send_text"));
        var mapped = ProjectionMapper.MapSnapshot(
            Snapshot(Session("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")), profile);
        Check(mapped.Succeeded);
        Check(mapped.Graph!.Phase == ConnectionPhase.Incompatible);
        Check(!mapped.Graph.Capabilities.HasMutationControl);
    }

    static void UnverifiedHashRemovesMutation()
    {
        var profile = Unverified();
        Check(profile.RuntimeSchemaSha256Status == SchemaCompatibilityBinding.RuntimeUnverified);
        Check(profile.VerifiedOperations.Count == 0);
        Check(!profile.HasMutationControl);
        var session = Session("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var mapped = ProjectionMapper.MapSnapshot(Snapshot(session), profile);
        Check(mapped.Succeeded);
        Check(mapped.Graph!.Phase == ConnectionPhase.Incompatible);
        var store = new DeviceProjectionStore();
        Check(store.InstallSnapshot(new ConnectionEpoch(1), mapped.Graph).Succeeded);
        var change = new ProjectionEntityChangeSet(session, [], [], [Pane()], [], []);
        var entity = store.InstallEntityRead(new ConnectionEpoch(1), change);
        Check(!entity.Succeeded);
        Check(entity.Code == ProjectionCodes.FullSnapshotRequired);
    }

    static void RebuildEquivalent()
    {
        var session = Session("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var store = new DeviceProjectionStore();
        var first = store.InstallSnapshot(new ConnectionEpoch(1), Graph(session));
        Check(first.Succeeded);
        var secondStore = new DeviceProjectionStore();
        var second = secondStore.InstallSnapshot(new ConnectionEpoch(1), Graph(session));
        Check(second.Succeeded);
        Check(first.Snapshot!.GraphEquivalent(second.Snapshot!));
        Check(first.Snapshot.Revision == 1);
        Check(second.Snapshot!.Revision == 1);
        var cleared = secondStore.ClearForNewEpoch(new ConnectionEpoch(2));
        Check(cleared.Succeeded);
        Check(store.Read().GraphEquivalent(first.Snapshot));
        Check(store.Read().Devices.Count == 1);
    }

    static void GraphEpochMismatchRejected()
    {
        var session = Session("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var store = new DeviceProjectionStore();
        Check(store.InstallSnapshot(new ConnectionEpoch(1), Graph(session)).Succeeded);
        var before = store.Read();
        var mismatched = store.InstallSnapshot(new ConnectionEpoch(1), Graph(session, epoch: 2));
        Check(!mismatched.Succeeded);
        Check(mismatched.Code == ProjectionCodes.StaleEpoch);
        Check(ReferenceEquals(before, store.Read()));
        Check(store.Read().GraphEquivalent(before));
        Check(store.Read().Revision == before.Revision);
    }

    static void IncompatibleSessionClearsDeviceOperations()
    {
        var device = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
        var sessionA = Session(device, "local-api");
        var sessionB = new SessionKey(sessionA.Device, "local-api", "other");
        var store = new DeviceProjectionStore();
        Check(store.InstallSnapshot(new ConnectionEpoch(1), Graph(sessionA)).Succeeded);
        Check(store.Read().Devices[0].Capabilities.HasMutationControl);
        Check(store.InstallSnapshot(new ConnectionEpoch(1), Graph(sessionB, capabilities: Unverified()))
            .Succeeded);
        var snapshot = store.Read();
        Check(snapshot.Devices.Count == 1);
        Check(snapshot.Devices[0].Sessions.Count == 2);
        Check(snapshot.Phase == ConnectionPhase.Incompatible);
        Check(snapshot.Devices[0].Capabilities.VerifiedOperations.Count == 0);
        Check(!snapshot.Devices[0].Capabilities.HasMutationControl);
    }

    static void CoreHasNoInfrastructure()
    {
        var names = typeof(ProjectionMapper).Assembly.GetReferencedAssemblies()
            .Select(item => item.Name!).ToArray();
        Check(names.Contains("HerdDesk.Contracts"));
        Check(!names.Contains("HerdDesk.Infrastructure"));
        Check(!names.Contains("HerdDesk.App"));
        Check(!names.Contains("HerdDesk.Terminal.Web"));
        foreach (var name in names)
        {
            var lower = name.ToLowerInvariant();
            Check(!lower.Contains("windowsappsdk"));
            Check(!lower.Contains("webview2"));
            Check(!lower.Contains("winui"));
            Check(!lower.Contains("ssh"));
        }
    }
}
