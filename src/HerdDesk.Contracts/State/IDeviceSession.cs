namespace HerdDesk.Contracts;

public enum DeviceFreshness
{
    Unknown,
    Current,
    Refreshing,
    Stale
}

public sealed record SessionRecoveryProgress(
    int Attempt,
    DateTimeOffset? NextRetryUtc,
    string? Cause,
    string? Decision,
    int RetryTimerCount,
    int ReconnectEffectCount,
    bool InputNotReplayed = false,
    long? BlockedProfileRevision = null,
    string? PublicCode = null)
{
    public static SessionRecoveryProgress None { get; } = new(0, null, null, null, 0, 0);
}

public sealed record DeviceSessionState(
    SessionKey Session,
    ConnectionEpoch Epoch,
    ConnectionPhase Phase,
    DeviceFreshness Freshness,
    CapabilityProfile Capabilities,
    DeviceProjectionSnapshot Projection,
    long DirtyGeneration,
    int DirtyScopeCount,
    string? LastErrorCode,
    bool BaselineInstalled,
    int AcceptedInvalidations,
    int ActiveTimerCount,
    int InFlightReadEffects,
    int PendingEffectTasks,
    SessionRecoveryProgress Recovery)
{
    public DeviceSessionState(
        SessionKey session,
        ConnectionEpoch epoch,
        ConnectionPhase phase,
        DeviceFreshness freshness,
        CapabilityProfile capabilities,
        DeviceProjectionSnapshot projection,
        long dirtyGeneration,
        int dirtyScopeCount,
        string? lastErrorCode,
        bool baselineInstalled,
        int acceptedInvalidations,
        int activeTimerCount,
        int inFlightReadEffects,
        int pendingEffectTasks)
        : this(
            session,
            epoch,
            phase,
            freshness,
            capabilities,
            projection,
            dirtyGeneration,
            dirtyScopeCount,
            lastErrorCode,
            baselineInstalled,
            acceptedInvalidations,
            activeTimerCount,
            inFlightReadEffects,
            pendingEffectTasks,
            SessionRecoveryProgress.None)
    {
    }
}

public static class SessionLifecycleKinds
{
    public const string BaselineEstablished = "baseline-established";
}

public interface ISessionNotificationSink
{
    void OnLifecycleNotification(string kind, DeviceSessionState state);
}

public interface IDeviceSession : IAsyncDisposable
{
    SessionKey Session { get; }
    ConnectionEpoch Epoch { get; }
    DeviceSessionState Current { get; }
    ValueTask ConnectAsync(string socketPath, CancellationToken cancellationToken = default);
    ValueTask DisconnectAsync(CancellationToken cancellationToken = default);
    ValueTask RetryNowAsync(CancellationToken cancellationToken = default);
    ValueTask CancelRetryAsync(CancellationToken cancellationToken = default);
    ValueTask NotifyAppStoppingAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<DeviceSessionState> ReadStatesAsync(CancellationToken cancellationToken = default);
}
