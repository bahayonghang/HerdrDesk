using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class CapabilitySelectionTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("exact version match is verified", ExactVersionMatch),
        ("other version is unknown", OtherVersionUnknown),
        ("other os is unknown", OtherOsUnknown),
        ("other renderer is unknown", OtherRendererUnknown),
        ("missing evidence is unknown and does not advertise attachment", MissingIsUnknown),
        ("unsupported evidence is not verified", UnsupportedEvidence),
        ("incomplete key is unknown", IncompleteKeyUnknown),
        ("catalog has no title or terminal guess", NoGuessFromTitle),
        ("unknown and unsupported deny direct clipboard and offer copy path", UnknownOffersCopyPath)
    ];

    static void ExactVersionMatch()
    {
        var key = AttachmentHarness.Key();
        var catalog = AttachmentHarness.Catalog(new AttachmentCapabilityRecord(
            key, AttachmentHarness.Verified(AttachmentOperations.PathInsert, AttachmentOperations.DirectClipboard)));
        var evidence = catalog.Resolve(key);
        AttachmentHarness.Check(evidence.Status == CapabilityStatus.Verified);
        AttachmentHarness.Check(evidence.AdvertisesAttachmentSupport);
        AttachmentHarness.Check(evidence.Allows(AttachmentOperations.DirectClipboard));
        AttachmentHarness.Check(evidence.Allows(AttachmentOperations.PathInsert));
    }

    static void OtherVersionUnknown()
    {
        var matched = AttachmentHarness.Key(version: "1.2.3");
        var catalog = AttachmentHarness.Catalog(new AttachmentCapabilityRecord(
            matched, AttachmentHarness.Verified(AttachmentOperations.PathInsert)));
        var other = catalog.Resolve(AttachmentHarness.Key(version: "1.2.4"));
        AttachmentHarness.Check(other.Status == CapabilityStatus.Unknown);
        AttachmentHarness.Check(!other.AdvertisesAttachmentSupport);
        AttachmentHarness.Check(!other.Allows(AttachmentOperations.PathInsert));
        AttachmentHarness.Check(catalog.Resolve(AttachmentHarness.Key(version: "1.2.3")).Status
            == CapabilityStatus.Verified);
    }

    static void OtherOsUnknown()
    {
        var matched = AttachmentHarness.Key(os: "linux");
        var catalog = AttachmentHarness.Catalog(new AttachmentCapabilityRecord(
            matched, AttachmentHarness.Verified(AttachmentOperations.PathInsert)));
        var other = catalog.Resolve(AttachmentHarness.Key(os: "windows-11"));
        AttachmentHarness.Check(other.Status == CapabilityStatus.Unknown);
        AttachmentHarness.Check(!other.AdvertisesAttachmentSupport);
        AttachmentHarness.Check(!other.Allows(AttachmentOperations.PathInsert));
    }

    static void OtherRendererUnknown()
    {
        var matched = AttachmentHarness.Key(renderer: "web-1");
        var catalog = AttachmentHarness.Catalog(new AttachmentCapabilityRecord(
            matched, AttachmentHarness.Verified(AttachmentOperations.DirectClipboard)));
        var other = catalog.Resolve(AttachmentHarness.Key(renderer: "web-2"));
        AttachmentHarness.Check(other.Status == CapabilityStatus.Unknown);
        AttachmentHarness.Check(!other.Allows(AttachmentOperations.DirectClipboard));
        AttachmentHarness.Check(!other.AdvertisesAttachmentSupport);
    }

    static void MissingIsUnknown()
    {
        var catalog = AttachmentCapabilityCatalog.Empty;
        var evidence = catalog.Resolve(AttachmentHarness.Key());
        AttachmentHarness.Check(evidence.Status == CapabilityStatus.Unknown);
        AttachmentHarness.Check(!evidence.AdvertisesAttachmentSupport);
        AttachmentHarness.Check(evidence.VerifiedOperations.Count == 0);
        AttachmentHarness.Check(!evidence.Allows(AttachmentOperations.ImageAttachment));
    }

    static void UnsupportedEvidence()
    {
        var key = AttachmentHarness.Key(agent: "codex");
        var catalog = AttachmentHarness.Catalog(new AttachmentCapabilityRecord(key, AttachmentHarness.Unsupported()));
        var evidence = catalog.Resolve(key);
        AttachmentHarness.Check(evidence.Status == CapabilityStatus.Unsupported);
        AttachmentHarness.Check(!evidence.AdvertisesAttachmentSupport);
        AttachmentHarness.Check(!evidence.Allows(AttachmentOperations.DirectClipboard));
    }

    static void IncompleteKeyUnknown()
    {
        var catalog = AttachmentHarness.Catalog(new AttachmentCapabilityRecord(
            AttachmentHarness.Key(), AttachmentHarness.Verified(AttachmentOperations.PathInsert)));
        var incomplete = new AttachmentCapabilityKey("claude", "", "linux", "web-1");
        AttachmentHarness.Check(!incomplete.IsComplete);
        AttachmentHarness.Check(catalog.Resolve(incomplete).Status == CapabilityStatus.Unknown);
        AttachmentHarness.Check(catalog.Resolve(null).Status == CapabilityStatus.Unknown);
    }

    static void NoGuessFromTitle()
    {
        AttachmentHarness.Check(typeof(AttachmentCapabilityCatalog).GetMethod("ResolveFromTitle") is null);
        AttachmentHarness.Check(typeof(AttachmentCapabilityCatalog).GetMethod("Guess") is null);
        AttachmentHarness.Check(typeof(AttachmentCapabilityCatalog).GetMethod("FromPaneTitle") is null);
        AttachmentHarness.Check(typeof(AttachmentCapabilityCatalog).GetMethod("FromTerminalText") is null);
        var catalog = AttachmentCapabilityCatalog.Empty;
        AttachmentHarness.Check(catalog.Resolve(AttachmentHarness.Key(agent: "Claude Code")).Status
            == CapabilityStatus.Unknown);
    }

    static void UnknownOffersCopyPath()
    {
        var input = new RecordingAttachmentInput();
        var clipboard = new RecordingAttachmentClipboard();
        var coordinator = AttachmentHarness.Coordinator(input: input, clipboard: clipboard);
        coordinator.SetIntent(AttachmentIntent.InsertFilePath);
        coordinator.ChooseSource(AttachmentHarness.Source(), AttachmentHarness.Utf8("/tmp/a.png"));
        var unknown = coordinator.ConfirmTarget(AttachmentHarness.Confirm());
        AttachmentHarness.Check(unknown.Succeeded);
        AttachmentHarness.Check(coordinator.Evidence.Status == CapabilityStatus.Unknown);
        AttachmentHarness.Check(!coordinator.VerifiedDirectClipboardEnabled);
        AttachmentHarness.Check(coordinator.CopyPathEnabled);
        AttachmentHarness.Check(coordinator.SelectedMethod is null);
        var denied = coordinator.ChooseDeliveryMethod(AttachmentDeliveryMethod.VerifiedDirectClipboard);
        AttachmentHarness.Check(!denied.Succeeded);
        AttachmentHarness.Check(denied.Code == AttachmentCodes.DirectClipboardDenied);
        AttachmentHarness.Check(!coordinator.VerifiedDirectClipboardEnabled);
        coordinator.ChooseDeliveryMethod(AttachmentDeliveryMethod.Path);
        var copy = coordinator.CopyPath();
        AttachmentHarness.Check(copy.Succeeded);
        AttachmentHarness.Check(clipboard.Copied.Count == 1);
        AttachmentHarness.Check(clipboard.Copied[0] == "note.txt");
        AttachmentHarness.Check(!coordinator.ClaimsAgentAccepted);

        var unsupportedKey = AttachmentHarness.Key(agent: "opencode");
        var unsupported = AttachmentHarness.Coordinator(
            AttachmentHarness.Catalog(new AttachmentCapabilityRecord(
                unsupportedKey, AttachmentHarness.Unsupported())),
            input: new RecordingAttachmentInput(),
            clipboard: new RecordingAttachmentClipboard());
        unsupported.SetIntent(AttachmentIntent.ImageAttachment);
        unsupported.ChooseSource(AttachmentHarness.Source(display: "img.png"));
        unsupported.ConfirmTarget(AttachmentHarness.Confirm(key: unsupportedKey));
        AttachmentHarness.Check(unsupported.Evidence.Status == CapabilityStatus.Unsupported);
        AttachmentHarness.Check(!unsupported.VerifiedDirectClipboardEnabled);
        AttachmentHarness.Check(unsupported.CopyPathEnabled);
        AttachmentHarness.Check(unsupported.ChooseDeliveryMethod(AttachmentDeliveryMethod.VerifiedDirectClipboard)
            .Code == AttachmentCodes.DirectClipboardDenied);
    }
}
