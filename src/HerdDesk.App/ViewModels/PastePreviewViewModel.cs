using System.Globalization;
using System.Text;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Clipboard;

namespace HerdDesk.App;

public sealed class PastePreviewViewModel
{
    readonly PasteCoordinator _paste;
    readonly AttachmentCoordinator? _attachments;
    readonly AttachmentCache? _cache;

    public PastePreviewViewModel(
        PasteCoordinator paste,
        AttachmentCoordinator? attachments = null,
        AttachmentCache? cache = null)
    {
        _paste = paste ?? throw new ArgumentNullException(nameof(paste));
        _attachments = attachments;
        _cache = cache;
    }

    public PasteCoordinator Coordinator => _paste;
    public PasteUiState State => _paste.State;
    public ClipboardIntent? Intent => _paste.Intent;
    public PasteTargetLease? Lease => _paste.Lease;
    public bool ClaimsAgentAccepted => false;
    public bool PreviewImpliesReceipt => false;
    public bool DefaultIsCancel => _paste.DefaultActionIsCancel;
    public bool WatcherEnabled => false;
    public string DefaultButton => DefaultIsCancel ? "cancel" : "send";
    public string SendAutomationName => ShellStrings.PasteSend;
    public string CancelAutomationName => ShellStrings.PasteCancel;
    public string RetryAutomationName => ShellStrings.PasteRetry;
    public string DiagnosticsAutomationName => ShellStrings.PasteOpenDiagnostics;
    public string CleanupAutomationName => ShellStrings.PasteCleanupExpired;
    public string ConfirmAutomationName => AutomationName;
    public Guid? LastCacheObjectId { get; private set; }

    public string TargetBreadcrumb
    {
        get
        {
            if (_paste.Lease is not { } lease)
                return "";
            var pane = lease.Pane;
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

    public string IntentLabel => Intent?.Kind switch
    {
        ClipboardIntentKind.FileList => ShellStrings.PasteFiles,
        ClipboardIntentKind.Image => ShellStrings.PasteImage,
        ClipboardIntentKind.Mixed => ShellStrings.PasteMixed,
        ClipboardIntentKind.Empty => ShellStrings.PasteEmpty,
        ClipboardIntentKind.Unsupported => ShellStrings.PasteUnsupported,
        ClipboardIntentKind.AccessDenied => ShellStrings.PasteAccessDenied,
        _ => ShellStrings.PasteText
    };

    public string LineCountText =>
        (Intent?.LineCount ?? 0).ToString(CultureInfo.InvariantCulture) + " 行";

    public string ByteCountText =>
        (Intent?.Utf8ByteCount ?? 0).ToString(CultureInfo.InvariantCulture) + " 字节";

    public string PreviewText => UntrustedText.Display(Intent?.Preview);

    public string StatusLabel => State switch
    {
        PasteUiState.LoadingClipboard => ShellStrings.Loading,
        PasteUiState.NeedsIntentChoice => ShellStrings.PasteChooseIntent,
        PasteUiState.NeedsMultilineConfirm => ShellStrings.PasteMultiline,
        PasteUiState.CacheFull => ShellStrings.PasteCacheFull,
        PasteUiState.AccessDenied => ShellStrings.PasteAccessDenied,
        PasteUiState.Stale => ShellStrings.PasteStale,
        PasteUiState.Sending => ShellStrings.Loading,
        PasteUiState.UnknownOutcome => ShellStrings.UnknownOutcome,
        PasteUiState.Failed => FailedLabel,
        PasteUiState.Cancelled => ShellStrings.PasteCancel,
        PasteUiState.Ready when _paste.LastCode == ClipboardCodes.ContinueToAttachment =>
            ShellStrings.PasteContinueAttachment,
        PasteUiState.Ready => ShellStrings.Ready,
        _ => ShellStrings.Empty
    };

    public string FailedLabel => _paste.LastCode switch
    {
        ClipboardCodes.Empty => ShellStrings.PasteEmpty,
        ClipboardCodes.Unsupported => ShellStrings.PasteUnsupported,
        ClipboardCodes.AccessDenied => ShellStrings.PasteAccessDenied,
        ClipboardCodes.Busy => ShellStrings.PasteBusy,
        ClipboardCodes.Oversize => ShellStrings.PasteOversize,
        ClipboardCodes.TargetStale or ClipboardCodes.StaleEpoch => ShellStrings.PasteStale,
        ClipboardCodes.ControlRevoked => ShellStrings.AttachDisabled,
        ClipboardCodes.CacheFull => ShellStrings.PasteCacheFull,
        ClipboardCodes.CleanupFailed => ShellStrings.PasteCleanupFailed,
        ClipboardCodes.ResultUnknown => ShellStrings.UnknownOutcome,
        _ => ShellStrings.Failed
    };

    public string AutomationName
    {
        get
        {
            var builder = new StringBuilder();
            builder.Append(TargetBreadcrumb);
            builder.Append(' ');
            builder.Append(Intent is { RequiresMultilineConfirm: true }
                ? ShellStrings.PasteMultiline
                : IntentLabel);
            builder.Append(' ');
            builder.Append(ByteCountText);
            if (DefaultIsCancel)
            {
                builder.Append(' ');
                builder.Append(ShellStrings.PasteDefaultCancel);
            }

            builder.Append(' ');
            builder.Append(ShellStrings.PasteNotAgentReceipt);
            return builder.ToString();
        }
    }

    public string CacheUsageText
    {
        get
        {
            if (_cache is null)
                return "";
            var usage = _cache.Usage;
            return usage.UsedBytes.ToString(CultureInfo.InvariantCulture)
                + "/"
                + usage.MaxBytes.ToString(CultureInfo.InvariantCulture);
        }
    }

    public PasteActionResult BeginPaste(PasteConfirmRequest request) => _paste.BeginPaste(request);

    public PasteActionResult BeginPasteFromKeyboard(PasteConfirmRequest request) => BeginPaste(request);

    public PasteActionResult BeginPasteFromScreenReader(PasteConfirmRequest request) => BeginPaste(request);

    public PasteActionResult ChooseIntent(ClipboardIntentKind kind) => _paste.ChooseIntent(kind);

    public PasteActionResult ChooseIntentFromKeyboard(ClipboardIntentKind kind) => ChooseIntent(kind);

    public ValueTask<PasteActionResult> ConfirmAsync(PasteLiveTarget live) => _paste.ConfirmAsync(live);

    public ValueTask<PasteActionResult> ConfirmFromKeyboardAsync(PasteLiveTarget live) => ConfirmAsync(live);

    public ValueTask<PasteActionResult> ConfirmFromScreenReaderAsync(PasteLiveTarget live) => ConfirmAsync(live);

    public PasteActionResult Cancel() => _paste.Cancel();

    public PasteActionResult CancelFromKeyboard() => Cancel();

    public PasteActionResult CancelFromScreenReader() => Cancel();

    public PasteActionResult DismissEsc() => Cancel();

    public PasteActionResult NoteLiveTarget(PasteLiveTarget live) => _paste.NoteLiveTarget(live);

    public PasteActionResult ContinueToAttachment()
    {
        if (State == PasteUiState.Stale)
            return new(false, _paste.LastCode ?? ClipboardCodes.TargetStale, State, ReadOnlyMemory<byte>.Empty, Intent);
        if (State == PasteUiState.Cancelled)
            return new(false, ClipboardCodes.Cancelled, State, ReadOnlyMemory<byte>.Empty, Intent);
        if (_attachments is null || _paste.PendingSnapshot is not { } snapshot || Intent is null)
            return new(false, ClipboardCodes.Empty, State, ReadOnlyMemory<byte>.Empty, Intent);
        if (Intent.Kind is not (ClipboardIntentKind.FileList or ClipboardIntentKind.Image))
            return new(false, ClipboardCodes.Unsupported, State, ReadOnlyMemory<byte>.Empty, Intent);

        if (Intent.Kind == ClipboardIntentKind.Image)
        {
            _attachments.SetIntent(AttachmentIntent.ImageAttachment);
            if (_cache is not null && snapshot.Image is { } image && image.Bytes.Length > 0)
            {
                var stored = _cache.Store(image.Bytes);
                if (!stored.Succeeded)
                {
                    return new(false, stored.Code, stored.Code == ClipboardCodes.CacheFull
                        ? PasteUiState.CacheFull
                        : PasteUiState.Failed, ReadOnlyMemory<byte>.Empty, Intent);
                }

                LastCacheObjectId = stored.ObjectId;
            }
        }
        else
            _attachments.SetIntent(AttachmentIntent.InsertFilePath);

        var source = Intent.Kind == ClipboardIntentKind.Image
            ? new AttachmentSource(
                AttachmentSourceKind.ImageBuffer,
                snapshot.Image?.OpaqueId ?? "image",
                "clipboard-image",
                snapshot.Image?.SizeBytes,
                snapshot.Image?.ContentType)
            : new AttachmentSource(
                AttachmentSourceKind.LocalFile,
                snapshot.Files.Count > 0 ? snapshot.Files[0].OpaqueId : "file",
                snapshot.Files.Count > 0 ? snapshot.Files[0].DisplayName : "clipboard-file",
                snapshot.Files.Count > 0 ? snapshot.Files[0].SizeBytes : null,
                "application/octet-stream");
        var chosen = _attachments.ChooseSource(source);
        return new(
            chosen.Succeeded,
            chosen.Succeeded ? ClipboardCodes.ContinueToAttachment : chosen.Code,
            State,
            ReadOnlyMemory<byte>.Empty,
            Intent,
            source);
    }

    public CacheCleanupResult CleanupExpired()
    {
        if (_cache is null)
            return new(false, ClipboardCodes.Empty, 0, 0);
        return _cache.CleanupExpired();
    }
}
