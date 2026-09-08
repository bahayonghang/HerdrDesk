using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Configuration;

internal static class TerminalDisplaySettingsTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("observe display changes never send upstream resize", ObserveNoResize),
        ("control resize requires capability and verified lease", ControlResizeGate),
        ("display save failure restores last valid preferences", SaveFailureRestores)
    ];

    static void ObserveNoResize()
    {
        var surface = new CountingDisplaySurface();
        var snapshot = AppTestHost.TwoNamedPanes();
        var pane = snapshot.Devices[0].Sessions[0].Panes[0].Key;
        var catalog = new ProjectionCatalog { Snapshot = snapshot, DaemonAvailable = true };
        catalog.SetAccess(pane, TerminalAccess.Observing, false);
        var shell = AppTestHost.Shell(
            new MemoryDeviceProfileStore
            {
                Snapshot = new ConfigurationSnapshot(
                    1, 1,
                    [
                        new DeviceProfile(
                            AppTestHost.DeviceA, "lab", ConnectionKinds.Local, "/tmp/herdr",
                            [SessionProfile.Named("dev")])
                    ])
            },
            catalog,
            surface: surface);
        shell.StartAsync().AsTask().GetAwaiter().GetResult();
        shell.ExpandAll();
        shell.Select(shell.VisibleItems.First(item => item.Pane == pane));
        var before = surface.LocalApplyCount;
        for (var i = 0; i < 100; i++)
            shell.Settings.PreviewDisplay(new TerminalDisplayPreferences("Cascadia Mono", 12, 100 + i % 3));
        AppTestHost.Check(surface.LocalApplyCount == before + 100);
        AppTestHost.Check(surface.UpstreamResizeCount == 0);
        AppTestHost.Check(!shell.TryRequestResize(80, 24));
        AppTestHost.Check(surface.UpstreamResizeCount == 0);
    }

    static void ControlResizeGate()
    {
        var surface = new CountingDisplaySurface();
        var snapshot = AppTestHost.TwoNamedPanes();
        var pane = snapshot.Devices[0].Sessions[0].Panes[0].Key;
        var catalog = new ProjectionCatalog { Snapshot = snapshot, DaemonAvailable = true };
        catalog.SetAccess(pane, TerminalAccess.Controlling, true);
        var shell = AppTestHost.Shell(
            new MemoryDeviceProfileStore
            {
                Snapshot = new ConfigurationSnapshot(
                    1, 1,
                    [
                        new DeviceProfile(
                            AppTestHost.DeviceA, "lab", ConnectionKinds.Local, "/tmp/herdr",
                            [SessionProfile.Named("dev")])
                    ])
            },
            catalog,
            surface: surface);
        shell.StartAsync().AsTask().GetAwaiter().GetResult();
        shell.ExpandAll();
        shell.Select(shell.VisibleItems.First(item => item.Pane == pane));
        AppTestHost.Check(shell.ControlVerified);
        AppTestHost.Check(shell.TryRequestResize(120, 40));
        AppTestHost.Check(surface.UpstreamResizeCount == 1);
        var denied = shell.Display.TryRequestResize(
            80, 24,
            new InputContext(pane, snapshot.Epoch, TerminalAccess.Controlling, true),
            CapabilityGate.Evaluate(SchemaCompatibilityBinding.PinnedUnverified("0.9.0"), 22, "0.9.0"));
        AppTestHost.Check(!denied);
        AppTestHost.Check(surface.UpstreamResizeCount == 1);
    }

    static void SaveFailureRestores()
    {
        var root = AppTestHost.TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            Directory.CreateDirectory(paths.SettingsDirectory);
            var ui = new UiPreferenceStore(paths);
            var surface = new CountingDisplaySurface();
            var shell = AppTestHost.Shell(
                new AtomicConfigurationStore(paths),
                paths: paths,
                ui: ui,
                surface: surface);
            shell.StartAsync().AsTask().GetAwaiter().GetResult();
            shell.Settings.PreviewDisplay(new TerminalDisplayPreferences("Cascadia Mono", 14, 125));
            shell.Settings.SaveUiPreferencesAsync().AsTask().GetAwaiter().GetResult();
            AppTestHost.Check(shell.Settings.Lifecycle == SettingsLifecycle.Saved);
            AppTestHost.Check(shell.Settings.CommittedUi.FontSize == 14);
            var failing = new UiPreferenceStore(paths, new UiPreferenceStoreHooks
            {
                Serialize = _ => throw new InvalidOperationException("serialize_boom")
            });
            var failShell = AppTestHost.Shell(
                new AtomicConfigurationStore(paths),
                paths: paths,
                ui: failing,
                surface: new CountingDisplaySurface());
            failShell.StartAsync().AsTask().GetAwaiter().GetResult();
            AppTestHost.Check(failShell.Settings.CommittedUi.FontSize == 14);
            failShell.Settings.PreviewDisplay(new TerminalDisplayPreferences("Cascadia Mono", 18, 150));
            failShell.Settings.SaveUiPreferencesAsync().AsTask().GetAwaiter().GetResult();
            AppTestHost.Check(failShell.Settings.Lifecycle == SettingsLifecycle.SaveFailed);
            AppTestHost.Check(failShell.Settings.UiDraft.FontSize == 14);
            AppTestHost.Check(failShell.Display.Committed.FontSize == 14);
            var reloaded = ui.LoadAsync().AsTask().GetAwaiter().GetResult();
            AppTestHost.Check(reloaded.Succeeded);
            AppTestHost.Check(reloaded.Preferences!.FontSize == 14);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
