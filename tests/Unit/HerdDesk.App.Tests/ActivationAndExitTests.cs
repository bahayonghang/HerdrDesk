using System.Text;
using HerdDesk.App;
using HerdDesk.Contracts;

internal static class ActivationAndExitTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("exclusive lock gives one configuration writer", SingleWriter),
        ("notification intent without command is re-resolved", NotificationRedirect),
        ("forbidden activation fields are rejected", ForbiddenIntentRejected),
        ("exit releases owned children only", ExitOwnership)
    ];

    static void SingleWriter()
    {
        var root = AppTestHost.TempRoot();
        try
        {
            var intent = Path.Combine(root, "intent.json");
            var name = "HerdDeskHD011" + Guid.NewGuid().ToString("N");
            using var primary = AppActivationCoordinator.Claim(name, intent);
            using var secondary = AppActivationCoordinator.Claim(name, intent);
            AppTestHost.Check(primary.IsPrimary);
            AppTestHost.Check(primary.OwnsConfigurationWriter);
            AppTestHost.Check(!secondary.IsPrimary);
            AppTestHost.Check(!secondary.OwnsConfigurationWriter);
            AppTestHost.Check(primary.WindowCount == 1);
            AppTestHost.Check(secondary.WindowCount == 1);
            var pane = new PaneKey(AppTestHost.SessionOf(AppTestHost.DeviceA), "ws", "p1");
            AppTestHost.Check(secondary.Redirect(new ActivationIntent(
                ActivationKind.NotificationTarget,
                pane.Session.Device,
                pane.Session,
                pane.WorkspaceId,
                pane,
                new ConnectionEpoch(1))));
            var received = primary.PollRedirect();
            AppTestHost.Check(received is not null);
            AppTestHost.Check(received!.Pane == pane);
            AppTestHost.Check(received.Kind == ActivationKind.NotificationTarget);
            AppTestHost.Check(primary.PollRedirect() is null);
            AppTestHost.Check(primary.LastRedirected!.Pane == pane);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void NotificationRedirect()
    {
        var snapshot = AppTestHost.TwoNamedPanes();
        var catalog = new ProjectionCatalog
        {
            Snapshot = snapshot,
            DaemonAvailable = true,
            RendererReadyDefault = true
        };
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
            catalog);
        shell.StartAsync().AsTask().GetAwaiter().GetResult();
        var live = snapshot.Devices[0].Sessions[0].Panes[0].Key;
        shell.ReceiveActivation(new ActivationIntent(
            ActivationKind.NotificationTarget, live.Session.Device, live.Session, live.WorkspaceId, live,
            snapshot.Epoch));
        AppTestHost.Check(shell.Selection.Pane == live);
        AppTestHost.Check(!shell.ActivationExpired);
        AppTestHost.Check(shell.WindowCount == 1);
        var missing = new PaneKey(AppTestHost.SessionOf(AppTestHost.DeviceC), "ws", "p1");
        shell.ReceiveActivation(new ActivationIntent(
            ActivationKind.NotificationTarget, missing.Session.Device, missing.Session, missing.WorkspaceId,
            missing, snapshot.Epoch));
        AppTestHost.Check(shell.ActivationExpired);
        AppTestHost.Check(shell.WindowCount == 1);
        AppTestHost.Check(typeof(ActivationIntent).GetProperty("Command") is null);
        AppTestHost.Check(typeof(ActivationIntent).GetProperty("Input") is null);
        AppTestHost.Check(typeof(ActivationIntent).GetProperty("Takeover") is null);
    }

    static void ForbiddenIntentRejected()
    {
        var json = """{"kind":"notification_target","command":"send-keys","input":"x"}"""u8.ToArray();
        AppTestHost.Check(!ActivationIntent.TryParse(json, out var intent, out var code));
        AppTestHost.Check(intent is null);
        AppTestHost.Check(code == ConfigurationCodes.ForbiddenField);
        var ok = new ActivationIntent(ActivationKind.Settings).ToUtf8();
        AppTestHost.Check(ActivationIntent.TryParse(ok, out var parsed, out _));
        AppTestHost.Check(parsed!.Kind == ActivationKind.Settings);
        AppTestHost.Check(!Encoding.UTF8.GetString(ok).Contains("command", StringComparison.Ordinal));
    }

    static void ExitOwnership()
    {
        var exit = new AppExitCoordinator();
        var child = new FakeOwnedChild();
        var daemon = new FakeDaemon();
        exit.RegisterOwned(4242, child);
        var shell = AppTestHost.Shell(exit: exit);
        shell.StartAsync().AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(exit.AllowNewConnections);
        shell.ExitAsync().AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(child.Disposed);
        AppTestHost.Check(exit.ReleasedIds.Contains(4242));
        AppTestHost.Check(!exit.AllowNewConnections);
        AppTestHost.Check(!exit.AcceptingActivation);
        AppTestHost.Check(!daemon.Stopped);
        AppTestHost.Check(!exit.StoppedDaemon);
        AppTestHost.Check(!exit.StoppedAgent);
        AppTestHost.Check(!exit.ClosedRemotePane);
    }
}
