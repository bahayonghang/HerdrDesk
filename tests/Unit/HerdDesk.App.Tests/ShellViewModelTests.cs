using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Configuration;

internal static class ShellViewModelTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("empty config shows shell and add/diagnostics within budget", EmptyConfigShell),
        ("failed load keeps shell visible with retry", FailedLoad),
        ("daemon unavailable does not pretend online", DaemonUnavailable),
        ("settings diagnostics about stay available when providers missing", RoutesWhenUnavailable),
        ("narrow overlay keeps breadcrumb", NarrowOverlay),
        ("status fields stay independent", IndependentStatus)
    ];

    static void EmptyConfigShell()
    {
        var root = AppTestHost.TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            var shell = AppTestHost.Shell(new AtomicConfigurationStore(paths), paths: paths);
            shell.StartAsync().AsTask().GetAwaiter().GetResult();
            AppTestHost.Check(shell.ShellVisible);
            AppTestHost.Check(shell.Lifecycle == ShellLifecycle.NoDevices);
            AppTestHost.Check(!shell.DaemonOnline);
            AppTestHost.Check(shell.LastStartMs < ShellViewModel.EmptyShellBudgetMs);
            AppTestHost.Check(shell.AddDeviceLabel == ShellStrings.AddDevice);
            AppTestHost.Check(shell.DiagnosticsLabel == ShellStrings.Diagnostics);
            AppTestHost.Check(shell.SettingsAvailability.Kind == RouteAvailabilityKind.Enabled);
            AppTestHost.Check(shell.HiddenTerminalBridgeCount == 0);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void FailedLoad()
    {
        var store = new MemoryDeviceProfileStore { LoadCode = ConfigurationCodes.Malformed };
        var shell = AppTestHost.Shell(store);
        shell.StartAsync().AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(shell.ShellVisible);
        AppTestHost.Check(shell.Lifecycle == ShellLifecycle.Failed);
        AppTestHost.Check(shell.CanRetry);
        AppTestHost.Check(shell.ErrorCategory == ConfigurationCodes.Malformed);
        shell.OpenDiagnostics();
        AppTestHost.Check(shell.Route == ShellRoute.Diagnostics);
        AppTestHost.Check(shell.Diagnostics.Opened);
        AppTestHost.Check(shell.Diagnostics.ImplicitWrites == 0);
    }

    static void DaemonUnavailable()
    {
        var catalog = new ProjectionCatalog
        {
            Snapshot = AppTestHost.TwoNamedPanes(),
            DaemonAvailable = false
        };
        var store = new MemoryDeviceProfileStore
        {
            Snapshot = new ConfigurationSnapshot(
                1, 1,
                [
                    new DeviceProfile(
                        AppTestHost.DeviceA, "lab", ConnectionKinds.Local, "/tmp/herdr",
                        [SessionProfile.Named("dev")])
                ])
        };
        var shell = AppTestHost.Shell(store, catalog);
        shell.StartAsync().AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(shell.Lifecycle == ShellLifecycle.DaemonUnavailable);
        AppTestHost.Check(!shell.DaemonOnline);
        AppTestHost.Check(shell.CanRetry);
        shell.RetryProjection();
        AppTestHost.Check(shell.RetryCount == 1);
    }

    static void RoutesWhenUnavailable()
    {
        var shell = AppTestHost.Shell();
        shell.StartAsync().AsTask().GetAwaiter().GetResult();
        shell.OpenSettings();
        AppTestHost.Check(shell.Route == ShellRoute.Settings);
        AppTestHost.Check(shell.SshAvailability.Kind == RouteAvailabilityKind.Enabled);
        AppTestHost.Check(shell.FilesAvailability.Kind == RouteAvailabilityKind.Disabled);
        shell.OpenAbout();
        AppTestHost.Check(shell.Route == ShellRoute.About);
        AppTestHost.Check(shell.Diagnostics.AboutStatement == ProductInfo.IndependentClientStatement);
        AppTestHost.Check(shell.FilesAvailability.Kind == RouteAvailabilityKind.Disabled);
        AppTestHost.Check(shell.Settings.SaveCalls == 0);
    }

    static void NarrowOverlay()
    {
        var catalog = new ProjectionCatalog { Snapshot = AppTestHost.TwoNamedPanes(), DaemonAvailable = true };
        var store = new MemoryDeviceProfileStore
        {
            Snapshot = new ConfigurationSnapshot(
                1, 1,
                [
                    new DeviceProfile(
                        AppTestHost.DeviceA, "lab", ConnectionKinds.Local, "/tmp/herdr",
                        [SessionProfile.Named("dev")])
                ])
        };
        var shell = AppTestHost.Shell(store, catalog);
        shell.StartAsync().AsTask().GetAwaiter().GetResult();
        shell.ExpandAll();
        var pane = shell.VisibleItems.First(item => item.Kind == NavigationKind.Pane);
        shell.Select(pane);
        shell.SetWidth(400);
        AppTestHost.Check(shell.Layout == LayoutBreakpoint.Narrow);
        shell.ToggleNavigationOverlay();
        AppTestHost.Check(shell.NavigationOverlayOpen);
        AppTestHost.Check(!string.IsNullOrWhiteSpace(shell.Breadcrumb));
        shell.SetWidth(1400);
        AppTestHost.Check(shell.Layout == LayoutBreakpoint.Wide);
        AppTestHost.Check(!shell.NavigationOverlayOpen);
    }

    static void IndependentStatus()
    {
        var snapshot = AppTestHost.TwoNamedPanes();
        var pane = snapshot.Devices[0].Sessions[0].Panes[0].Key;
        var catalog = new ProjectionCatalog { Snapshot = snapshot, DaemonAvailable = true };
        catalog.SetAccess(pane, TerminalAccess.Observing, false);
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
        shell.ExpandAll();
        shell.Select(shell.VisibleItems.First(item => item.Pane == pane));
        AppTestHost.Check(shell.UnreadCount == 0);
        AppTestHost.Check(shell.Access == TerminalAccess.Observing);
        AppTestHost.Check(!shell.ControlVerified);
        AppTestHost.Check(shell.AgentStatus.Known == AgentStatusKind.Idle);
        catalog.Snapshot = AppTestHost.WithAgentStatus(snapshot, pane, AppTestHost.Blocked());
        catalog.SetAccess(pane, TerminalAccess.Observing, false);
        shell.RefreshFromCatalog();
        shell.Select(shell.VisibleItems.First(item => item.Pane == pane));
        AppTestHost.Check(shell.UnreadCount == 1);
        AppTestHost.Check(shell.NotificationUnread == 1);
        AppTestHost.Check(shell.Access == TerminalAccess.Observing);
        AppTestHost.Check(!shell.ControlVerified);
        AppTestHost.Check(shell.AgentStatus.Known == AgentStatusKind.Blocked);
        catalog.SetAccess(pane, TerminalAccess.Controlling, true);
        shell.RefreshFromCatalog();
        shell.Select(shell.VisibleItems.First(item => item.Pane == pane));
        AppTestHost.Check(shell.ControlVerified);
        AppTestHost.Check(shell.Access == TerminalAccess.Controlling);
        shell.Select(shell.VisibleItems.First(item => item.Kind == NavigationKind.Device));
        AppTestHost.Check(!shell.ControlVerified);
        AppTestHost.Check(shell.Access == TerminalAccess.Disconnected);
    }
}
