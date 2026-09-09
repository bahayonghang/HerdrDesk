namespace HerdDesk.Contracts;

public enum ClipboardIntentKind
{
    Text,
    FileList,
    Image,
    Mixed,
    Empty,
    Unsupported,
    AccessDenied
}

public enum OscClipboardKind
{
    None,
    Read,
    Write
}

public enum PasteUiState
{
    Idle,
    LoadingClipboard,
    Ready,
    NeedsIntentChoice,
    NeedsMultilineConfirm,
    Caching,
    CacheFull,
    AccessDenied,
    Stale,
    Sending,
    UnknownOutcome,
    Failed,
    Cancelled
}

public static class ClipboardCodes
{
    public const string Allowed = "allowed";
    public const string Empty = "empty";
    public const string Unsupported = "unsupported";
    public const string AccessDenied = "access_denied";
    public const string Busy = "busy";
    public const string Oversize = "oversize";
    public const string MixedIntentRequired = "mixed_intent_required";
    public const string MultilineConfirmRequired = "multiline_confirm_required";
    public const string Cancelled = "cancelled";
    public const string TargetStale = "target_stale";
    public const string ControlRevoked = "control_revoked";
    public const string StaleEpoch = "stale_epoch";
    public const string ClipboardReadDenied = "clipboard_read_denied";
    public const string ClipboardWriteDenied = "clipboard_write_denied";
    public const string CacheFull = "cache_full";
    public const string CleanupFailed = "cleanup_failed";
    public const string DiskFull = "disk_full";
    public const string PermissionDenied = "permission_denied";
    public const string Unknown = "unknown";
    public const string ContinueToAttachment = "continue_to_attachment";
    public const string ControlNotVerified = "control_not_verified";
    public const string InvalidIdentity = "invalid_identity";
    public const string InputBytesLimit = "input_bytes_limit";
    public const string InputRejected = "input_rejected";
    public const string ResultUnknown = "result_unknown";
    public const string WatcherDenied = "clipboard_watcher_denied";
}

public sealed record ClipboardFileHandle(
    string OpaqueId,
    string DisplayName,
    ulong? SizeBytes,
    string? LocalPath = null);

public sealed record ClipboardImageHandle(
    string OpaqueId,
    string ContentType,
    ulong SizeBytes,
    ReadOnlyMemory<byte> Bytes);

public sealed record ClipboardSnapshot(
    Guid Id,
    DateTimeOffset CapturedAt,
    bool HasText,
    bool HasFileList,
    bool HasImage,
    IReadOnlyList<string> FormatNames,
    ReadOnlyMemory<byte> TextBytes,
    IReadOnlyList<ClipboardFileHandle> Files,
    ClipboardImageHandle? Image,
    ulong EstimatedBytes,
    string? ErrorCode = null);

public sealed record ClipboardIntent(
    ClipboardIntentKind Kind,
    IReadOnlyList<ClipboardIntentKind> MixedOptions,
    int LineCount,
    int CharacterCount,
    int Utf8ByteCount,
    string Preview,
    bool RequiresMultilineConfirm,
    string? Code = null);

public sealed record PasteConfirmRequest(
    PaneKey Pane,
    ConnectionEpoch Epoch,
    string ControlLeaseId,
    long ControlLeaseGeneration,
    bool ControlVerified,
    TerminalAccess Access,
    DateTimeOffset Now);

public sealed record PasteLiveTarget(
    PaneKey Pane,
    ConnectionEpoch Epoch,
    string ControlLeaseId,
    long ControlLeaseGeneration,
    bool ControlVerified,
    TerminalAccess Access,
    DateTimeOffset Now);

public sealed record PasteTargetLease(
    PaneKey Pane,
    ConnectionEpoch Epoch,
    string ControlLeaseId,
    long ControlLeaseGeneration,
    Guid SnapshotId)
{
    public bool Matches(PasteLiveTarget live)
    {
        ArgumentNullException.ThrowIfNull(live);
        if (!live.ControlVerified || live.Access != TerminalAccess.Controlling)
            return false;
        return Pane == live.Pane
            && Epoch == live.Epoch
            && ControlLeaseId == live.ControlLeaseId
            && ControlLeaseGeneration == live.ControlLeaseGeneration;
    }

    public bool SameFrozenTarget(PasteConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Pane == request.Pane
            && Epoch == request.Epoch
            && ControlLeaseId == request.ControlLeaseId
            && ControlLeaseGeneration == request.ControlLeaseGeneration;
    }
}

public sealed record PasteActionResult(
    bool Succeeded,
    string Code,
    PasteUiState State,
    ReadOnlyMemory<byte> SentBytes,
    ClipboardIntent? Intent = null,
    AttachmentSource? AttachmentSource = null);

public sealed record OscClipboardDecision(
    OscClipboardKind Kind,
    bool Allowed,
    string Code);

public sealed record CacheStoreResult(
    bool Succeeded,
    string Code,
    Guid? ObjectId,
    long UsedBytes);

public sealed record CacheCleanupResult(
    bool Succeeded,
    string Code,
    int RemovedCount,
    long UsedBytes);

public sealed record CacheUsage(
    long UsedBytes,
    long MaxBytes,
    int ObjectCount,
    DateTimeOffset? EarliestExpiry);

public interface IClipboardSnapshotReader
{
    ClipboardSnapshot Read();
}
