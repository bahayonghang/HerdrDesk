using HerdDesk.Contracts;

namespace HerdDesk.Core;

public readonly record struct DirtyScope(
    bool WholeSession,
    string? WorkspaceId,
    string? TabId,
    string? PaneId,
    string? TerminalId,
    bool Layout)
{
    public static DirtyScope Whole() => new(true, null, null, null, null, false);

    public static DirtyScope Workspace(string id) => new(false, id, null, null, null, false);

    public static DirtyScope Tab(string id) => new(false, null, id, null, null, false);

    public static DirtyScope Pane(string id) => new(false, null, null, id, null, false);

    public static DirtyScope Agent(string terminalId) => new(false, null, null, null, terminalId, false);

    public static DirtyScope ForLayout(string workspaceId, string tabId) =>
        new(false, workspaceId, tabId, null, null, true);

    public string Key
    {
        get
        {
            if (WholeSession)
                return "*";
            if (Layout)
                return "l:" + WorkspaceId + "\u001f" + TabId;
            if (WorkspaceId is not null)
                return "w:" + WorkspaceId;
            if (TabId is not null)
                return "t:" + TabId;
            if (PaneId is not null)
                return "p:" + PaneId;
            if (TerminalId is not null)
                return "a:" + TerminalId;
            return "*";
        }
    }
}

public static class ReconcilePlanner
{
    public static DirtyScope Plan(
        DecodedRpcEvent evt,
        CapabilityProfile capabilities,
        bool degradedFullSnapshotOnly)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (degradedFullSnapshotOnly || evt.Event.Known is null)
            return DirtyScope.Whole();

        return evt.Event.Known.Value switch
        {
            RpcEventKind.WorkspaceCreated or
                RpcEventKind.WorkspaceClosed or
                RpcEventKind.WorkspaceMoved or
                RpcEventKind.WorkspaceReordered or
                RpcEventKind.TabCreated or
                RpcEventKind.TabClosed or
                RpcEventKind.TabMoved or
                RpcEventKind.PaneCreated or
                RpcEventKind.PaneClosed or
                RpcEventKind.PaneExited or
                RpcEventKind.PaneMoved or
                RpcEventKind.WorktreeCreated or
                RpcEventKind.WorktreeOpened or
                RpcEventKind.WorktreeRemoved => DirtyScope.Whole(),
            RpcEventKind.LayoutUpdated => PlanLayout(evt, capabilities),
            RpcEventKind.WorkspaceUpdated or
                RpcEventKind.WorkspaceMetadataUpdated or
                RpcEventKind.WorkspaceRenamed or
                RpcEventKind.WorkspaceFocused =>
                EntityOrWhole(evt.WorkspaceId ?? evt.Workspace?.WorkspaceId, "workspace.get",
                    capabilities, DirtyScope.Workspace),
            RpcEventKind.TabRenamed or RpcEventKind.TabFocused =>
                EntityOrWhole(evt.TabId ?? evt.Tab?.TabId, "tab.get", capabilities, DirtyScope.Tab),
            RpcEventKind.PaneUpdated or RpcEventKind.PaneFocused or RpcEventKind.PaneOutputChanged =>
                EntityOrWhole(evt.PaneId ?? evt.Pane?.PaneId, "pane.get", capabilities, DirtyScope.Pane),
            RpcEventKind.PaneAgentDetected or RpcEventKind.PaneAgentStatusChanged =>
                PlanAgent(evt, capabilities),
            _ => DirtyScope.Whole()
        };
    }

    private static DirtyScope PlanLayout(DecodedRpcEvent evt, CapabilityProfile capabilities)
    {
        var workspace = evt.WorkspaceId ?? evt.Layout?.WorkspaceId;
        var tab = evt.TabId ?? evt.Layout?.TabId;
        if (string.IsNullOrWhiteSpace(workspace) || string.IsNullOrWhiteSpace(tab) ||
            !capabilities.HasOperation("pane.layout"))
            return DirtyScope.Whole();
        return DirtyScope.ForLayout(workspace, tab);
    }

    private static DirtyScope PlanAgent(DecodedRpcEvent evt, CapabilityProfile capabilities)
    {
        var terminal = evt.Pane?.TerminalId;
        if (!string.IsNullOrWhiteSpace(terminal) && capabilities.HasOperation("agent.get"))
            return DirtyScope.Agent(terminal);
        return EntityOrWhole(evt.PaneId ?? evt.Pane?.PaneId, "pane.get", capabilities, DirtyScope.Pane);
    }

    private static DirtyScope EntityOrWhole(
        string? id,
        string getter,
        CapabilityProfile capabilities,
        Func<string, DirtyScope> create)
    {
        if (string.IsNullOrWhiteSpace(id) || !capabilities.HasOperation(getter))
            return DirtyScope.Whole();
        return create(id);
    }
}

internal sealed class DirtyTracker
{
    private readonly Dictionary<string, long> _items = new(StringComparer.Ordinal);

    public long Generation { get; private set; }
    public int Count => _items.Count;
    public bool RequiresFullSnapshot => _items.ContainsKey("*");

    public long Add(DirtyScope scope)
    {
        Generation++;
        if (scope.WholeSession || string.IsNullOrEmpty(scope.Key) || scope.Key == "*")
        {
            _items.Clear();
            _items["*"] = Generation;
            return Generation;
        }

        if (!_items.ContainsKey("*"))
            _items[scope.Key] = Generation;
        return Generation;
    }

    public void ClearUpTo(long generation)
    {
        foreach (var key in _items.Keys.ToArray())
        {
            if (_items[key] <= generation)
                _items.Remove(key);
        }
    }

    public DirtyScope[] Items()
    {
        if (_items.ContainsKey("*"))
            return [DirtyScope.Whole()];
        return _items.Keys.Select(Parse).ToArray();
    }

    private static DirtyScope Parse(string key)
    {
        if (key == "*")
            return DirtyScope.Whole();
        if (key.StartsWith("w:", StringComparison.Ordinal))
            return DirtyScope.Workspace(key[2..]);
        if (key.StartsWith("t:", StringComparison.Ordinal))
            return DirtyScope.Tab(key[2..]);
        if (key.StartsWith("p:", StringComparison.Ordinal))
            return DirtyScope.Pane(key[2..]);
        if (key.StartsWith("a:", StringComparison.Ordinal))
            return DirtyScope.Agent(key[2..]);
        if (key.StartsWith("l:", StringComparison.Ordinal))
        {
            var parts = key[2..].Split('\u001f');
            if (parts.Length == 2)
                return DirtyScope.ForLayout(parts[0], parts[1]);
        }

        return DirtyScope.Whole();
    }
}
