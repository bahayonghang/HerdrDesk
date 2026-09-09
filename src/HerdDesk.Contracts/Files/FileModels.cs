namespace HerdDesk.Contracts;

public enum FileEntryKind
{
    File,
    Directory,
    Symlink,
    Other
}

public enum FileConflictMode
{
    Fail,
    KeepBoth,
    Replace
}

public enum TransferStage
{
    Queued,
    Admitted,
    Starting,
    Staging,
    Transferring,
    Verifying,
    Committing,
    Completed,
    CancelRequested,
    Failed,
    Cleaning,
    Cancelled,
    FailedCleanup
}

public static class FileOpCodes
{
    public const string Ok = "ok";
    public const string Cancelled = "cancelled";
    public const string Conflict = "conflict";
    public const string StaleTarget = "stale_target";
    public const string NotFound = "not_found";
    public const string PermissionDenied = "permission_denied";
    public const string NameExists = "name_exists";
    public const string ParentMissing = "parent_missing";
    public const string IsDirectory = "is_directory";
    public const string NotDirectory = "not_directory";
    public const string HashMismatch = "hash_mismatch";
    public const string LengthMismatch = "length_mismatch";
    public const string Unsupported = "unsupported";
    public const string OutcomeUnknown = "outcome_unknown";
    public const string MappingRequired = "mapping_required";
    public const string ReplaceObservationRequired = "replace_observation_required";
    public const string ProtocolPollution = "protocol_pollution";
    public const string SourceChanged = "source_changed";
    public const string InvalidPath = "invalid_path";
}

public sealed class FileComponent : IEquatable<FileComponent>
{
    public FileComponent(byte[] raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        Raw = raw;
    }

    public byte[] Raw { get; }

    public bool Equals(FileComponent? other) =>
        other is not null && Raw.AsSpan().SequenceEqual(other.Raw);

    public override bool Equals(object? obj) => Equals(obj as FileComponent);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(Raw);
        return hash.ToHashCode();
    }
}

public sealed class FileLocator : IEquatable<FileLocator>
{
    public FileLocator(IReadOnlyList<FileComponent> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        Components = components;
    }

    public static FileLocator Root { get; } = new([]);

    public IReadOnlyList<FileComponent> Components { get; }

    public FileLocator Append(FileComponent name)
    {
        var list = new FileComponent[Components.Count + 1];
        for (var i = 0; i < Components.Count; i++)
            list[i] = Components[i];
        list[^1] = name;
        return new FileLocator(list);
    }

    public bool Equals(FileLocator? other)
    {
        if (other is null || other.Components.Count != Components.Count)
            return false;
        for (var i = 0; i < Components.Count; i++)
        {
            if (!Components[i].Equals(other.Components[i]))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as FileLocator);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var component in Components)
            hash.Add(component);
        return hash.ToHashCode();
    }
}

public sealed record FileIdentity(byte[] Raw)
{
    public bool SameAs(FileIdentity? other) =>
        other is not null && Raw.AsSpan().SequenceEqual(other.Raw);
}

public sealed record FileObservation(string Hex);

public sealed record FileEntry(
    FileComponent Name,
    string DisplayName,
    FileEntryKind Kind,
    ulong Size,
    ulong Mtime,
    ulong MtimePrecision,
    FileIdentity Identity,
    bool Symlink,
    FileObservation Observation);

public sealed record FileStat(
    FileLocator Path,
    FileEntryKind Kind,
    ulong Size,
    ulong Mtime,
    ulong MtimePrecision,
    FileIdentity Identity,
    FileObservation Observation,
    bool Symlink,
    string Sha256);

public sealed record TransferEndpointKey(
    DeviceId Device,
    SessionKey Session,
    ConnectionEpoch Epoch,
    string EndpointId);

public sealed record TransferProgress(
    Guid JobId,
    TransferEndpointKey Source,
    TransferEndpointKey Destination,
    ulong BytesAccepted,
    ulong BytesTotal);

public sealed record FileOpResult(
    bool Succeeded,
    string Code,
    FileStat? Stat = null,
    IReadOnlyList<FileEntry>? Entries = null,
    bool HasMore = false,
    string? Cursor = null,
    FileLocator? FinalPath = null,
    FileObservation? Observation = null,
    string? Sha256 = null,
    ulong Length = 0)
{
    public static FileOpResult Fail(string code) => new(false, code);
    public static FileOpResult Ok() => new(true, FileOpCodes.Ok);
}

public sealed record FileWriteRequest(
    Guid JobId,
    FileLocator Parent,
    FileComponent Name,
    FileConflictMode Mode,
    FileObservation ParentObservation,
    FileObservation? TargetObservation,
    ulong Length,
    string Sha256);

public sealed record TransferRequest(
    Guid JobId,
    IFileEndpoint Source,
    FileLocator SourcePath,
    IFileEndpoint Destination,
    FileLocator DestinationParent,
    FileComponent DestinationName,
    FileConflictMode Conflict,
    FileObservation? DestinationTargetObservation);

public sealed record TransferResult(
    bool Succeeded,
    string Code,
    TransferStage Stage,
    Guid JobId,
    FileLocator? FinalPath,
    string? Sha256,
    ulong Length,
    TransferProgress? Progress);
