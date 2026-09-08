using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class SharedResolverTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("search notification and tree share resolver for namesake panes", SharedHits),
        ("expired search does not select namesake on another device", ExpiredSearch),
        ("search cancel ignores late hits", CancelLate),
        ("empty aggregate does not resurrect snapshot namesakes", EmptyAggregateIgnoresSnapshot)
    ];

    static SessionProjection Namesake(DeviceId device) =>
        AppTestHost.SessionState(
            AppTestHost.SessionOf(device),
            [AppTestHost.Workspace(AppTestHost.SessionOf(device), "w1", "lab", 1)],
            [AppTestHost.Pane(new PaneKey(AppTestHost.SessionOf(device), "w1", "p1"), "main")]);

    static DeviceSessionState ReadyState(DeviceId device, long epoch) =>
        new(
            AppTestHost.SessionOf(device),
            new ConnectionEpoch(epoch),
            ConnectionPhase.Ready,
            DeviceFreshness.Current,
            AppTestHost.Compatible(),
            new DeviceProjectionSnapshot(
                new ConnectionEpoch(epoch), epoch, ConnectionPhase.Ready,
                [AppTestHost.Projected(device, AppTestHost.Compatible(), Namesake(device))]),
            0, 0, null, true, 0, 0, 0, 0);

    static ShellViewModel ShellWith(GlobalProjectionStore store)
    {
        var catalog = new ProjectionCatalog
        {
            Aggregate = store,
            DaemonAvailable = true,
            RendererReadyDefault = true
        };
        var profiles = new MemoryDeviceProfileStore
        {
            Snapshot = new ConfigurationSnapshot(
                1, 1,
                [
                    new DeviceProfile(
                        AppTestHost.DeviceA, "alpha", ConnectionKinds.Local, "/tmp/herdr",
                        [SessionProfile.Named("dev")]),
                    new DeviceProfile(
                        AppTestHost.DeviceB, "beta", ConnectionKinds.Local, "/tmp/herdr",
                        [SessionProfile.Named("dev")])
                ])
        };
        var shell = AppTestHost.Shell(profiles, catalog, aggregate: store);
        shell.StartAsync().AsTask().GetAwaiter().GetResult();
        shell.ExpandAll();
        return shell;
    }

    static void SharedHits()
    {
        var store = new GlobalProjectionStore();
        AppTestHost.Check(store.ApplySession(ReadyState(AppTestHost.DeviceA, 1), 0, "alpha").Succeeded);
        AppTestHost.Check(store.ApplySession(ReadyState(AppTestHost.DeviceB, 4), 1, "beta").Succeeded);
        var shell = ShellWith(store);
        var paneA = new PaneKey(AppTestHost.SessionOf(AppTestHost.DeviceA), "w1", "p1");
        var paneB = new PaneKey(AppTestHost.SessionOf(AppTestHost.DeviceB), "w1", "p1");
        CatalogBlocked(shell, paneA);
        var token = shell.Notifications.Items.First(item => item.Key.Pane == paneA).TransitionId;
        for (var i = 0; i < 100; i++)
        {
            var wantA = i % 2 == 0;
            var pane = wantA ? paneA : paneB;
            if (wantA)
            {
                AppTestHost.Check(shell.ActivateNotification(token) || shell.Notifications.Items.Any());
                var resolved = shell.Resolver.ResolveNotification(new NotificationTarget(
                    new AttentionKey(paneA, AttentionKey.AgentEntityKind, "term-p1"),
                    new ConnectionEpoch(1),
                    NotificationTarget.CurrentRouteVersion));
                AppTestHost.Check(resolved.Target is null || resolved.Target.Pane == paneA);
            }

            var searchResolved = shell.Resolver.Resolve(new GlobalEntityRef(
                GlobalEntityKind.Pane, pane.Session.Device, pane.Session, pane.WorkspaceId, pane, null,
                new FreshnessStamp(new ConnectionEpoch(wantA ? 1 : 4), 0)));
            AppTestHost.Check(searchResolved.IsResolved);
            AppTestHost.Check(searchResolved.Target!.Pane == pane);
            var item = shell.VisibleItems.First(nav => nav.Pane == pane);
            shell.Select(item);
            AppTestHost.Check(shell.Selection.Pane == pane);
            AppTestHost.Check(shell.Selection.Device == pane.Session.Device);
        }
    }

    static void CatalogBlocked(ShellViewModel shell, PaneKey pane)
    {
        var store = shell.Catalog.Aggregate!;
        var session = Namesake(pane.Session.Device);
        var blocked = session with
        {
            Panes = [session.Panes[0] with { AgentStatus = AppTestHost.Blocked() }],
            Agents = [session.Agents[0] with { AgentStatus = AppTestHost.Blocked() }]
        };
        AppTestHost.Check(store.ApplySession(ReadyState(pane.Session.Device, 1) with
        {
            Projection = new DeviceProjectionSnapshot(
                new ConnectionEpoch(1), 1, ConnectionPhase.Ready,
                [AppTestHost.Projected(pane.Session.Device, AppTestHost.Compatible(), blocked)])
        }, 0, "alpha").Succeeded);
        shell.RefreshFromCatalog();
    }

    static void ExpiredSearch()
    {
        var store = new GlobalProjectionStore();
        AppTestHost.Check(store.ApplySession(ReadyState(AppTestHost.DeviceA, 1), 0, "alpha").Succeeded);
        AppTestHost.Check(store.ApplySession(ReadyState(AppTestHost.DeviceB, 4), 1, "beta").Succeeded);
        var shell = ShellWith(store);
        shell.Search.SetQuery("main");
        AppTestHost.Check(store.RemoveDevice(AppTestHost.DeviceA).Succeeded);
        shell.RefreshFromCatalog();
        shell.OpenSearch();
        shell.Search.SetQuery("main");
        var guard = 0;
        while (shell.Search.Selected?.Device != AppTestHost.DeviceA &&
               shell.Search.Results.Any(item => item.Device == AppTestHost.DeviceA && item.IsExpired))
        {
            AppTestHost.Check(guard++ < 32);
            shell.Search.MoveSelection(1);
        }

        if (shell.Search.Results.Any(item => item.Device == AppTestHost.DeviceA))
        {
            while (shell.Search.Selected?.Device != AppTestHost.DeviceA)
            {
                AppTestHost.Check(guard++ < 32);
                shell.Search.MoveSelection(1);
            }

            AppTestHost.Check(!shell.ActivateSearchResult());
            AppTestHost.Check(shell.ActivationExpired);
        }

        var namesake = shell.VisibleItems.First(item =>
            item.Kind == NavigationKind.Pane && item.Device == AppTestHost.DeviceB);
        AppTestHost.Check(!namesake.IsSelected || shell.Selection.IsExpired);
        AppTestHost.Check(namesake.Pane!.Value.PaneId == "p1");
        AppTestHost.Check(namesake.Pane.Value.WorkspaceId == "w1");
    }

    static void CancelLate()
    {
        var store = new GlobalProjectionStore();
        AppTestHost.Check(store.ApplySession(ReadyState(AppTestHost.DeviceA, 1), 0, "alpha").Succeeded);
        var shell = ShellWith(store);
        shell.Search.SetQuery("main");
        var staleGeneration = shell.Search.Generation;
        var stale = shell.Search.Results.ToArray();
        shell.Search.CancelSearch();
        shell.Search.SetQuery("zzz-no-match");
        AppTestHost.Check(!shell.Search.TryCommit(staleGeneration, stale));
        AppTestHost.Check(shell.Search.Results.All(item => item.Label != "main") ||
                          shell.Search.Query == "zzz-no-match");
        AppTestHost.Check(shell.GlobalSearch is not null);
        shell.GlobalSearch!.UpdateQuery("main");
        var gen = shell.GlobalSearch.Generation;
        var docs = shell.GlobalSearch.Results.ToArray();
        shell.GlobalSearch.CancelSearch();
        shell.GlobalSearch.UpdateQuery("zzz");
        AppTestHost.Check(!shell.GlobalSearch.TryCommit(gen, docs));
    }

    static void EmptyAggregateIgnoresSnapshot()
    {
        var store = new GlobalProjectionStore();
        var catalog = new ProjectionCatalog
        {
            Aggregate = store,
            Snapshot = AppTestHost.TwoNamedPanes(),
            DaemonAvailable = true,
            RendererReadyDefault = true
        };
        var profiles = new MemoryDeviceProfileStore
        {
            Snapshot = new ConfigurationSnapshot(
                1, 1,
                [
                    new DeviceProfile(
                        AppTestHost.DeviceA, "alpha", ConnectionKinds.Local, "/tmp/herdr",
                        [SessionProfile.Named("dev")]),
                    new DeviceProfile(
                        AppTestHost.DeviceB, "beta", ConnectionKinds.Local, "/tmp/herdr",
                        [SessionProfile.Named("dev")])
                ])
        };
        var shell = AppTestHost.Shell(profiles, catalog, aggregate: store);
        shell.StartAsync().AsTask().GetAwaiter().GetResult();
        shell.ExpandAll();
        AppTestHost.Check(shell.MultiDevice.TotalCount == 0);
        AppTestHost.Check(!shell.VisibleItems.Any(item => item.Kind == NavigationKind.Pane));
        var paneA = new PaneKey(AppTestHost.SessionOf(AppTestHost.DeviceA), "ws", "p1");
        var resolved = shell.Resolver.Resolve(new GlobalEntityRef(
            GlobalEntityKind.Pane, paneA.Session.Device, paneA.Session, paneA.WorkspaceId, paneA, null,
            new FreshnessStamp(new ConnectionEpoch(1), 0)));
        AppTestHost.Check(resolved.Status == ResolveStatus.Expired);
        shell.Search.SetQuery("main");
        AppTestHost.Check(!shell.Search.Results.Any(item => !item.IsExpired && item.Pane == paneA));
    }
}
