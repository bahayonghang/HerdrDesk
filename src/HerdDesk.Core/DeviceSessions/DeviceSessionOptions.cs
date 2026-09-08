using HerdDesk.Contracts;

namespace HerdDesk.Core;

public sealed record DeviceSessionOptions
{
    public TimeSpan CoalesceWindow { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan CalibrationPeriod { get; init; } = TimeSpan.FromSeconds(5);
    public int MailboxCapacity { get; init; } = 32;
    public string SessionAlias { get; init; } = "session";
    public bool DegradedFullSnapshotOnly { get; init; }
    public ISessionNotificationSink? Notifications { get; init; }
    public IRecoveryEntropy Entropy { get; init; } = ZeroRecoveryEntropy.Instance;
    public IRetryRandom Random { get; init; } = ZeroRetryRandom.Instance;
    public ISessionRecoveryGate? RecoveryGate { get; init; }

    public static DeviceSessionOptions Default { get; } = new();
}
