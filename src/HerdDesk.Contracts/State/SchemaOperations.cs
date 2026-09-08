using System.Collections.Frozen;

namespace HerdDesk.Contracts;

// Method names from herdr v0.9.0 schema (protocol 22, schema_version 1).
// server.stop / server.live_handoff stay excluded. Graphics mutation and
// endpoint generation 1 are not 1.0 verified operations.
public static class SchemaOperations
{
    public static FrozenSet<string> EntityGetters { get; } = FrozenSet.ToFrozenSet(
        [
            "workspace.get",
            "tab.get",
            "pane.get",
            "agent.get",
            "pane.layout"
        ],
        StringComparer.Ordinal);

    public static FrozenSet<string> MutationControl { get; } = FrozenSet.ToFrozenSet(
        [
            "workspace.create",
            "workspace.focus",
            "workspace.rename",
            "workspace.move",
            "workspace.move_block",
            "workspace.close",
            "tab.create",
            "tab.focus",
            "tab.rename",
            "tab.move",
            "tab.close",
            "pane.split",
            "pane.swap",
            "pane.move",
            "pane.zoom",
            "pane.focus",
            "pane.focus_direction",
            "pane.resize",
            "pane.scroll",
            "pane.rename",
            "pane.send_text",
            "pane.send_keys",
            "pane.send_input",
            "pane.close",
            "pane.input.set",
            "agent.send_keys",
            "agent.rename",
            "agent.focus",
            "agent.start",
            "agent.prompt",
            "agent.view.set",
            "agent.view.clear",
            "layout.apply",
            "layout.set_split_ratio",
            "worktree.create",
            "worktree.open",
            "worktree.remove"
        ],
        StringComparer.Ordinal);

    public const string WorkspaceCreate = "workspace.create";
    public const string WorkspaceRename = "workspace.rename";
    public const string WorkspaceClose = "workspace.close";
    public const string TabCreate = "tab.create";
    public const string TabRename = "tab.rename";
    public const string TabClose = "tab.close";
    public const string PaneRename = "pane.rename";
    public const string PaneClose = "pane.close";
    public const string AgentStart = "agent.start";
    public const string AgentRename = "agent.rename";
    public const string WorkspaceGet = "workspace.get";
    public const string TabGet = "tab.get";
    public const string PaneGet = "pane.get";
    public const string AgentGet = "agent.get";
    public const string SessionSnapshot = "session.snapshot";

    // HD-017 L1 allowlist. Other MutationControl methods stay capability-gated but unexposed.
    public static FrozenSet<string> ResourceMutations { get; } = FrozenSet.ToFrozenSet(
        [
            WorkspaceCreate,
            WorkspaceRename,
            WorkspaceClose,
            TabCreate,
            TabRename,
            TabClose,
            PaneRename,
            PaneClose,
            AgentStart,
            AgentRename
        ],
        StringComparer.Ordinal);

    public static FrozenSet<string> VerifiedWhenCompatible { get; } = FrozenSet.ToFrozenSet(
        [
            "ping",
            SessionSnapshot,
            "events.subscribe",
            "workspace.list",
            WorkspaceGet,
            "tab.list",
            TabGet,
            "pane.list",
            "pane.current",
            PaneGet,
            "pane.layout",
            "agent.list",
            AgentGet,
            .. MutationControl
        ],
        StringComparer.Ordinal);
}
