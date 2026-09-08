using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Configuration;
using HerdDesk.Infrastructure.Diagnostics;

internal static class DeepLinkRoutingTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("closed target expires and does not jump namesake", ClosedDoesNotJumpNamesake),
        ("old epoch activation expires", OldEpochExpires),
        ("new click cancels pending focus", CancelPending),
        ("activate does not takeover or send input", NoTakeover),
        ("muted toast stays in center with unread", MutedKeepsCenter),
        ("center states hover selection focus and copy", CenterStates),
        ("diagnostics omit terminal body and input", DiagnosticsRedacted)
    ];

    static ShellViewModel Ready(DeviceProjectionSnapshot snapshot, ProjectionCatalog catalog)
    {
        var store = new MemoryDeviceProfileStore
        {
            Snapshot = new ConfigurationSnapshot(
                1, 1,
                [
                    new DeviceProfile(
                        snapshot.Devices[0].Device, "lab", ConnectionKinds.Local, "/tmp/herdr",
                        [SessionProfile.Named("dev")])
                ])
        };
        var shell = AppTestHost.Shell(store, catalog);
        shell.StartAsync().AsTask().GetAwaiter().GetResult();
        shell.ExpandAll();
        return shell;
    }

    static void ClosedDoesNotJumpNamesake()
    {
        var snapshot = AppTestHost.TwoNamedPanes();
        var catalog = new ProjectionCatalog
        {
            Snapshot = snapshot,
            DaemonAvailable = true,
            RendererReadyDefault = true
        };
        var shell = Ready(snapshot, catalog);
        var first = snapshot.Devices[0].Sessions[0].Panes[0].Key;
        catalog.Snapshot = AppTestHost.WithAgentStatus(snapshot, first, AppTestHost.Blocked());
        shell.RefreshFromCatalog();
        var token = shell.Notifications.Items.Single(item => item.Key.Pane == first).TransitionId;
        var remaining = snapshot.Devices.First(item => item.Device != first.Session.Device);
        catalog.Snapshot = AppTestHost.Snapshot(snapshot.Epoch, ConnectionPhase.Ready, remaining);
        shell.RefreshFromCatalog();
        AppTestHost.Check(shell.Notifications.Items.Single(item => item.Key.Pane == first).IsExpired);
        AppTestHost.Check(shell.Notifications.Items.Single(item => item.Key.Pane == first).Key.Pane !=
                          remaining.Sessions[0].Panes[0].Key);
        var ok = shell.ActivateNotification(token);
        AppTestHost.Check(!ok);
        AppTestHost.Check(shell.ActivationExpired);
        AppTestHost.Check(shell.Selection.IsExpired || shell.Selection.Pane != remaining.Sessions[0].Panes[0].Key);
        var namesake = shell.VisibleItems.FirstOrDefault(item => item.Kind == NavigationKind.Pane);
        AppTestHost.Check(namesake is not null);
        AppTestHost.Check(!namesake!.IsSelected);
        AppTestHost.Check(namesake.Pane != first);
    }

    static void OldEpochExpires()
    {
        var snapshot = AppTestHost.TwoNamedPanes();
        var catalog = new ProjectionCatalog
        {
            Snapshot = snapshot,
            DaemonAvailable = true,
            RendererReadyDefault = true
        };
        var shell = Ready(snapshot, catalog);
        var pane = snapshot.Devices[0].Sessions[0].Panes[0].Key;
        catalog.Snapshot = AppTestHost.WithAgentStatus(snapshot, pane, AppTestHost.Done());
        shell.RefreshFromCatalog();
        var token = shell.Notifications.Items.Single().TransitionId;
        catalog.Snapshot = snapshot with { Epoch = new ConnectionEpoch(2) };
        AppTestHost.Check(!shell.ActivateNotification(token));
        AppTestHost.Check(shell.ActivationExpired);
        AppTestHost.Check(shell.Notifications.Items.Single().Headline == ShellStrings.NotificationDone);
        AppTestHost.Check(!shell.Notifications.Items.Single().Headline.Contains("成功", StringComparison.Ordinal));
        AppTestHost.Check(!shell.Notifications.Items.Single().Body.Contains("成功", StringComparison.Ordinal));
        AppTestHost.Check(!shell.Notifications.Items.Single().Body.Contains("success", StringComparison.OrdinalIgnoreCase));
    }

    static void CancelPending()
    {
        var session = AppTestHost.SessionOf(AppTestHost.DeviceA);
        var paneA = new PaneKey(session, "ws", "p1");
        var paneB = new PaneKey(session, "ws", "p2");
        var snapshot = AppTestHost.Snapshot(
            new ConnectionEpoch(1),
            ConnectionPhase.Ready,
            AppTestHost.Projected(
                AppTestHost.DeviceA,
                AppTestHost.Compatible(),
                AppTestHost.SessionState(
                    session,
                    [AppTestHost.Workspace(session, "ws", "lab", 2)],
                    [AppTestHost.Pane(paneA, "one"), AppTestHost.Pane(paneB, "two")])));
        var catalog = new ProjectionCatalog { Snapshot = snapshot, DaemonAvailable = true };
        catalog.SetRendererReady(paneA, false);
        catalog.SetRendererReady(paneB, false);
        var shell = Ready(snapshot, catalog);
        catalog.Snapshot = AppTestHost.WithAgentStatus(
            AppTestHost.WithAgentStatus(snapshot, paneA, AppTestHost.Blocked()),
            paneB,
            AppTestHost.Done());
        shell.RefreshFromCatalog();
        var first = shell.Notifications.Items.First(item => item.Key.Pane == paneA).TransitionId;
        var second = shell.Notifications.Items.First(item => item.Key.Pane == paneB).TransitionId;
        AppTestHost.Check(!shell.ActivateNotification(first));
        AppTestHost.Check(!shell.ContentFocused);
        catalog.SetRendererReady(paneB, true);
        AppTestHost.Check(shell.ActivateNotification(second));
        AppTestHost.Check(shell.Selection.Pane == paneB);
        AppTestHost.Check(shell.Selection.Pane != paneA);
        AppTestHost.Check(shell.ContentFocused);
    }

    static void NoTakeover()
    {
        var snapshot = AppTestHost.TwoNamedPanes();
        var pane = snapshot.Devices[0].Sessions[0].Panes[0].Key;
        var catalog = new ProjectionCatalog
        {
            Snapshot = snapshot,
            DaemonAvailable = true,
            RendererReadyDefault = true
        };
        catalog.SetAccess(pane, TerminalAccess.Observing, false);
        var shell = Ready(snapshot, catalog);
        catalog.Snapshot = AppTestHost.WithAgentStatus(snapshot, pane, AppTestHost.Blocked());
        shell.RefreshFromCatalog();
        var token = shell.Notifications.Items.Single().TransitionId;
        AppTestHost.Check(shell.ActivateNotification(token));
        AppTestHost.Check(shell.Selection.Pane == pane);
        AppTestHost.Check(shell.Access == TerminalAccess.Observing);
        AppTestHost.Check(!shell.ControlVerified);
        AppTestHost.Check(typeof(NotificationTarget).GetProperty("Command") is null);
        AppTestHost.Check(typeof(NotificationTarget).GetProperty("Input") is null);
        AppTestHost.Check(typeof(NotificationTarget).GetProperty("Takeover") is null);
        AppTestHost.Check(typeof(ActivationIntent).GetProperty("Command") is null);
    }

    static void MutedKeepsCenter()
    {
        var root = AppTestHost.TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            var ui = new UiPreferenceStore(paths);
            ui.SaveAsync(UiPreferences.Default with { NotificationsEnabled = false }, 0)
                .AsTask().GetAwaiter().GetResult();
            var snapshot = AppTestHost.TwoNamedPanes();
            var pane = snapshot.Devices[0].Sessions[0].Panes[0].Key;
            var catalog = new ProjectionCatalog
            {
                Snapshot = snapshot,
                DaemonAvailable = true,
                RendererReadyDefault = true
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
            var shell = AppTestHost.Shell(store, catalog, paths, ui: ui);
            shell.StartAsync().AsTask().GetAwaiter().GetResult();
            catalog.Snapshot = AppTestHost.WithAgentStatus(snapshot, pane, AppTestHost.Blocked());
            shell.RefreshFromCatalog();
            AppTestHost.Check(shell.Notifications.LastDeliverAttempts == 0);
            AppTestHost.Check(shell.Notifications.Items.Count == 1);
            AppTestHost.Check(shell.Notifications.UnreadCount == 1);
            AppTestHost.Check(shell.Notifications.Lifecycle == NotificationCenterLifecycle.Muted);
            shell.OpenNotifications();
            AppTestHost.Check(shell.Route == ShellRoute.Notifications);
            AppTestHost.Check(shell.Notifications.AutomationName.Contains("1", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void CenterStates()
    {
        var snapshot = AppTestHost.TwoNamedPanes();
        var catalog = new ProjectionCatalog
        {
            Snapshot = snapshot,
            DaemonAvailable = true,
            RendererReadyDefault = true
        };
        var shell = Ready(snapshot, catalog);
        shell.OpenNotifications();
        AppTestHost.Check(shell.Notifications.Lifecycle is NotificationCenterLifecycle.Empty
            or NotificationCenterLifecycle.Ready);
        var pane = snapshot.Devices[0].Sessions[0].Panes[0].Key;
        catalog.Snapshot = AppTestHost.WithAgentStatus(snapshot, pane, AppTestHost.Blocked());
        shell.RefreshFromCatalog();
        shell.OpenNotifications();
        AppTestHost.Check(shell.Notifications.Items.Count == 1);
        var item = shell.Notifications.Items[0];
        shell.Notifications.SetHover(item);
        shell.Notifications.SetFocusVisible(item);
        shell.Notifications.Select(item);
        AppTestHost.Check(shell.Notifications.Selected is not null);
        AppTestHost.Check(shell.Notifications.Selected!.IsSelected);
        AppTestHost.Check(shell.Notifications.Selected.IsHovered);
        AppTestHost.Check(shell.Notifications.Selected.IsFocusVisible);
        AppTestHost.Check(shell.Notifications.Move(TreeMove.Activate));
        shell.Notifications.SetFilter(NotificationFilter.Unread);
        AppTestHost.Check(shell.Notifications.VisibleItems.Count == 1);
        shell.Notifications.SetFilter(NotificationFilter.Done);
        AppTestHost.Check(shell.Notifications.VisibleItems.Count == 0);
        catalog.LastErrorCode = "error";
        shell.Notifications.Open();
        AppTestHost.Check(shell.Notifications.Lifecycle == NotificationCenterLifecycle.Error);
        AppTestHost.Check(shell.Notifications.BannerCode == ShellCodes.Error);
        catalog.LastErrorCode = null;
        catalog.Snapshot = snapshot with { Phase = ConnectionPhase.Offline };
        shell.Notifications.Open();
        AppTestHost.Check(shell.Notifications.Lifecycle == NotificationCenterLifecycle.Offline);
        AppTestHost.Check(!item.Body.Contains("成功", StringComparison.Ordinal));
    }

    static void DiagnosticsRedacted()
    {
        var sink = new RecordingDiagnosticSink();
        var aliases = new DiagnosticAliasProjector(new byte[16]);
        var snapshot = AppTestHost.TwoNamedPanes();
        var pane = snapshot.Devices[0].Sessions[0].Panes[0].Key;
        var catalog = new ProjectionCatalog
        {
            Snapshot = snapshot,
            DaemonAvailable = true,
            RendererReadyDefault = true
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
        var shell = AppTestHost.Shell(store, catalog, diagnostics: sink, aliases: aliases);
        shell.StartAsync().AsTask().GetAwaiter().GetResult();
        catalog.Snapshot = AppTestHost.WithAgentStatus(snapshot, pane, AppTestHost.Blocked());
        shell.RefreshFromCatalog();
        AppTestHost.Check(sink.Events.Count > 0);
        foreach (var evt in sink.Events)
        {
            AppTestHost.Check(evt.Component == "attention");
            AppTestHost.Check(evt.Operation.Contains("terminal.input", StringComparison.Ordinal) is false);
            AppTestHost.Check(evt.Operation.Contains("password", StringComparison.Ordinal) is false);
            AppTestHost.Check((evt.ErrorCode ?? "").Contains("ansi", StringComparison.Ordinal) is false);
            AppTestHost.Check((evt.DeviceAlias ?? "").Contains('\\') is false);
            AppTestHost.Check((evt.SessionAlias ?? "").Contains("named-session", StringComparison.Ordinal) is false);
        }

        AppTestHost.Check(new WindowsNotificationSink().Available is false);
        var entry = shell.Notifications.Reducer.Entries[0];
        var transition = new AttentionTransition(
            entry.Key, entry.From, entry.To, entry.Stamp, entry.TransitionId, false, entry.Freshness, entry.Target);
        AppTestHost.Check(new WindowsNotificationSink().TryDeliver(transition, entry.Target).Kind ==
                          NotificationDeliveryKind.Unavailable);
    }
}
