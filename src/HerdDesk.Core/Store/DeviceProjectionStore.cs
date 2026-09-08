using System.Collections.Frozen;
using HerdDesk.Contracts;

namespace HerdDesk.Core;

public sealed class DeviceProjectionStore
{
    private readonly object _gate = new();
    private ConnectionEpoch _epoch;
    private long _revision;
    private ConnectionPhase _phase = ConnectionPhase.Offline;
    private bool _replaceOnNextInstall;
    private DeviceProjectionSnapshot _snapshot = DeviceProjectionSnapshot.Empty;
    private readonly Dictionary<SessionKey, DeviceProjectionGraph> _sessions = new();

    public DeviceProjectionSnapshot Read()
    {
        lock (_gate)
            return _snapshot;
    }

    public ProjectionInstallResult InstallSnapshot(ConnectionEpoch epoch, DeviceProjectionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        lock (_gate)
        {
            if (graph.Device != graph.Session.Device || graph.SessionState.Session != graph.Session)
                return ProjectionInstallResult.Fail(ProjectionCodes.InvalidIdentity);
            if (graph.Epoch != epoch)
                return ProjectionInstallResult.Fail(ProjectionCodes.StaleEpoch);
            var guard = GuardEpoch(epoch, initialize: true);
            if (guard is not null)
                return ProjectionInstallResult.Fail(guard);
            if (_replaceOnNextInstall)
            {
                _sessions.Clear();
                _replaceOnNextInstall = false;
            }
            _sessions[graph.Session] = graph;
            _phase = ComputePhase();
            return Commit();
        }
    }

    public ProjectionInstallResult InstallEntityRead(
        ConnectionEpoch epoch,
        ProjectionEntityChangeSet changeSet)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        lock (_gate)
        {
            var guard = GuardEpoch(epoch, initialize: false);
            if (guard is not null)
                return ProjectionInstallResult.Fail(guard);
            if (_replaceOnNextInstall || !_sessions.TryGetValue(changeSet.Session, out var current))
                return ProjectionInstallResult.Fail(ProjectionCodes.FullSnapshotRequired);
            var mapped = ProjectionMapper.MapEntityRead(current, changeSet, current.Capabilities);
            if (!mapped.Succeeded)
                return ProjectionInstallResult.Fail(mapped.Code!);
            _sessions[changeSet.Session] = mapped.Graph!;
            _phase = ComputePhase();
            return Commit();
        }
    }

    public ProjectionInstallResult MarkStale(ConnectionEpoch epoch, string reason)
    {
        _ = reason;
        lock (_gate)
        {
            var guard = GuardEpoch(epoch, initialize: false);
            if (guard is not null)
                return ProjectionInstallResult.Fail(guard);
            _phase = ConnectionPhase.Stale;
            return Commit();
        }
    }

    public ProjectionInstallResult ClearForNewEpoch(ConnectionEpoch epoch)
    {
        lock (_gate)
        {
            if (epoch.Value <= 0)
                return ProjectionInstallResult.Fail(ProjectionCodes.InvalidIdentity);
            if (_epoch.Value != 0 && epoch.Value <= _epoch.Value)
                return ProjectionInstallResult.Fail(ProjectionCodes.StaleEpoch);
            _epoch = epoch;
            _replaceOnNextInstall = true;
            _phase = _sessions.Count == 0 ? ConnectionPhase.Offline : ConnectionPhase.Stale;
            return Commit();
        }
    }

    private string? GuardEpoch(ConnectionEpoch epoch, bool initialize)
    {
        if (epoch.Value <= 0)
            return ProjectionCodes.InvalidIdentity;
        if (_epoch.Value == 0)
        {
            if (!initialize)
                return ProjectionCodes.StaleEpoch;
            _epoch = epoch;
            return null;
        }
        return epoch == _epoch ? null : ProjectionCodes.StaleEpoch;
    }

    private ConnectionPhase ComputePhase()
    {
        if (_sessions.Count == 0)
            return ConnectionPhase.Offline;
        if (_phase == ConnectionPhase.Stale && _replaceOnNextInstall)
            return ConnectionPhase.Stale;
        if (_sessions.Values.Any(item => item.Phase == ConnectionPhase.Incompatible))
            return ConnectionPhase.Incompatible;
        return ConnectionPhase.Ready;
    }

    private ProjectionInstallResult Commit()
    {
        _revision++;
        var devices = _sessions.Values
            .GroupBy(item => item.Device)
            .OrderBy(item => item.Key.Value)
            .Select(group =>
            {
                var members = group.ToArray();
                var sessions = members
                    .OrderBy(item => item.Session.EndpointKey, StringComparer.Ordinal)
                    .ThenBy(item => item.Session.SessionName, StringComparer.Ordinal)
                    .Select(item => item.SessionState)
                    .ToArray();
                return new ProjectedDevice(group.Key, DeviceCapabilities(members), sessions);
            })
            .ToArray();
        _snapshot = new DeviceProjectionSnapshot(_epoch, _revision, _phase, devices);
        return ProjectionInstallResult.Ok(_snapshot);
    }

    private static CapabilityProfile DeviceCapabilities(DeviceProjectionGraph[] graphs)
    {
        var first = graphs[0].Capabilities;
        if (graphs.Any(item => item.Capabilities.VerifiedOperations.Count == 0))
            return first with { VerifiedOperations = FrozenSet<string>.Empty };
        return first;
    }
}
