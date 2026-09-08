using HerdDesk.App;
using HerdDesk.Contracts;

internal static class NavigationIdentityTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("same pane labels keep full identity keys", SameNamePanes),
        ("deleted pane expires without substituting the namesake", DeletedPaneDoesNotSubstitute),
        ("old epoch selection expires", StaleEpochExpires),
        ("tree keyboard walks thirty panes", ThirtyPaneKeyboard),
        ("session is not workspace even with the same label", SessionNotWorkspace),
        ("muse stays unknown agent kind", MuseIsUnknown),
        ("incompatible shows text and disables writes", IncompatibleReason),
        ("select does not take control", SelectDoesNotTakeControl)
    ];

    static ShellViewModel Ready(DeviceProjectionSnapshot snapshot, ProjectionCatalog? catalog = null)
    {
        catalog ??= new ProjectionCatalog { Snapshot = snapshot, DaemonAvailable = true };
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

    static void SameNamePanes()
    {
        var shell = Ready(AppTestHost.TwoNamedPanes());
        var panes = shell.VisibleItems.Where(item => item.Kind == NavigationKind.Pane).ToArray();
        AppTestHost.Check(panes.Length == 2);
        AppTestHost.Check(panes[0].Label == "main");
        AppTestHost.Check(panes[1].Label == "main");
        AppTestHost.Check(panes[0].Pane != panes[1].Pane);
        AppTestHost.Check(panes[0].Device != panes[1].Device);
        shell.Select(panes[0]);
        AppTestHost.Check(shell.Selection.Pane == panes[0].Pane);
        AppTestHost.Check(shell.Selection.Device == panes[0].Device);
        shell.Select(panes[1]);
        AppTestHost.Check(shell.Selection.Pane == panes[1].Pane);
        AppTestHost.Check(shell.Selection.Session == panes[1].Session);
    }

    static void DeletedPaneDoesNotSubstitute()
    {
        var snapshot = AppTestHost.TwoNamedPanes();
        var catalog = new ProjectionCatalog { Snapshot = snapshot, DaemonAvailable = true };
        var shell = Ready(snapshot, catalog);
        var first = shell.VisibleItems.First(item => item.Kind == NavigationKind.Pane);
        var firstKey = first.Pane;
        shell.Select(first);
        var remainingDevice = snapshot.Devices.First(item => item.Device != first.Device);
        catalog.Snapshot = AppTestHost.Snapshot(
            snapshot.Epoch, ConnectionPhase.Ready, remainingDevice);
        shell.RefreshFromCatalog();
        AppTestHost.Check(shell.Selection.IsExpired);
        AppTestHost.Check(shell.Selection.Pane == firstKey);
        var remaining = shell.VisibleItems.Where(item => item.Kind == NavigationKind.Pane).ToArray();
        AppTestHost.Check(remaining.Length == 1);
        AppTestHost.Check(remaining[0].Label == "main");
        AppTestHost.Check(!remaining[0].IsSelected);
    }

    static void StaleEpochExpires()
    {
        var snapshot = AppTestHost.TwoNamedPanes();
        var catalog = new ProjectionCatalog
        {
            Snapshot = snapshot,
            DaemonAvailable = true,
            RendererReadyDefault = true
        };
        var shell = Ready(snapshot, catalog);
        var pane = shell.VisibleItems.First(item => item.Kind == NavigationKind.Pane);
        catalog.SetAccess(pane.Pane!.Value, TerminalAccess.Controlling, true);
        shell.RefreshFromCatalog();
        pane = shell.VisibleItems.First(item => item.Pane == pane.Pane);
        shell.Select(pane);
        AppTestHost.Check(shell.ContentFocused);
        AppTestHost.Check(shell.TryRequestResize(80, 24));
        catalog.Snapshot = snapshot with { Epoch = new ConnectionEpoch(2) };
        shell.RefreshFromCatalog();
        AppTestHost.Check(shell.Selection.IsExpired);
        AppTestHost.Check(shell.Selection.Epoch == snapshot.Epoch);
        AppTestHost.Check(!shell.ContentFocused);
        AppTestHost.Check(!shell.TryRequestResize(80, 24));
    }

    static void ThirtyPaneKeyboard()
    {
        var shell = Ready(AppTestHost.ThirtyPanes());
        var panes = shell.VisibleItems.Where(item => item.Kind == NavigationKind.Pane).ToArray();
        AppTestHost.Check(panes.Length == 30);
        shell.Select(panes[0]);
        for (var i = 0; i < 14; i++)
            shell.MoveTree(TreeMove.Down);
        AppTestHost.Check(shell.Selection.Pane == panes[14].Pane);
        AppTestHost.Check(shell.Selection.Pane!.Value.WorkspaceId == "w1");
        shell.MoveTree(TreeMove.Down);
        AppTestHost.Check(shell.Selection.Kind == SelectionKind.Workspace);
        AppTestHost.Check(shell.Selection.WorkspaceId == "w2");
        shell.MoveTree(TreeMove.Child);
        AppTestHost.Check(shell.Selection.Pane == panes[15].Pane);
        AppTestHost.Check(shell.Selection.Pane!.Value.WorkspaceId == "w2");
        shell.Select(panes[29]);
        shell.MoveTree(TreeMove.Parent);
        AppTestHost.Check(shell.Selection.Kind == SelectionKind.Workspace);
        AppTestHost.Check(shell.Selection.WorkspaceId == "w2");
        shell.Select(panes[29]);
        var workspace = shell.VisibleItems.First(item =>
            item.Kind == NavigationKind.Workspace && item.WorkspaceId == "w2");
        shell.ToggleExpanded(workspace);
        AppTestHost.Check(shell.Selection.Pane == panes[29].Pane);
        AppTestHost.Check(!shell.Selection.IsExpired);
        var focused = panes[10];
        shell.SetFocusVisible(focused);
        shell.SetHover(focused);
        var current = shell.VisibleItems.First(item => item.Pane == focused.Pane);
        AppTestHost.Check(current.IsFocusVisible);
        AppTestHost.Check(current.IsHovered);
        AppTestHost.Check(!string.IsNullOrEmpty(current.Status.Text));
        AppTestHost.Check(!string.IsNullOrEmpty(current.Status.IconKey));
    }

    static void SessionNotWorkspace()
    {
        var session = AppTestHost.SessionOf(AppTestHost.DeviceA, name: "lab");
        var pane = new PaneKey(session, "ws", "p1");
        var snapshot = AppTestHost.Snapshot(
            new ConnectionEpoch(1),
            ConnectionPhase.Ready,
            AppTestHost.Projected(
                AppTestHost.DeviceA,
                AppTestHost.Compatible(),
                AppTestHost.SessionState(
                    session,
                    [AppTestHost.Workspace(session, "ws", "lab", 1)],
                    [AppTestHost.Pane(pane, "lab")])));
        var shell = Ready(snapshot);
        var sessionNode = shell.VisibleItems.First(item => item.Kind == NavigationKind.Session);
        var workspaceNode = shell.VisibleItems.First(item => item.Kind == NavigationKind.Workspace);
        AppTestHost.Check(sessionNode.Label == "lab");
        AppTestHost.Check(workspaceNode.Label == "lab");
        AppTestHost.Check(sessionNode.KindLabel == ShellStrings.Session);
        AppTestHost.Check(workspaceNode.KindLabel == ShellStrings.Workspace);
        AppTestHost.Check(sessionNode.IdentityKey != workspaceNode.IdentityKey);
    }

    static void MuseIsUnknown()
    {
        var session = AppTestHost.SessionOf(AppTestHost.DeviceA);
        var pane = new PaneKey(session, "ws", "p1");
        var snapshot = AppTestHost.Snapshot(
            new ConnectionEpoch(1),
            ConnectionPhase.Ready,
            AppTestHost.Projected(
                AppTestHost.DeviceA,
                AppTestHost.Compatible(),
                AppTestHost.SessionState(
                    session,
                    [AppTestHost.Workspace(session, "ws", "lab", 1)],
                    [AppTestHost.Pane(pane, "muse-pane", "muse", null)])));
        var shell = Ready(snapshot);
        var item = shell.VisibleItems.First(item => item.Kind == NavigationKind.Pane);
        AppTestHost.Check(item.AgentStatus.Known == AgentStatusKind.Unknown);
        AppTestHost.Check(item.KindLabel != "Muse");
        AppTestHost.Check(!Enum.GetNames<KnownAgentKind>().Contains("Muse"));
    }

    static void IncompatibleReason()
    {
        var session = AppTestHost.SessionOf(AppTestHost.DeviceA);
        var pane = new PaneKey(session, "ws", "p1");
        var snapshot = AppTestHost.Snapshot(
            new ConnectionEpoch(1),
            ConnectionPhase.Incompatible,
            AppTestHost.Projected(
                AppTestHost.DeviceA,
                AppTestHost.Incompatible(),
                AppTestHost.SessionState(
                    session,
                    [AppTestHost.Workspace(session, "ws", "lab", 1)],
                    [AppTestHost.Pane(pane, "main")])));
        var shell = Ready(snapshot);
        var item = shell.VisibleItems.First(item => item.Kind == NavigationKind.Pane);
        AppTestHost.Check(item.IsIncompatible);
        AppTestHost.Check(item.IsDisabled);
        AppTestHost.Check(item.Status.Text == ShellStrings.Incompatible);
        AppTestHost.Check(item.CapabilityReasons.Contains(ShellCodes.CapabilityUnverified));
        AppTestHost.Check(item.Status.Action == RecoveryActionKind.OpenDiagnostics);
    }

    static void SelectDoesNotTakeControl()
    {
        var snapshot = AppTestHost.TwoNamedPanes();
        var pane = snapshot.Devices[0].Sessions[0].Panes[0].Key;
        var catalog = new ProjectionCatalog { Snapshot = snapshot, DaemonAvailable = true };
        catalog.SetAccess(pane, TerminalAccess.Observing, false);
        var shell = Ready(snapshot, catalog);
        var item = shell.VisibleItems.First(item => item.Pane == pane);
        shell.Select(item);
        shell.MoveTree(TreeMove.Activate);
        AppTestHost.Check(shell.Access == TerminalAccess.Observing);
        AppTestHost.Check(!shell.ControlVerified);
    }
}
