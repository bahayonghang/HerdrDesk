using HerdDesk.Contracts;

namespace HerdDesk.Core;

public static class ProjectionMapper
{
    public static ProjectionMapResult MapSnapshot(
        DecodedSessionSnapshot decoded,
        CapabilityProfile capabilities)
    {
        ArgumentNullException.ThrowIfNull(decoded);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (!IsValidSession(decoded.Session) || decoded.Epoch.Value <= 0)
            return ProjectionMapResult.Fail(ProjectionCodes.InvalidIdentity);

        var error = ValidateGraph(decoded);
        if (error is not null)
            return ProjectionMapResult.Fail(error);

        var phase = capabilities.VerifiedOperations.Count == 0
            ? ConnectionPhase.Incompatible
            : ConnectionPhase.Ready;
        var session = BuildSession(decoded);
        return ProjectionMapResult.Ok(new DeviceProjectionGraph(
            decoded.Session.Device,
            decoded.Session,
            decoded.Epoch,
            phase,
            capabilities,
            session));
    }

    public static ProjectionMapResult MapEntityRead(
        DeviceProjectionGraph current,
        ProjectionEntityChangeSet changeSet,
        CapabilityProfile capabilities)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(changeSet);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (changeSet.Session != current.Session)
            return ProjectionMapResult.Fail(ProjectionCodes.InvalidIdentity);
        if (!HasVerifiedGetter(capabilities, changeSet))
            return ProjectionMapResult.Fail(ProjectionCodes.FullSnapshotRequired);

        var merged = Merge(current.SessionState, changeSet);
        var decoded = ToDecoded(current, merged);
        var error = ValidateGraph(decoded);
        if (error is not null)
            return ProjectionMapResult.Fail(error == ProjectionCodes.ParentMissing
                ? ProjectionCodes.FullSnapshotRequired
                : error);
        return MapSnapshot(decoded, capabilities);
    }

    private static bool HasVerifiedGetter(CapabilityProfile capabilities, ProjectionEntityChangeSet changeSet)
    {
        if (changeSet.Workspaces.Count > 0 && !capabilities.HasOperation("workspace.get"))
            return false;
        if (changeSet.Tabs.Count > 0 && !capabilities.HasOperation("tab.get"))
            return false;
        if (changeSet.Panes.Count > 0 && !capabilities.HasOperation("pane.get"))
            return false;
        if (changeSet.Agents.Count > 0 && !capabilities.HasOperation("agent.get"))
            return false;
        if (changeSet.Layouts.Count > 0 && !capabilities.HasOperation("pane.layout"))
            return false;
        return changeSet.Workspaces.Count + changeSet.Tabs.Count + changeSet.Panes.Count +
            changeSet.Agents.Count + changeSet.Layouts.Count > 0;
    }

    private static string? ValidateGraph(DecodedSessionSnapshot decoded)
    {
        var workspaces = new HashSet<string>(StringComparer.Ordinal);
        foreach (var workspace in decoded.Workspaces)
        {
            if (string.IsNullOrWhiteSpace(workspace.WorkspaceId))
                return ProjectionCodes.RequiredFieldMissing;
            if (!workspaces.Add(workspace.WorkspaceId))
                return ProjectionCodes.DuplicateIdentity;
        }

        var tabs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tab in decoded.Tabs)
        {
            if (string.IsNullOrWhiteSpace(tab.TabId) || string.IsNullOrWhiteSpace(tab.WorkspaceId))
                return ProjectionCodes.RequiredFieldMissing;
            if (!workspaces.Contains(tab.WorkspaceId))
                return ProjectionCodes.ParentMissing;
            if (!tabs.TryAdd(tab.TabId, tab.WorkspaceId))
                return ProjectionCodes.DuplicateIdentity;
        }

        foreach (var workspace in decoded.Workspaces)
        {
            if (!string.IsNullOrWhiteSpace(workspace.ActiveTabId) &&
                (!tabs.TryGetValue(workspace.ActiveTabId, out var activeWorkspace) ||
                 activeWorkspace != workspace.WorkspaceId))
                return ProjectionCodes.ParentMissing;
        }

        var panes = new Dictionary<string, (string Workspace, string Tab)>(StringComparer.Ordinal);
        foreach (var pane in decoded.Panes)
        {
            if (string.IsNullOrWhiteSpace(pane.PaneId) ||
                string.IsNullOrWhiteSpace(pane.WorkspaceId) ||
                string.IsNullOrWhiteSpace(pane.TabId) ||
                string.IsNullOrWhiteSpace(pane.TerminalId))
                return ProjectionCodes.RequiredFieldMissing;
            if (!tabs.TryGetValue(pane.TabId, out var tabWorkspace) ||
                tabWorkspace != pane.WorkspaceId ||
                !workspaces.Contains(pane.WorkspaceId))
                return ProjectionCodes.ParentMissing;
            if (!panes.TryAdd(pane.PaneId, (pane.WorkspaceId, pane.TabId)))
                return ProjectionCodes.DuplicateIdentity;
        }

        var agents = new HashSet<string>(StringComparer.Ordinal);
        foreach (var agent in decoded.Agents)
        {
            if (string.IsNullOrWhiteSpace(agent.TerminalId) ||
                string.IsNullOrWhiteSpace(agent.WorkspaceId) ||
                string.IsNullOrWhiteSpace(agent.TabId) ||
                string.IsNullOrWhiteSpace(agent.PaneId))
                return ProjectionCodes.RequiredFieldMissing;
            if (!panes.TryGetValue(agent.PaneId, out var parent) ||
                parent.Workspace != agent.WorkspaceId ||
                parent.Tab != agent.TabId)
                return ProjectionCodes.ParentMissing;
            if (!agents.Add(agent.TerminalId))
                return ProjectionCodes.DuplicateIdentity;
        }

        var layouts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var layout in decoded.Layouts)
        {
            if (string.IsNullOrWhiteSpace(layout.WorkspaceId) || string.IsNullOrWhiteSpace(layout.TabId))
                return ProjectionCodes.RequiredFieldMissing;
            if (!tabs.TryGetValue(layout.TabId, out var tabWorkspace) ||
                tabWorkspace != layout.WorkspaceId)
                return ProjectionCodes.ParentMissing;
            if (!layouts.Add(layout.WorkspaceId + "\u001f" + layout.TabId))
                return ProjectionCodes.DuplicateIdentity;
            if (!string.IsNullOrEmpty(layout.FocusedPaneId) &&
                (!panes.TryGetValue(layout.FocusedPaneId, out var focused) ||
                 focused.Workspace != layout.WorkspaceId ||
                 focused.Tab != layout.TabId))
                return ProjectionCodes.ParentMissing;
            foreach (var pane in layout.Panes)
            {
                if (string.IsNullOrWhiteSpace(pane.PaneId))
                    return ProjectionCodes.RequiredFieldMissing;
                if (!panes.TryGetValue(pane.PaneId, out var layoutPane) ||
                    layoutPane.Workspace != layout.WorkspaceId ||
                    layoutPane.Tab != layout.TabId)
                    return ProjectionCodes.ParentMissing;
            }
        }

        var focusedError = CheckFocused(decoded.FocusedWorkspaceId, workspaces, ProjectionCodes.ParentMissing);
        if (focusedError is not null)
            return focusedError;
        if (decoded.FocusedTabId is not null && !tabs.ContainsKey(decoded.FocusedTabId))
            return ProjectionCodes.ParentMissing;
        if (decoded.FocusedPaneId is not null && !panes.ContainsKey(decoded.FocusedPaneId))
            return ProjectionCodes.ParentMissing;
        return null;
    }

    private static string? CheckFocused(string? id, HashSet<string> known, string missingCode)
    {
        if (id is null)
            return null;
        return known.Contains(id) ? null : missingCode;
    }

    private static SessionProjection BuildSession(DecodedSessionSnapshot decoded)
    {
        var session = decoded.Session;
        var workspaces = decoded.Workspaces.Select(item => new WorkspaceProjection(
            session,
            item.WorkspaceId,
            item.Number,
            item.Label,
            item.Focused,
            item.PaneCount,
            item.TabCount,
            item.ActiveTabId,
            item.AgentStatus,
            item.Worktree is null
                ? null
                : new WorktreeProjection(item.Worktree.RepoKey, item.Worktree.RepoName,
                    item.Worktree.IsLinkedWorktree))).ToArray();
        var tabs = decoded.Tabs.Select(item => new TabProjection(
            session, item.TabId, item.WorkspaceId, item.Number, item.Label, item.Focused,
            item.PaneCount, item.AgentStatus)).ToArray();
        var panes = decoded.Panes.Select(item => new PaneProjection(
            new PaneKey(session, item.WorkspaceId, item.PaneId),
            item.TerminalId,
            item.TabId,
            item.Focused,
            item.Label,
            item.AgentRaw,
            item.AgentKind,
            item.DisplayAgent,
            item.AgentStatus,
            item.Revision)).ToArray();
        var layouts = decoded.Layouts.Select(item => new LayoutProjection(
            session,
            item.WorkspaceId,
            item.TabId,
            item.Zoomed,
            item.FocusedPaneId,
            item.Panes.Select(pane => new LayoutPaneProjection(
                pane.PaneId, pane.Focused, pane.Rect.X, pane.Rect.Y, pane.Rect.Width, pane.Rect.Height))
                .ToArray())).ToArray();
        var agents = decoded.Agents.Select(item => new AgentProjection(
            session,
            item.TerminalId,
            item.TabId,
            new PaneKey(session, item.WorkspaceId, item.PaneId),
            item.Name,
            item.AgentRaw,
            item.AgentKind,
            item.DisplayAgent,
            item.AgentStatus,
            item.Focused,
            item.Revision)).ToArray();
        return new SessionProjection(
            session,
            decoded.Version,
            decoded.Protocol,
            decoded.FocusedWorkspaceId,
            decoded.FocusedTabId,
            decoded.FocusedPaneId,
            workspaces,
            tabs,
            panes,
            layouts,
            agents);
    }

    private static SessionProjection Merge(SessionProjection current, ProjectionEntityChangeSet changeSet)
    {
        var workspaces = Replace(current.Workspaces, changeSet.Workspaces,
            item => item.WorkspaceId,
            item => new WorkspaceProjection(
                current.Session, item.WorkspaceId, item.Number, item.Label, item.Focused,
                item.PaneCount, item.TabCount, item.ActiveTabId, item.AgentStatus,
                item.Worktree is null
                    ? null
                    : new WorktreeProjection(item.Worktree.RepoKey, item.Worktree.RepoName,
                        item.Worktree.IsLinkedWorktree)));
        var tabs = Replace(current.Tabs, changeSet.Tabs, item => item.TabId,
            item => new TabProjection(current.Session, item.TabId, item.WorkspaceId, item.Number,
                item.Label, item.Focused, item.PaneCount, item.AgentStatus));
        var panes = Replace(current.Panes, changeSet.Panes, item => item.Key.PaneId,
            item => new PaneProjection(
                new PaneKey(current.Session, item.WorkspaceId, item.PaneId),
                item.TerminalId, item.TabId, item.Focused, item.Label, item.AgentRaw,
                item.AgentKind, item.DisplayAgent, item.AgentStatus, item.Revision));
        var agents = Replace(current.Agents, changeSet.Agents, item => item.TerminalId,
            item => new AgentProjection(
                current.Session, item.TerminalId, item.TabId,
                new PaneKey(current.Session, item.WorkspaceId, item.PaneId),
                item.Name, item.AgentRaw, item.AgentKind, item.DisplayAgent, item.AgentStatus,
                item.Focused, item.Revision));
        var layouts = Replace(current.Layouts, changeSet.Layouts,
            item => item.WorkspaceId + "\u001f" + item.TabId,
            item => new LayoutProjection(
                current.Session, item.WorkspaceId, item.TabId, item.Zoomed, item.FocusedPaneId,
                item.Panes.Select(pane => new LayoutPaneProjection(
                    pane.PaneId, pane.Focused, pane.Rect.X, pane.Rect.Y, pane.Rect.Width, pane.Rect.Height))
                    .ToArray()));
        return current with
        {
            Workspaces = workspaces,
            Tabs = tabs,
            Panes = panes,
            Layouts = layouts,
            Agents = agents
        };
    }

    private static IReadOnlyList<TProjection> Replace<TProjection, TDecoded>(
        IReadOnlyList<TProjection> current,
        IReadOnlyList<TDecoded> incoming,
        Func<TProjection, string> currentKey,
        Func<TDecoded, TProjection> map)
        where TProjection : class
    {
        if (incoming.Count == 0)
            return current;
        var mapped = incoming.Select(map).ToArray();
        var keys = new HashSet<string>(mapped.Select(currentKey), StringComparer.Ordinal);
        var kept = current.Where(item => !keys.Contains(currentKey(item))).ToList();
        kept.AddRange(mapped);
        return kept;
    }

    private static DecodedSessionSnapshot ToDecoded(DeviceProjectionGraph current, SessionProjection session) =>
        new(
            session.Session,
            current.Epoch,
            session.ServerVersion,
            session.Protocol,
            session.FocusedWorkspaceId,
            session.FocusedTabId,
            session.FocusedPaneId,
            session.Workspaces.Select(item => new DecodedWorkspace(
                item.WorkspaceId, item.Number, item.Label, item.Focused, item.PaneCount,
                item.TabCount, item.ActiveTabId, item.AgentStatus, null,
                item.Worktree is null
                    ? null
                    : new DecodedWorktree(item.Worktree.RepoKey, item.Worktree.RepoName, "", "",
                        item.Worktree.IsLinkedWorktree, EmptyJson()),
                EmptyJson())).ToArray(),
            session.Tabs.Select(item => new DecodedTab(
                item.TabId, item.WorkspaceId, item.Number, item.Label, item.Focused,
                item.PaneCount, item.AgentStatus, EmptyJson())).ToArray(),
            session.Panes.Select(item => new DecodedPane(
                item.Key.PaneId, item.TerminalId, item.Key.WorkspaceId, item.TabId, item.Focused,
                null, null, item.Label, item.AgentRaw, item.AgentKind, null, null, item.DisplayAgent,
                item.AgentStatus, null, null, null, item.Revision, EmptyJson())).ToArray(),
            session.Layouts.Select(item => new DecodedLayout(
                item.WorkspaceId, item.TabId, item.Zoomed,
                new DecodedLayoutRect(0, 0, 0, 0), item.FocusedPaneId,
                item.Panes.Select(pane => new DecodedLayoutPane(
                    pane.PaneId, pane.Focused,
                    new DecodedLayoutRect(pane.X, pane.Y, pane.Width, pane.Height))).ToArray(),
                [], EmptyJson())).ToArray(),
            session.Agents.Select(item => new DecodedAgent(
                item.TerminalId, item.Pane.WorkspaceId, item.TabId, item.Pane.PaneId,
                item.Name, item.AgentRaw, item.AgentKind, null, item.DisplayAgent,
                item.AgentStatus, item.Focused, item.Revision, EmptyJson())).ToArray(),
            EmptyJson());

    private static System.Collections.Frozen.FrozenDictionary<string, System.Text.Json.JsonElement> EmptyJson() =>
        System.Collections.Frozen.FrozenDictionary<string, System.Text.Json.JsonElement>.Empty;

    private static bool IsValidSession(SessionKey session) =>
        session.Device.Value != Guid.Empty && !string.IsNullOrWhiteSpace(session.EndpointKey);
}
