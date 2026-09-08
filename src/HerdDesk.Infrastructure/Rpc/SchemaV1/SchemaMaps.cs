using System.Collections.Frozen;
using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.Rpc.SchemaV1;

internal static class SchemaMaps
{
    public static FrozenSet<string> SnapshotFields { get; } = FrozenSet.ToFrozenSet(
        [
            "version", "protocol", "focused_workspace_id", "focused_tab_id", "focused_pane_id",
            "workspaces", "tabs", "panes", "layouts", "agents"
        ],
        StringComparer.Ordinal);

    public static FrozenSet<string> WorkspaceFields { get; } = FrozenSet.ToFrozenSet(
        [
            "workspace_id", "number", "label", "focused", "pane_count", "tab_count",
            "active_tab_id", "agent_status", "tokens", "worktree"
        ],
        StringComparer.Ordinal);

    public static FrozenSet<string> WorktreeFields { get; } = FrozenSet.ToFrozenSet(
        ["repo_key", "repo_name", "repo_root", "checkout_path", "is_linked_worktree"],
        StringComparer.Ordinal);

    public static FrozenSet<string> TabFields { get; } = FrozenSet.ToFrozenSet(
        ["tab_id", "workspace_id", "number", "label", "focused", "pane_count", "agent_status"],
        StringComparer.Ordinal);

    public static FrozenSet<string> PaneFields { get; } = FrozenSet.ToFrozenSet(
        [
            "pane_id", "terminal_id", "workspace_id", "tab_id", "focused", "cwd", "foreground_cwd",
            "label", "agent", "title", "terminal_title", "terminal_title_stripped", "display_agent",
            "agent_status", "state_labels", "tokens", "agent_session", "scroll", "revision"
        ],
        StringComparer.Ordinal);

    public static FrozenSet<string> AgentFields { get; } = FrozenSet.ToFrozenSet(
        [
            "terminal_id", "name", "agent", "title", "terminal_title", "terminal_title_stripped",
            "display_agent", "agent_status", "screen_detection_skipped", "state_labels", "tokens",
            "agent_session", "workspace_id", "tab_id", "pane_id", "focused", "launch_pending",
            "interactive_ready", "state_change_seq", "cwd", "foreground_cwd", "revision"
        ],
        StringComparer.Ordinal);

    public static FrozenSet<string> AgentSessionFields { get; } = FrozenSet.ToFrozenSet(
        ["source", "agent", "kind", "value"],
        StringComparer.Ordinal);

    public static FrozenSet<string> LayoutFields { get; } = FrozenSet.ToFrozenSet(
        ["workspace_id", "tab_id", "zoomed", "area", "focused_pane_id", "panes", "splits"],
        StringComparer.Ordinal);

    public static FrozenSet<string> LayoutPaneFields { get; } = FrozenSet.ToFrozenSet(
        ["pane_id", "focused", "rect"],
        StringComparer.Ordinal);

    public static FrozenSet<string> LayoutSplitFields { get; } = FrozenSet.ToFrozenSet(
        ["id", "direction", "ratio", "rect"],
        StringComparer.Ordinal);

    public static FrozenSet<string> RectFields { get; } = FrozenSet.ToFrozenSet(
        ["x", "y", "width", "height"],
        StringComparer.Ordinal);

    public static FrozenSet<string> EventFields { get; } = FrozenSet.ToFrozenSet(
        ["event", "data"],
        StringComparer.Ordinal);

    public static FrozenSet<string> EventDataKnown { get; } = FrozenSet.ToFrozenSet(
        [
            "type", "workspace", "workspace_id", "tab", "tab_id", "pane", "pane_id", "layout",
            "label", "insert_index", "workspaces", "workspace_ids", "before_workspace_id",
            "worktree", "already_open", "forced", "previous_pane_id", "previous_workspace_id",
            "previous_tab_id", "created_workspace", "created_tab", "closed_workspace_id",
            "closed_tab_id", "revision", "agent", "released", "final_status", "agent_status",
            "title", "display_agent", "state_labels", "close_group"
        ],
        StringComparer.Ordinal);

    public static FrozenDictionary<string, AgentStatusKind> AgentStatus { get; } =
        new Dictionary<string, AgentStatusKind>(StringComparer.Ordinal)
        {
            ["idle"] = AgentStatusKind.Idle,
            ["working"] = AgentStatusKind.Working,
            ["blocked"] = AgentStatusKind.Blocked,
            ["done"] = AgentStatusKind.Done,
            ["unknown"] = AgentStatusKind.Unknown
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static FrozenDictionary<string, AgentSessionRefKind> SessionRef { get; } =
        new Dictionary<string, AgentSessionRefKind>(StringComparer.Ordinal)
        {
            ["id"] = AgentSessionRefKind.Id,
            ["path"] = AgentSessionRefKind.Path
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static FrozenDictionary<string, SplitDirectionKind> Split { get; } =
        new Dictionary<string, SplitDirectionKind>(StringComparer.Ordinal)
        {
            ["right"] = SplitDirectionKind.Right,
            ["down"] = SplitDirectionKind.Down
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static FrozenDictionary<string, RpcEventKind> Events { get; } =
        new Dictionary<string, RpcEventKind>(StringComparer.Ordinal)
        {
            ["workspace_created"] = RpcEventKind.WorkspaceCreated,
            ["workspace.created"] = RpcEventKind.WorkspaceCreated,
            ["workspace_updated"] = RpcEventKind.WorkspaceUpdated,
            ["workspace.updated"] = RpcEventKind.WorkspaceUpdated,
            ["workspace_metadata_updated"] = RpcEventKind.WorkspaceMetadataUpdated,
            ["workspace.metadata_updated"] = RpcEventKind.WorkspaceMetadataUpdated,
            ["workspace_closed"] = RpcEventKind.WorkspaceClosed,
            ["workspace.closed"] = RpcEventKind.WorkspaceClosed,
            ["workspace_renamed"] = RpcEventKind.WorkspaceRenamed,
            ["workspace.renamed"] = RpcEventKind.WorkspaceRenamed,
            ["workspace_moved"] = RpcEventKind.WorkspaceMoved,
            ["workspace.moved"] = RpcEventKind.WorkspaceMoved,
            ["workspace_reordered"] = RpcEventKind.WorkspaceReordered,
            ["workspace.reordered"] = RpcEventKind.WorkspaceReordered,
            ["workspace_focused"] = RpcEventKind.WorkspaceFocused,
            ["workspace.focused"] = RpcEventKind.WorkspaceFocused,
            ["worktree_created"] = RpcEventKind.WorktreeCreated,
            ["worktree.created"] = RpcEventKind.WorktreeCreated,
            ["worktree_opened"] = RpcEventKind.WorktreeOpened,
            ["worktree.opened"] = RpcEventKind.WorktreeOpened,
            ["worktree_removed"] = RpcEventKind.WorktreeRemoved,
            ["worktree.removed"] = RpcEventKind.WorktreeRemoved,
            ["tab_created"] = RpcEventKind.TabCreated,
            ["tab.created"] = RpcEventKind.TabCreated,
            ["tab_closed"] = RpcEventKind.TabClosed,
            ["tab.closed"] = RpcEventKind.TabClosed,
            ["tab_renamed"] = RpcEventKind.TabRenamed,
            ["tab.renamed"] = RpcEventKind.TabRenamed,
            ["tab_moved"] = RpcEventKind.TabMoved,
            ["tab.moved"] = RpcEventKind.TabMoved,
            ["tab_focused"] = RpcEventKind.TabFocused,
            ["tab.focused"] = RpcEventKind.TabFocused,
            ["pane_created"] = RpcEventKind.PaneCreated,
            ["pane.created"] = RpcEventKind.PaneCreated,
            ["pane_closed"] = RpcEventKind.PaneClosed,
            ["pane.closed"] = RpcEventKind.PaneClosed,
            ["pane_updated"] = RpcEventKind.PaneUpdated,
            ["pane.updated"] = RpcEventKind.PaneUpdated,
            ["pane_focused"] = RpcEventKind.PaneFocused,
            ["pane.focused"] = RpcEventKind.PaneFocused,
            ["pane_moved"] = RpcEventKind.PaneMoved,
            ["pane.moved"] = RpcEventKind.PaneMoved,
            ["pane_output_changed"] = RpcEventKind.PaneOutputChanged,
            ["pane.output_changed"] = RpcEventKind.PaneOutputChanged,
            ["pane_exited"] = RpcEventKind.PaneExited,
            ["pane.exited"] = RpcEventKind.PaneExited,
            ["pane_agent_detected"] = RpcEventKind.PaneAgentDetected,
            ["pane.agent_detected"] = RpcEventKind.PaneAgentDetected,
            ["pane_agent_status_changed"] = RpcEventKind.PaneAgentStatusChanged,
            ["pane.agent_status_changed"] = RpcEventKind.PaneAgentStatusChanged,
            ["layout_updated"] = RpcEventKind.LayoutUpdated,
            ["layout.updated"] = RpcEventKind.LayoutUpdated
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static KnownAgentKind? AgentKind(string? raw) => raw switch
    {
        "claude" => KnownAgentKind.Claude,
        "codex" => KnownAgentKind.Codex,
        "opencode" => KnownAgentKind.OpenCode,
        _ => null
    };
}
