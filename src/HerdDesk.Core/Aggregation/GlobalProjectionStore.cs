using HerdDesk.Contracts;

namespace HerdDesk.Core;

public sealed class GlobalProjectionStore
{
    private readonly object _gate = new();
    private readonly Dictionary<DeviceId, DevicePartition> _partitions = new();
    private readonly GlobalSearchIndex _index = new();
    private long _generation;

    public int PartitionCount
    {
        get
        {
            lock (_gate)
                return _partitions.Count;
        }
    }

    public int DocumentCount
    {
        get
        {
            lock (_gate)
                return _index.DocumentCount;
        }
    }

    public int TerminalProcessDelta => 0;

    public AggregateView Read()
    {
        lock (_gate)
            return Snapshot();
    }

    public bool TryGetPartition(DeviceId device, out DevicePartition partition)
    {
        lock (_gate)
            return _partitions.TryGetValue(device, out partition!);
    }

    public PartitionApplyResult ReplacePartition(DevicePartition partition)
    {
        ArgumentNullException.ThrowIfNull(partition);
        lock (_gate)
        {
            if (!IsValid(partition.Device) || partition.Epoch.Value <= 0)
                return PartitionApplyResult.Fail(AggregationCodes.InvalidIdentity);
            if (_partitions.TryGetValue(partition.Device, out var current) &&
                partition.Epoch.Value < current.Epoch.Value)
                return PartitionApplyResult.Fail(AggregationCodes.StaleEpoch);
            var generation = current is null ? 1 : current.Generation + 1;
            var stored = partition with { Generation = generation };
            _partitions[partition.Device] = stored;
            _index.ReplaceDevice(stored);
            _generation++;
            return PartitionApplyResult.Ok(Snapshot());
        }
    }

    public PartitionApplyResult ApplySession(
        DeviceSessionState state,
        int? userOrder = null,
        string? displayLabel = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate)
        {
            var device = state.Session.Device;
            if (!IsValid(device) || !IsValid(state.Session) || state.Epoch.Value <= 0)
                return PartitionApplyResult.Fail(AggregationCodes.InvalidIdentity);
            if (state.Session.Device != device)
                return PartitionApplyResult.Fail(AggregationCodes.WrongDevice);
            if (_partitions.TryGetValue(device, out var current))
            {
                var existingEpoch = current.EpochFor(state.Session);
                if (existingEpoch.Value > 0 && state.Epoch.Value < existingEpoch.Value)
                    return PartitionApplyResult.Fail(AggregationCodes.StaleEpoch);
            }

            var incoming = SessionFrom(state);
            var sessions = MergeSessions(current, incoming, state.Session);
            var epochs = MergeEpochs(current, state.Session, state.Epoch);
            var capabilities = MergeCapabilities(current, state);
            var label = GlobalSearchIndex.Strip(
                displayLabel ?? current?.DisplayLabel ?? device.Value.ToString("D"));
            var order = userOrder ?? current?.UserOrder ?? 0;
            var partitionEpoch = current is not null && HasOtherSession(current, state.Session)
                ? current.Epoch
                : state.Epoch;
            var partition = new DevicePartition(
                device,
                partitionEpoch,
                (current?.Generation ?? 0) + 1,
                state.Phase,
                state.Freshness,
                capabilities,
                MapReadiness(state, sessions.Count),
                SanitizeError(state.LastErrorCode),
                label,
                order,
                sessions,
                epochs);
            _partitions[device] = partition;
            _index.ReplaceDevice(partition);
            _generation++;
            return PartitionApplyResult.Ok(Snapshot());
        }
    }

    public PartitionApplyResult SetPresentation(DeviceId device, int userOrder, string displayLabel)
    {
        lock (_gate)
        {
            if (!IsValid(device))
                return PartitionApplyResult.Fail(AggregationCodes.InvalidIdentity);
            if (!_partitions.TryGetValue(device, out var current))
                return PartitionApplyResult.Fail(AggregationCodes.Expired);
            var updated = current with
            {
                UserOrder = userOrder,
                DisplayLabel = GlobalSearchIndex.Strip(displayLabel ?? current.DisplayLabel),
                Generation = current.Generation + 1
            };
            _partitions[device] = updated;
            _index.ReplaceDevice(updated);
            _generation++;
            return PartitionApplyResult.Ok(Snapshot());
        }
    }

    public PartitionApplyResult RemoveDevice(DeviceId device)
    {
        lock (_gate)
        {
            if (!IsValid(device))
                return PartitionApplyResult.Fail(AggregationCodes.InvalidIdentity);
            _partitions.Remove(device);
            _index.RemoveDevice(device);
            _generation++;
            return PartitionApplyResult.Ok(Snapshot());
        }
    }

    public GlobalSearchResult Search(
        GlobalSearchQuery query,
        IReadOnlyList<AggregationRecent>? recents = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        lock (_gate)
        {
            var view = Snapshot();
            return _index.Query(query, recents, view.Readiness);
        }
    }

    public PaneProjection? FindPane(PaneKey key)
    {
        lock (_gate)
        {
            if (!_partitions.TryGetValue(key.Session.Device, out var partition))
                return null;
            foreach (var session in partition.Sessions)
            {
                foreach (var pane in session.Panes)
                {
                    if (pane.Key == key)
                        return pane;
                }
            }

            return null;
        }
    }

    public WorkspaceProjection? FindWorkspace(SessionKey session, string workspaceId)
    {
        lock (_gate)
        {
            if (!_partitions.TryGetValue(session.Device, out var partition))
                return null;
            foreach (var projected in partition.Sessions)
            {
                if (projected.Session != session)
                    continue;
                foreach (var workspace in projected.Workspaces)
                {
                    if (workspace.WorkspaceId == workspaceId)
                        return workspace;
                }
            }

            return null;
        }
    }

    public SessionProjection? FindSession(SessionKey session)
    {
        lock (_gate)
        {
            if (!_partitions.TryGetValue(session.Device, out var partition))
                return null;
            foreach (var projected in partition.Sessions)
            {
                if (projected.Session == session)
                    return projected;
            }

            return null;
        }
    }

    private AggregateView Snapshot()
    {
        var partitions = _partitions.Values
            .OrderBy(item => item.UserOrder)
            .ThenBy(item => item.Device.Value)
            .ToArray();
        var ready = 0;
        var loading = 0;
        foreach (var partition in partitions)
        {
            if (partition.Readiness == PartitionReadiness.Ready)
                ready++;
            if (partition.Readiness == PartitionReadiness.Loading)
                loading++;
        }

        var readiness = ComputeReadiness(partitions.Length, ready, loading);
        return new AggregateView(
            partitions, readiness, ready, partitions.Length, null, 0, _generation);
    }

    private static AggregateReadiness ComputeReadiness(int total, int ready, int loading)
    {
        if (total == 0)
            return AggregateReadiness.Empty;
        if (ready == total)
            return AggregateReadiness.Ready;
        if (loading == total)
            return AggregateReadiness.Loading;
        if (ready > 0 && ready < total)
            return loading > 0 ? AggregateReadiness.PartialReady : AggregateReadiness.Degraded;
        if (loading > 0)
            return AggregateReadiness.PartialReady;
        return AggregateReadiness.Degraded;
    }

    private static SessionProjection? SessionFrom(DeviceSessionState state)
    {
        foreach (var device in state.Projection.Devices)
        {
            if (device.Device != state.Session.Device)
                continue;
            foreach (var session in device.Sessions)
            {
                if (session.Session == state.Session)
                    return session;
            }
        }

        return null;
    }

    private static IReadOnlyList<SessionProjection> MergeSessions(
        DevicePartition? current,
        SessionProjection? incoming,
        SessionKey session)
    {
        var list = new List<SessionProjection>();
        if (current is not null)
        {
            foreach (var item in current.Sessions)
            {
                if (item.Session != session)
                    list.Add(item);
            }
        }

        if (incoming is not null)
            list.Add(incoming);
        list.Sort(static (left, right) =>
        {
            var endpoint = string.CompareOrdinal(left.Session.EndpointKey, right.Session.EndpointKey);
            return endpoint != 0
                ? endpoint
                : string.CompareOrdinal(left.Session.SessionName, right.Session.SessionName);
        });
        return list;
    }

    private static IReadOnlyList<SessionEpochStamp> MergeEpochs(
        DevicePartition? current,
        SessionKey session,
        ConnectionEpoch epoch)
    {
        var list = new List<SessionEpochStamp>();
        if (current is not null)
        {
            foreach (var item in current.SessionEpochs)
            {
                if (item.Session != session)
                    list.Add(item);
            }
        }

        list.Add(new SessionEpochStamp(session, epoch));
        return list;
    }

    private static CapabilityProfile MergeCapabilities(
        DevicePartition? current,
        DeviceSessionState state)
    {
        if (current is null)
            return state.Capabilities;
        if (state.Capabilities.VerifiedOperations.Count == 0)
            return state.Capabilities;
        if (current.Capabilities.VerifiedOperations.Count == 0 &&
            current.Sessions.Any(item => item.Session != state.Session))
            return current.Capabilities;
        return state.Capabilities;
    }

    internal static PartitionReadiness MapReadiness(DeviceSessionState state, int sessionCount)
    {
        var error = state.LastErrorCode ?? "";
        if (error is RecoveryCodes.Cancellation or "cancelling")
            return PartitionReadiness.Cancelling;
        if (error is RecoveryCodes.Authentication or "auth_required" or "auth_failed")
            return PartitionReadiness.AuthRequired;
        if (error is "permission_denied")
            return PartitionReadiness.PermissionDenied;
        if (state.Phase == ConnectionPhase.Incompatible)
            return PartitionReadiness.Incompatible;
        if (state.Phase is ConnectionPhase.Connecting or ConnectionPhase.Synchronizing)
            return PartitionReadiness.Loading;
        if (state.Phase == ConnectionPhase.Offline)
            return PartitionReadiness.Offline;
        if (state.Phase == ConnectionPhase.Stale || state.Freshness == DeviceFreshness.Stale)
            return PartitionReadiness.Stale;
        if (state.Capabilities.VerifiedOperations.Count == 0)
            return PartitionReadiness.Incompatible;
        if (!string.IsNullOrEmpty(state.LastErrorCode))
            return PartitionReadiness.Error;
        if (sessionCount == 0)
            return PartitionReadiness.Empty;
        return PartitionReadiness.Ready;
    }

    internal static string? SanitizeError(string? code)
    {
        if (string.IsNullOrEmpty(code))
            return null;
        if (code.Contains('\\', StringComparison.Ordinal) ||
            code.Contains('/', StringComparison.Ordinal) ||
            code.Contains('@', StringComparison.Ordinal) ||
            code.Contains('\u001b', StringComparison.Ordinal))
            return AggregationCodes.Error;
        return code.Length <= 64 ? code : code[..64];
    }

    private static bool HasOtherSession(DevicePartition current, SessionKey session)
    {
        foreach (var item in current.Sessions)
        {
            if (item.Session != session)
                return true;
        }

        return false;
    }

    private static bool IsValid(DeviceId device) => device.Value != Guid.Empty;

    private static bool IsValid(SessionKey session) =>
        session.Device.Value != Guid.Empty && !string.IsNullOrWhiteSpace(session.EndpointKey);
}
