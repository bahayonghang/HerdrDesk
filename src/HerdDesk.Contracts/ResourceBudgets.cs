namespace HerdDesk.Contracts;

public enum ConnectionLeaseKind
{
    RequestRpc,
    EventRpc,
    Terminal,
    FileJob,
    Maintenance
}

public enum PaneVisibilityKind
{
    Hidden,
    Visible,
    WaitingForCapacity
}

public static class ResourceBudgetCodes
{
    public const string ConnectionBudgetExhausted = "connection_budget_exhausted";
    public const string TerminalQueueLimit = "terminal_queue_limit";
    public const string StaleRenderAck = "stale_render_ack";
    public const string TransportCancelTimeout = "transport_cancel_timeout";
    public const string ProcessStartFailed = "process_start_failed";
    public const string SwitchPaneRequired = "switch_pane_required";
    public const string InvalidIdentity = "invalid_identity";
    public const string StaleEpoch = "stale_epoch";
    public const string DeltaDropForbidden = "delta_drop_forbidden";
}

public readonly record struct LeaseOwner(
    DeviceId Device,
    SessionKey Session,
    ConnectionEpoch Epoch,
    ConnectionLeaseKind Kind,
    PaneKey? Pane,
    string? JobId)
{
    public static LeaseOwner RequestRpc(SessionKey session, ConnectionEpoch epoch) =>
        new(session.Device, session, epoch, ConnectionLeaseKind.RequestRpc, null, null);

    public static LeaseOwner EventRpc(SessionKey session, ConnectionEpoch epoch) =>
        new(session.Device, session, epoch, ConnectionLeaseKind.EventRpc, null, null);

    public static LeaseOwner Terminal(PaneKey pane, ConnectionEpoch epoch) =>
        new(pane.Session.Device, pane.Session, epoch, ConnectionLeaseKind.Terminal, pane, null);

    public static LeaseOwner FileJob(SessionKey session, ConnectionEpoch epoch, string jobId) =>
        new(session.Device, session, epoch, ConnectionLeaseKind.FileJob, null, jobId);

    public static LeaseOwner Maintenance(SessionKey session, ConnectionEpoch epoch, string jobId) =>
        new(session.Device, session, epoch, ConnectionLeaseKind.Maintenance, null, jobId);
}

public sealed record ResourceBudgets(
    int MaxRemoteDevices,
    int MaxGlobalTerminals,
    int MaxFileJobsPerDevice,
    int MaxGlobalFileJobs,
    int MaxGlobalMaintenance,
    int SshSlots,
    bool FileJobsEnabled,
    bool SshSlotsMeasured)
{
    public const int ProductRemoteDevices = 3;
    public const int ProductGlobalTerminals = 4;
    public const int ProductFileJobsPerDevice = 1;
    public const int ProductGlobalFileJobs = 2;
    public const int ProductGlobalMaintenance = 1;
    public const int UnmeasuredSshSlotCeiling = 16;

    public static ResourceBudgets Product { get; } = new(
        ProductRemoteDevices,
        ProductGlobalTerminals,
        ProductFileJobsPerDevice,
        ProductGlobalFileJobs,
        ProductGlobalMaintenance,
        UnmeasuredSshSlotCeiling,
        FileJobsEnabled: false,
        SshSlotsMeasured: false);

    public static ResourceBudgets ForTests(
        int sshSlots,
        bool fileJobsEnabled = true,
        int maxRemoteDevices = ProductRemoteDevices,
        int maxGlobalTerminals = ProductGlobalTerminals) =>
        new(
            maxRemoteDevices,
            maxGlobalTerminals,
            ProductFileJobsPerDevice,
            ProductGlobalFileJobs,
            ProductGlobalMaintenance,
            sshSlots,
            fileJobsEnabled,
            SshSlotsMeasured: false);

    public int EffectiveFileJobsPerDevice => FileJobsEnabled ? MaxFileJobsPerDevice : 0;
    public int EffectiveGlobalFileJobs => FileJobsEnabled ? MaxGlobalFileJobs : 0;
}

public sealed record BudgetOccupancy(
    int SshSlots,
    int RemoteDevices,
    int Terminals,
    int FileJobs,
    int Maintenance,
    int WaitingSessions,
    int PausedSessions);
