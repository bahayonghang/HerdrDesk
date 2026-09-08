using HerdDesk.Contracts;

namespace HerdDesk.Core;

public sealed class GlobalTargetResolver
{
    private readonly GlobalProjectionStore? _store;
    private readonly ICatalogProjection? _catalog;

    public GlobalTargetResolver(GlobalProjectionStore? store = null, ICatalogProjection? catalog = null)
    {
        if (store is null && catalog is null)
            throw new ArgumentException(AggregationCodes.InvalidIdentity);
        _store = store;
        _catalog = catalog;
    }

    public ResolveResult Resolve(GlobalEntityRef target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Device.Value == Guid.Empty)
            return ResolveResult.Expired(AggregationCodes.InvalidIdentity);
        if (_store is not null)
            return ResolvePartition(target);
        if (_catalog is not null)
            return ResolveCatalog(target);
        return ResolveResult.Expired();
    }

    public ResolveResult ResolveNotification(NotificationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.RouteVersion != NotificationTarget.CurrentRouteVersion)
            return ResolveResult.Expired();
        var entity = new GlobalEntityRef(
            GlobalEntityKind.Pane,
            target.Key.Pane.Session.Device,
            target.Key.Pane.Session,
            target.Key.Pane.WorkspaceId,
            target.Key.Pane,
            target.Key.EntityId,
            new FreshnessStamp(target.Epoch, 0));
        return Resolve(entity);
    }

    private ResolveResult ResolvePartition(GlobalEntityRef target)
    {
        if (_store is null || !_store.TryGetPartition(target.Device, out var partition))
            return ResolveResult.Expired();
        var epoch = target.Session is { } session ? partition.EpochFor(session) : partition.Epoch;
        if (target.Stamp.Epoch.Value > 0 && target.Stamp.Epoch != epoch)
            return new ResolveResult(ResolveStatus.Expired, target, partition, AggregationCodes.Expired, partition.DisplayLabel);
        if (!Exists(partition, target))
            return new ResolveResult(ResolveStatus.Expired, target, partition, AggregationCodes.Expired, partition.DisplayLabel);
        if (partition.Readiness == PartitionReadiness.Empty && target.Kind != GlobalEntityKind.Device)
            return new ResolveResult(ResolveStatus.Expired, target, partition, AggregationCodes.Expired, partition.DisplayLabel);
        var status = StatusOf(partition);
        var current = target with { Stamp = new FreshnessStamp(epoch, partition.Generation) };
        return new ResolveResult(status, current, partition, CodeOf(status, partition), LabelOf(partition, target));
    }

    private ResolveResult ResolveCatalog(GlobalEntityRef target)
    {
        if (_catalog is null)
            return ResolveResult.Expired();
        var epoch = _catalog.EpochFor(target.Device, target.Session);
        if (target.Stamp.Epoch.Value > 0 && target.Stamp.Epoch != epoch)
            return ResolveResult.Expired();
        if (!CatalogExists(target))
            return ResolveResult.Expired();
        var phase = _catalog.PhaseFor(target.Device);
        var error = _catalog.ErrorFor(target.Device);
        var freshness = _catalog.FreshnessFor(target.Device);
        var status = CatalogStatus(phase, freshness, error, target.Device);
        var current = target with { Stamp = new FreshnessStamp(epoch, 0) };
        return new ResolveResult(status, current, null, CatalogCode(status, error), _catalog.LabelFor(target.Device));
    }

    private static bool Exists(DevicePartition partition, GlobalEntityRef target) =>
        target.Kind switch
        {
            GlobalEntityKind.Device => partition.Device == target.Device,
            GlobalEntityKind.Session => target.Session is { } session &&
                                        partition.Sessions.Any(item => item.Session == session),
            GlobalEntityKind.Workspace => target.Session is { } session &&
                                          target.WorkspaceId is { } workspace &&
                                          HasWorkspace(partition, session, workspace),
            GlobalEntityKind.Pane or GlobalEntityKind.Agent => target.Pane is { } pane &&
                                                               HasPane(partition, pane),
            _ => false
        };

    private bool CatalogExists(GlobalEntityRef target)
    {
        if (_catalog is null)
            return false;
        return target.Kind switch
        {
            GlobalEntityKind.Device => _catalog.FindDevice(target.Device) is not null,
            GlobalEntityKind.Session => target.Session is { } session &&
                                        _catalog.FindSession(session) is not null,
            GlobalEntityKind.Workspace => target.Session is { } session &&
                                          target.WorkspaceId is { } workspace &&
                                          _catalog.FindWorkspace(session, workspace) is not null,
            GlobalEntityKind.Pane or GlobalEntityKind.Agent => target.Pane is { } pane &&
                                                               _catalog.FindPane(pane) is not null,
            _ => false
        };
    }

    private static bool HasWorkspace(DevicePartition partition, SessionKey session, string workspaceId)
    {
        foreach (var projected in partition.Sessions)
        {
            if (projected.Session != session)
                continue;
            foreach (var workspace in projected.Workspaces)
            {
                if (workspace.WorkspaceId == workspaceId)
                    return true;
            }
        }

        return false;
    }

    private static bool HasPane(DevicePartition partition, PaneKey pane)
    {
        if (pane.Session.Device != partition.Device)
            return false;
        foreach (var session in partition.Sessions)
        {
            foreach (var item in session.Panes)
            {
                if (item.Key == pane)
                    return true;
            }
        }

        return false;
    }

    private static ResolveStatus StatusOf(DevicePartition partition) =>
        partition.Readiness switch
        {
            PartitionReadiness.Offline => ResolveStatus.Offline,
            PartitionReadiness.Stale => ResolveStatus.Stale,
            PartitionReadiness.Incompatible => ResolveStatus.Incompatible,
            PartitionReadiness.AuthRequired => ResolveStatus.AuthRequired,
            PartitionReadiness.PermissionDenied => ResolveStatus.PermissionDenied,
            PartitionReadiness.Cancelling => ResolveStatus.Stale,
            PartitionReadiness.Error => ResolveStatus.Resolved,
            PartitionReadiness.Empty => ResolveStatus.Resolved,
            PartitionReadiness.Loading => ResolveStatus.Stale,
            _ => ResolveStatus.Resolved
        };

    private ResolveStatus CatalogStatus(
        ConnectionPhase phase,
        DeviceFreshness freshness,
        string? error,
        DeviceId device)
    {
        if (error is RecoveryCodes.Authentication or "auth_required" or "auth_failed")
            return ResolveStatus.AuthRequired;
        if (error is "permission_denied")
            return ResolveStatus.PermissionDenied;
        if (phase == ConnectionPhase.Incompatible)
            return ResolveStatus.Incompatible;
        if (phase == ConnectionPhase.Offline)
            return ResolveStatus.Offline;
        if (phase == ConnectionPhase.Stale || freshness == DeviceFreshness.Stale)
            return ResolveStatus.Stale;
        var projected = _catalog?.FindDevice(device);
        if (projected is not null && projected.Capabilities.VerifiedOperations.Count == 0)
            return ResolveStatus.Incompatible;
        return ResolveStatus.Resolved;
    }

    private static string CodeOf(ResolveStatus status, DevicePartition partition) =>
        status switch
        {
            ResolveStatus.Resolved => AggregationCodes.Allowed,
            ResolveStatus.Offline => AggregationCodes.Offline,
            ResolveStatus.Stale => AggregationCodes.Stale,
            ResolveStatus.Incompatible => AggregationCodes.Incompatible,
            ResolveStatus.AuthRequired => AggregationCodes.AuthRequired,
            ResolveStatus.PermissionDenied => AggregationCodes.PermissionDenied,
            _ => partition.ErrorCode ?? AggregationCodes.Expired
        };

    private static string CatalogCode(ResolveStatus status, string? error) =>
        status switch
        {
            ResolveStatus.Resolved => AggregationCodes.Allowed,
            ResolveStatus.Offline => AggregationCodes.Offline,
            ResolveStatus.Stale => AggregationCodes.Stale,
            ResolveStatus.Incompatible => AggregationCodes.Incompatible,
            ResolveStatus.AuthRequired => AggregationCodes.AuthRequired,
            ResolveStatus.PermissionDenied => AggregationCodes.PermissionDenied,
            _ => error ?? AggregationCodes.Expired
        };

    private static string? LabelOf(DevicePartition partition, GlobalEntityRef target)
    {
        if (target.Kind == GlobalEntityKind.Device)
            return partition.DisplayLabel;
        if (target.Pane is { } pane)
        {
            foreach (var session in partition.Sessions)
            {
                foreach (var item in session.Panes)
                {
                    if (item.Key == pane)
                        return string.IsNullOrEmpty(item.Label) ? pane.PaneId : item.Label;
                }
            }
        }

        return partition.DisplayLabel;
    }
}

public interface ICatalogProjection
{
    ConnectionEpoch EpochFor(DeviceId device, SessionKey? session);
    ConnectionPhase PhaseFor(DeviceId device);
    DeviceFreshness FreshnessFor(DeviceId device);
    string? ErrorFor(DeviceId device);
    string LabelFor(DeviceId device);
    ProjectedDevice? FindDevice(DeviceId device);
    SessionProjection? FindSession(SessionKey session);
    WorkspaceProjection? FindWorkspace(SessionKey session, string workspaceId);
    PaneProjection? FindPane(PaneKey pane);
}
