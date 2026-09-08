using HerdDesk.Contracts;

namespace HerdDesk.Core;

public sealed class ConnectionLease : IDisposable
{
    private ConnectionAdmissionPolicy? _policy;
    private int _disposed;

    internal ConnectionLease(ConnectionAdmissionPolicy policy, LeaseOwner owner)
    {
        _policy = policy;
        Owner = owner;
    }

    public LeaseOwner Owner { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        var policy = Interlocked.Exchange(ref _policy, null);
        policy?.Release(Owner);
    }
}

public sealed class RpcPairLease : IDisposable
{
    private int _disposed;

    internal RpcPairLease(ConnectionLease request, ConnectionLease events)
    {
        Request = request;
        Events = events;
    }

    public ConnectionLease Request { get; }
    public ConnectionLease Events { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        Request.Dispose();
        Events.Dispose();
    }
}

public sealed record AdmissionDecision(
    bool Admitted,
    string Code,
    ConnectionPhase Phase,
    ConnectionLease? Lease,
    RpcPairLease? Pair,
    IReadOnlyList<SessionKey> PauseCandidates,
    SessionKey? FairWaiter)
{
    public static AdmissionDecision Reject(
        string code,
        ConnectionPhase phase,
        IReadOnlyList<SessionKey>? pauseCandidates = null,
        SessionKey? fairWaiter = null) =>
        new(false, code, phase, null, null, pauseCandidates ?? [], fairWaiter);
}

public sealed class ConnectionAdmissionPolicy
{
    private readonly object _gate = new();
    private readonly ResourceBudgets _budgets;
    private readonly Dictionary<LeaseOwner, ConnectionLease> _leases = [];
    private readonly Dictionary<DeviceId, int> _deviceSlots = [];
    private readonly Dictionary<DeviceId, int> _fileByDevice = [];
    private readonly Dictionary<SessionKey, SessionRecord> _sessions = [];
    private readonly List<WaitEntry> _wait = [];
    private long _admitSeq;
    private long _waitSeq;
    private DeviceId? _lastServed;
    private int _sshSlots;
    private int _terminals;
    private int _fileJobs;
    private int _maintenance;

    public ConnectionAdmissionPolicy(ResourceBudgets? budgets = null)
    {
        _budgets = budgets ?? ResourceBudgets.Product;
        if (_budgets.MaxRemoteDevices < 1 ||
            _budgets.MaxGlobalTerminals < 1 ||
            _budgets.SshSlots < 1)
            throw new ArgumentOutOfRangeException(nameof(budgets));
    }

    public ResourceBudgets Budgets => _budgets;
    private int _highWater;
    public int HighWaterSshSlots
    {
        get { lock (_gate) return _highWater; }
    }

    public bool SshSlotsMeasured => _budgets.SshSlotsMeasured;

    public BudgetOccupancy Snapshot()
    {
        lock (_gate)
        {
            return new(
                _sshSlots,
                _deviceSlots.Count,
                _terminals,
                _fileJobs,
                _maintenance,
                _wait.Count,
                _sessions.Values.Count(item => item.Phase == ConnectionPhase.PausedForCapacity));
        }
    }

    public ConnectionPhase PhaseOf(SessionKey session)
    {
        lock (_gate)
            return _sessions.TryGetValue(session, out var record)
                ? record.Phase
                : ConnectionPhase.Offline;
    }

    public SessionKey? PeekFairWaiter()
    {
        lock (_gate)
            return FairWaiterLocked();
    }

    public void SetSessionActivity(
        SessionKey session,
        bool hasVisiblePane,
        bool mutationInFlight,
        bool fileInFlight)
    {
        if (!IsSession(session))
            return;
        lock (_gate)
        {
            var record = RecordLocked(session);
            record.HasVisiblePane = hasVisiblePane;
            record.MutationInFlight = mutationInFlight;
            record.FileInFlight = fileInFlight;
        }
    }

    public AdmissionDecision TryAcquireRpcPair(SessionKey session, ConnectionEpoch epoch)
    {
        if (!IsSession(session) || epoch.Value <= 0)
            return AdmissionDecision.Reject(
                epoch.Value <= 0 ? ResourceBudgetCodes.StaleEpoch : ResourceBudgetCodes.InvalidIdentity,
                ConnectionPhase.Offline);

        lock (_gate)
        {
            var requestOwner = LeaseOwner.RequestRpc(session, epoch);
            var eventOwner = LeaseOwner.EventRpc(session, epoch);
            if (_leases.ContainsKey(requestOwner) || _leases.ContainsKey(eventOwner))
                return AdmissionDecision.Reject(
                    ResourceBudgetCodes.ConnectionBudgetExhausted,
                    PhaseOfLocked(session));

            var newDevice = !_deviceSlots.ContainsKey(session.Device);
            var needDevice = newDevice && _deviceSlots.Count >= _budgets.MaxRemoteDevices;
            var needSlots = _sshSlots + 2 > _budgets.SshSlots;
            IReadOnlyList<SessionKey> paused = [];
            if (needDevice || needSlots)
            {
                var needed = needSlots ? _sshSlots + 2 - _budgets.SshSlots : 2;
                paused = PauseIdleLocked(session, needed);
                newDevice = !_deviceSlots.ContainsKey(session.Device);
                if ((newDevice && _deviceSlots.Count >= _budgets.MaxRemoteDevices) ||
                    _sshSlots + 2 > _budgets.SshSlots)
                    return WaitLocked(session, ResourceBudgetCodes.ConnectionBudgetExhausted, paused);
            }

            RemoveWaitLocked(session);
            var request = AddLocked(requestOwner);
            var events = AddLocked(eventOwner);
            var record = RecordLocked(session);
            record.Epoch = epoch;
            record.Phase = ConnectionPhase.Connecting;
            record.AdmitSeq = ++_admitSeq;
            _lastServed = session.Device;
            return new AdmissionDecision(
                true,
                "admitted",
                ConnectionPhase.Connecting,
                null,
                new RpcPairLease(request, events),
                [],
                FairWaiterLocked());
        }
    }

    public AdmissionDecision TryAcquire(LeaseOwner owner)
    {
        if (!TryValidate(owner, out var code))
            return AdmissionDecision.Reject(code, ConnectionPhase.Offline);

        lock (_gate)
        {
            if (owner.Kind is ConnectionLeaseKind.RequestRpc or ConnectionLeaseKind.EventRpc)
                return AdmissionDecision.Reject(
                    ResourceBudgetCodes.ConnectionBudgetExhausted,
                    ConnectionPhase.WaitingForCapacity);

            if (_leases.ContainsKey(owner))
                return AdmissionDecision.Reject(
                    ResourceBudgetCodes.ConnectionBudgetExhausted,
                    PhaseOfLocked(owner.Session));

            if (!CanAdmitLocked(owner, out code))
                return AdmissionDecision.Reject(code, PhaseOfLocked(owner.Session));

            var lease = AddLocked(owner);
            if (owner.Kind == ConnectionLeaseKind.Terminal)
            {
                var record = RecordLocked(owner.Session);
                if (record.Phase is ConnectionPhase.Offline or ConnectionPhase.WaitingForCapacity
                    or ConnectionPhase.PausedForCapacity)
                    record.Phase = ConnectionPhase.Connecting;
            }

            return new AdmissionDecision(
                true,
                "admitted",
                PhaseOfLocked(owner.Session),
                lease,
                null,
                [],
                FairWaiterLocked());
        }
    }

    public AdmissionDecision TryAcquireTerminal(PaneKey pane, ConnectionEpoch epoch) =>
        TryAcquire(LeaseOwner.Terminal(pane, epoch));

    public AdmissionDecision TryAcquireFileJob(SessionKey session, ConnectionEpoch epoch, string jobId) =>
        TryAcquire(LeaseOwner.FileJob(session, epoch, jobId));

    public AdmissionDecision TryAcquireMaintenance(SessionKey session, ConnectionEpoch epoch, string jobId) =>
        TryAcquire(LeaseOwner.Maintenance(session, epoch, jobId));

    internal void Release(LeaseOwner owner)
    {
        lock (_gate)
        {
            if (!_leases.Remove(owner))
                return;
            _sshSlots--;
            if (owner.Kind == ConnectionLeaseKind.Terminal)
                _terminals--;
            else if (owner.Kind == ConnectionLeaseKind.FileJob)
            {
                _fileJobs--;
                if (_fileByDevice.TryGetValue(owner.Device, out var files))
                {
                    files--;
                    if (files <= 0)
                        _fileByDevice.Remove(owner.Device);
                    else
                        _fileByDevice[owner.Device] = files;
                }
            }
            else if (owner.Kind == ConnectionLeaseKind.Maintenance)
                _maintenance--;

            if (_deviceSlots.TryGetValue(owner.Device, out var slots))
            {
                slots--;
                if (slots <= 0)
                    _deviceSlots.Remove(owner.Device);
                else
                    _deviceSlots[owner.Device] = slots;
            }

            if (_sessions.TryGetValue(owner.Session, out var record) &&
                !_leases.Values.Any(item => item.Owner.Session == owner.Session))
            {
                if (record.Phase != ConnectionPhase.PausedForCapacity)
                    record.Phase = ConnectionPhase.Offline;
            }
        }
    }

    private ConnectionLease AddLocked(LeaseOwner owner)
    {
        var lease = new ConnectionLease(this, owner);
        _leases[owner] = lease;
        _sshSlots++;
        if (_sshSlots > _highWater)
            _highWater = _sshSlots;
        if (!_deviceSlots.TryGetValue(owner.Device, out var slots))
            _deviceSlots[owner.Device] = 1;
        else
            _deviceSlots[owner.Device] = slots + 1;
        if (owner.Kind == ConnectionLeaseKind.Terminal)
            _terminals++;
        else if (owner.Kind == ConnectionLeaseKind.FileJob)
        {
            _fileJobs++;
            _fileByDevice[owner.Device] = _fileByDevice.GetValueOrDefault(owner.Device) + 1;
        }
        else if (owner.Kind == ConnectionLeaseKind.Maintenance)
            _maintenance++;
        return lease;
    }

    private bool CanAdmitLocked(LeaseOwner owner, out string code)
    {
        code = ResourceBudgetCodes.ConnectionBudgetExhausted;
        var newDevice = !_deviceSlots.ContainsKey(owner.Device);
        if (newDevice && _deviceSlots.Count >= _budgets.MaxRemoteDevices)
            return false;
        if (_sshSlots + 1 > _budgets.SshSlots)
            return false;
        switch (owner.Kind)
        {
            case ConnectionLeaseKind.Terminal:
                return _terminals < _budgets.MaxGlobalTerminals;
            case ConnectionLeaseKind.FileJob:
                if (_fileJobs >= _budgets.EffectiveGlobalFileJobs)
                    return false;
                return _fileByDevice.GetValueOrDefault(owner.Device) < _budgets.EffectiveFileJobsPerDevice;
            case ConnectionLeaseKind.Maintenance:
                if (_maintenance >= _budgets.MaxGlobalMaintenance)
                    return false;
                return _fileByDevice.GetValueOrDefault(owner.Device) == 0;
            default:
                return false;
        }
    }

    private AdmissionDecision WaitLocked(
        SessionKey session,
        string code,
        IReadOnlyList<SessionKey>? paused = null)
    {
        EnqueueWaitLocked(session);
        var record = RecordLocked(session);
        if (record.Phase != ConnectionPhase.PausedForCapacity)
            record.Phase = ConnectionPhase.WaitingForCapacity;
        return AdmissionDecision.Reject(code, ConnectionPhase.WaitingForCapacity, paused, FairWaiterLocked());
    }

    private IReadOnlyList<SessionKey> PauseIdleLocked(SessionKey requester, int neededSlots)
    {
        var paused = new List<SessionKey>();
        var remaining = neededSlots;
        foreach (var record in _sessions.Values
                     .Where(item =>
                         item.Session != requester &&
                         !item.HasVisiblePane &&
                         !item.MutationInFlight &&
                         !item.FileInFlight &&
                         HoldsPairLocked(item.Session))
                     .OrderBy(item => item.AdmitSeq)
                     .ToArray())
        {
            record.Phase = ConnectionPhase.PausedForCapacity;
            paused.Add(record.Session);
            remaining -= RpcSlotsLocked(record.Session);
            if (remaining <= 0)
                break;
        }

        return paused;
    }

    private int RpcSlotsLocked(SessionKey session) =>
        _leases.Values.Count(item =>
            item.Owner.Session == session &&
            item.Owner.Kind is ConnectionLeaseKind.RequestRpc or ConnectionLeaseKind.EventRpc);

    private bool HoldsPairLocked(SessionKey session) =>
        _leases.Values.Any(item =>
            item.Owner.Session == session &&
            item.Owner.Kind is ConnectionLeaseKind.RequestRpc or ConnectionLeaseKind.EventRpc);

    private void EnqueueWaitLocked(SessionKey session)
    {
        if (_wait.Any(item => item.Session == session))
            return;
        _wait.Add(new WaitEntry(session.Device, session, ++_waitSeq));
    }

    private void RemoveWaitLocked(SessionKey session)
    {
        for (var i = _wait.Count - 1; i >= 0; i--)
        {
            if (_wait[i].Session == session)
                _wait.RemoveAt(i);
        }
    }

    private SessionKey? FairWaiterLocked()
    {
        if (_wait.Count == 0)
            return null;
        var devices = new List<DeviceId>();
        foreach (var entry in _wait)
        {
            if (!devices.Contains(entry.Device))
                devices.Add(entry.Device);
        }

        var start = 0;
        if (_lastServed is { } last)
        {
            var idx = devices.IndexOf(last);
            if (idx >= 0)
                start = (idx + 1) % devices.Count;
        }

        var target = devices[start];
        foreach (var entry in _wait)
        {
            if (entry.Device == target)
                return entry.Session;
        }

        return _wait[0].Session;
    }

    private SessionRecord RecordLocked(SessionKey session)
    {
        if (_sessions.TryGetValue(session, out var record))
            return record;
        record = new SessionRecord(session);
        _sessions[session] = record;
        return record;
    }

    private ConnectionPhase PhaseOfLocked(SessionKey session) =>
        _sessions.TryGetValue(session, out var record) ? record.Phase : ConnectionPhase.Offline;

    private static bool IsSession(SessionKey session) =>
        session.Device.Value != Guid.Empty && !string.IsNullOrWhiteSpace(session.EndpointKey);

    private static bool TryValidate(LeaseOwner owner, out string code)
    {
        code = ResourceBudgetCodes.InvalidIdentity;
        if (owner.Device.Value == Guid.Empty ||
            owner.Session.Device != owner.Device ||
            string.IsNullOrWhiteSpace(owner.Session.EndpointKey))
            return false;
        if (owner.Epoch.Value <= 0)
        {
            code = ResourceBudgetCodes.StaleEpoch;
            return false;
        }

        switch (owner.Kind)
        {
            case ConnectionLeaseKind.Terminal:
                if (owner.Pane is null || owner.Pane.Value.Session != owner.Session)
                    return false;
                break;
            case ConnectionLeaseKind.FileJob:
            case ConnectionLeaseKind.Maintenance:
                if (string.IsNullOrWhiteSpace(owner.JobId))
                    return false;
                break;
            case ConnectionLeaseKind.RequestRpc:
            case ConnectionLeaseKind.EventRpc:
                break;
            default:
                code = ResourceBudgetCodes.ConnectionBudgetExhausted;
                return false;
        }

        code = "ok";
        return true;
    }

    private sealed class SessionRecord(SessionKey session)
    {
        public SessionKey Session { get; } = session;
        public ConnectionEpoch Epoch;
        public ConnectionPhase Phase = ConnectionPhase.Offline;
        public bool HasVisiblePane;
        public bool MutationInFlight;
        public bool FileInFlight;
        public long AdmitSeq;
    }

    private readonly record struct WaitEntry(DeviceId Device, SessionKey Session, long Seq);
}
