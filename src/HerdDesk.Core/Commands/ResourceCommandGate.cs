using HerdDesk.Contracts;

namespace HerdDesk.Core;

public static class ResourceCommandGate
{
    public static ResourceGateDecision EvaluateWrite(
        ResourceStoreSnapshot store,
        SessionKey session,
        string operation)
    {
        if (!IsValidSession(session))
            return ResourceGateDecision.Deny(ResourceCommandCodes.InvalidIdentity, ResourceErrorKind.Validation);
        if (string.IsNullOrEmpty(operation) || !SchemaOperations.ResourceMutations.Contains(operation))
            return ResourceGateDecision.Deny(ResourceCommandCodes.OperationDisabled, ResourceErrorKind.Capability);

        var view = FindSession(store.Projection, session);
        var capabilities = FindCapabilities(store.Projection, session);
        if (view is null || capabilities is null)
            return ResourceGateDecision.Deny(ResourceCommandCodes.StaleTarget, ResourceErrorKind.StaleTarget);

        if (store.Projection.Phase is ConnectionPhase.Incompatible ||
            capabilities.VerifiedOperations.Count == 0)
            return ResourceGateDecision.Deny(ResourceCommandCodes.SchemaIncompatible, ResourceErrorKind.Capability);
        if (store.Projection.Phase is ConnectionPhase.Offline)
            return ResourceGateDecision.Deny(ResourceCommandCodes.Offline, ResourceErrorKind.Transport);
        if (store.Projection.Phase is ConnectionPhase.Stale || store.Freshness is DeviceFreshness.Stale
            or DeviceFreshness.Unknown)
            return ResourceGateDecision.Deny(ResourceCommandCodes.Stale, ResourceErrorKind.StaleTarget);
        if (store.Freshness is not DeviceFreshness.Current ||
            store.Projection.Phase is not ConnectionPhase.Ready)
            return ResourceGateDecision.Deny(ResourceCommandCodes.OperationDisabled, ResourceErrorKind.Capability);
        if (!capabilities.HasOperation(operation))
            return ResourceGateDecision.Deny(ResourceCommandCodes.CapabilityUnknown, ResourceErrorKind.Capability);
        return ResourceGateDecision.Ok();
    }

    public static ResourceGateDecision EvaluateCreate(
        ResourceStoreSnapshot store,
        ResourceOperationKind kind,
        ResourceKey parent,
        string? name,
        string? workingDirectory,
        KnownAgentKind? agentKind,
        bool requireFields = true)
    {
        var operation = kind switch
        {
            ResourceOperationKind.CreateWorkspace => SchemaOperations.WorkspaceCreate,
            ResourceOperationKind.CreateTerminal => SchemaOperations.TabCreate,
            ResourceOperationKind.CreateAgent => SchemaOperations.AgentStart,
            _ => ""
        };
        var write = EvaluateWrite(store, parent.Session, operation);
        if (!write.Allowed)
            return write;
        if (!IsValidSession(parent.Session))
            return ResourceGateDecision.Deny(ResourceCommandCodes.InvalidIdentity, ResourceErrorKind.Validation);
        if (ContainsNul(name) || ContainsNul(workingDirectory))
            return ResourceGateDecision.Deny(ResourceCommandCodes.ValidationError, ResourceErrorKind.Validation);

        var session = FindSession(store.Projection, parent.Session);
        if (session is null)
            return ResourceGateDecision.Deny(ResourceCommandCodes.StaleTarget, ResourceErrorKind.StaleTarget);

        switch (kind)
        {
            case ResourceOperationKind.CreateWorkspace:
                return ResourceGateDecision.Ok();
            case ResourceOperationKind.CreateTerminal:
                if (string.IsNullOrWhiteSpace(parent.WorkspaceId) ||
                    FindWorkspace(session, parent.WorkspaceId) is null)
                    return ResourceGateDecision.Deny(ResourceCommandCodes.StaleTarget, ResourceErrorKind.StaleTarget);
                return ResourceGateDecision.Ok();
            case ResourceOperationKind.CreateAgent:
                if (agentKind is null || !VerifiedAgentWires.IsVerified(agentKind.Value))
                    return ResourceGateDecision.Deny(ResourceCommandCodes.AgentKindUnverified, ResourceErrorKind.Validation);
                if (requireFields && string.IsNullOrWhiteSpace(name))
                    return ResourceGateDecision.Deny(ResourceCommandCodes.ValidationError, ResourceErrorKind.Validation);
                if (string.IsNullOrWhiteSpace(parent.PaneId) || FindPane(session, parent) is null)
                    return ResourceGateDecision.Deny(ResourceCommandCodes.StaleTarget, ResourceErrorKind.StaleTarget);
                return ResourceGateDecision.Ok();
            default:
                return ResourceGateDecision.Deny(ResourceCommandCodes.OperationDisabled, ResourceErrorKind.Capability);
        }
    }

    public static ResourceGateDecision EvaluateRename(
        ResourceStoreSnapshot store,
        RenameResourceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var operation = RenameOperation(request.Target.Kind);
        var write = EvaluateWrite(store, request.Target.Session, operation);
        if (!write.Allowed)
            return write;
        if (string.IsNullOrWhiteSpace(request.NewName) || ContainsNul(request.NewName))
            return ResourceGateDecision.Deny(ResourceCommandCodes.ValidationError, ResourceErrorKind.Validation);
        return EvaluateTarget(store, request.Target, request.Expected, request.Confirmation);
    }

    public static ResourceGateDecision EvaluateClose(
        ResourceStoreSnapshot store,
        CloseResourceRequest request,
        ResourceConfirmationToken expectedToken,
        bool allowGroupClose)
    {
        ArgumentNullException.ThrowIfNull(request);
        var operation = CloseOperation(request.Target.Kind);
        var write = EvaluateWrite(store, request.Target.Session, operation);
        if (!write.Allowed)
            return write;
        if (request.CloseGroup && !allowGroupClose)
            return ResourceGateDecision.Deny(
                ResourceCommandCodes.CloseGroupUnconfirmed, ResourceErrorKind.GroupCloseRequired);
        var target = EvaluateTarget(store, request.Target, request.Expected, request.Confirmation);
        if (!target.Allowed)
            return target;
        if (request.Confirmation.Value != expectedToken.Value)
            return ResourceGateDecision.Deny(ResourceCommandCodes.StaleConfirmation, ResourceErrorKind.StaleTarget);
        return ResourceGateDecision.Ok();
    }

    public static string RenameOperation(ResourceKind kind) => kind switch
    {
        ResourceKind.Workspace => SchemaOperations.WorkspaceRename,
        ResourceKind.Tab => SchemaOperations.TabRename,
        ResourceKind.Agent => SchemaOperations.AgentRename,
        _ => SchemaOperations.PaneRename
    };

    public static string CloseOperation(ResourceKind kind) => kind switch
    {
        ResourceKind.Workspace => SchemaOperations.WorkspaceClose,
        ResourceKind.Tab => SchemaOperations.TabClose,
        _ => SchemaOperations.PaneClose
    };

    public static bool IsValidSession(SessionKey session) =>
        session.Device.Value != Guid.Empty && !string.IsNullOrWhiteSpace(session.EndpointKey);

    public static SessionProjection? FindSession(DeviceProjectionSnapshot snapshot, SessionKey session)
    {
        foreach (var device in snapshot.Devices)
        {
            foreach (var item in device.Sessions)
            {
                if (item.Session == session)
                    return item;
            }
        }

        return null;
    }

    public static CapabilityProfile? FindCapabilities(DeviceProjectionSnapshot snapshot, SessionKey session)
    {
        foreach (var device in snapshot.Devices)
        {
            foreach (var item in device.Sessions)
            {
                if (item.Session == session)
                    return device.Capabilities;
            }
        }

        return null;
    }

    public static WorkspaceProjection? FindWorkspace(SessionProjection session, string workspaceId)
    {
        foreach (var workspace in session.Workspaces)
        {
            if (workspace.WorkspaceId == workspaceId)
                return workspace;
        }

        return null;
    }

    public static TabProjection? FindTab(SessionProjection session, ResourceKey key)
    {
        if (string.IsNullOrWhiteSpace(key.TabId))
            return null;
        foreach (var tab in session.Tabs)
        {
            if (tab.TabId == key.TabId &&
                (string.IsNullOrWhiteSpace(key.WorkspaceId) || tab.WorkspaceId == key.WorkspaceId))
                return tab;
        }

        return null;
    }

    public static PaneProjection? FindPane(SessionProjection session, ResourceKey key)
    {
        if (string.IsNullOrWhiteSpace(key.PaneId))
            return null;
        foreach (var pane in session.Panes)
        {
            if (pane.Key.PaneId == key.PaneId && pane.Key.WorkspaceId == key.WorkspaceId)
                return pane;
        }

        return null;
    }

    public static AgentProjection? FindAgent(SessionProjection session, ResourceKey key)
    {
        foreach (var agent in session.Agents)
        {
            if (!string.IsNullOrWhiteSpace(key.PaneId) && agent.Pane.PaneId == key.PaneId &&
                agent.Pane.WorkspaceId == key.WorkspaceId)
                return agent;
            if (!string.IsNullOrWhiteSpace(key.TerminalId) && agent.TerminalId == key.TerminalId)
                return agent;
        }

        return null;
    }

    public static string CurrentName(SessionProjection session, ResourceKey key) => key.Kind switch
    {
        ResourceKind.Workspace => FindWorkspace(session, key.WorkspaceId)?.Label ?? "",
        ResourceKind.Tab => FindTab(session, key)?.Label ?? "",
        ResourceKind.Agent => FindAgent(session, key)?.Name ?? FindPane(session, key)?.Label ?? "",
        _ => FindPane(session, key)?.Label ?? ""
    };

    public static bool TargetExists(SessionProjection session, ResourceKey key) => key.Kind switch
    {
        ResourceKind.Workspace => FindWorkspace(session, key.WorkspaceId) is not null,
        ResourceKind.Tab => FindTab(session, key) is not null,
        ResourceKind.Agent => FindAgent(session, key) is not null || FindPane(session, key) is not null,
        _ => FindPane(session, key) is not null
    };

    static ResourceGateDecision EvaluateTarget(
        ResourceStoreSnapshot store,
        ResourceKey target,
        ResourceProjectionStamp expected,
        ResourceConfirmationToken confirmation)
    {
        if (string.IsNullOrWhiteSpace(confirmation.Value))
            return ResourceGateDecision.Deny(ResourceCommandCodes.StaleConfirmation, ResourceErrorKind.StaleTarget);
        if (store.Projection.Epoch != expected.Epoch || store.Projection.Revision != expected.Revision)
            return ResourceGateDecision.Deny(ResourceCommandCodes.StaleTarget, ResourceErrorKind.StaleTarget);
        var session = FindSession(store.Projection, target.Session);
        if (session is null || !TargetExists(session, target))
            return ResourceGateDecision.Deny(ResourceCommandCodes.StaleTarget, ResourceErrorKind.StaleTarget);
        return ResourceGateDecision.Ok();
    }

    static bool ContainsNul(string? value) => value is not null && value.Contains('\0');
}
