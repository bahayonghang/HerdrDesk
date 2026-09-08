using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Configuration;

internal static class LocalDeviceSettingsTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("empty device can add named and explicit sessions atomically", CreateTwoSessions),
        ("editing one session keeps the other", EditOneSession),
        ("save failure restores committed values", SaveFailureRestores),
        ("secondary instance cannot write configuration", SecondaryCannotWrite)
    ];

    static void CreateTwoSessions()
    {
        var root = AppTestHost.TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            Directory.CreateDirectory(paths.SettingsDirectory);
            var store = new AtomicConfigurationStore(paths);
            var shell = AppTestHost.Shell(store, paths: paths);
            shell.StartAsync().AsTask().GetAwaiter().GetResult();
            AppTestHost.Check(shell.Lifecycle == ShellLifecycle.NoDevices);
            shell.RequestAddDevice();
            var device = AppTestHost.DeviceA;
            shell.Settings.BeginNewDevice(device);
            shell.Settings.SetDeviceLabel("lab");
            shell.Settings.SetHerdrPath(Path.Combine(paths.Root, "herdr"));
            shell.Settings.AddNamedSession("dev");
            shell.Settings.AddExplicitEndpoint(AppTestHost.ExplicitLocation(), AppTestHost.ExplicitKind());
            shell.Settings.SaveLocalDeviceAsync().AsTask().GetAwaiter().GetResult();
            AppTestHost.Check(shell.Settings.Lifecycle == SettingsLifecycle.Saved);
            AppTestHost.Check(shell.Settings.CommittedDevice is not null);
            AppTestHost.Check(shell.Settings.CommittedDevice!.Sessions.Count == 2);
            var named = shell.Settings.CommittedDevice.Sessions[0].ToSessionKey(device);
            var explicitKey = shell.Settings.CommittedDevice.Sessions[1].ToSessionKey(device);
            AppTestHost.Check(named != explicitKey);
            shell.Settings.RequestConnect(0);
            shell.Settings.RequestConnect(1);
            AppTestHost.Check(shell.Settings.PendingConnects.Count == 2);
            AppTestHost.Check(shell.Settings.PendingConnects[0] == named);
            AppTestHost.Check(shell.HiddenTerminalBridgeCount == 0);
            var loaded = store.LoadAsync().AsTask().GetAwaiter().GetResult();
            AppTestHost.Check(loaded.Succeeded);
            AppTestHost.Check(loaded.Snapshot!.Devices.Single().Sessions.Count == 2);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void EditOneSession()
    {
        var root = AppTestHost.TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            Directory.CreateDirectory(paths.SettingsDirectory);
            var store = new AtomicConfigurationStore(paths);
            var shell = AppTestHost.Shell(store, paths: paths);
            shell.StartAsync().AsTask().GetAwaiter().GetResult();
            shell.Settings.BeginNewDevice(AppTestHost.DeviceA);
            shell.Settings.SetDeviceLabel("lab");
            shell.Settings.SetHerdrPath(Path.Combine(paths.Root, "herdr"));
            shell.Settings.AddNamedSession("dev");
            shell.Settings.AddExplicitEndpoint(AppTestHost.ExplicitLocation(), AppTestHost.ExplicitKind());
            shell.Settings.SaveLocalDeviceAsync().AsTask().GetAwaiter().GetResult();
            var before = shell.Settings.CommittedDevice!.Sessions[0];
            shell.Settings.ReplaceSession(1, new LocalSessionDraft(
                SessionProfileKind.NamedSession, "other", null, null));
            shell.Settings.SaveLocalDeviceAsync().AsTask().GetAwaiter().GetResult();
            AppTestHost.Check(shell.Settings.Lifecycle == SettingsLifecycle.Saved);
            AppTestHost.Check(shell.Settings.CommittedDevice!.Sessions[0].SessionName == before.SessionName);
            AppTestHost.Check(shell.Settings.CommittedDevice.Sessions[1].SessionName == "other");
            AppTestHost.Check(shell.Settings.CommittedDevice.Sessions[0].ToSessionKey(AppTestHost.DeviceA) !=
                              shell.Settings.CommittedDevice.Sessions[1].ToSessionKey(AppTestHost.DeviceA));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void SaveFailureRestores()
    {
        var store = new MemoryDeviceProfileStore();
        var shell = AppTestHost.Shell(store);
        shell.StartAsync().AsTask().GetAwaiter().GetResult();
        shell.Settings.BeginNewDevice(AppTestHost.DeviceA);
        shell.Settings.SetDeviceLabel("lab");
        shell.Settings.SetHerdrPath("/tmp/herdr");
        shell.Settings.AddNamedSession("dev");
        shell.Settings.SaveLocalDeviceAsync().AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(shell.Settings.Lifecycle == SettingsLifecycle.Saved);
        store.NextWriteCode = ConfigurationCodes.ReplaceFailed;
        shell.Settings.SetDeviceLabel("changed");
        shell.Settings.SaveLocalDeviceAsync().AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(shell.Settings.Lifecycle == SettingsLifecycle.SaveFailed);
        AppTestHost.Check(shell.Settings.ErrorCode == ConfigurationCodes.ReplaceFailed);
        AppTestHost.Check(shell.Settings.Draft.Label == "lab");
        AppTestHost.Check(store.Snapshot.Devices.Single().Label == "lab");
    }

    static void SecondaryCannotWrite()
    {
        var store = new MemoryDeviceProfileStore();
        var shell = AppTestHost.Shell(store, ownership: ConfigurationOwnership.Secondary);
        shell.StartAsync().AsTask().GetAwaiter().GetResult();
        shell.OpenSettings();
        shell.Settings.BeginNewDevice(AppTestHost.DeviceA);
        shell.Settings.SetDeviceLabel("lab");
        shell.Settings.SetHerdrPath("/tmp/herdr");
        shell.Settings.AddNamedSession("dev");
        shell.Settings.SaveLocalDeviceAsync().AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(shell.Settings.Lifecycle == SettingsLifecycle.PermissionDenied);
        AppTestHost.Check(store.SaveCalls == 0);
        AppTestHost.Check(shell.Route == ShellRoute.Settings);
    }
}
