using HerdDesk.Contracts;

namespace HerdDesk.Core;

public sealed record SessionRecoveryContext(
    long ProfileRevision,
    long CredentialRevision,
    long KnownHostRevision,
    bool HostTrusted,
    bool AuthenticationSupported)
{
    public static SessionRecoveryContext Unrestricted { get; } =
        new(0, 0, 0, true, true);
}

public sealed record RecoveryBlockSnapshot(
    DeviceId Device,
    long BlockedProfileRevision,
    RecoveryCause Kind,
    long? CredentialRevision,
    long? KnownHostRevision,
    string PublicCode);

public sealed record RecoveryBlockWriteResult(bool Succeeded, string? Code);

public interface ISessionRecoveryGate
{
    SessionRecoveryContext ReadContext(SessionKey session);
    RecoveryBlockSnapshot? ReadBlock(DeviceId device);
    RecoveryBlockWriteResult PersistBlock(RecoveryBlockSnapshot snapshot);
    void ClearBlock(DeviceId device);
}

public interface IRetryRandom
{
    double NextUnitInterval();
}

public sealed class ZeroRetryRandom : IRetryRandom
{
    public static ZeroRetryRandom Instance { get; } = new();

    public double NextUnitInterval() => 0;
}
