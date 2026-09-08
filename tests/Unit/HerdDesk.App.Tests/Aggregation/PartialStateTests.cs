using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class PartialStateTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("partial loading offline auth and empty stay independently reachable", MixedReadiness),
        ("diagnostics hide host user and path", DiagnosticsRedacted)
    ];

    static ShellViewModel Ready(GlobalProjectionStore store, ProjectionCatalog catalog)
    {
        catalog.Aggregate = store;
        catalog.DaemonAvailable = true;
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
                        [SessionProfile.Named("dev")]),
                    new DeviceProfile(
                        AppTestHost.DeviceC, "gamma", ConnectionKinds.Local, "/tmp/herdr",
                        [SessionProfile.Named("dev")])
                ])
        };
        var shell = AppTestHost.Shell(profiles, catalog, aggregate: store);
        shell.StartAsync().AsTask().GetAwaiter().GetResult();
        shell.ExpandAll();
        return shell;
    }

    static SessionProjection Namesake(DeviceId device) =>
        AppTestHost.SessionState(
            AppTestHost.SessionOf(device),
            [AppTestHost.Workspace(AppTestHost.SessionOf(device), "w1", "lab", 1)],
            [AppTestHost.Pane(new PaneKey(AppTestHost.SessionOf(device), "w1", "p1"), "main")]);

    static DeviceSessionState State(
        DeviceId device,
        long epoch,
        ConnectionPhase phase,
        DeviceFreshness freshness,
        string? error = null,
        CapabilityProfile? caps = null,
        SessionProjection? session = null) =>
        new(
            AppTestHost.SessionOf(device),
            new ConnectionEpoch(epoch),
            phase,
            freshness,
            caps ?? AppTestHost.Compatible(),
            new DeviceProjectionSnapshot(
                new ConnectionEpoch(epoch), epoch, phase,
                [AppTestHost.Projected(device, caps ?? AppTestHost.Compatible(), session ?? Namesake(device))]),
            0, 0, error, phase == ConnectionPhase.Ready, 0, 0, 0, 0);

    static void MixedReadiness()
    {
        var store = new GlobalProjectionStore();
        AppTestHost.Check(store.ApplySession(
            State(AppTestHost.DeviceA, 1, ConnectionPhase.Synchronizing, DeviceFreshness.Refreshing),
            0, "alpha").Succeeded);
        AppTestHost.Check(store.ApplySession(
            State(AppTestHost.DeviceB, 2, ConnectionPhase.Ready, DeviceFreshness.Current),
            1, "beta").Succeeded);
        var emptyKey = AppTestHost.SessionOf(AppTestHost.DeviceC);
        var empty = new DeviceSessionState(
            emptyKey,
            new ConnectionEpoch(3),
            ConnectionPhase.Ready,
            DeviceFreshness.Current,
            AppTestHost.Compatible(),
            new DeviceProjectionSnapshot(
                new ConnectionEpoch(3), 3, ConnectionPhase.Ready,
                [AppTestHost.Projected(AppTestHost.DeviceC, AppTestHost.Compatible())]),
            0, 0, null, true, 0, 0, 0, 0);
        AppTestHost.Check(store.ApplySession(empty, 2, "gamma").Succeeded);
        var catalog = new ProjectionCatalog { DaemonAvailable = true, RendererReadyDefault = true };
        var shell = Ready(store, catalog);
        AppTestHost.Check(shell.MultiDevice.PartialBanner);
        AppTestHost.Check(shell.MultiDevice.TotalCount == 3);
        AppTestHost.Check(shell.MultiDevice.ReadyCount >= 1);
        var summaries = shell.MultiDevice.Devices;
        AppTestHost.Check(summaries.Any(item => item.Readiness == PartitionReadiness.Loading));
        AppTestHost.Check(summaries.Any(item => item.Readiness == PartitionReadiness.Ready));
        AppTestHost.Check(summaries.Any(item => item.Readiness == PartitionReadiness.Empty));
        AppTestHost.Check(store.ApplySession(
            State(AppTestHost.DeviceA, 1, ConnectionPhase.Stale, DeviceFreshness.Stale,
                RecoveryCodes.Authentication),
            0, "alpha").Succeeded);
        shell.RefreshFromCatalog();
        AppTestHost.Check(shell.MultiDevice.Devices.Any(item =>
            item.Device == AppTestHost.DeviceA && item.Readiness == PartitionReadiness.AuthRequired));
        AppTestHost.Check(shell.FindPaneLive());
        AppTestHost.Check(shell.MultiDevice.ReconnectAvailability.Kind == RouteAvailabilityKind.Disabled);
        AppTestHost.Check(shell.MultiDevice.RequestReconnect(AppTestHost.DeviceA) ==
                          AggregationCodes.ReconnectProviderUnverified);
        var live = shell.VisibleItems.First(item =>
            item.Kind == NavigationKind.Pane && item.Device == AppTestHost.DeviceB);
        shell.Select(live);
        AppTestHost.Check(shell.Selection.Pane == live.Pane);
        AppTestHost.Check(!shell.Selection.IsExpired);
    }

    static void DiagnosticsRedacted()
    {
        var store = new GlobalProjectionStore();
        AppTestHost.Check(store.ApplySession(
            State(AppTestHost.DeviceA, 1, ConnectionPhase.Ready, DeviceFreshness.Current),
            0, "alpha").Succeeded);
        var catalog = new ProjectionCatalog { DaemonAvailable = true };
        var shell = Ready(store, catalog);
        shell.OpenDiagnostics();
        var preview = shell.Diagnostics.BuildPreview();
        var json = string.Join(" ", preview.Fields.Select(item => item.Value));
        AppTestHost.Check(!json.Contains("password", StringComparison.OrdinalIgnoreCase));
        AppTestHost.Check(!json.Contains("/tmp/herdr", StringComparison.Ordinal));
        AppTestHost.Check(!json.Contains("user@", StringComparison.Ordinal));
        AppTestHost.Check(preview.Fields.Any(item => item.Name.StartsWith("connection_", StringComparison.Ordinal)));
        AppTestHost.Check(preview.Fields.Any(item => item.Name.StartsWith("sync_", StringComparison.Ordinal)));
        AppTestHost.Check(preview.Fields.Any(item => item.Redacted));
    }
}

file static class PartialStateExtensions
{
    public static bool FindPaneLive(this ShellViewModel shell) =>
        shell.VisibleItems.Any(item =>
            item.Kind == NavigationKind.Pane && item.Device == AppTestHost.DeviceB && !item.IsDisabled);
}
