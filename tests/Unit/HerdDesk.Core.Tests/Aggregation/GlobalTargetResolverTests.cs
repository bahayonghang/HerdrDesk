using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class GlobalTargetResolverTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("namesake w1 p1 resolves the full key one hundred times", HundredNamesakeHits),
        ("expired activation does not jump namesake pane", ExpiredDoesNotJump),
        ("offline and incompatible stay distinct", DistinctStatuses),
        ("search and notification share the resolver", SharedResolver),
        ("removed device expires old refs", RemovedExpires),
        ("empty aggregate does not resolve catalog snapshot panes", EmptyStoreIgnoresCatalog)
    ];

    static GlobalProjectionStore TwoNamesakes()
    {
        var store = new GlobalProjectionStore();
        AggregationHarness.Check(store.ApplySession(
            AggregationHarness.DeviceState(
                AggregationHarness.DeviceA, 1, ConnectionPhase.Ready, DeviceFreshness.Current,
                AggregationHarness.NamesakeSession(AggregationHarness.DeviceA)),
            0, "alpha").Succeeded);
        AggregationHarness.Check(store.ApplySession(
            AggregationHarness.DeviceState(
                AggregationHarness.DeviceB, 7, ConnectionPhase.Ready, DeviceFreshness.Current,
                AggregationHarness.NamesakeSession(AggregationHarness.DeviceB)),
            1, "beta").Succeeded);
        return store;
    }

    static void HundredNamesakeHits()
    {
        var store = TwoNamesakes();
        var resolver = new GlobalTargetResolver(store);
        var paneA = AggregationHarness.PaneKeyOf(AggregationHarness.DeviceA);
        var paneB = AggregationHarness.PaneKeyOf(AggregationHarness.DeviceB);
        for (var i = 0; i < 100; i++)
        {
            var wantA = i % 2 == 0;
            var target = wantA
                ? AggregationHarness.PaneRef(AggregationHarness.DeviceA, 1)
                : AggregationHarness.PaneRef(AggregationHarness.DeviceB, 7);
            var resolved = resolver.Resolve(target);
            AggregationHarness.Check(resolved.IsResolved);
            AggregationHarness.Check(resolved.Target!.Pane == (wantA ? paneA : paneB));
            AggregationHarness.Check(resolved.Target.Device == (wantA ? paneA.Session.Device : paneB.Session.Device));
            var notified = resolver.ResolveNotification(new NotificationTarget(
                new AttentionKey(wantA ? paneA : paneB, AttentionKey.AgentEntityKind, "term-p1"),
                new ConnectionEpoch(wantA ? 1 : 7),
                NotificationTarget.CurrentRouteVersion));
            AggregationHarness.Check(notified.IsResolved);
            AggregationHarness.Check(notified.Target!.Pane == resolved.Target.Pane);
        }
    }

    static void ExpiredDoesNotJump()
    {
        var store = TwoNamesakes();
        var resolver = new GlobalTargetResolver(store);
        AggregationHarness.Check(store.RemoveDevice(AggregationHarness.DeviceA).Succeeded);
        var expired = resolver.Resolve(AggregationHarness.PaneRef(AggregationHarness.DeviceA, 1));
        AggregationHarness.Check(expired.Status == ResolveStatus.Expired);
        var live = resolver.Resolve(AggregationHarness.PaneRef(AggregationHarness.DeviceB, 7));
        AggregationHarness.Check(live.IsResolved);
        AggregationHarness.Check(live.Target!.Pane == AggregationHarness.PaneKeyOf(AggregationHarness.DeviceB));
        var staleEpoch = resolver.Resolve(AggregationHarness.PaneRef(AggregationHarness.DeviceB, 1));
        AggregationHarness.Check(staleEpoch.Status == ResolveStatus.Expired);
        AggregationHarness.Check(staleEpoch.Target is null ||
                                 staleEpoch.Target.Pane != AggregationHarness.PaneKeyOf(AggregationHarness.DeviceA));
    }

    static void DistinctStatuses()
    {
        var store = new GlobalProjectionStore();
        AggregationHarness.Check(store.ApplySession(
            AggregationHarness.DeviceState(
                AggregationHarness.DeviceA, 1, ConnectionPhase.Offline, DeviceFreshness.Stale,
                AggregationHarness.NamesakeSession(AggregationHarness.DeviceA)),
            0, "alpha").Succeeded);
        AggregationHarness.Check(store.ApplySession(
            AggregationHarness.DeviceState(
                AggregationHarness.DeviceB, 1, ConnectionPhase.Incompatible, DeviceFreshness.Current,
                AggregationHarness.NamesakeSession(AggregationHarness.DeviceB),
                capabilities: AggregationHarness.Incompatible()),
            1, "beta").Succeeded);
        AggregationHarness.Check(store.ApplySession(
            AggregationHarness.DeviceState(
                AggregationHarness.DeviceC, 1, ConnectionPhase.Stale, DeviceFreshness.Stale,
                AggregationHarness.NamesakeSession(AggregationHarness.DeviceC), RecoveryCodes.Authentication),
            2, "gamma").Succeeded);
        var resolver = new GlobalTargetResolver(store);
        AggregationHarness.Check(
            resolver.Resolve(AggregationHarness.PaneRef(AggregationHarness.DeviceA, 1)).Status ==
            ResolveStatus.Offline);
        AggregationHarness.Check(
            resolver.Resolve(AggregationHarness.PaneRef(AggregationHarness.DeviceB, 1)).Status ==
            ResolveStatus.Incompatible);
        AggregationHarness.Check(
            resolver.Resolve(AggregationHarness.PaneRef(AggregationHarness.DeviceC, 1)).Status ==
            ResolveStatus.AuthRequired);
    }

    static void SharedResolver()
    {
        var store = TwoNamesakes();
        var resolver = new GlobalTargetResolver(store);
        var search = store.Search(new GlobalSearchQuery("main", null, 1)).Documents
            .First(item => item.Kind == GlobalEntityKind.Pane && item.Ref.Device == AggregationHarness.DeviceA);
        var fromSearch = resolver.Resolve(search.Ref);
        var fromNotification = resolver.ResolveNotification(new NotificationTarget(
            new AttentionKey(AggregationHarness.PaneKeyOf(AggregationHarness.DeviceA), AttentionKey.AgentEntityKind,
                "term-p1"),
            new ConnectionEpoch(1),
            NotificationTarget.CurrentRouteVersion));
        AggregationHarness.Check(fromSearch.IsResolved && fromNotification.IsResolved);
        AggregationHarness.Check(fromSearch.Target!.Pane == fromNotification.Target!.Pane);
        AggregationHarness.Check(fromSearch.Target.Device == fromNotification.Target.Device);
    }

    static void RemovedExpires()
    {
        var store = TwoNamesakes();
        var resolver = new GlobalTargetResolver(store);
        var before = resolver.Resolve(AggregationHarness.PaneRef(AggregationHarness.DeviceA, 1));
        AggregationHarness.Check(before.IsResolved);
        AggregationHarness.Check(store.RemoveDevice(AggregationHarness.DeviceA).Succeeded);
        var after = resolver.Resolve(before.Target!);
        AggregationHarness.Check(after.Status == ResolveStatus.Expired);
        AggregationHarness.Check(resolver.Resolve(AggregationHarness.PaneRef(AggregationHarness.DeviceB, 7)).IsResolved);
    }

    static void EmptyStoreIgnoresCatalog()
    {
        var store = new GlobalProjectionStore();
        var catalog = new SnapshotCatalog(AggregationHarness.DeviceState(
            AggregationHarness.DeviceA, 1, ConnectionPhase.Ready, DeviceFreshness.Current,
            AggregationHarness.NamesakeSession(AggregationHarness.DeviceA)).Projection);
        var resolver = new GlobalTargetResolver(store, catalog);
        var resolved = resolver.Resolve(AggregationHarness.PaneRef(AggregationHarness.DeviceA, 1));
        AggregationHarness.Check(resolved.Status == ResolveStatus.Expired);
        AggregationHarness.Check(resolved.Target is null);
    }
}

file sealed class SnapshotCatalog : ICatalogProjection
{
    readonly DeviceProjectionSnapshot _snapshot;

    public SnapshotCatalog(DeviceProjectionSnapshot snapshot) => _snapshot = snapshot;

    public ConnectionEpoch EpochFor(DeviceId device, SessionKey? session)
    {
        _ = (device, session);
        return _snapshot.Epoch;
    }

    public ConnectionPhase PhaseFor(DeviceId device)
    {
        _ = device;
        return _snapshot.Phase;
    }

    public DeviceFreshness FreshnessFor(DeviceId device)
    {
        _ = device;
        return DeviceFreshness.Current;
    }

    public string? ErrorFor(DeviceId device)
    {
        _ = device;
        return null;
    }

    public string LabelFor(DeviceId device) => device.Value.ToString("D");

    public ProjectedDevice? FindDevice(DeviceId device)
    {
        foreach (var item in _snapshot.Devices)
        {
            if (item.Device == device)
                return item;
        }

        return null;
    }

    public SessionProjection? FindSession(SessionKey session)
    {
        foreach (var device in _snapshot.Devices)
        {
            foreach (var item in device.Sessions)
            {
                if (item.Session == session)
                    return item;
            }
        }

        return null;
    }

    public WorkspaceProjection? FindWorkspace(SessionKey session, string workspaceId)
    {
        var projected = FindSession(session);
        if (projected is null)
            return null;
        foreach (var workspace in projected.Workspaces)
        {
            if (workspace.WorkspaceId == workspaceId)
                return workspace;
        }

        return null;
    }

    public PaneProjection? FindPane(PaneKey pane)
    {
        var projected = FindSession(pane.Session);
        if (projected is null)
            return null;
        foreach (var item in projected.Panes)
        {
            if (item.Key == pane)
                return item;
        }

        return null;
    }
}
