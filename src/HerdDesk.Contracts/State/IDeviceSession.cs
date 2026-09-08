namespace HerdDesk.Contracts;

public enum DeviceFreshness
{
    Unknown,
    Current,
    Refreshing,
    Stale
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
    int PendingEffectTasks);

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
    IAsyncEnumerable<DeviceSessionState> ReadStatesAsync(CancellationToken cancellationToken = default);
}
