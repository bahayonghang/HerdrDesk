using HerdDesk.Contracts;

namespace HerdDesk.Core;

internal sealed record ResourceExpectation(
    ResourceOperationKind Kind,
    ResourceKey Target,
    ConnectionEpoch Epoch,
    string? ExpectedName,
    string? ExpectedWorkspaceId,
    string? ExpectedTabId,
    string? ExpectedPaneId,
    KnownAgentKind? ExpectedAgentKind,
    IReadOnlyList<string> BaselineIds,
    bool ExpectCreated,
    bool ExpectAbsent);

internal static class ResourcePostcondition
{
    public static bool Matches(ResourceStoreSnapshot store, ResourceExpectation expected)
    {
        if (store.Projection.Epoch != expected.Epoch)
            return false;
        var session = ResourceCommandGate.FindSession(store.Projection, expected.Target.Session);
        if (session is null)
            return expected.ExpectAbsent;

        if (expected.ExpectAbsent)
            return !ResourceCommandGate.TargetExists(session, expected.Target);

        if (expected.ExpectCreated)
            return MatchesCreated(session, expected);

        if (expected.ExpectedName is not null)
            return string.Equals(
                ResourceCommandGate.CurrentName(session, expected.Target),
                expected.ExpectedName,
                StringComparison.Ordinal);

        return ResourceCommandGate.TargetExists(session, expected.Target);
    }

    public static bool MutationDidNotOccur(ResourceStoreSnapshot store, ResourceExpectation expected)
    {
        if (store.Projection.Epoch != expected.Epoch)
            return false;
        var session = ResourceCommandGate.FindSession(store.Projection, expected.Target.Session);
        if (session is null)
            return false;
        if (expected.ExpectCreated)
        {
            if (MatchesCreated(session, expected))
                return false;
            return expected.Kind switch
            {
                ResourceOperationKind.CreateWorkspace =>
                    ExtraCount(WorkspaceIds(session), expected.BaselineIds) == 0,
                ResourceOperationKind.CreateTerminal =>
                    ExtraCount(TabIds(session, expected.Target.WorkspaceId), expected.BaselineIds) == 0,
                ResourceOperationKind.CreateAgent => false,
                _ => false
            };
        }

        if (expected.ExpectAbsent)
            return ResourceCommandGate.TargetExists(session, expected.Target);
        return !string.Equals(
            ResourceCommandGate.CurrentName(session, expected.Target),
            expected.ExpectedName,
            StringComparison.Ordinal);
    }

    public static ResourceKey? CreatedKey(SessionProjection session, ResourceExpectation expected)
    {
        switch (expected.Kind)
        {
            case ResourceOperationKind.CreateWorkspace:
                var workspaceId = expected.ExpectedWorkspaceId ?? UniqueNew(WorkspaceIds(session), expected.BaselineIds);
                if (workspaceId is null)
                    return null;
                return expected.Target with { Kind = ResourceKind.Workspace, WorkspaceId = workspaceId };
            case ResourceOperationKind.CreateTerminal:
                var tabId = expected.ExpectedTabId ?? UniqueNew(TabIds(session, expected.Target.WorkspaceId), expected.BaselineIds);
                if (tabId is null)
                    return null;
                return expected.Target with { Kind = ResourceKind.Tab, TabId = tabId };
            case ResourceOperationKind.CreateAgent:
                return expected.Target with { Kind = ResourceKind.Agent };
            default:
                return expected.Target;
        }
    }

    static bool MatchesCreated(SessionProjection session, ResourceExpectation expected)
    {
        switch (expected.Kind)
        {
            case ResourceOperationKind.CreateWorkspace:
                if (expected.ExpectedWorkspaceId is not null)
                    return ResourceCommandGate.FindWorkspace(session, expected.ExpectedWorkspaceId) is not null;
                return UniqueNew(WorkspaceIds(session), expected.BaselineIds) is not null;
            case ResourceOperationKind.CreateTerminal:
                if (expected.ExpectedTabId is not null)
                    return ResourceCommandGate.FindTab(
                        session,
                        expected.Target with { Kind = ResourceKind.Tab, TabId = expected.ExpectedTabId }) is not null;
                return UniqueNew(TabIds(session, expected.Target.WorkspaceId), expected.BaselineIds) is not null;
            case ResourceOperationKind.CreateAgent:
                var pane = ResourceCommandGate.FindPane(session, expected.Target);
                var agent = ResourceCommandGate.FindAgent(session, expected.Target);
                if (expected.ExpectedAgentKind is { } kind)
                    return pane?.AgentKind == kind || agent?.AgentKind == kind;
                return pane is not null;
            default:
                return false;
        }
    }

    static string? UniqueNew(IReadOnlyList<string> current, IReadOnlyList<string> baseline)
    {
        string? found = null;
        foreach (var id in current)
        {
            if (IsBaseline(id, baseline))
                continue;
            if (found is not null)
                return null;
            found = id;
        }

        return found;
    }

    static int ExtraCount(IReadOnlyList<string> current, IReadOnlyList<string> baseline)
    {
        var extra = 0;
        foreach (var id in current)
        {
            if (!IsBaseline(id, baseline))
                extra++;
        }

        return extra;
    }

    static bool IsBaseline(string id, IReadOnlyList<string> baseline)
    {
        foreach (var prior in baseline)
        {
            if (prior == id)
                return true;
        }

        return false;
    }

    static IReadOnlyList<string> WorkspaceIds(SessionProjection session)
    {
        var ids = new string[session.Workspaces.Count];
        for (var i = 0; i < session.Workspaces.Count; i++)
            ids[i] = session.Workspaces[i].WorkspaceId;
        return ids;
    }

    static IReadOnlyList<string> TabIds(SessionProjection session, string workspaceId)
    {
        var ids = new List<string>();
        foreach (var tab in session.Tabs)
        {
            if (tab.WorkspaceId == workspaceId)
                ids.Add(tab.TabId);
        }

        return ids;
    }
}
