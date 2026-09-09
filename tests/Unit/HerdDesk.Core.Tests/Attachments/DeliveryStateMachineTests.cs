using System.Text;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class DeliveryStateMachineTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("upload hash rename then path insert stays unconfirmed", UploadThenUnconfirmed),
        ("image insert without hash rename is disabled", ImageInsertRequiresUpload),
        ("preview and upload are not agent accepted", PreviewUploadNotAccepted),
        ("cancel releases only this draft cache", CancelReleasesOnlyThisDraft),
        ("offline permission and unknown result stay failed", ErrorStates),
        ("paste text skips upload", PasteTextSkipsUpload)
    ];

    static void UploadThenUnconfirmed()
    {
        var key = AttachmentHarness.Key();
        var catalog = AttachmentHarness.Catalog(new AttachmentCapabilityRecord(
            key, AttachmentHarness.Verified(AttachmentOperations.PathInsert, AttachmentOperations.ImageAttachment)));
        var input = new RecordingAttachmentInput();
        var diagnostics = new RecordingAttachmentDiagnostics();
        var coordinator = AttachmentHarness.Coordinator(catalog, input: input, diagnostics: diagnostics);
        coordinator.SetIntent(AttachmentIntent.ImageAttachment);
        var src = AttachmentHarness.Endpoint("src");
        var dst = AttachmentHarness.Endpoint("dst");
        src.AddFile("img.png", "SECRET_BODY"u8.ToArray());
        coordinator.ChooseSource(
            AttachmentHarness.Source("img", "img.png", AttachmentSourceKind.ImageBuffer),
            fileSource: src,
            filePath: FileLocator.Root.Append(AttachmentHarness.Comp("img.png")));
        coordinator.ConfirmTarget(AttachmentHarness.Confirm(key: key));
        AttachmentHarness.Check(coordinator.SelectedMethod == AttachmentDeliveryMethod.Path);
        var live = AttachmentHarness.Live(key: key);
        var uploaded = coordinator.StartUploadAsync(
            dst, FileLocator.Root, AttachmentHarness.Comp("img.png"), live).AsTask().GetAwaiter().GetResult();
        AttachmentHarness.Check(uploaded.Succeeded);
        AttachmentHarness.Check(coordinator.State == DeliveryState.PathReady);
        AttachmentHarness.Check(!string.IsNullOrEmpty(coordinator.Sha256));
        AttachmentHarness.Check(coordinator.FinalPath is not null);
        AttachmentHarness.Check(!coordinator.ClaimsAgentAccepted);
        var inserted = coordinator.InsertAsync(live).AsTask().GetAwaiter().GetResult();
        AttachmentHarness.Check(inserted.Succeeded);
        AttachmentHarness.Check(coordinator.State == DeliveryState.PathInsertedUnconfirmed);
        AttachmentHarness.Check(!coordinator.ClaimsAgentAccepted);
        AttachmentHarness.Check(input.Submitted.Count == 1);
        AttachmentHarness.Check(input.Submitted[0].Origin == InputOrigin.ExplicitPaste);
        AttachmentHarness.Check(Encoding.UTF8.GetString(coordinator.LastInsertedBytes.Span) == "/img.png");
        AttachmentHarness.Check(!AttachmentHarness.HasSubmitKey(coordinator.LastInsertedBytes.Span));
        AttachmentHarness.Check(coordinator.LastInsertedBytes.Span[^1] is not 0x0A and not 0x0D and not 0x20);
        foreach (var evt in diagnostics.Events)
        {
            AttachmentHarness.Check(evt.ErrorCode is null
                || (!evt.ErrorCode.Contains("SECRET_BODY", StringComparison.Ordinal)
                    && !evt.ErrorCode.Contains("/img.png", StringComparison.Ordinal)));
            AttachmentHarness.Check(evt.DeviceAlias is null);
            AttachmentHarness.Check(evt.SessionAlias is null);
        }
    }

    static void ImageInsertRequiresUpload()
    {
        var key = AttachmentHarness.Key();
        var catalog = AttachmentHarness.Catalog(new AttachmentCapabilityRecord(
            key, AttachmentHarness.Verified(AttachmentOperations.PathInsert, AttachmentOperations.ImageAttachment)));
        var input = new RecordingAttachmentInput();
        var coordinator = AttachmentHarness.Coordinator(catalog, input: input);
        var src = AttachmentHarness.Endpoint("src-skip");
        src.AddFile("img.png", "SECRET_BODY"u8.ToArray());
        coordinator.SetIntent(AttachmentIntent.ImageAttachment);
        coordinator.ChooseSource(
            AttachmentHarness.Source("img", "img.png", AttachmentSourceKind.ImageBuffer),
            AttachmentHarness.Utf8("/tmp/img.png"),
            src,
            FileLocator.Root.Append(AttachmentHarness.Comp("img.png")));
        coordinator.ConfirmTarget(AttachmentHarness.Confirm(key: key));
        AttachmentHarness.Check(coordinator.State == DeliveryState.Ready);
        AttachmentHarness.Check(!coordinator.InsertEnabled);
        var inserted = coordinator.InsertAsync(AttachmentHarness.Live(key: key))
            .AsTask().GetAwaiter().GetResult();
        AttachmentHarness.Check(!inserted.Succeeded);
        AttachmentHarness.Check(inserted.Code == AttachmentCodes.Disabled);
        AttachmentHarness.Check(coordinator.State != DeliveryState.PathInsertedUnconfirmed);
        AttachmentHarness.Check(coordinator.State != DeliveryState.PathReady);
        AttachmentHarness.Check(input.Submitted.Count == 0);
        AttachmentHarness.Check(coordinator.TransferStarts == 0);
        AttachmentHarness.Check(!coordinator.ClaimsAgentAccepted);
    }

    static void PreviewUploadNotAccepted()
    {
        AttachmentHarness.Check(!Enum.GetNames<DeliveryState>().Contains("AgentAccepted"));
        var coordinator = AttachmentHarness.Coordinator();
        AttachmentHarness.Check(!coordinator.ClaimsAgentAccepted);
        AttachmentHarness.Check(!coordinator.PreviewImpliesReceipt);
        coordinator.SetIntent(AttachmentIntent.ImageAttachment);
        coordinator.ChooseSource(AttachmentHarness.Source(display: "preview.png", kind: AttachmentSourceKind.ImageBuffer));
        AttachmentHarness.Check(coordinator.State == DeliveryState.Editing);
        AttachmentHarness.Check(!coordinator.ClaimsAgentAccepted);
    }

    static void CancelReleasesOnlyThisDraft()
    {
        var key = AttachmentHarness.Key();
        var catalog = AttachmentHarness.Catalog(new AttachmentCapabilityRecord(
            key, AttachmentHarness.Verified(AttachmentOperations.PathInsert, AttachmentOperations.ImageAttachment)));
        var transfers = AttachmentHarness.Transfers();
        var leases = new AttachmentLeaseRegistry();
        var aInput = new RecordingAttachmentInput();
        var bInput = new RecordingAttachmentInput();
        var a = AttachmentHarness.Coordinator(catalog, transfers, leases, aInput);
        var b = AttachmentHarness.Coordinator(catalog, transfers, leases, bInput);
        var srcA = AttachmentHarness.Endpoint("src-a");
        var dstA = AttachmentHarness.Endpoint("dst-a");
        var srcB = AttachmentHarness.Endpoint("src-b");
        var dstB = AttachmentHarness.Endpoint("dst-b");
        srcA.AddFile("a.png", "aaa"u8.ToArray());
        srcB.AddFile("b.png", "bbb"u8.ToArray());
        a.SetIntent(AttachmentIntent.ImageAttachment);
        b.SetIntent(AttachmentIntent.ImageAttachment);
        a.ChooseSource(
            AttachmentHarness.Source("a", "a.png", AttachmentSourceKind.ImageBuffer),
            fileSource: srcA,
            filePath: FileLocator.Root.Append(AttachmentHarness.Comp("a.png")));
        b.ChooseSource(
            AttachmentHarness.Source("b", "b.png", AttachmentSourceKind.ImageBuffer),
            fileSource: srcB,
            filePath: FileLocator.Root.Append(AttachmentHarness.Comp("b.png")));
        a.ConfirmTarget(AttachmentHarness.Confirm(key: key));
        b.ConfirmTarget(AttachmentHarness.Confirm(key: key));
        var live = AttachmentHarness.Live(key: key);
        a.StartUploadAsync(dstA, FileLocator.Root, AttachmentHarness.Comp("a.png"), live)
            .AsTask().GetAwaiter().GetResult();
        b.StartUploadAsync(dstB, FileLocator.Root, AttachmentHarness.Comp("b.png"), live)
            .AsTask().GetAwaiter().GetResult();
        AttachmentHarness.Check(a.CacheHeld);
        AttachmentHarness.Check(b.CacheHeld);
        var aId = a.Draft!.Id;
        var bId = b.Draft!.Id;
        a.CancelAsync().AsTask().GetAwaiter().GetResult();
        AttachmentHarness.Check(a.State == DeliveryState.Cancelled);
        AttachmentHarness.Check(!leases.IsHeld(aId));
        AttachmentHarness.Check(leases.IsHeld(bId));
        AttachmentHarness.Check(b.State == DeliveryState.PathReady);
        AttachmentHarness.Check(dstB.Has("b.png"));
        AttachmentHarness.Check(Encoding.UTF8.GetString(dstB.ReadAll("b.png")) == "bbb");
        AttachmentHarness.Check(b.CacheHeld);
    }

    static void ErrorStates()
    {
        var coordinator = AttachmentHarness.Coordinator();
        coordinator.ChooseSource(AttachmentHarness.Source(), AttachmentHarness.Utf8("hi"));
        var offline = coordinator.ConfirmTarget(AttachmentHarness.Confirm(access: TerminalAccess.Disconnected));
        AttachmentHarness.Check(!offline.Succeeded);
        AttachmentHarness.Check(offline.Code == AttachmentCodes.Offline);
        AttachmentHarness.Check(coordinator.State == DeliveryState.Failed);

        var disabled = AttachmentHarness.Coordinator();
        disabled.ChooseSource(AttachmentHarness.Source(), AttachmentHarness.Utf8("hi"));
        var deny = disabled.ConfirmTarget(AttachmentHarness.Confirm(verified: false));
        AttachmentHarness.Check(!deny.Succeeded);
        AttachmentHarness.Check(deny.Code == AttachmentCodes.Disabled);

        var key = AttachmentHarness.Key();
        var catalog = AttachmentHarness.Catalog(new AttachmentCapabilityRecord(
            key, AttachmentHarness.Verified(AttachmentOperations.PathInsert, AttachmentOperations.ImageAttachment)));
        var perm = AttachmentHarness.Coordinator(catalog);
        var src = AttachmentHarness.Endpoint("src-p");
        var dst = AttachmentHarness.Endpoint("dst-p");
        src.AddFile("x.bin", "x"u8.ToArray());
        dst.FailNextWrite = true;
        perm.SetIntent(AttachmentIntent.ImageAttachment);
        perm.ChooseSource(
            AttachmentHarness.Source("x", "x.bin", AttachmentSourceKind.ImageBuffer),
            fileSource: src,
            filePath: FileLocator.Root.Append(AttachmentHarness.Comp("x.bin")));
        perm.ConfirmTarget(AttachmentHarness.Confirm(key: key));
        var failed = perm.StartUploadAsync(
            dst, FileLocator.Root, AttachmentHarness.Comp("x.bin"), AttachmentHarness.Live(key: key))
            .AsTask().GetAwaiter().GetResult();
        AttachmentHarness.Check(!failed.Succeeded);
        AttachmentHarness.Check(failed.Code == AttachmentCodes.PermissionDenied);
        AttachmentHarness.Check(perm.State == DeliveryState.Failed);
        AttachmentHarness.Check(perm.State != DeliveryState.PathReady);
    }

    static void PasteTextSkipsUpload()
    {
        var key = AttachmentHarness.Key();
        var catalog = AttachmentHarness.Catalog(new AttachmentCapabilityRecord(
            key, AttachmentHarness.Verified(AttachmentOperations.PathInsert)));
        var input = new RecordingAttachmentInput();
        var coordinator = AttachmentHarness.Coordinator(catalog, input: input);
        coordinator.SetIntent(AttachmentIntent.PasteText);
        coordinator.ChooseSource(AttachmentHarness.Source(kind: AttachmentSourceKind.ClipboardText), AttachmentHarness.Utf8("hello"));
        coordinator.ConfirmTarget(AttachmentHarness.Confirm(key: key));
        AttachmentHarness.Check(!coordinator.UploadEnabled);
        var live = AttachmentHarness.Live(key: key);
        var inserted = coordinator.InsertAsync(live).AsTask().GetAwaiter().GetResult();
        AttachmentHarness.Check(inserted.Succeeded);
        AttachmentHarness.Check(coordinator.State == DeliveryState.PathInsertedUnconfirmed);
        AttachmentHarness.Check(coordinator.TransferStarts == 0);
        AttachmentHarness.Check(!coordinator.ClaimsAgentAccepted);
    }
}
