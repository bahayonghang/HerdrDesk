using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class PartitionIsolationTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("same pane id on two devices stays two partitions", NamesakeNotMerged),
        ("device A offline does not block B and C", PartialFailureIsolated),
        ("auth error stays on one device", AuthDoesNotClearOthers),
        ("old epoch does not replace a newer partition", StaleEpochRejected),
        ("snapshot with extra device does not leak into the owner partition", ForeignDeviceIgnored),
        ("remove device only clears that partition", RemoveIsLocal),
        ("rename label keeps the device key", RenameKeepsKey),
        ("search documents omit ansi and terminal bytes", NoTerminalPayload),
        ("two sessions on one device keep independent epochs", IndependentSessionEpochs),
        ("capacity phases are not ready", CapacityPhasesNotReady)
    ];

    static void NamesakeNotMerged()
    {
        var store = new GlobalProjectionStore();
        AggregationHarness.Check(store.ApplySession(
            AggregationHarness.DeviceState(
                AggregationHarness.DeviceA, 1, ConnectionPhase.Ready, DeviceFreshness.Current,
                AggregationHarness.NamesakeSession(AggregationHarness.DeviceA)),
            0, "alpha").Succeeded);
        AggregationHarness.Check(store.ApplySession(
            AggregationHarness.DeviceState(
                AggregationHarness.DeviceB, 2, ConnectionPhase.Ready, DeviceFreshness.Current,
                AggregationHarness.NamesakeSession(AggregationHarness.DeviceB)),
            1, "beta").Succeeded);
        var view = store.Read();
        AggregationHarness.Check(view.Partitions.Count == 2);
        var paneA = store.FindPane(AggregationHarness.PaneKeyOf(AggregationHarness.DeviceA));
        var paneB = store.FindPane(AggregationHarness.PaneKeyOf(AggregationHarness.DeviceB));
        AggregationHarness.Check(paneA is not null && paneB is not null);
        AggregationHarness.Check(paneA!.Key != paneB!.Key);
        AggregationHarness.Check(paneA.Key.PaneId == paneB.Key.PaneId);
        AggregationHarness.Check(paneA.Label == paneB.Label);
    }

    static void PartialFailureIsolated()
    {
        var store = new GlobalProjectionStore();
        AggregationHarness.Check(store.ApplySession(
            AggregationHarness.DeviceState(
                AggregationHarness.DeviceA, 1, ConnectionPhase.Offline, DeviceFreshness.Stale,
                AggregationHarness.NamesakeSession(AggregationHarness.DeviceA), "offline"),
            0, "alpha").Succeeded);
        AggregationHarness.Check(store.ApplySession(
            AggregationHarness.DeviceState(
                AggregationHarness.DeviceB, 3, ConnectionPhase.Ready, DeviceFreshness.Current,
                AggregationHarness.NamesakeSession(AggregationHarness.DeviceB)),
            1, "beta").Succeeded);
        AggregationHarness.Check(store.ApplySession(
            AggregationHarness.DeviceState(
                AggregationHarness.DeviceC, 5, ConnectionPhase.Ready, DeviceFreshness.Current,
                AggregationHarness.NamesakeSession(AggregationHarness.DeviceC)),
            2, "gamma").Succeeded);
        var view = store.Read();
        AggregationHarness.Check(view.ReadyCount == 2);
        AggregationHarness.Check(view.TotalCount == 3);
        AggregationHarness.Check(view.Readiness is AggregateReadiness.Degraded or AggregateReadiness.PartialReady);
        AggregationHarness.Check(store.TryGetPartition(AggregationHarness.DeviceA, out var a) &&
                                 a.Readiness == PartitionReadiness.Offline);
        AggregationHarness.Check(store.FindPane(AggregationHarness.PaneKeyOf(AggregationHarness.DeviceB)) is not null);
        AggregationHarness.Check(store.FindPane(AggregationHarness.PaneKeyOf(AggregationHarness.DeviceC)) is not null);
        var hits = store.Search(new GlobalSearchQuery("main", null, 1)).Documents
            .Where(item => item.Kind == GlobalEntityKind.Pane).ToArray();
        AggregationHarness.Check(hits.Any(item => item.Ref.Device == AggregationHarness.DeviceB));
        AggregationHarness.Check(hits.Any(item => item.Ref.Device == AggregationHarness.DeviceC));
    }

    static void AuthDoesNotClearOthers()
    {
        var store = new GlobalProjectionStore();
        AggregationHarness.Check(store.ApplySession(
            AggregationHarness.DeviceState(
                AggregationHarness.DeviceA, 1, ConnectionPhase.Stale, DeviceFreshness.Stale,
                AggregationHarness.NamesakeSession(AggregationHarness.DeviceA), RecoveryCodes.Authentication),
            0, "alpha").Succeeded);
        AggregationHarness.Check(store.ApplySession(
            AggregationHarness.DeviceState(
                AggregationHarness.DeviceB, 1, ConnectionPhase.Ready, DeviceFreshness.Current,
                AggregationHarness.NamesakeSession(AggregationHarness.DeviceB)),
            1, "beta").Succeeded);
        AggregationHarness.Check(store.TryGetPartition(AggregationHarness.DeviceA, out var a) &&
                                 a.Readiness == PartitionReadiness.AuthRequired);
        AggregationHarness.Check(store.TryGetPartition(AggregationHarness.DeviceB, out var b) &&
                                 b.Readiness == PartitionReadiness.Ready);
        AggregationHarness.Check(store.FindPane(AggregationHarness.PaneKeyOf(AggregationHarness.DeviceB)) is not null);
    }

    static void StaleEpochRejected()
    {
        var store = new GlobalProjectionStore();
        var first = AggregationHarness.DeviceState(
            AggregationHarness.DeviceA, 4, ConnectionPhase.Ready, DeviceFreshness.Current,
            AggregationHarness.NamesakeSession(AggregationHarness.DeviceA));
        AggregationHarness.Check(store.ApplySession(first, 0, "alpha").Succeeded);
        var stale = AggregationHarness.DeviceState(
            AggregationHarness.DeviceA, 2, ConnectionPhase.Ready, DeviceFreshness.Current,
            AggregationHarness.NamesakeSession(AggregationHarness.DeviceA));
        var result = store.ApplySession(stale, 0, "alpha");
        AggregationHarness.Check(!result.Succeeded);
        AggregationHarness.Check(result.Code == AggregationCodes.StaleEpoch);
        AggregationHarness.Check(store.TryGetPartition(AggregationHarness.DeviceA, out var partition));
        AggregationHarness.Check(partition.Epoch.Value == 4);
    }

    static void ForeignDeviceIgnored()
    {
        var store = new GlobalProjectionStore();
        var sessionA = AggregationHarness.NamesakeSession(AggregationHarness.DeviceA);
        var sessionB = AggregationHarness.NamesakeSession(AggregationHarness.DeviceB);
        var mixed = new DeviceProjectionSnapshot(
            new ConnectionEpoch(1), 1, ConnectionPhase.Ready,
            [
                new ProjectedDevice(AggregationHarness.DeviceA, AggregationHarness.Compatible(), [sessionA]),
                new ProjectedDevice(AggregationHarness.DeviceB, AggregationHarness.Compatible(), [sessionB])
            ]);
        var state = new DeviceSessionState(
            sessionA.Session, new ConnectionEpoch(1), ConnectionPhase.Ready, DeviceFreshness.Current,
            AggregationHarness.Compatible(), mixed, 0, 0, null, true, 0, 0, 0, 0);
        AggregationHarness.Check(store.ApplySession(state, 0, "alpha").Succeeded);
        AggregationHarness.Check(store.PartitionCount == 1);
        AggregationHarness.Check(store.FindPane(AggregationHarness.PaneKeyOf(AggregationHarness.DeviceB)) is null);
        AggregationHarness.Check(store.FindPane(AggregationHarness.PaneKeyOf(AggregationHarness.DeviceA)) is not null);
    }

    static void RemoveIsLocal()
    {
        var store = new GlobalProjectionStore();
        AggregationHarness.Check(store.ApplySession(
            AggregationHarness.DeviceState(
                AggregationHarness.DeviceA, 1, ConnectionPhase.Ready, DeviceFreshness.Current,
                AggregationHarness.NamesakeSession(AggregationHarness.DeviceA)),
            0, "alpha").Succeeded);
        AggregationHarness.Check(store.ApplySession(
            AggregationHarness.DeviceState(
                AggregationHarness.DeviceB, 1, ConnectionPhase.Ready, DeviceFreshness.Current,
                AggregationHarness.NamesakeSession(AggregationHarness.DeviceB)),
            1, "beta").Succeeded);
        AggregationHarness.Check(store.RemoveDevice(AggregationHarness.DeviceA).Succeeded);
        AggregationHarness.Check(store.FindPane(AggregationHarness.PaneKeyOf(AggregationHarness.DeviceA)) is null);
        AggregationHarness.Check(store.FindPane(AggregationHarness.PaneKeyOf(AggregationHarness.DeviceB)) is not null);
    }

    static void RenameKeepsKey()
    {
        var store = new GlobalProjectionStore();
        AggregationHarness.Check(store.ApplySession(
            AggregationHarness.DeviceState(
                AggregationHarness.DeviceA, 1, ConnectionPhase.Ready, DeviceFreshness.Current,
                AggregationHarness.NamesakeSession(AggregationHarness.DeviceA)),
            0, "old").Succeeded);
        AggregationHarness.Check(store.SetPresentation(AggregationHarness.DeviceA, 0, "new-lab").Succeeded);
        AggregationHarness.Check(store.TryGetPartition(AggregationHarness.DeviceA, out var partition));
        AggregationHarness.Check(partition.Device == AggregationHarness.DeviceA);
        AggregationHarness.Check(partition.DisplayLabel == "new-lab");
        var resolver = new GlobalTargetResolver(store);
        var resolved = resolver.Resolve(AggregationHarness.PaneRef(AggregationHarness.DeviceA, 1));
        AggregationHarness.Check(resolved.IsResolved);
        AggregationHarness.Check(resolved.Target!.Device == AggregationHarness.DeviceA);
    }

    static void NoTerminalPayload()
    {
        var store = new GlobalProjectionStore();
        AggregationHarness.Check(store.ApplySession(
            AggregationHarness.DeviceState(
                AggregationHarness.DeviceA, 1, ConnectionPhase.Ready, DeviceFreshness.Current,
                AggregationHarness.NamesakeSession(AggregationHarness.DeviceA, "main\u001b[31m")),
            0, "alpha").Succeeded);
        var docs = store.Search(new GlobalSearchQuery("main", null, 1)).Documents;
        AggregationHarness.Check(docs.All(item => !item.DisplayLabel.Contains('\u001b')));
        AggregationHarness.Check(typeof(DevicePartition).GetProperty("Bytes") is null);
        AggregationHarness.Check(typeof(SearchDocument).GetProperty("Bytes") is null);
        AggregationHarness.Check(store.TerminalProcessDelta == 0);
    }

    static void IndependentSessionEpochs()
    {
        var store = new GlobalProjectionStore();
        var first = AggregationHarness.NamedPane(AggregationHarness.DeviceA, "dev", "p1", "main");
        var second = AggregationHarness.NamedPane(AggregationHarness.DeviceA, "other", "p1", "main");
        AggregationHarness.Check(store.ApplySession(
            AggregationHarness.DeviceState(
                AggregationHarness.DeviceA, 5, ConnectionPhase.Ready, DeviceFreshness.Current, first),
            0, "alpha").Succeeded);
        AggregationHarness.Check(store.ApplySession(
            AggregationHarness.DeviceState(
                AggregationHarness.DeviceA, 2, ConnectionPhase.Ready, DeviceFreshness.Current, second),
            0, "alpha").Succeeded);
        AggregationHarness.Check(store.TryGetPartition(AggregationHarness.DeviceA, out var partition));
        AggregationHarness.Check(partition.EpochFor(first.Session).Value == 5);
        AggregationHarness.Check(partition.EpochFor(second.Session).Value == 2);
        AggregationHarness.Check(partition.Epoch.Value == 5);
        var staleFirst = store.ApplySession(
            AggregationHarness.DeviceState(
                AggregationHarness.DeviceA, 1, ConnectionPhase.Ready, DeviceFreshness.Current, first),
            0, "alpha");
        AggregationHarness.Check(!staleFirst.Succeeded);
        AggregationHarness.Check(staleFirst.Code == AggregationCodes.StaleEpoch);
        var paneFirst = new PaneKey(first.Session, "w1", "p1");
        var paneSecond = new PaneKey(second.Session, "w1", "p1");
        AggregationHarness.Check(paneFirst != paneSecond);
        var liveFirst = WriteIntentGuard.Evaluate(
            store,
            new AggregationWriteIntent(
                AggregationWriteKind.Activate, AggregationHarness.DeviceA, first.Session, paneFirst,
                new ConnectionEpoch(5), AggregationHarness.PaneRef(AggregationHarness.DeviceA, 5)));
        AggregationHarness.Check(liveFirst.Allowed);
        var crossedEpoch = WriteIntentGuard.Evaluate(
            store,
            new AggregationWriteIntent(
                AggregationWriteKind.Event, AggregationHarness.DeviceA, first.Session, paneFirst,
                new ConnectionEpoch(2), AggregationHarness.PaneRef(AggregationHarness.DeviceA, 2)));
        AggregationHarness.Check(!crossedEpoch.Allowed);
        AggregationHarness.Check(crossedEpoch.Code == AggregationCodes.StaleEpoch);
        var liveSecond = WriteIntentGuard.Evaluate(
            store,
            new AggregationWriteIntent(
                AggregationWriteKind.Activate, AggregationHarness.DeviceA, second.Session, paneSecond,
                new ConnectionEpoch(2),
                new GlobalEntityRef(
                    GlobalEntityKind.Pane, AggregationHarness.DeviceA, second.Session, "w1", paneSecond, null,
                    new FreshnessStamp(new ConnectionEpoch(2), 0))));
        AggregationHarness.Check(liveSecond.Allowed);
        var reducer = new AttentionReducer();
        var feeds = partition.ToAttentionFeeds(DateTimeOffset.UnixEpoch);
        AggregationHarness.Check(feeds.Count == 2);
        AggregationHarness.Check(feeds.Any(item => item.Stamp.Epoch.Value == 5));
        AggregationHarness.Check(feeds.Any(item => item.Stamp.Epoch.Value == 2));
        var applied = reducer.ApplyAggregate(feeds, AttentionSyncKind.Live);
        AggregationHarness.Check(applied.Decisions.All(item => item.Reason != AttentionCodes.OldEpoch));
    }

    static void CapacityPhasesNotReady()
    {
        var store = new GlobalProjectionStore();
        AggregationHarness.Check(store.ApplySession(
            AggregationHarness.DeviceState(
                AggregationHarness.DeviceA, 1, ConnectionPhase.WaitingForCapacity, DeviceFreshness.Unknown,
                AggregationHarness.NamesakeSession(AggregationHarness.DeviceA)),
            0, "alpha").Succeeded);
        AggregationHarness.Check(store.TryGetPartition(AggregationHarness.DeviceA, out var waiting));
        AggregationHarness.Check(waiting.Phase == ConnectionPhase.WaitingForCapacity);
        AggregationHarness.Check(waiting.Readiness == PartitionReadiness.Loading);
        AggregationHarness.Check(waiting.Readiness != PartitionReadiness.Ready);
        AggregationHarness.Check(store.Read().ReadyCount == 0);
        var waitWrite = WriteIntentGuard.Evaluate(
            store,
            new AggregationWriteIntent(
                AggregationWriteKind.Activate, AggregationHarness.DeviceA,
                AggregationHarness.SessionOf(AggregationHarness.DeviceA),
                AggregationHarness.PaneKeyOf(AggregationHarness.DeviceA),
                new ConnectionEpoch(1), AggregationHarness.PaneRef(AggregationHarness.DeviceA, 1)));
        AggregationHarness.Check(!waitWrite.Allowed);
        AggregationHarness.Check(store.ApplySession(
            AggregationHarness.DeviceState(
                AggregationHarness.DeviceA, 1, ConnectionPhase.PausedForCapacity, DeviceFreshness.Stale,
                AggregationHarness.NamesakeSession(AggregationHarness.DeviceA)),
            0, "alpha").Succeeded);
        AggregationHarness.Check(store.TryGetPartition(AggregationHarness.DeviceA, out var paused));
        AggregationHarness.Check(paused.Phase == ConnectionPhase.PausedForCapacity);
        AggregationHarness.Check(paused.Readiness == PartitionReadiness.Stale);
        AggregationHarness.Check(paused.Readiness != PartitionReadiness.Ready);
        var pauseWrite = WriteIntentGuard.Evaluate(
            store,
            new AggregationWriteIntent(
                AggregationWriteKind.Activate, AggregationHarness.DeviceA,
                AggregationHarness.SessionOf(AggregationHarness.DeviceA),
                AggregationHarness.PaneKeyOf(AggregationHarness.DeviceA),
                new ConnectionEpoch(1), AggregationHarness.PaneRef(AggregationHarness.DeviceA, 1)));
        AggregationHarness.Check(!pauseWrite.Allowed);
    }
}
