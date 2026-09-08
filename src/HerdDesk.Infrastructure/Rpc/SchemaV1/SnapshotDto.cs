using System.Collections.Frozen;
using System.Text.Json;
using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.Rpc.SchemaV1;

public sealed record SnapshotDto(
    string Version,
    int Protocol,
    string? FocusedWorkspaceId,
    string? FocusedTabId,
    string? FocusedPaneId,
    IReadOnlyList<WorkspaceDto> Workspaces,
    IReadOnlyList<TabDto> Tabs,
    IReadOnlyList<PaneDto> Panes,
    IReadOnlyList<LayoutDto> Layouts,
    IReadOnlyList<AgentDto> Agents,
    FrozenDictionary<string, JsonElement> Extensions);

public sealed record WorkspaceDto(
    string WorkspaceId,
    ulong Number,
    string Label,
    bool Focused,
    ulong PaneCount,
    ulong TabCount,
    string ActiveTabId,
    SchemaValue<AgentStatusKind> AgentStatus,
    FrozenDictionary<string, string>? Tokens,
    WorktreeDto? Worktree,
    FrozenDictionary<string, JsonElement> Extensions);

public sealed record WorktreeDto(
    string RepoKey,
    string RepoName,
    string RepoRoot,
    string CheckoutPath,
    bool IsLinkedWorktree,
    FrozenDictionary<string, JsonElement> Extensions);

public sealed record TabDto(
    string TabId,
    string WorkspaceId,
    ulong Number,
    string Label,
    bool Focused,
    ulong PaneCount,
    SchemaValue<AgentStatusKind> AgentStatus,
    FrozenDictionary<string, JsonElement> Extensions);

public sealed record PaneDto(
    string PaneId,
    string TerminalId,
    string WorkspaceId,
    string TabId,
    bool Focused,
    string? Cwd,
    string? ForegroundCwd,
    string? Label,
    string? Agent,
    string? Title,
    string? DisplayAgent,
    SchemaValue<AgentStatusKind> AgentStatus,
    FrozenDictionary<string, string>? StateLabels,
    FrozenDictionary<string, string>? Tokens,
    AgentSessionDto? AgentSession,
    ulong Revision,
    FrozenDictionary<string, JsonElement> Extensions);

public sealed record AgentSessionDto(
    string Source,
    string Agent,
    SchemaValue<AgentSessionRefKind> Kind,
    string Value,
    FrozenDictionary<string, JsonElement> Extensions);

public sealed record AgentDto(
    string TerminalId,
    string WorkspaceId,
    string TabId,
    string PaneId,
    string? Name,
    string? Agent,
    string? Title,
    string? DisplayAgent,
    SchemaValue<AgentStatusKind> AgentStatus,
    bool Focused,
    ulong Revision,
    FrozenDictionary<string, JsonElement> Extensions);

public sealed record LayoutDto(
    string WorkspaceId,
    string TabId,
    bool Zoomed,
    LayoutRectDto Area,
    string FocusedPaneId,
    IReadOnlyList<LayoutPaneDto> Panes,
    IReadOnlyList<LayoutSplitDto> Splits,
    FrozenDictionary<string, JsonElement> Extensions);

public sealed record LayoutRectDto(ushort X, ushort Y, ushort Width, ushort Height);

public sealed record LayoutPaneDto(string PaneId, bool Focused, LayoutRectDto Rect);

public sealed record LayoutSplitDto(
    string Id,
    SchemaValue<SplitDirectionKind> Direction,
    double Ratio,
    LayoutRectDto Rect);
