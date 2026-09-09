using System.Globalization;
using System.Text;
using HerdDesk.Contracts;
using HerdDesk.Core;

namespace HerdDesk.App;

public sealed class AttachToAgentViewModel
{
    readonly AttachmentCoordinator _coordinator;
    AttachmentLiveTarget? _savedFocus;
    bool _savedRendererReady;
    IFileEndpoint? _uploadDestination;
    FileLocator? _uploadParent;
    FileComponent? _uploadName;

    public AttachToAgentViewModel(AttachmentCoordinator coordinator)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    public AttachmentCoordinator Coordinator => _coordinator;
    public AttachmentIntent Intent => _coordinator.Intent;
    public DeliveryState State => _coordinator.State;
    public CapabilityEvidence Evidence => _coordinator.Evidence;
    public AttachmentDraft? Draft => _coordinator.Draft;
    public AttachmentTargetLease? Lease => _coordinator.Lease;
    public AttachmentLiveTarget? LiveTarget { get; private set; }
    public bool PreviewAvailable { get; private set; }
    public bool AgentAccepted => false;
    public bool ClaimsAgentReceived => false;

    public string IntentPasteTextAutomationName => ShellStrings.AttachPasteText;
    public string IntentInsertPathAutomationName => ShellStrings.AttachInsertPath;
    public string IntentImageAutomationName => ShellStrings.AttachImage;
    public string CopyPathAutomationName => ShellStrings.AttachCopyPath;
    public string InsertPathAutomationName => ShellStrings.AttachInsertPathAction;
    public string ManualAutomationName => ShellStrings.AttachManual;
    public string DirectClipboardAutomationName => ShellStrings.AttachDirectClipboard;
    public string UploadAutomationName => ShellStrings.AttachUpload;
    public string CancelAutomationName => ShellStrings.AttachCancel;
    public string CloseAutomationName => ShellStrings.AttachClose;
    public string ChooseSourceAutomationName => ShellStrings.AttachChooseSource;
    public string DropZoneAutomationName => ShellStrings.AttachDropZone;

    public string IntentLabel => Intent switch
    {
        AttachmentIntent.InsertFilePath => ShellStrings.AttachInsertPath,
        AttachmentIntent.ImageAttachment => ShellStrings.AttachImage,
        _ => ShellStrings.AttachPasteText
    };

    public string CapabilityStatusText => Evidence.Status switch
    {
        CapabilityStatus.Verified => ShellStrings.AttachVerified,
        CapabilityStatus.Unsupported => ShellStrings.AttachUnsupported,
        _ => ShellStrings.AttachUnknown
    };

    public string CapabilityBadgeText
    {
        get
        {
            if (Evidence.Status == CapabilityStatus.Unknown)
                return CapabilityStatusText + " " + ShellStrings.AttachNoEvidence;
            var version = Lease?.CapabilityKey.ExactAgentVersion ?? "";
            var when = Evidence.ObservedAt is DateTimeOffset at
                ? at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : "";
            return CapabilityStatusText + " " + version + " " + when;
        }
    }

    public string CapabilityAutomationName => CapabilityBadgeText;

    public bool ShowsSupportsAttachment => Evidence.AdvertisesAttachmentSupport;

    public string TargetBreadcrumb
    {
        get
        {
            PaneKey? candidate = Lease?.Pane ?? LiveTarget?.Pane;
            if (candidate is not PaneKey pane)
                return "";
            var builder = new StringBuilder();
            builder.Append(pane.Session.Device.Value.ToString("D"));
            builder.Append(" > ");
            builder.Append(UntrustedText.Display(pane.Session.SessionName ?? pane.Session.EndpointKey));
            builder.Append(" > ");
            builder.Append(UntrustedText.Display(pane.WorkspaceId));
            builder.Append(" > ");
            builder.Append(UntrustedText.Display(pane.PaneId));
            return builder.ToString();
        }
    }

    public string AgentProfileText
    {
        get
        {
            var key = Lease?.CapabilityKey ?? LiveTarget?.CapabilityKey;
            if (key is null)
                return UntrustedText.Display(ShellStrings.Unknown);
            return UntrustedText.Display(key.AgentKind) + " " + UntrustedText.Display(key.ExactAgentVersion);
        }
    }

    public string SourceDisplay => Draft is null ? "" : UntrustedText.Display(Draft.Source.DisplayName);

    public string StatusLabel => State switch
    {
        DeliveryState.CapabilityLoading => ShellStrings.AttachCapabilityLoading,
        DeliveryState.Ready when Evidence.Status == CapabilityStatus.Unknown => ShellStrings.AttachUnknown,
        DeliveryState.Ready when Evidence.Status == CapabilityStatus.Unsupported => ShellStrings.AttachUnsupported,
        DeliveryState.Ready => ShellStrings.Ready,
        DeliveryState.Uploading => ShellStrings.FileTransferring,
        DeliveryState.Uploaded => ShellStrings.FileCompleted,
        DeliveryState.PathReady => ShellStrings.AttachPathReady,
        DeliveryState.Inserting => ShellStrings.Loading,
        DeliveryState.PathInsertedUnconfirmed => ShellStrings.AttachPathInsertedUnconfirmed,
        DeliveryState.Cancelled => ShellStrings.FileCancelled,
        DeliveryState.Failed => FailedLabel,
        DeliveryState.Stale => ShellStrings.Expired,
        DeliveryState.Editing when Draft is null => ShellStrings.Empty,
        _ => ShellStrings.Empty
    };

    public string StatusAutomationName => StatusLabel + " " + ShellStrings.AttachNotAutoSubmitted;

    public string FailedLabel => _coordinator.LastCode switch
    {
        AttachmentCodes.Offline => ShellStrings.Offline,
        AttachmentCodes.PermissionDenied => ShellStrings.PermissionDenied,
        AttachmentCodes.UploadIntegrity => ShellStrings.FileFailed,
        AttachmentCodes.SourceUnavailable => ShellStrings.FileFailed,
        AttachmentCodes.ControlRevoked => ShellStrings.AttachDisabled,
        AttachmentCodes.InputRejected => ShellStrings.FileFailed,
        AttachmentCodes.ResultUnknown => ShellStrings.UnknownOutcome,
        AttachmentCodes.Expired => ShellStrings.Expired,
        AttachmentCodes.Disabled => ShellStrings.AttachDisabled,
        _ => ShellStrings.Failed
    };

    public bool CopyPathEnabled => _coordinator.CopyPathEnabled;
    public bool InsertPathEnabled => _coordinator.InsertEnabled;
    public bool UploadEnabled => _coordinator.UploadEnabled;
    public bool DirectClipboardEnabled => _coordinator.VerifiedDirectClipboardEnabled;
    public bool ManualEnabled => State is DeliveryState.Ready or DeliveryState.PathReady;

    public void Open(AttachmentLiveTarget target, bool rendererReady)
    {
        LiveTarget = target;
        _savedFocus = target;
        _savedRendererReady = rendererReady;
    }

    public bool TryRestoreFocus(AttachmentLiveTarget current, bool rendererReady)
    {
        if (_savedFocus is null || !rendererReady || !_savedRendererReady)
            return false;
        return _savedFocus.Pane == current.Pane && _savedFocus.Epoch == current.Epoch;
    }

    public void NoteLiveTarget(AttachmentLiveTarget live)
    {
        LiveTarget = live;
        _coordinator.NoteLiveTarget(live);
    }

    public AttachmentActionResult SetIntent(AttachmentIntent intent) => _coordinator.SetIntent(intent);

    public AttachmentActionResult SetIntentFromKeyboard(AttachmentIntent intent) => SetIntent(intent);

    public AttachmentActionResult SetIntentFromScreenReader(AttachmentIntent intent) => SetIntent(intent);

    public AttachmentActionResult ChooseSource(
        AttachmentSource source,
        ReadOnlyMemory<byte> insertPayload = default,
        IFileEndpoint? fileSource = null,
        FileLocator? filePath = null)
    {
        PreviewAvailable = source.Kind == AttachmentSourceKind.ImageBuffer
            || Intent == AttachmentIntent.ImageAttachment;
        return _coordinator.ChooseSource(
            source,
            insertPayload,
            fileSource,
            filePath,
            LiveTarget?.Pane);
    }

    public AttachmentActionResult ChooseSourceFromKeyboard(
        AttachmentSource source,
        ReadOnlyMemory<byte> insertPayload = default,
        IFileEndpoint? fileSource = null,
        FileLocator? filePath = null) =>
        ChooseSource(source, insertPayload, fileSource, filePath);

    public AttachmentActionResult AcceptDrop(
        AttachmentSource source,
        ReadOnlyMemory<byte> insertPayload = default,
        IFileEndpoint? fileSource = null,
        FileLocator? filePath = null) =>
        ChooseSource(source, insertPayload, fileSource, filePath);

    public void BindUploadTarget(IFileEndpoint destination, FileLocator parent, FileComponent name)
    {
        _uploadDestination = destination;
        _uploadParent = parent;
        _uploadName = name;
    }

    public AttachmentActionResult ConfirmTarget(AttachmentConfirmRequest request)
    {
        LiveTarget = new AttachmentLiveTarget(
            request.Pane,
            request.Epoch,
            request.ControlLeaseId,
            request.ControlLeaseGeneration,
            request.ControlVerified,
            request.Access,
            request.CapabilityKey,
            request.Now);
        return _coordinator.ConfirmTarget(request);
    }

    public AttachmentActionResult ChooseDeliveryMethod(AttachmentDeliveryMethod method) =>
        _coordinator.ChooseDeliveryMethod(method);

    public ValueTask<AttachmentActionResult> StartUploadAsync(AttachmentLiveTarget live)
    {
        if (_uploadDestination is null || _uploadParent is null || _uploadName is null)
            return ValueTask.FromResult(new AttachmentActionResult(
                false, AttachmentCodes.SourceUnavailable, State, ReadOnlyMemory<byte>.Empty));
        LiveTarget = live;
        return _coordinator.StartUploadAsync(
            _uploadDestination, _uploadParent, _uploadName, live);
    }

    public ValueTask<AttachmentActionResult> StartUploadFromKeyboardAsync(AttachmentLiveTarget live) =>
        StartUploadAsync(live);

    public ValueTask<AttachmentActionResult> InsertPathAsync(AttachmentLiveTarget live)
    {
        LiveTarget = live;
        return _coordinator.InsertAsync(live);
    }

    public ValueTask<AttachmentActionResult> InsertPathFromKeyboardAsync(AttachmentLiveTarget live) =>
        InsertPathAsync(live);

    public ValueTask<AttachmentActionResult> InsertPathFromScreenReaderAsync(AttachmentLiveTarget live) =>
        InsertPathAsync(live);

    public AttachmentActionResult CopyPath() => _coordinator.CopyPath();

    public AttachmentActionResult CopyPathFromKeyboard() => CopyPath();

    public AttachmentActionResult CopyPathFromScreenReader() => CopyPath();

    public ValueTask<AttachmentActionResult> CancelAsync() => _coordinator.CancelAsync();

    public ValueTask<AttachmentActionResult> CancelFromKeyboardAsync() => CancelAsync();

    public AttachmentActionResult Close()
    {
        PreviewAvailable = false;
        return _coordinator.DiscardDraft();
    }
}
