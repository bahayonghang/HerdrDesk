namespace HerdDesk.Contracts;

public sealed record WorktreeProjection(
    string RepoKey,
    string RepoName,
    bool IsLinkedWorktree);

public sealed record WorkspaceProjection(
    SessionKey Session,
    string WorkspaceId,
    ulong Number,
    string Label,
    bool Focused,
    ulong PaneCount,
    ulong TabCount,
    string ActiveTabId,
    WireEnum<AgentStatusKind> AgentStatus,
    WorktreeProjection? Worktree);

public sealed record TabProjection(
    SessionKey Session,
    string TabId,
    string WorkspaceId,
    ulong Number,
    string Label,
    bool Focused,
    ulong PaneCount,
    WireEnum<AgentStatusKind> AgentStatus);

public sealed record PaneProjection(
    PaneKey Key,
    string TerminalId,
    string TabId,
    bool Focused,
    string? Label,
    string? AgentRaw,
    KnownAgentKind? AgentKind,
    string? DisplayAgent,
    WireEnum<AgentStatusKind> AgentStatus,
    ulong Revision);

public sealed record LayoutPaneProjection(string PaneId, bool Focused, ushort X, ushort Y, ushort Width, ushort Height);

public sealed record LayoutProjection(
    SessionKey Session,
    string WorkspaceId,
    string TabId,
    bool Zoomed,
    string FocusedPaneId,
    IReadOnlyList<LayoutPaneProjection> Panes);

public sealed record AgentProjection(
    SessionKey Session,
    string TerminalId,
    string TabId,
    PaneKey Pane,
    string? Name,
    string? AgentRaw,
    KnownAgentKind? AgentKind,
    string? DisplayAgent,
    WireEnum<AgentStatusKind> AgentStatus,
    bool Focused,
    ulong Revision);

public sealed record SessionProjection(
    SessionKey Session,
    string ServerVersion,
    int Protocol,
    string? FocusedWorkspaceId,
    string? FocusedTabId,
    string? FocusedPaneId,
    IReadOnlyList<WorkspaceProjection> Workspaces,
    IReadOnlyList<TabProjection> Tabs,
    IReadOnlyList<PaneProjection> Panes,
    IReadOnlyList<LayoutProjection> Layouts,
    IReadOnlyList<AgentProjection> Agents);

public sealed record ProjectedDevice(
    DeviceId Device,
    CapabilityProfile Capabilities,
    IReadOnlyList<SessionProjection> Sessions);

public sealed record DeviceProjectionSnapshot(
    ConnectionEpoch Epoch,
    long Revision,
    ConnectionPhase Phase,
    IReadOnlyList<ProjectedDevice> Devices)
{
    public static DeviceProjectionSnapshot Empty { get; } =
        new(new ConnectionEpoch(0), 0, ConnectionPhase.Offline, []);

    public bool GraphEquivalent(DeviceProjectionSnapshot other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Epoch != other.Epoch || Phase != other.Phase || Devices.Count != other.Devices.Count)
            return false;
        for (var i = 0; i < Devices.Count; i++)
        {
            if (!DeviceEquivalent(Devices[i], other.Devices[i]))
                return false;
        }
        return true;
    }

    private static bool DeviceEquivalent(ProjectedDevice left, ProjectedDevice right)
    {
        if (left.Device != right.Device ||
            left.Capabilities.CliVersion != right.Capabilities.CliVersion ||
            left.Capabilities.ServerVersion != right.Capabilities.ServerVersion ||
            left.Capabilities.ApiSchemaProtocol != right.Capabilities.ApiSchemaProtocol ||
            left.Capabilities.SchemaVersion != right.Capabilities.SchemaVersion ||
            left.Capabilities.SchemaSha256 != right.Capabilities.SchemaSha256 ||
            left.Capabilities.RuntimeSchemaSha256Status != right.Capabilities.RuntimeSchemaSha256Status ||
            left.Capabilities.VerifiedOperations.Count != right.Capabilities.VerifiedOperations.Count ||
            left.Sessions.Count != right.Sessions.Count)
            return false;
        foreach (var operation in left.Capabilities.VerifiedOperations)
        {
            if (!right.Capabilities.VerifiedOperations.Contains(operation))
                return false;
        }
        for (var i = 0; i < left.Sessions.Count; i++)
        {
            if (!SessionEquivalent(left.Sessions[i], right.Sessions[i]))
                return false;
        }
        return true;
    }

    private static bool SessionEquivalent(SessionProjection left, SessionProjection right) =>
        left.Session == right.Session &&
        left.ServerVersion == right.ServerVersion &&
        left.Protocol == right.Protocol &&
        left.FocusedWorkspaceId == right.FocusedWorkspaceId &&
        left.FocusedTabId == right.FocusedTabId &&
        left.FocusedPaneId == right.FocusedPaneId &&
        left.Workspaces.SequenceEqual(right.Workspaces) &&
        left.Tabs.SequenceEqual(right.Tabs) &&
        left.Panes.SequenceEqual(right.Panes) &&
        left.Agents.SequenceEqual(right.Agents) &&
        LayoutsEqual(left.Layouts, right.Layouts);

    private static bool LayoutsEqual(IReadOnlyList<LayoutProjection> left, IReadOnlyList<LayoutProjection> right)
    {
        if (left.Count != right.Count)
            return false;
        for (var i = 0; i < left.Count; i++)
        {
            var a = left[i];
            var b = right[i];
            if (a.Session != b.Session || a.WorkspaceId != b.WorkspaceId || a.TabId != b.TabId ||
                a.Zoomed != b.Zoomed || a.FocusedPaneId != b.FocusedPaneId ||
                !a.Panes.SequenceEqual(b.Panes))
                return false;
        }
        return true;
    }
}

public sealed record DeviceProjectionGraph(
    DeviceId Device,
    SessionKey Session,
    ConnectionEpoch Epoch,
    ConnectionPhase Phase,
    CapabilityProfile Capabilities,
    SessionProjection SessionState);

public sealed record ProjectionMapResult(DeviceProjectionGraph? Graph, string? Code)
{
    public bool Succeeded => Graph is not null && Code is null;

    public static ProjectionMapResult Ok(DeviceProjectionGraph graph) => new(graph, null);

    public static ProjectionMapResult Fail(string code) => new(null, code);
}

public sealed record ProjectionInstallResult(DeviceProjectionSnapshot? Snapshot, string? Code)
{
    public bool Succeeded => Snapshot is not null && Code is null;

    public static ProjectionInstallResult Ok(DeviceProjectionSnapshot snapshot) => new(snapshot, null);

    public static ProjectionInstallResult Fail(string code) => new(null, code);
}
