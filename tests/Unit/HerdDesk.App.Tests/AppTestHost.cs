using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Configuration;
using HerdDesk.Infrastructure.Diagnostics;

internal static class AppTestHost
{
    public static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    public static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "herddesk-hd011-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static DeviceId DeviceA { get; } = new(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
    public static DeviceId DeviceB { get; } = new(Guid.Parse("11111111-2222-3333-4444-555555555555"));
    public static DeviceId DeviceC { get; } = new(Guid.Parse("22222222-3333-4444-5555-666666666666"));

    public static SessionKey SessionOf(DeviceId device, string endpoint = "named-session", string? name = "dev") =>
        new(device, endpoint, name);

    public static CapabilityProfile Compatible() =>
        CapabilityGate.Evaluate(SchemaCompatibilityBinding.PinnedMatchingRuntimeForTests("0.9.0"), 22, "0.9.0");

    public static CapabilityProfile Incompatible() =>
        CapabilityGate.Evaluate(SchemaCompatibilityBinding.PinnedUnverified("0.9.0"), 22, "0.9.0");

    public static WireEnum<AgentStatusKind> Idle() => new("idle", AgentStatusKind.Idle);

    public static PaneProjection Pane(
        PaneKey key,
        string? label = null,
        string? agentRaw = null,
        KnownAgentKind? agentKind = KnownAgentKind.Claude)
    {
        var display = agentKind?.ToString();
        return new(
            key,
            "term-" + key.PaneId,
            "t1",
            false,
            label,
            agentRaw ?? display?.ToLowerInvariant(),
            agentKind,
            display,
            Idle(),
            1);
    }

    public static WorkspaceProjection Workspace(SessionKey session, string id, string label, ulong panes) =>
        new(session, id, 1, label, false, panes, 1, "t1", Idle(), null);

    public static SessionProjection SessionState(
        SessionKey session,
        IReadOnlyList<WorkspaceProjection> workspaces,
        IReadOnlyList<PaneProjection> panes)
    {
        var agents = panes.Select(pane => new AgentProjection(
            session, pane.TerminalId, pane.TabId, pane.Key, pane.Label, pane.AgentRaw, pane.AgentKind,
            pane.DisplayAgent, pane.AgentStatus, pane.Focused, pane.Revision)).ToArray();
        return new(session, "0.9.0", 22, workspaces.FirstOrDefault()?.WorkspaceId, "t1",
            panes.FirstOrDefault()?.Key.PaneId, workspaces, [], panes, [], agents);
    }

    public static DeviceProjectionSnapshot WithAgentStatus(
        DeviceProjectionSnapshot snapshot,
        PaneKey pane,
        WireEnum<AgentStatusKind> status)
    {
        var devices = snapshot.Devices.Select(device =>
        {
            var sessions = device.Sessions.Select(session =>
            {
                var panes = session.Panes.Select(item =>
                    item.Key == pane ? item with { AgentStatus = status } : item).ToArray();
                var agents = session.Agents.Select(item =>
                    item.Pane == pane ? item with { AgentStatus = status } : item).ToArray();
                return session with { Panes = panes, Agents = agents };
            }).ToArray();
            return device with { Sessions = sessions };
        }).ToArray();
        return snapshot with { Devices = devices };
    }

    public static WireEnum<AgentStatusKind> Blocked() => new("blocked", AgentStatusKind.Blocked);

    public static WireEnum<AgentStatusKind> Done() => new("done", AgentStatusKind.Done);

    public static WireEnum<AgentStatusKind> Working() => new("working", AgentStatusKind.Working);

    public static DeviceProjectionSnapshot Snapshot(
        ConnectionEpoch epoch,
        ConnectionPhase phase,
        params ProjectedDevice[] devices) =>
        new(epoch, 1, phase, devices);

    public static ProjectedDevice Projected(
        DeviceId device,
        CapabilityProfile? capabilities,
        params SessionProjection[] sessions) =>
        new(device, capabilities ?? Compatible(), sessions);

    public static DeviceProjectionSnapshot TwoNamedPanes()
    {
        var sessionA = SessionOf(DeviceA);
        var sessionB = SessionOf(DeviceB, name: "dev");
        var paneA = new PaneKey(sessionA, "ws", "p1");
        var paneB = new PaneKey(sessionB, "ws", "p1");
        var stateA = SessionState(
            sessionA,
            [Workspace(sessionA, "ws", "lab", 1)],
            [Pane(paneA, "main")]);
        var stateB = SessionState(
            sessionB,
            [Workspace(sessionB, "ws", "lab", 1)],
            [Pane(paneB, "main")]);
        return Snapshot(
            new ConnectionEpoch(1),
            ConnectionPhase.Ready,
            Projected(DeviceA, Compatible(), stateA),
            Projected(DeviceB, Compatible(), stateB));
    }

    public static DeviceProjectionSnapshot ThirtyPanes()
    {
        var session = SessionOf(DeviceA);
        var workspaces = new[]
        {
            Workspace(session, "w1", "one", 15),
            Workspace(session, "w2", "two", 15)
        };
        var panes = new List<PaneProjection>(30);
        for (var i = 0; i < 30; i++)
        {
            var workspace = i < 15 ? "w1" : "w2";
            var key = new PaneKey(session, workspace, "p" + i.ToString("D2"));
            panes.Add(Pane(key, "pane-" + i.ToString("D2")));
        }

        return Snapshot(
            new ConnectionEpoch(1),
            ConnectionPhase.Ready,
            Projected(DeviceA, Compatible(), SessionState(session, workspaces, panes)));
    }

    public static DeviceProjectionSnapshot HundredPanes()
    {
        var devices = new[] { DeviceA, DeviceB, DeviceC };
        var projected = new List<ProjectedDevice>(3);
        var remaining = 100;
        var indexBase = 0;
        for (var d = 0; d < devices.Length; d++)
        {
            var count = d == devices.Length - 1 ? remaining : 34;
            remaining -= count;
            var session = SessionOf(devices[d], name: "dev");
            var workspace = Workspace(session, "ws", "lab", (ulong)count);
            var panes = new List<PaneProjection>(count);
            for (var i = 0; i < count; i++)
            {
                var index = indexBase + i;
                var key = new PaneKey(session, "ws", "p" + index.ToString("D3"));
                panes.Add(Pane(key, "pane-" + index.ToString("D3")));
            }

            indexBase += count;
            projected.Add(Projected(devices[d], Compatible(), SessionState(session, [workspace], panes)));
        }

        return Snapshot(new ConnectionEpoch(1), ConnectionPhase.Ready, [.. projected]);
    }

    public static ShellViewModel Shell(
        IDeviceProfileStore? profiles = null,
        ProjectionCatalog? catalog = null,
        AppDataPaths? paths = null,
        IConfigurationOwnership? ownership = null,
        ITerminalDisplaySurface? surface = null,
        AppExitCoordinator? exit = null,
        UiPreferenceStore? ui = null,
        IDiagnosticSink? diagnostics = null,
        INotificationSink? notifications = null,
        DiagnosticAliasProjector? aliases = null,
        TerminalInputViewModel? input = null,
        GlobalProjectionStore? aggregate = null)
    {
        paths ??= AppDataPaths.FromRoot(TempRoot());
        profiles ??= new AtomicConfigurationStore(paths);
        catalog ??= new ProjectionCatalog();
        return new ShellViewModel(new ShellDependencies
        {
            Profiles = profiles,
            Catalog = catalog,
            Ownership = ownership ?? ConfigurationOwnership.Owner,
            UiPreferences = ui,
            DisplaySurface = surface ?? new NullDisplaySurface(),
            Exit = exit ?? new AppExitCoordinator(),
            DiagnosticSink = diagnostics,
            NotificationSink = notifications,
            Aliases = aliases,
            TerminalInput = input,
            Aggregate = aggregate,
            Unavailable =
            [
                new UnavailableCapability("rpc-connection", "rpc_bridge_unavailable"),
                new UnavailableCapability("terminal-renderer", "renderer_packages_unverified")
            ]
        });
    }

    public static string ExplicitLocation() =>
        OperatingSystem.IsWindows() ? @"\\.\pipe\herdr-hd011" : "/tmp/herdr-hd011.sock";

    public static EndpointKind ExplicitKind() =>
        OperatingSystem.IsWindows() ? EndpointKind.NamedPipe : EndpointKind.UnixSocket;
}

internal sealed class MemoryDeviceProfileStore : IDeviceProfileStore
{
    public ConfigurationSnapshot Snapshot { get; set; } = ConfigurationSnapshot.Empty;
    public string? NextWriteCode { get; set; }
    public string? LoadCode { get; set; }
    public int SaveCalls { get; private set; }

    public ValueTask<ConfigurationLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        if (LoadCode is not null)
            return new(new ConfigurationLoadResult(null, LoadCode));
        if (Snapshot.Devices.Count == 0 && Snapshot.Revision == 0)
            return new(new ConfigurationLoadResult(null, ConfigurationCodes.Missing));
        return new(new ConfigurationLoadResult(Snapshot, null));
    }

    public ValueTask<ConfigurationWriteResult> SaveDeviceAsync(
        DeviceProfile profile,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        SaveCalls++;
        if (NextWriteCode is not null)
            return new(new ConfigurationWriteResult(null, NextWriteCode));
        if (Snapshot.Revision != expectedRevision)
            return new(new ConfigurationWriteResult(null, ConfigurationCodes.WriteConflict));
        var devices = Snapshot.Devices.Where(item => item.Device != profile.Device).ToList();
        devices.Add(profile);
        Snapshot = new ConfigurationSnapshot(
            ConfigurationSnapshot.CurrentSchemaVersion, Snapshot.Revision + 1, devices);
        return new(new ConfigurationWriteResult(Snapshot, null));
    }

    public ValueTask<ConfigurationWriteResult> DeleteDeviceAsync(
        DeviceId device,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        _ = (device, expectedRevision, cancellationToken);
        return new(new ConfigurationWriteResult(null, ConfigurationCodes.InvalidIdentity));
    }

    public ValueTask<ConfigurationWriteResult> RestoreFromBackupAsync(
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return new(new ConfigurationWriteResult(null, ConfigurationCodes.BackupMissing));
    }
}

internal sealed class CountingDisplaySurface : ITerminalDisplaySurface
{
    public int LocalApplyCount { get; private set; }
    public int UpstreamResizeCount { get; private set; }

    public void ApplyLocal(TerminalDisplayPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        LocalApplyCount++;
    }

    public bool TryUpstreamResize(int columns, int rows)
    {
        _ = (columns, rows);
        UpstreamResizeCount++;
        return true;
    }
}

internal sealed class FakeOwnedChild : IAsyncDisposable
{
    public bool Disposed { get; private set; }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeDaemon
{
    public bool Stopped { get; set; }
}

internal sealed class FixedClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);
}

internal sealed class RecordingDiagnosticSink : IDiagnosticSink
{
    public List<DiagnosticEvent> Events { get; } = [];
    public long DroppedCount => 0;

    public bool TryWrite(DiagnosticEvent evt)
    {
        Events.Add(evt);
        return true;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
