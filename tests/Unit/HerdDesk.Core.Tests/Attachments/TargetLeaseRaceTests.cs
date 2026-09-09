using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class TargetLeaseRaceTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("100 pane switches stale draft does not upload or input", HundredSwitches),
        ("reconnect epoch change stales draft", ReconnectStale),
        ("confirm after reconnect does not retarget frozen lease", ConfirmDoesNotRetarget),
        ("lease revoke stales draft", LeaseRevokeStale),
        ("agent version change stales draft", AgentVersionStale)
    ];

    static void HundredSwitches()
    {
        var key = AttachmentHarness.Key();
        var catalog = AttachmentHarness.Catalog(new AttachmentCapabilityRecord(
            key, AttachmentHarness.Verified(AttachmentOperations.PathInsert, AttachmentOperations.ImageAttachment)));
        var input = new RecordingAttachmentInput();
        var coordinator = AttachmentHarness.Coordinator(catalog, input: input);
        var src = AttachmentHarness.Endpoint("src");
        var dst = AttachmentHarness.Endpoint("dst");
        src.AddFile("payload.bin", "hello"u8.ToArray());
        coordinator.SetIntent(AttachmentIntent.ImageAttachment);
        coordinator.ChooseSource(
            AttachmentHarness.Source("payload", "payload.bin", AttachmentSourceKind.LocalFile),
            fileSource: src,
            filePath: FileLocator.Root.Append(AttachmentHarness.Comp("payload.bin")));
        var origin = AttachmentHarness.Pane("origin");
        coordinator.ConfirmTarget(AttachmentHarness.Confirm(pane: origin, key: key));
        AttachmentHarness.Check(coordinator.Lease is not null);
        AttachmentHarness.Check(coordinator.Lease!.Pane == origin);
        for (var i = 0; i < 100; i++)
        {
            var next = AttachmentHarness.Pane("p" + i.ToString("D3"));
            coordinator.NoteLiveTarget(AttachmentHarness.Live(pane: next, key: key));
        }

        AttachmentHarness.Check(coordinator.State == DeliveryState.Stale);
        AttachmentHarness.Check(coordinator.LastCode == AttachmentCodes.TargetStale);
        var liveNew = AttachmentHarness.Live(pane: AttachmentHarness.Pane("p099"), key: key);
        var upload = coordinator.StartUploadAsync(
            dst, FileLocator.Root, AttachmentHarness.Comp("payload.bin"), liveNew)
            .AsTask().GetAwaiter().GetResult();
        AttachmentHarness.Check(!upload.Succeeded);
        AttachmentHarness.Check(coordinator.TransferStarts == 0);
        AttachmentHarness.Check(!dst.Has("payload.bin"));
        var insertNew = coordinator.InsertAsync(liveNew).AsTask().GetAwaiter().GetResult();
        AttachmentHarness.Check(!insertNew.Succeeded);
        var insertOld = coordinator.InsertAsync(AttachmentHarness.Live(pane: origin, key: key))
            .AsTask().GetAwaiter().GetResult();
        AttachmentHarness.Check(!insertOld.Succeeded);
        AttachmentHarness.Check(input.Submitted.Count == 0);
        AttachmentHarness.Check(coordinator.Lease.Pane == origin);
    }

    static void ReconnectStale()
    {
        var key = AttachmentHarness.Key();
        var catalog = AttachmentHarness.Catalog(new AttachmentCapabilityRecord(
            key, AttachmentHarness.Verified(AttachmentOperations.PathInsert)));
        var input = new RecordingAttachmentInput();
        var coordinator = AttachmentHarness.Coordinator(catalog, input: input);
        coordinator.SetIntent(AttachmentIntent.PasteText);
        coordinator.ChooseSource(AttachmentHarness.Source(), AttachmentHarness.Utf8("hello"));
        coordinator.ConfirmTarget(AttachmentHarness.Confirm(epoch: 4, key: key));
        coordinator.NoteLiveTarget(AttachmentHarness.Live(epoch: 5, key: key));
        AttachmentHarness.Check(coordinator.State == DeliveryState.Stale);
        var insert = coordinator.InsertAsync(AttachmentHarness.Live(epoch: 5, key: key))
            .AsTask().GetAwaiter().GetResult();
        AttachmentHarness.Check(!insert.Succeeded);
        AttachmentHarness.Check(input.Submitted.Count == 0);
        var old = coordinator.InsertAsync(AttachmentHarness.Live(epoch: 4, key: key))
            .AsTask().GetAwaiter().GetResult();
        AttachmentHarness.Check(!old.Succeeded);
        AttachmentHarness.Check(input.Submitted.Count == 0);
    }

    static void ConfirmDoesNotRetarget()
    {
        var key = AttachmentHarness.Key();
        var catalog = AttachmentHarness.Catalog(new AttachmentCapabilityRecord(
            key, AttachmentHarness.Verified(AttachmentOperations.PathInsert)));
        var input = new RecordingAttachmentInput();
        var coordinator = AttachmentHarness.Coordinator(catalog, input: input);
        coordinator.SetIntent(AttachmentIntent.PasteText);
        coordinator.ChooseSource(AttachmentHarness.Source(), AttachmentHarness.Utf8("hello"));
        coordinator.ConfirmTarget(AttachmentHarness.Confirm(epoch: 4, key: key));
        AttachmentHarness.Check(coordinator.Lease is not null);
        AttachmentHarness.Check(coordinator.Lease!.Epoch.Value == 4);
        var retarget = coordinator.ConfirmTarget(AttachmentHarness.Confirm(epoch: 5, key: key));
        AttachmentHarness.Check(!retarget.Succeeded);
        AttachmentHarness.Check(retarget.Code == AttachmentCodes.TargetStale);
        AttachmentHarness.Check(coordinator.State == DeliveryState.Stale);
        AttachmentHarness.Check(coordinator.Lease.Epoch.Value == 4);
        var insertNew = coordinator.InsertAsync(AttachmentHarness.Live(epoch: 5, key: key))
            .AsTask().GetAwaiter().GetResult();
        AttachmentHarness.Check(!insertNew.Succeeded);
        var insertOld = coordinator.InsertAsync(AttachmentHarness.Live(epoch: 4, key: key))
            .AsTask().GetAwaiter().GetResult();
        AttachmentHarness.Check(!insertOld.Succeeded);
        AttachmentHarness.Check(input.Submitted.Count == 0);
        AttachmentHarness.Check(!coordinator.ClaimsAgentAccepted);
    }

    static void LeaseRevokeStale()
    {
        var key = AttachmentHarness.Key();
        var catalog = AttachmentHarness.Catalog(new AttachmentCapabilityRecord(
            key, AttachmentHarness.Verified(AttachmentOperations.PathInsert)));
        var input = new RecordingAttachmentInput();
        var coordinator = AttachmentHarness.Coordinator(catalog, input: input);
        coordinator.SetIntent(AttachmentIntent.PasteText);
        coordinator.ChooseSource(AttachmentHarness.Source(), AttachmentHarness.Utf8("hello"));
        coordinator.ConfirmTarget(AttachmentHarness.Confirm(leaseId: "lease-1", generation: 3, key: key));
        coordinator.NoteLiveTarget(AttachmentHarness.Live(leaseId: "lease-1", generation: 4, key: key));
        AttachmentHarness.Check(coordinator.State == DeliveryState.Stale);
        coordinator.NoteLiveTarget(AttachmentHarness.Live(verified: false, key: key));
        var insert = coordinator.InsertAsync(AttachmentHarness.Live(verified: false, key: key))
            .AsTask().GetAwaiter().GetResult();
        AttachmentHarness.Check(!insert.Succeeded);
        AttachmentHarness.Check(input.Submitted.Count == 0);
    }

    static void AgentVersionStale()
    {
        var original = AttachmentHarness.Key(version: "1.2.3");
        var catalog = AttachmentHarness.Catalog(new AttachmentCapabilityRecord(
            original, AttachmentHarness.Verified(AttachmentOperations.PathInsert)));
        var input = new RecordingAttachmentInput();
        var coordinator = AttachmentHarness.Coordinator(catalog, input: input);
        coordinator.SetIntent(AttachmentIntent.PasteText);
        coordinator.ChooseSource(AttachmentHarness.Source(), AttachmentHarness.Utf8("hello"));
        coordinator.ConfirmTarget(AttachmentHarness.Confirm(key: original));
        var changed = AttachmentHarness.Key(version: "1.2.4");
        coordinator.NoteLiveTarget(AttachmentHarness.Live(key: changed));
        AttachmentHarness.Check(coordinator.State == DeliveryState.Stale);
        var insert = coordinator.InsertAsync(AttachmentHarness.Live(key: changed))
            .AsTask().GetAwaiter().GetResult();
        AttachmentHarness.Check(!insert.Succeeded);
        AttachmentHarness.Check(input.Submitted.Count == 0);
    }
}
