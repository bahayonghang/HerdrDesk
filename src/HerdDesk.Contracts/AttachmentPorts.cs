using System.Collections.Frozen;

namespace HerdDesk.Contracts;

public enum AttachmentIntent
{
    PasteText,
    InsertFilePath,
    ImageAttachment
}

public enum CapabilityStatus
{
    Verified,
    Unsupported,
    Unknown
}

public enum AttachmentDeliveryMethod
{
    Path,
    VerifiedDirectClipboard,
    Manual
}

public enum DeliveryState
{
    Editing,
    CapabilityLoading,
    Ready,
    Uploading,
    Uploaded,
    PathReady,
    Inserting,
    PathInsertedUnconfirmed,
    Cancelled,
    Failed,
    Stale
}

public enum AttachmentSourceKind
{
    OpaqueHandle,
    LocalFile,
    RemoteFile,
    ClipboardText,
    ImageBuffer
}

public static class AttachmentOperations
{
    public const string PathInsert = "path_insert";
    public const string DirectClipboard = "direct_clipboard";
    public const string ImageAttachment = "image_attachment";
}

public static class AttachmentCodes
{
    public const string Allowed = "allowed";
    public const string Empty = "empty";
    public const string Disabled = "disabled";
    public const string SourceUnavailable = "source_unavailable";
    public const string CapabilityUnknown = "capability_unknown";
    public const string CapabilityUnsupported = "capability_unsupported";
    public const string Offline = "offline";
    public const string PermissionDenied = "permission_denied";
    public const string UploadIntegrity = "upload_integrity";
    public const string TargetStale = "target_stale";
    public const string ControlRevoked = "control_revoked";
    public const string InputRejected = "input_rejected";
    public const string ResultUnknown = "result_unknown";
    public const string Cancelled = "cancelled";
    public const string Expired = "expired";
    public const string DeliveryMethodRequired = "delivery_method_required";
    public const string DirectClipboardDenied = "direct_clipboard_denied";
    public const string InputBytesLimit = "input_bytes_limit";
    public const string InvalidPath = "invalid_path";
    public const string InFlight = "in_flight";
}

public sealed record AttachmentCapabilityKey(
    string AgentKind,
    string ExactAgentVersion,
    string TargetOs,
    string TerminalRendererVersion)
{
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(AgentKind)
        && !string.IsNullOrWhiteSpace(ExactAgentVersion)
        && !string.IsNullOrWhiteSpace(TargetOs)
        && !string.IsNullOrWhiteSpace(TerminalRendererVersion);
}

public sealed record CapabilityEvidence(
    CapabilityStatus Status,
    FrozenSet<string> VerifiedOperations,
    string? EvidenceRef,
    DateTimeOffset? ObservedAt)
{
    public static CapabilityEvidence Unknown { get; } = new(
        CapabilityStatus.Unknown,
        FrozenSet<string>.Empty,
        null,
        null);

    public bool AdvertisesAttachmentSupport =>
        Status == CapabilityStatus.Verified && VerifiedOperations.Count > 0;

    public bool Allows(string operation) =>
        Status == CapabilityStatus.Verified
        && !string.IsNullOrEmpty(operation)
        && VerifiedOperations.Contains(operation);
}

public sealed record AttachmentCapabilityRecord(
    AttachmentCapabilityKey Key,
    CapabilityEvidence Evidence);

public sealed record AttachmentSource(
    AttachmentSourceKind Kind,
    string Handle,
    string DisplayName,
    ulong? SizeBytes,
    string? ContentType);

public sealed record AttachmentDraft(
    Guid Id,
    DateTimeOffset CreatedAt,
    long Generation,
    AttachmentIntent Intent,
    AttachmentSource Source,
    PaneKey? InitialTarget);

public sealed record AttachmentTargetLease(
    PaneKey Pane,
    ConnectionEpoch Epoch,
    string ControlLeaseId,
    long ControlLeaseGeneration,
    AttachmentCapabilityKey CapabilityKey,
    DateTimeOffset Expiry)
{
    public bool Matches(AttachmentLiveTarget live)
    {
        ArgumentNullException.ThrowIfNull(live);
        if (live.Now > Expiry)
            return false;
        if (!live.ControlVerified || live.Access != TerminalAccess.Controlling)
            return false;
        return SameFrozenTarget(
            live.Pane, live.Epoch, live.ControlLeaseId, live.ControlLeaseGeneration, live.CapabilityKey);
    }

    public bool SameFrozenTarget(AttachmentConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SameFrozenTarget(
            request.Pane, request.Epoch, request.ControlLeaseId, request.ControlLeaseGeneration, request.CapabilityKey);
    }

    public bool SameFrozenTarget(
        PaneKey pane,
        ConnectionEpoch epoch,
        string controlLeaseId,
        long controlLeaseGeneration,
        AttachmentCapabilityKey capabilityKey) =>
        Pane == pane
        && Epoch == epoch
        && ControlLeaseId == controlLeaseId
        && ControlLeaseGeneration == controlLeaseGeneration
        && CapabilityKey == capabilityKey;
}

public sealed record AttachmentConfirmRequest(
    PaneKey Pane,
    ConnectionEpoch Epoch,
    string ControlLeaseId,
    long ControlLeaseGeneration,
    bool ControlVerified,
    TerminalAccess Access,
    AttachmentCapabilityKey CapabilityKey,
    DateTimeOffset Now,
    TimeSpan? Lifetime = null);

public sealed record AttachmentLiveTarget(
    PaneKey Pane,
    ConnectionEpoch Epoch,
    string ControlLeaseId,
    long ControlLeaseGeneration,
    bool ControlVerified,
    TerminalAccess Access,
    AttachmentCapabilityKey CapabilityKey,
    DateTimeOffset Now);

public sealed record AttachmentActionResult(
    bool Succeeded,
    string Code,
    DeliveryState State,
    ReadOnlyMemory<byte> InsertedBytes,
    FileLocator? FinalPath = null,
    string? Sha256 = null);

public sealed record AttachmentInputReceipt(
    bool Submitted,
    string Code,
    ReadOnlyMemory<byte> Bytes);

public interface IAttachmentInputSink
{
    ValueTask<AttachmentInputReceipt> SubmitAsync(
        RendererInput input,
        CancellationToken cancellationToken = default);
}

public interface IAttachmentClipboard
{
    bool CopyDisplayText(string displayText);
}
