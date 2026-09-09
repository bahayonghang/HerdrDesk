using System.Collections.Frozen;
using System.Text;
using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class AttachToAgentViewModelTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("three intents have distinct labels and keyboard paths", DistinctIntents),
        ("unknown capability does not advertise attachment and offers copy path", UnknownCopyPath),
        ("unsupported badge shows version and does not say supports attachment", UnsupportedBadge),
        ("untrusted source name is display only", UntrustedSourceName),
        ("states are text not icon only", StatesHaveText),
        ("target breadcrumb stays on frozen pane", FrozenBreadcrumb),
        ("preview does not upgrade capability", PreviewDoesNotUpgrade)
    ];

    static void DistinctIntents()
    {
        using var env = new AttachEnv();
        AppTestHost.Check(env.Vm.IntentPasteTextAutomationName == ShellStrings.AttachPasteText);
        AppTestHost.Check(env.Vm.IntentInsertPathAutomationName == ShellStrings.AttachInsertPath);
        AppTestHost.Check(env.Vm.IntentImageAutomationName == ShellStrings.AttachImage);
        AppTestHost.Check(env.Vm.IntentPasteTextAutomationName != env.Vm.IntentImageAutomationName);
        env.Vm.SetIntentFromKeyboard(AttachmentIntent.PasteText);
        AppTestHost.Check(env.Vm.IntentLabel == ShellStrings.AttachPasteText);
        env.Vm.SetIntentFromScreenReader(AttachmentIntent.ImageAttachment);
        AppTestHost.Check(env.Vm.IntentLabel == ShellStrings.AttachImage);
        env.Vm.SetIntent(AttachmentIntent.InsertFilePath);
        AppTestHost.Check(env.Vm.IntentLabel == ShellStrings.AttachInsertPath);
        AppTestHost.Check(env.Vm.DropZoneAutomationName.Contains("键盘", StringComparison.Ordinal));
        AppTestHost.Check(env.Vm.ChooseSourceAutomationName == ShellStrings.AttachChooseSource);
    }

    static void UnknownCopyPath()
    {
        using var env = new AttachEnv();
        env.Vm.SetIntent(AttachmentIntent.InsertFilePath);
        env.Vm.ChooseSourceFromKeyboard(new AttachmentSource(
            AttachmentSourceKind.LocalFile, "h1", "report.txt", 4, "text/plain"),
            Encoding.UTF8.GetBytes("/tmp/report.txt"));
        env.Vm.ConfirmTarget(env.Confirm());
        AppTestHost.Check(env.Vm.Evidence.Status == CapabilityStatus.Unknown);
        AppTestHost.Check(!env.Vm.ShowsSupportsAttachment);
        AppTestHost.Check(!env.Vm.CapabilityBadgeText.Contains(ShellStrings.AttachSupportsAttachment, StringComparison.Ordinal));
        AppTestHost.Check(env.Vm.CapabilityBadgeText.Contains(ShellStrings.AttachUnknown, StringComparison.Ordinal));
        AppTestHost.Check(!env.Vm.DirectClipboardEnabled);
        AppTestHost.Check(env.Vm.CopyPathEnabled);
        AppTestHost.Check(!env.Vm.InsertPathEnabled);
        env.Vm.ChooseDeliveryMethod(AttachmentDeliveryMethod.Path);
        var copy = env.Vm.CopyPathFromScreenReader();
        AppTestHost.Check(copy.Succeeded);
        AppTestHost.Check(env.Clipboard.Copied.Count == 1);
        AppTestHost.Check(!env.Vm.AgentAccepted);
        var denied = env.Vm.ChooseDeliveryMethod(AttachmentDeliveryMethod.VerifiedDirectClipboard);
        AppTestHost.Check(denied.Code == AttachmentCodes.DirectClipboardDenied);
    }

    static void UnsupportedBadge()
    {
        using var env = new AttachEnv(unsupported: true);
        env.Vm.SetIntent(AttachmentIntent.InsertFilePath);
        env.Vm.ChooseSource(new AttachmentSource(
            AttachmentSourceKind.LocalFile, "h1", "report.txt", 4, "text/plain"),
            Encoding.UTF8.GetBytes("/tmp/report.txt"));
        env.Vm.ConfirmTarget(env.Confirm());
        AppTestHost.Check(env.Vm.Evidence.Status == CapabilityStatus.Unsupported);
        AppTestHost.Check(!env.Vm.ShowsSupportsAttachment);
        AppTestHost.Check(!env.Vm.CapabilityBadgeText.Contains(ShellStrings.AttachSupportsAttachment, StringComparison.Ordinal));
        AppTestHost.Check(env.Vm.CapabilityBadgeText.Contains(ShellStrings.AttachUnsupported, StringComparison.Ordinal));
        AppTestHost.Check(env.Vm.CapabilityBadgeText.Contains("1.2.3", StringComparison.Ordinal));
        AppTestHost.Check(env.Vm.CapabilityBadgeText.Contains("2026-09-08", StringComparison.Ordinal));
        AppTestHost.Check(!env.Vm.DirectClipboardEnabled);
        AppTestHost.Check(env.Vm.CopyPathEnabled);
        AppTestHost.Check(!env.Vm.AgentAccepted);
    }

    static void UntrustedSourceName()
    {
        using var env = new AttachEnv();
        env.Vm.SetIntent(AttachmentIntent.InsertFilePath);
        env.Vm.AcceptDrop(new AttachmentSource(
            AttachmentSourceKind.LocalFile,
            "h2",
            "evil\nname\u0000.bin",
            1,
            "application/octet-stream"),
            Encoding.UTF8.GetBytes("evil.bin"));
        AppTestHost.Check(!env.Vm.SourceDisplay.Contains('\n'));
        AppTestHost.Check(!env.Vm.SourceDisplay.Contains('\0'));
        AppTestHost.Check(UntrustedText.TryAsPath(env.Vm.SourceDisplay) is null);
        AppTestHost.Check(!UntrustedText.TryAsCommand(env.Vm.SourceDisplay));
        AppTestHost.Check(!UntrustedText.TryAsUri(env.Vm.SourceDisplay));
    }

    static void StatesHaveText()
    {
        using var env = new AttachEnv();
        AppTestHost.Check(env.Vm.StatusLabel == ShellStrings.Empty);
        AppTestHost.Check(env.Vm.StatusAutomationName.Contains(ShellStrings.AttachNotAutoSubmitted, StringComparison.Ordinal));
        env.Vm.ChooseSource(new AttachmentSource(
            AttachmentSourceKind.ClipboardText, "t", "clip", 5, "text/plain"),
            Encoding.UTF8.GetBytes("hello"));
        env.Vm.ConfirmTarget(env.Confirm(access: TerminalAccess.Disconnected));
        AppTestHost.Check(env.Vm.StatusLabel == ShellStrings.Offline);
        AppTestHost.Check(env.Vm.FailedLabel == ShellStrings.Offline);

        using var ready = new AttachEnv(verified: true);
        ready.Vm.ChooseSource(new AttachmentSource(
            AttachmentSourceKind.ClipboardText, "t", "clip", 5, "text/plain"),
            Encoding.UTF8.GetBytes("hello"));
        ready.Vm.ConfirmTarget(ready.Confirm());
        AppTestHost.Check(ready.Vm.StatusLabel.Length > 0);
        AppTestHost.Check(ready.Vm.CapabilityStatusText == ShellStrings.AttachVerified);
        ready.Vm.InsertPathFromKeyboardAsync(ready.Live()).AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(ready.Vm.State == DeliveryState.PathInsertedUnconfirmed);
        AppTestHost.Check(ready.Vm.StatusLabel == ShellStrings.AttachPathInsertedUnconfirmed);
        AppTestHost.Check(!ready.Vm.ClaimsAgentReceived);
        AppTestHost.Check(ready.Vm.StatusAutomationName.Contains(ShellStrings.AttachNotAutoSubmitted, StringComparison.Ordinal));
    }

    static void FrozenBreadcrumb()
    {
        using var env = new AttachEnv(verified: true);
        var pane = env.Pane;
        env.Vm.Open(env.Live(), true);
        env.Vm.ChooseSource(new AttachmentSource(
            AttachmentSourceKind.ClipboardText, "t", "clip", 5, "text/plain"),
            Encoding.UTF8.GetBytes("hello"));
        env.Vm.ConfirmTarget(env.Confirm());
        AppTestHost.Check(env.Vm.TargetBreadcrumb.Contains(pane.PaneId, StringComparison.Ordinal));
        AppTestHost.Check(env.Vm.TargetBreadcrumb.Contains(" > ", StringComparison.Ordinal));
        var other = new PaneKey(pane.Session, pane.WorkspaceId, "other");
        env.Vm.NoteLiveTarget(env.Live(other));
        AppTestHost.Check(env.Vm.State == DeliveryState.Stale);
        AppTestHost.Check(env.Vm.StatusLabel == ShellStrings.Expired);
        AppTestHost.Check(env.Vm.Lease is not null);
        AppTestHost.Check(env.Vm.Lease!.Pane == pane);
        AppTestHost.Check(!env.Vm.TryRestoreFocus(env.Live(other), true));
        AppTestHost.Check(env.Vm.TryRestoreFocus(env.Live(pane), true));
    }

    static void PreviewDoesNotUpgrade()
    {
        using var env = new AttachEnv();
        env.Vm.SetIntent(AttachmentIntent.ImageAttachment);
        env.Vm.ChooseSource(new AttachmentSource(
            AttachmentSourceKind.ImageBuffer, "img", "shot.png", 12, "image/png"));
        AppTestHost.Check(env.Vm.PreviewAvailable);
        env.Vm.ConfirmTarget(env.Confirm());
        AppTestHost.Check(env.Vm.Evidence.Status == CapabilityStatus.Unknown);
        AppTestHost.Check(!env.Vm.ShowsSupportsAttachment);
        AppTestHost.Check(!env.Vm.DirectClipboardEnabled);
        AppTestHost.Check(!env.Vm.AgentAccepted);
    }
}

internal sealed class AttachEnv : IDisposable
{
    public RecordingAttachmentInput Input { get; } = new();
    public RecordingAttachmentClipboard Clipboard { get; } = new();
    public AttachmentLeaseRegistry Leases { get; } = new();
    public PaneKey Pane { get; }
    public AttachmentCapabilityKey CapabilityKey { get; }
    public AttachToAgentViewModel Vm { get; }

    public AttachEnv(bool verified = false, bool unsupported = false)
    {
        Pane = new PaneKey(AppTestHost.SessionOf(AppTestHost.DeviceA), "ws", "p1");
        CapabilityKey = new AttachmentCapabilityKey("claude", "1.2.3", "linux", "web-1");
        AttachmentCapabilityCatalog catalog;
        if (verified)
        {
            catalog = new AttachmentCapabilityCatalog(
            [
                new AttachmentCapabilityRecord(
                    CapabilityKey,
                    new CapabilityEvidence(
                        CapabilityStatus.Verified,
                        new[] { AttachmentOperations.PathInsert }.ToFrozenSet(),
                        "evidence/hd-030/synthetic",
                        new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero)))
            ]);
        }
        else if (unsupported)
        {
            catalog = new AttachmentCapabilityCatalog(
            [
                new AttachmentCapabilityRecord(
                    CapabilityKey,
                    new CapabilityEvidence(
                        CapabilityStatus.Unsupported,
                        FrozenSet<string>.Empty,
                        "evidence/hd-030/unsupported",
                        new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero)))
            ]);
        }
        else
        {
            catalog = AttachmentCapabilityCatalog.Empty;
        }
        var coordinator = new AttachmentCoordinator(
            catalog,
            new TransferCoordinator(new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(8))),
            Leases,
            Input,
            Clipboard);
        Vm = new AttachToAgentViewModel(coordinator);
    }

    public AttachmentConfirmRequest Confirm(
        TerminalAccess access = TerminalAccess.Controlling,
        bool verified = true) =>
        new(
            Pane,
            new ConnectionEpoch(1),
            "lease-1",
            1,
            verified,
            access,
            CapabilityKey,
            new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));

    public AttachmentLiveTarget Live(PaneKey? pane = null) =>
        new(
            pane ?? Pane,
            new ConnectionEpoch(1),
            "lease-1",
            1,
            true,
            TerminalAccess.Controlling,
            CapabilityKey,
            new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
    }
}

internal sealed class RecordingAttachmentInput : IAttachmentInputSink
{
    public List<RendererInput> Submitted { get; } = [];

    public ValueTask<AttachmentInputReceipt> SubmitAsync(
        RendererInput input,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        Submitted.Add(input);
        return ValueTask.FromResult(new AttachmentInputReceipt(true, AttachmentCodes.Allowed, input.Bytes));
    }
}

internal sealed class RecordingAttachmentClipboard : IAttachmentClipboard
{
    public List<string> Copied { get; } = [];

    public bool CopyDisplayText(string displayText)
    {
        Copied.Add(displayText);
        return true;
    }
}
