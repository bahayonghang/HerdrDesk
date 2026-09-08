namespace HerdDesk.Contracts;

public enum SessionProfileKind { LocalDefault, NamedSession, ExplicitEndpoint }

public static class ConnectionKinds
{
    public const string Local = "local";
    public const string Ssh = "ssh";
}

public static class ConfigurationCodes
{
    public const string Missing = "configuration_missing";
    public const string Malformed = "configuration_malformed";
    public const string ForbiddenField = "configuration_forbidden_field";
    public const string VersionUnsupported = "configuration_version_unsupported";
    public const string WriteConflict = "configuration_write_conflict";
    public const string SerializeFailed = "configuration_serialize_failed";
    public const string ReplaceFailed = "configuration_replace_failed";
    public const string BackupMissing = "configuration_backup_missing";
    public const string InvalidIdentity = "invalid_identity";
    public const string InvalidProfile = "invalid_profile";
    public const string DuplicateSession = "duplicate_session";
    public const string InvalidSshProfile = "ssh_profile_invalid";
}

public sealed record SessionProfile(
    SessionProfileKind Kind,
    string EndpointKey,
    string? SessionName,
    EndpointKind? Endpoint,
    string? CanonicalLocation)
{
    public const string LocalDefaultEndpointKey = "local-default";
    public const string NamedSessionEndpointKey = "named-session";

    public static SessionProfile LocalDefault() =>
        new(SessionProfileKind.LocalDefault, LocalDefaultEndpointKey, null, null, null);

    public static SessionProfile Named(string sessionName) =>
        new(SessionProfileKind.NamedSession, NamedSessionEndpointKey, sessionName, null, null);

    public static SessionProfile Explicit(
        string canonicalLocation,
        EndpointKind kind,
        string? sessionName = null) =>
        new(SessionProfileKind.ExplicitEndpoint, canonicalLocation, sessionName, kind, canonicalLocation);

    public SessionKey ToSessionKey(DeviceId device) => new(device, EndpointKey, SessionName);
}

public sealed record DeviceProfile(
    DeviceId Device,
    string Label,
    string ConnectionKind,
    string VerifiedHerdrPath,
    IReadOnlyList<SessionProfile> Sessions,
    SshDeviceSettings? Ssh = null)
{
    public bool IsSshConnection =>
        string.Equals(ConnectionKind, ConnectionKinds.Ssh, StringComparison.Ordinal);
}

public sealed record ConfigurationSnapshot(
    int SchemaVersion,
    long Revision,
    IReadOnlyList<DeviceProfile> Devices)
{
    public const int CurrentSchemaVersion = 1;

    public static ConfigurationSnapshot Empty { get; } =
        new(CurrentSchemaVersion, 0, Array.Empty<DeviceProfile>());
}

public sealed record ConfigurationLoadResult(ConfigurationSnapshot? Snapshot, string? Code)
{
    public bool Succeeded => Snapshot is not null && Code is null;
}

public sealed record ConfigurationWriteResult(ConfigurationSnapshot? Snapshot, string? Code)
{
    public bool Succeeded => Snapshot is not null && Code is null;
}

public interface IDeviceProfileStore
{
    ValueTask<ConfigurationLoadResult> LoadAsync(CancellationToken cancellationToken = default);
    ValueTask<ConfigurationWriteResult> SaveDeviceAsync(
        DeviceProfile profile, long expectedRevision, CancellationToken cancellationToken = default);
    ValueTask<ConfigurationWriteResult> DeleteDeviceAsync(
        DeviceId device, long expectedRevision, CancellationToken cancellationToken = default);
    ValueTask<ConfigurationWriteResult> RestoreFromBackupAsync(
        CancellationToken cancellationToken = default);
}
