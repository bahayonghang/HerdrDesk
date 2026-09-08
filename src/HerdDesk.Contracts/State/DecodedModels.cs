using System.Collections.Frozen;
using System.Text.Json;

namespace HerdDesk.Contracts;

public sealed record DecodeResult<T>(T? Value, string? Code)
{
    public bool Succeeded => Value is not null && Code is null;

    public static DecodeResult<T> Ok(T value) => new(value, null);

    public static DecodeResult<T> Fail(string code) => new(default, code);
}

public sealed record DecodedWorktree(
    string RepoKey,
    string RepoName,
    string RepoRoot,
    string CheckoutPath,
    bool IsLinkedWorktree,
    FrozenDictionary<string, JsonElement> Extensions);

public sealed record DecodedWorkspace(
    string WorkspaceId,
    ulong Number,
    string Label,
    bool Focused,
    ulong PaneCount,
    ulong TabCount,
    string ActiveTabId,
    WireEnum<AgentStatusKind> AgentStatus,
    FrozenDictionary<string, string>? Tokens,
    DecodedWorktree? Worktree,
    FrozenDictionary<string, JsonElement> Extensions);

public sealed record DecodedTab(
    string TabId,
    string WorkspaceId,
    ulong Number,
    string Label,
    bool Focused,
    ulong PaneCount,
    WireEnum<AgentStatusKind> AgentStatus,
    FrozenDictionary<string, JsonElement> Extensions);

public sealed record DecodedLayoutRect(ushort X, ushort Y, ushort Width, ushort Height);

public sealed record DecodedLayoutPane(string PaneId, bool Focused, DecodedLayoutRect Rect);

public sealed record DecodedLayoutSplit(
    string Id,
    WireEnum<SplitDirectionKind> Direction,
    double Ratio,
    DecodedLayoutRect Rect);

public sealed record DecodedLayout(
    string WorkspaceId,
    string TabId,
    bool Zoomed,
    DecodedLayoutRect Area,
    string FocusedPaneId,
    IReadOnlyList<DecodedLayoutPane> Panes,
    IReadOnlyList<DecodedLayoutSplit> Splits,
    FrozenDictionary<string, JsonElement> Extensions);

public sealed record DecodedAgentSession(
    string Source,
    string Agent,
    WireEnum<AgentSessionRefKind> Kind,
    string Value,
    FrozenDictionary<string, JsonElement> Extensions);

public sealed record DecodedPane(
    string PaneId,
    string TerminalId,
    string WorkspaceId,
    string TabId,
    bool Focused,
    string? Cwd,
    string? ForegroundCwd,
    string? Label,
    string? AgentRaw,
    KnownAgentKind? AgentKind,
    string? Title,
    string? TerminalTitle,
    string? DisplayAgent,
    WireEnum<AgentStatusKind> AgentStatus,
    FrozenDictionary<string, string>? StateLabels,
    FrozenDictionary<string, string>? Tokens,
    DecodedAgentSession? AgentSession,
    ulong Revision,
    FrozenDictionary<string, JsonElement> Extensions);

public sealed record DecodedAgent(
    string TerminalId,
    string WorkspaceId,
    string TabId,
    string PaneId,
    string? Name,
    string? AgentRaw,
    KnownAgentKind? AgentKind,
    string? Title,
    string? DisplayAgent,
    WireEnum<AgentStatusKind> AgentStatus,
    bool Focused,
    ulong Revision,
    FrozenDictionary<string, JsonElement> Extensions);

public sealed record DecodedSessionSnapshot(
    SessionKey Session,
    ConnectionEpoch Epoch,
    string Version,
    int Protocol,
    string? FocusedWorkspaceId,
    string? FocusedTabId,
    string? FocusedPaneId,
    IReadOnlyList<DecodedWorkspace> Workspaces,
    IReadOnlyList<DecodedTab> Tabs,
    IReadOnlyList<DecodedPane> Panes,
    IReadOnlyList<DecodedLayout> Layouts,
    IReadOnlyList<DecodedAgent> Agents,
    FrozenDictionary<string, JsonElement> Extensions);

public sealed record DecodedRpcEvent(
    SessionKey Session,
    ConnectionEpoch Epoch,
    WireEnum<RpcEventKind> Event,
    string? WorkspaceId,
    string? TabId,
    string? PaneId,
    DecodedWorkspace? Workspace,
    DecodedTab? Tab,
    DecodedPane? Pane,
    DecodedLayout? Layout,
    bool? CloseGroup,
    FrozenDictionary<string, JsonElement> Extensions);

public sealed record ProjectionEntityChangeSet(
    SessionKey Session,
    IReadOnlyList<DecodedWorkspace> Workspaces,
    IReadOnlyList<DecodedTab> Tabs,
    IReadOnlyList<DecodedPane> Panes,
    IReadOnlyList<DecodedAgent> Agents,
    IReadOnlyList<DecodedLayout> Layouts);
