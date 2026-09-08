using System.Diagnostics;
using HerdDesk.App;
using HerdDesk.Contracts;

internal static class SearchPaletteTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("one hundred synthetic panes search under p95 budget without bridges", HundredPanesP95),
        ("ctrl+k yields during composition and restores focus", CompositionAndFocus),
        ("expired recent does not activate a namesake pane", ExpiredRecent)
    ];

    static void HundredPanesP95()
    {
        var snapshot = AppTestHost.HundredPanes();
        AppTestHost.Check(snapshot.Devices.SelectMany(item => item.Sessions)
            .SelectMany(item => item.Panes).Count() == 100);
        var catalog = new ProjectionCatalog { Snapshot = snapshot, DaemonAvailable = true };
        var shell = AppTestHost.Shell(new MemoryDeviceProfileStore
        {
            Snapshot = new ConfigurationSnapshot(
                1, 1,
                [
                    new DeviceProfile(
                        AppTestHost.DeviceA, "lab", ConnectionKinds.Local, "/tmp/herdr",
                        [SessionProfile.Named("dev")])
                ])
        }, catalog);
        shell.StartAsync().AsTask().GetAwaiter().GetResult();
        var samples = new List<double>(40);
        for (var i = 0; i < 40; i++)
        {
            var watch = Stopwatch.StartNew();
            shell.Search.SetQuery("pane-");
            watch.Stop();
            if (i >= 10)
                samples.Add(watch.Elapsed.TotalMilliseconds);
        }

        samples.Sort();
        var p95 = samples[(int)Math.Ceiling(samples.Count * 0.95) - 1];
        AppTestHost.Check(shell.Search.Results.Count >= 100);
        AppTestHost.Check(p95 < 100);
        AppTestHost.Check(shell.HiddenTerminalBridgeCount == 0);
        AppTestHost.Check(shell.Search.HiddenTerminalBridgeCount == 0);
        var hit = shell.Search.Results.First(item => item.Kind == SearchResultKind.Pane);
        AppTestHost.Check(hit.Pane is not null);
        AppTestHost.Check(hit.Device.Value != Guid.Empty);
    }

    static void CompositionAndFocus()
    {
        var catalog = new ProjectionCatalog
        {
            Snapshot = AppTestHost.TwoNamedPanes(),
            DaemonAvailable = true,
            RendererReadyDefault = true
        };
        var shell = AppTestHost.Shell(new MemoryDeviceProfileStore
        {
            Snapshot = new ConfigurationSnapshot(
                1, 1,
                [
                    new DeviceProfile(
                        AppTestHost.DeviceA, "lab", ConnectionKinds.Local, "/tmp/herdr",
                        [SessionProfile.Named("dev")])
                ])
        }, catalog);
        shell.StartAsync().AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(shell.CurrentFocus.ControlId == "title");
        shell.Search.IsComposing = true;
        AppTestHost.Check(!shell.HandleAccelerator(ShellAccelerator.OpenSearch));
        AppTestHost.Check(!shell.Search.IsOpen);
        shell.Search.IsComposing = false;
        AppTestHost.Check(shell.HandleAccelerator(ShellAccelerator.OpenSearch));
        AppTestHost.Check(shell.Search.IsOpen);
        AppTestHost.Check(shell.CurrentFocus.ControlId == "search");
        AppTestHost.Check(shell.Search.SavedFocus!.Value.ControlId == "title");
        AppTestHost.Check(shell.FocusedRegion == FocusRegion.Search);
        shell.CloseSearch();
        AppTestHost.Check(!shell.Search.IsOpen);
        AppTestHost.Check(shell.CurrentFocus.ControlId == "title");
        AppTestHost.Check(shell.FocusedRegion == FocusRegion.Title);
    }

    static void ExpiredRecent()
    {
        var snapshot = AppTestHost.TwoNamedPanes();
        var catalog = new ProjectionCatalog
        {
            Snapshot = snapshot,
            DaemonAvailable = true,
            RendererReadyDefault = false
        };
        var recents = new RecentAccessStore();
        var first = snapshot.Devices[0].Sessions[0].Panes[0];
        recents.Record(new RecentEntry(
            first.Key.Session.Device, first.Key.Session, first.Key.WorkspaceId, first.Key,
            new ConnectionEpoch(1), new DateTimeOffset(2026, 9, 8, 0, 0, 0, TimeSpan.Zero),
            "main"));
        var shell = new ShellViewModel(new ShellDependencies
        {
            Profiles = new MemoryDeviceProfileStore
            {
                Snapshot = new ConfigurationSnapshot(
                    1, 1,
                    [
                        new DeviceProfile(
                            AppTestHost.DeviceA, "lab", ConnectionKinds.Local, "/tmp/herdr",
                            [SessionProfile.Named("dev")])
                    ])
            },
            Catalog = catalog,
            Recents = recents
        });
        shell.StartAsync().AsTask().GetAwaiter().GetResult();
        shell.Search.SetQuery("main");
        var remainingDevice = snapshot.Devices[1];
        catalog.Snapshot = AppTestHost.Snapshot(snapshot.Epoch, ConnectionPhase.Ready, remainingDevice);
        shell.Search.Refresh();
        var expired = shell.Search.Results.First(item => item.IsRecent);
        AppTestHost.Check(expired.IsExpired);
        AppTestHost.Check(expired.Pane == first.Key);
        shell.Search.SetQuery("main");
        var live = shell.Search.Results.First(item => item.Kind == SearchResultKind.Pane && !item.IsExpired);
        AppTestHost.Check(live.Pane != first.Key);
        AppTestHost.Check(live.Label == "main");
        shell.OpenSearch();
        var guard = 0;
        while (shell.Search.Selected?.Pane != first.Key)
        {
            AppTestHost.Check(guard++ < 32);
            shell.Search.MoveSelection(1);
        }
        AppTestHost.Check(!shell.ActivateSearchResult());
        AppTestHost.Check(shell.ActivationExpired);
        var namesake = shell.VisibleItems.FirstOrDefault(item =>
            item.Kind == NavigationKind.Pane && item.Label == "main" && item.Pane != first.Key);
        AppTestHost.Check(namesake is null || !namesake.IsSelected);
        catalog.SetRendererReady(live.Pane!.Value, true);
        shell.Search.SetQuery("main");
        guard = 0;
        while (shell.Search.Selected?.Pane != live.Pane)
        {
            AppTestHost.Check(guard++ < 32);
            shell.Search.MoveSelection(1);
        }
        AppTestHost.Check(shell.ActivateSearchResult());
        AppTestHost.Check(shell.Selection.Pane == live.Pane);
        AppTestHost.Check(shell.ContentFocused);
        recents.Remove(first.Key);
        AppTestHost.Check(recents.Items.All(item => item.Pane != first.Key));
    }
}
