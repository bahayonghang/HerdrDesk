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

    public static FrozenSet<string> VerifiedWhenCompatible { get; } = FrozenSet.ToFrozenSet(
        [
            "ping",
            "session.snapshot",
            "events.subscribe",
            "workspace.list",
            "workspace.get",
            "tab.list",
            "tab.get",
            "pane.list",
            "pane.current",
            "pane.get",
            "pane.layout",
            "agent.list",
            "agent.get",
            .. MutationControl
        ],
        StringComparer.Ordinal);
}
