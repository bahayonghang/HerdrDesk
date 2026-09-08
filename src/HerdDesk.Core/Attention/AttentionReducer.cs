using HerdDesk.Contracts;

namespace HerdDesk.Core;

public sealed class AttentionReducer
{
    private readonly NotificationPolicy _policy;
    private readonly Dictionary<SessionKey, SessionTrack> _sessions = new();
    private readonly Dictionary<DeviceId, AttentionFreshness> _deviceFreshness = new();
    private readonly HashSet<SessionKey> _baselineOpen = new();
    private readonly List<Record> _records = new();
    private readonly HashSet<string> _seenIds = new(StringComparer.Ordinal);
    private readonly Dictionary<SessionKey, Queue<DateTimeOffset>> _deliverTimes = new();
    private readonly HashSet<DeviceId> _mutedDevices = new();
    private readonly HashSet<SessionKey> _mutedSessions = new();
    private bool _globalMuted;

    public AttentionReducer(NotificationPolicy? policy = null)
    {
        _policy = policy ?? NotificationPolicy.Default;
    }

    public IReadOnlyList<AttentionEntry> Entries =>
        _records.Select(ToEntry).ToArray();

    public void BeginBaseline(SessionKey session, ConnectionEpoch epoch)
    {
        if (!IsValid(session) || epoch.Value <= 0)
            return;
        _baselineOpen.Add(session);
        ResetSessionIfEpochChanged(session, epoch);
    }

    public void SetGlobalMute(bool muted) => _globalMuted = muted;

    public void SetDeviceMute(DeviceId device, bool muted)
    {
        if (device.Value == Guid.Empty)
            return;
        if (muted)
            _mutedDevices.Add(device);
        else
            _mutedDevices.Remove(device);
    }

    public void SetSessionMute(SessionKey session, bool muted)
    {
        if (!IsValid(session))
            return;
        if (muted)
            _mutedSessions.Add(session);
        else
            _mutedSessions.Remove(session);
    }

    public void MarkDeviceStale(DeviceId device)
    {
        if (device.Value == Guid.Empty)
            return;
        _deviceFreshness[device] = AttentionFreshness.Stale;
        foreach (var record in _records)
        {
            if (record.Key.Pane.Session.Device == device && !record.IsExpired)
                record.Freshness = AttentionFreshness.Stale;
        }
    }

    public void MarkDeviceOffline(DeviceId device)
    {
        if (device.Value == Guid.Empty)
            return;
        _deviceFreshness[device] = AttentionFreshness.Offline;
        foreach (var record in _records)
        {
            if (record.Key.Pane.Session.Device == device && !record.IsExpired)
                record.Freshness = AttentionFreshness.Offline;
        }
    }

    public AttentionApplyResult Apply(
        DeviceProjectionSnapshot snapshot,
        ProjectionStamp stamp,
        AttentionSyncKind kind)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Epoch.Value <= 0)
            return AttentionApplyResult.Empty;
        var epoch = snapshot.Epoch;
        if (stamp.Epoch.Value > 0 && stamp.Epoch.Value < epoch.Value)
            return SuppressOldEpoch(snapshot, stamp);
        var observed = stamp.ObservedAt;
        var decisions = new List<NotificationDecision>();
        var present = new HashSet<AttentionKey>();
        var currentSessions = new HashSet<SessionKey>();
        var snapshotDevices = new HashSet<DeviceId>();
        var snapshotSessions = new HashSet<SessionKey>();
        foreach (var device in snapshot.Devices)
        {
            snapshotDevices.Add(device.Device);
            foreach (var session in device.Sessions)
            {
                snapshotSessions.Add(session.Session);
                ApplySession(
                    device.Device, session, epoch, observed, stamp.BaselineGeneration, kind, present,
                    currentSessions, decisions);
            }
        }

        ExpireMissing(present, currentSessions, snapshotDevices, snapshotSessions);
        return new(decisions, Entries);
    }

    public AttentionApplyResult ApplyAggregate(
        IReadOnlyList<AttentionPartitionFeed> parts,
        AttentionSyncKind kind)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var decisions = new List<NotificationDecision>();
        var present = new HashSet<AttentionKey>();
        var currentSessions = new HashSet<SessionKey>();
        var snapshotDevices = new HashSet<DeviceId>();
        var snapshotSessions = new HashSet<SessionKey>();
        foreach (var part in parts)
        {
            ArgumentNullException.ThrowIfNull(part);
            ArgumentNullException.ThrowIfNull(part.Snapshot);
            var snapshot = part.Snapshot;
            var stamp = part.Stamp;
            if (snapshot.Epoch.Value <= 0)
                continue;
            if (stamp.Epoch.Value > 0 && stamp.Epoch.Value < snapshot.Epoch.Value)
            {
                foreach (var device in snapshot.Devices)
                {
                    snapshotDevices.Add(device.Device);
                    foreach (var session in device.Sessions)
                        snapshotSessions.Add(session.Session);
                }

                foreach (var decision in SuppressOldEpoch(snapshot, stamp).Decisions)
                    decisions.Add(decision);
                continue;
            }

            foreach (var device in snapshot.Devices)
            {
                snapshotDevices.Add(device.Device);
                foreach (var session in device.Sessions)
                {
                    snapshotSessions.Add(session.Session);
                    ApplySession(
                        device.Device, session, snapshot.Epoch, stamp.ObservedAt, stamp.BaselineGeneration,
                        kind, present, currentSessions, decisions);
                }
            }
        }

        ExpireMissing(present, currentSessions, snapshotDevices, snapshotSessions);
        return new(decisions, Entries);
    }

    public void MarkRead(string transitionId)
    {
        if (string.IsNullOrWhiteSpace(transitionId))
            return;
        foreach (var record in _records)
        {
            if (record.TransitionId == transitionId)
                record.IsRead = true;
        }
    }

    public void MarkScopeRead(AttentionScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        foreach (var record in _records)
        {
            if (Matches(record, scope))
                record.IsRead = true;
        }
    }

    public void ClearExpired() =>
        _records.RemoveAll(item => item.IsExpired);

    public int UnreadFor(AttentionKey key) => CountUnread(item => item.Key == key);

    public int UnreadForPane(PaneKey pane) => CountUnread(item => item.Key.Pane == pane);

    public int UnreadForSession(SessionKey session) =>
        CountUnread(item => item.Key.Pane.Session == session);

    public int UnreadForDevice(DeviceId device) =>
        CountUnread(item => item.Key.Pane.Session.Device == device);

    public int UnreadTotal => CountUnread(_ => true);

    public AttentionEntry? Find(string transitionId)
    {
        if (string.IsNullOrWhiteSpace(transitionId))
            return null;
        foreach (var record in _records)
        {
            if (record.TransitionId == transitionId)
                return ToEntry(record);
        }

        return null;
    }

    public void MarkExpired(string transitionId)
    {
        foreach (var record in _records)
        {
            if (record.TransitionId != transitionId)
                continue;
            record.IsExpired = true;
            record.Freshness = AttentionFreshness.Expired;
        }
    }

    private void ApplySession(
        DeviceId device,
        SessionProjection session,
        ConnectionEpoch epoch,
        DateTimeOffset observed,
        long baselineGeneration,
        AttentionSyncKind kind,
        HashSet<AttentionKey> present,
        HashSet<SessionKey> currentSessions,
        List<NotificationDecision> decisions)
    {
        if (!IsValid(session.Session) || session.Session.Device != device)
            return;
        var track = ResetSessionIfEpochChanged(session.Session, epoch);
        var oldEpoch = track.Epoch.Value > 0 && epoch.Value < track.Epoch.Value;
        if (oldEpoch)
        {
            foreach (var entity in Collect(session))
            {
                if (!entity.Key.IsValid)
                    continue;
                var unknown = BusinessState.Unknown("unknown");
                var id = AttentionTransitionId.Format(entity.Key, unknown, entity.State, epoch, 0);
                var stampOld = new ProjectionStamp(epoch, track.BaselineGeneration, observed);
                decisions.Add(new NotificationDecision(
                    NotificationAction.Suppress,
                    AttentionCodes.OldEpoch,
                    BuildTransition(entity.Key, unknown, entity.State, stampOld, id, true,
                        AttentionFreshness.Expired)));
            }

            return;
        }

        currentSessions.Add(session.Session);
        var isBaseline = kind is AttentionSyncKind.Baseline or AttentionSyncKind.ReconnectBaseline ||
                         _baselineOpen.Contains(session.Session) ||
                         !track.BaselineComplete ||
                         track.Epoch != epoch;
        if (isBaseline && track.Epoch != epoch)
            track.Entities.Clear();
        track.Epoch = epoch;
        if (isBaseline)
            track.BaselineGeneration = Math.Max(track.BaselineGeneration, baselineGeneration) + 1;
        var stamp = new ProjectionStamp(epoch, track.BaselineGeneration, observed);
        var stale = FreshnessOf(device) is AttentionFreshness.Stale;
        var offline = FreshnessOf(device) is AttentionFreshness.Offline;
        foreach (var entity in Collect(session))
        {
            if (!entity.Key.IsValid)
                continue;
            present.Add(entity.Key);
            var decision = Observe(track, entity.Key, entity.State, stamp, kind, isBaseline, stale, offline);
            if (decision is not null)
                decisions.Add(decision);
        }

        if (isBaseline)
        {
            track.BaselineComplete = true;
            _baselineOpen.Remove(session.Session);
            if (FreshnessOf(device) is not AttentionFreshness.Offline)
                _deviceFreshness[device] = AttentionFreshness.Current;
        }
    }

    private NotificationDecision? Observe(
        SessionTrack track,
        AttentionKey key,
        BusinessState next,
        ProjectionStamp stamp,
        AttentionSyncKind kind,
        bool isBaseline,
        bool stale,
        bool offline)
    {
        if (!track.Entities.TryGetValue(key, out var entity))
        {
            entity = new EntityTrack();
            track.Entities[key] = entity;
        }

        if (entity.HasState && entity.Last == next)
        {
            if (kind is AttentionSyncKind.DirtyRefresh or AttentionSyncKind.SnapshotRepeat)
                return DuplicateIfRequested(key, entity, next, stamp, kind, isBaseline, stale, offline);
            return null;
        }

        var from = entity.HasState ? entity.Last : BusinessState.Unknown("unknown");
        entity.Ordinal++;
        var transitionId = AttentionTransitionId.Format(key, from, next, stamp.Epoch, entity.Ordinal);
        var duplicate = !_seenIds.Add(transitionId);
        var muted = IsMuted(key);
        var rateLimited = IsRateLimited(key.Pane.Session, stamp.ObservedAt);
        var policy = _policy.Evaluate(new AttentionPolicyInput(
            kind, isBaseline, duplicate, false, stale, offline, muted, rateLimited, next));
        entity.Last = next;
        entity.HasState = true;
        if (isBaseline || policy.Action == NotificationAction.Suppress)
            return new NotificationDecision(
                policy.Action,
                policy.Reason,
                BuildTransition(key, from, next, stamp, transitionId, isBaseline, FreshnessOf(key.Pane.Session.Device)));
        var freshness = stale
            ? AttentionFreshness.Stale
            : offline
                ? AttentionFreshness.Offline
                : AttentionFreshness.Current;
        var transition = BuildTransition(key, from, next, stamp, transitionId, false, freshness);
        _records.Add(new Record
        {
            TransitionId = transitionId,
            Key = key,
            Target = transition.Target,
            From = from,
            To = next,
            Stamp = stamp,
            Freshness = freshness,
            IsRead = !NotificationPolicy.CountsAsUnread(next, policy.Action),
            IsExpired = false,
            Reason = policy.Reason,
            Action = policy.Action
        });
        if (policy.Action == NotificationAction.Deliver)
            RecordDeliver(key.Pane.Session, stamp.ObservedAt);
        return new NotificationDecision(policy.Action, policy.Reason, transition);
    }

    private NotificationDecision? DuplicateIfRequested(
        AttentionKey key,
        EntityTrack entity,
        BusinessState next,
        ProjectionStamp stamp,
        AttentionSyncKind kind,
        bool isBaseline,
        bool stale,
        bool offline)
    {
        if (kind is not (AttentionSyncKind.DirtyRefresh or AttentionSyncKind.SnapshotRepeat or AttentionSyncKind.Live))
            return null;
        var from = entity.Last;
        var transitionId = AttentionTransitionId.Format(key, from, next, stamp.Epoch, entity.Ordinal);
        var policy = _policy.Evaluate(new AttentionPolicyInput(
            kind, isBaseline, true, false, stale, offline, IsMuted(key), false, next));
        return new NotificationDecision(
            policy.Action,
            policy.Action == NotificationAction.Suppress ? AttentionCodes.Duplicate : policy.Reason,
            BuildTransition(
                key, from, next, stamp, transitionId, isBaseline, FreshnessOf(key.Pane.Session.Device)));
    }

    private AttentionApplyResult SuppressOldEpoch(DeviceProjectionSnapshot snapshot, ProjectionStamp stamp)
    {
        var decisions = new List<NotificationDecision>();
        foreach (var entity in snapshot.Devices.SelectMany(device => device.Sessions).SelectMany(Collect))
        {
            if (!entity.Key.IsValid)
                continue;
            var unknown = BusinessState.Unknown("unknown");
            var id = AttentionTransitionId.Format(entity.Key, unknown, entity.State, stamp.Epoch, 0);
            var transition = BuildTransition(
                entity.Key, unknown, entity.State, stamp, id, true, AttentionFreshness.Expired);
            decisions.Add(new NotificationDecision(NotificationAction.Suppress, AttentionCodes.OldEpoch, transition));
        }

        return new(decisions, Entries);
    }

    private void ExpireMissing(
        HashSet<AttentionKey> present,
        HashSet<SessionKey> currentSessions,
        HashSet<DeviceId> snapshotDevices,
        HashSet<SessionKey> snapshotSessions)
    {
        foreach (var record in _records)
        {
            var key = record.Key;
            var session = key.Pane.Session;
            var device = session.Device;
            var gone = !snapshotDevices.Contains(device) ||
                       !snapshotSessions.Contains(session) ||
                       (currentSessions.Contains(session) && !present.Contains(key));
            if (!gone)
                continue;
            record.IsExpired = true;
            record.Freshness = AttentionFreshness.Expired;
        }

        foreach (var pair in _sessions.ToArray())
        {
            if (!snapshotSessions.Contains(pair.Key))
            {
                _sessions.Remove(pair.Key);
                continue;
            }

            if (!currentSessions.Contains(pair.Key))
                continue;
            foreach (var entity in pair.Value.Entities.Keys.ToArray())
            {
                if (!present.Contains(entity))
                    pair.Value.Entities.Remove(entity);
            }
        }
    }

    private SessionTrack ResetSessionIfEpochChanged(SessionKey session, ConnectionEpoch epoch)
    {
        if (!_sessions.TryGetValue(session, out var track))
        {
            track = new SessionTrack { Epoch = epoch };
            _sessions[session] = track;
            return track;
        }

        if (track.Epoch.Value == 0 || track.Epoch == epoch)
            return track;
        if (epoch.Value < track.Epoch.Value)
            return track;
        foreach (var record in _records)
        {
            if (record.Key.Pane.Session != session)
                continue;
            record.IsExpired = true;
            record.Freshness = AttentionFreshness.Expired;
        }

        track.Epoch = epoch;
        track.BaselineComplete = false;
        track.Entities.Clear();
        return track;
    }

    private AttentionFreshness FreshnessOf(DeviceId device) =>
        _deviceFreshness.GetValueOrDefault(device, AttentionFreshness.Current);

    private bool IsMuted(AttentionKey key) =>
        _globalMuted ||
        _mutedDevices.Contains(key.Pane.Session.Device) ||
        _mutedSessions.Contains(key.Pane.Session);

    private bool IsRateLimited(SessionKey session, DateTimeOffset now)
    {
        if (!_deliverTimes.TryGetValue(session, out var queue))
            return false;
        while (queue.Count > 0 && now - queue.Peek() > _policy.RateLimitWindow)
            queue.Dequeue();
        return queue.Count >= _policy.MaxDeliversPerSession;
    }

    private void RecordDeliver(SessionKey session, DateTimeOffset at)
    {
        if (!_deliverTimes.TryGetValue(session, out var queue))
        {
            queue = new Queue<DateTimeOffset>();
            _deliverTimes[session] = queue;
        }

        queue.Enqueue(at);
    }

    private int CountUnread(Func<Record, bool> match)
    {
        var count = 0;
        foreach (var record in _records)
        {
            if (!record.IsRead && match(record))
                count++;
        }

        return count;
    }

    private static bool Matches(Record record, AttentionScope scope) =>
        scope.Kind switch
        {
            AttentionScopeKind.Item => record.TransitionId == scope.TransitionId,
            AttentionScopeKind.Key => scope.Key is { } key && record.Key == key,
            AttentionScopeKind.Pane => scope.Pane is { } pane && record.Key.Pane == pane,
            AttentionScopeKind.Session => scope.Session is { } session && record.Key.Pane.Session == session,
            AttentionScopeKind.Device => scope.Device is { } device && record.Key.Pane.Session.Device == device,
            AttentionScopeKind.All => true,
            _ => false
        };

    private static AttentionTransition BuildTransition(
        AttentionKey key,
        BusinessState from,
        BusinessState to,
        ProjectionStamp stamp,
        string transitionId,
        bool isBaseline,
        AttentionFreshness freshness) =>
        new(
            key, from, to, stamp, transitionId, isBaseline, freshness,
            new NotificationTarget(key, stamp.Epoch, NotificationTarget.CurrentRouteVersion));

    private static IEnumerable<(AttentionKey Key, BusinessState State)> Collect(SessionProjection session)
    {
        var seen = new HashSet<PaneKey>();
        foreach (var agent in session.Agents)
        {
            var entityId = string.IsNullOrWhiteSpace(agent.TerminalId) ? agent.Pane.PaneId : agent.TerminalId;
            yield return (new AttentionKey(agent.Pane, AttentionKey.AgentEntityKind, entityId),
                BusinessState.From(agent.AgentStatus));
            seen.Add(agent.Pane);
        }

        foreach (var pane in session.Panes)
        {
            if (seen.Contains(pane.Key))
                continue;
            if (pane.AgentKind is null && string.IsNullOrEmpty(pane.AgentRaw) &&
                pane.AgentStatus.Known is AgentStatusKind.Idle)
                continue;
            var entityId = string.IsNullOrWhiteSpace(pane.TerminalId) ? pane.Key.PaneId : pane.TerminalId;
            yield return (new AttentionKey(pane.Key, AttentionKey.AgentEntityKind, entityId),
                BusinessState.From(pane.AgentStatus));
        }
    }

    private static bool IsValid(SessionKey session) =>
        session.Device.Value != Guid.Empty && !string.IsNullOrWhiteSpace(session.EndpointKey);

    private static AttentionEntry ToEntry(Record record) =>
        new(
            record.TransitionId,
            record.Key,
            record.Target,
            record.From,
            record.To,
            record.Stamp,
            record.Freshness,
            record.IsRead,
            record.IsExpired,
            record.Reason,
            record.Action);

    private sealed class SessionTrack
    {
        public ConnectionEpoch Epoch { get; set; }
        public bool BaselineComplete { get; set; }
        public long BaselineGeneration { get; set; }
        public Dictionary<AttentionKey, EntityTrack> Entities { get; } = new();
    }

    private sealed class EntityTrack
    {
        public BusinessState Last { get; set; }
        public bool HasState { get; set; }
        public int Ordinal { get; set; }
    }

    private sealed class Record
    {
        public required string TransitionId { get; init; }
        public required AttentionKey Key { get; init; }
        public required NotificationTarget Target { get; init; }
        public required BusinessState From { get; init; }
        public required BusinessState To { get; init; }
        public required ProjectionStamp Stamp { get; init; }
        public AttentionFreshness Freshness { get; set; }
        public bool IsRead { get; set; }
        public bool IsExpired { get; set; }
        public required string Reason { get; init; }
        public required NotificationAction Action { get; init; }
    }
}
