using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Configuration;

internal static class WorkbenchLayoutTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("empty session state keeps empty tabs and layouts", EmptyFixtureCompiles),
        ("two tabs and two rects project a mosaic", TwoTabSplit),
        ("fifth pane waits with capacity copy", FivePaneCapacity),
        ("selecting a tab does not set control verified", SelectTabNoControl),
        ("begin observe no-op does not fake ready", BeginObserveNoReady),
        ("layout focus stays a single projection match", UniqueFocusedSlot),
        ("zoomed layout is a single focused slot", ZoomedOneSlot),
        ("tab switch hides left panes on a new epoch", TabSwitchNewEpoch),
        ("missing layout falls back to one placeholder slot", NoLayoutFallback)
    ];

    static void EmptyFixtureCompiles()
    {
        var session = AppTestHost.SessionOf(AppTestHost.DeviceA);
        var state = AppTestHost.SessionState(
            session,
            [AppTestHost.Workspace(session, "ws", "lab", 1)],
            [AppTestHost.Pane(new PaneKey(session, "ws", "p1"), "main")]);
        AppTestHost.Check(state.Tabs.Count == 0);
        AppTestHost.Check(state.Layouts.Count == 0);
        var layout = WorkbenchLayout.CreateProduct();
        layout.Project(state, "ws", state.Panes[0].Key);
        AppTestHost.Check(layout.Tabs.Count == 0);
        AppTestHost.Check(layout.Slots.Count == 1);
        AppTestHost.Check(layout.Slots[0].Pane.PaneId == "p1");
        AppTestHost.Check(layout.Slots[0].BindHost);
        AppTestHost.Check(layout.Slots[0].NeedsPlaceholder);
        AppTestHost.Check(layout.Slots[0].StatusText == ShellStrings.TerminalPlaceholder);
        AppTestHost.Check(!layout.HasLayout);
    }

    static void TwoTabSplit()
    {
        AppTestHost.Check(PaneVisibilityCoordinator.MaxVisiblePanes == ResourceBudgets.ProductGlobalTerminals);
        AppTestHost.Check(ResourceBudgets.Product.MaxGlobalTerminals == 4);
        var snapshot = AppTestHost.TwoTabSplitSnapshot();
        var session = snapshot.Devices[0].Sessions[0];
        var layout = WorkbenchLayout.CreateProduct();
        layout.Project(session, "ws");
        AppTestHost.Check(layout.Tabs.Count == 2);
        AppTestHost.Check(layout.Tabs[0].TabId == "t1");
        AppTestHost.Check(layout.Tabs[1].TabId == "t2");
        AppTestHost.Check(layout.SelectedTabId == "t1");
        AppTestHost.Check(layout.Tabs[0].Selected);
        AppTestHost.Check(layout.Tabs[0].ProjectionFocused);
        AppTestHost.Check(!layout.Tabs[1].Selected);
        AppTestHost.Check(layout.Slots.Count == 2);
        AppTestHost.Check(layout.Slots[0].Pane == new PaneKey(session.Session, "ws", "p1"));
        AppTestHost.Check(layout.Slots[1].Pane == new PaneKey(session.Session, "ws", "p2"));
        AppTestHost.Check(layout.Slots[0].Focused);
        AppTestHost.Check(!layout.Slots[1].Focused);
        AppTestHost.Check(layout.Slots[0].Visibility == PaneVisibilityKind.Visible);
        AppTestHost.Check(layout.Slots[1].Visibility == PaneVisibilityKind.Visible);
        AppTestHost.Check(layout.Slots[0].BindHost);
        AppTestHost.Check(layout.Slots[1].BindHost);
        AppTestHost.Check(layout.Slots[0].Rect.X == 0);
        AppTestHost.Check(layout.Slots[0].Rect.Width == 0.5);
        AppTestHost.Check(layout.Slots[1].Rect.X == 0.5);
        AppTestHost.Check(layout.Slots[1].Rect.Width == 0.5);
        AppTestHost.Check(layout.Slots[0].Rect.X + layout.Slots[0].Rect.Width <= layout.Slots[1].Rect.X + 1e-9);
        AppTestHost.Check(layout.Slots.Count(item => item.Focused) == 1);
        AppTestHost.Check(layout.Visibility.VisibleCount == 2);
        AppTestHost.Check(!layout.Visibility.InputReplayed);
        AppTestHost.Check(!layout.Visibility.ControlRestored);
    }

    static void FivePaneCapacity()
    {
        var snapshot = AppTestHost.FivePaneLayoutSnapshot();
        var session = snapshot.Devices[0].Sessions[0];
        var host = new RecordingPaneHost();
        var layout = new WorkbenchLayout(
            new PaneVisibilityCoordinator(new ConnectionAdmissionPolicy(ResourceBudgets.Product), host));
        layout.Project(session, "ws");
        AppTestHost.Check(layout.Slots.Count == 5);
        AppTestHost.Check(layout.Slots.Count(item => item.Visibility == PaneVisibilityKind.Visible) == 4);
        AppTestHost.Check(layout.Slots.Count(item => item.BindHost) == 4);
        var waiting = layout.Slots.Single(item => item.Visibility == PaneVisibilityKind.WaitingForCapacity);
        AppTestHost.Check(waiting.Pane.PaneId == "p5");
        AppTestHost.Check(!waiting.BindHost);
        AppTestHost.Check(waiting.StatusText == ShellStrings.WaitingForCapacity);
        AppTestHost.Check(layout.Visibility.VisibleCount == 4);
        AppTestHost.Check(layout.Visibility.StatusText(waiting.Pane) == ShellStrings.WaitingForCapacity);
        AppTestHost.Check(host.Observed.Count == 4);
        AppTestHost.Check(!host.Observed.Any(item => item.PaneId == "p5"));
        AppTestHost.Check(!host.MarkedReady);
    }

    static void SelectTabNoControl()
    {
        var snapshot = AppTestHost.TwoTabSplitSnapshot();
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
            catalog);
        shell.StartAsync().AsTask().GetAwaiter().GetResult();
        shell.ExpandAll();
        var workspace = shell.VisibleItems.First(item => item.Kind == NavigationKind.Workspace);
        shell.Select(workspace);
        AppTestHost.Check(shell.Route == ShellRoute.Pane);
        AppTestHost.Check(shell.ShowsWorkbench);
        AppTestHost.Check(shell.Workbench.Tabs.Count == 2);
        AppTestHost.Check(shell.Workbench.SelectedTabId == "t1");
        AppTestHost.Check(shell.Workbench.Slots.Count == 2);
        AppTestHost.Check(!shell.ControlVerified);
        foreach (var slot in shell.Workbench.Slots)
            AppTestHost.Check(!catalog.RendererReadyFor(slot.Pane));
        shell.SelectTab("t2");
        AppTestHost.Check(shell.Workbench.SelectedTabId == "t2");
        AppTestHost.Check(shell.Workbench.Slots.Count == 1);
        AppTestHost.Check(shell.Workbench.Slots[0].Pane.PaneId == "p3");
        AppTestHost.Check(!shell.ControlVerified);
        AppTestHost.Check(shell.Access != TerminalAccess.Controlling);
        AppTestHost.Check(!catalog.ControlVerifiedFor(pane));
        AppTestHost.Check(!catalog.ControlVerifiedFor(shell.Workbench.Slots[0].Pane));
        AppTestHost.Check(!catalog.RendererReadyFor(shell.Workbench.Slots[0].Pane));
        AppTestHost.Check(!catalog.RendererReadyDefault);
    }

    static void BeginObserveNoReady()
    {
        var host = new NoOpPaneVisibilityHost();
        var pane = new PaneKey(AppTestHost.SessionOf(AppTestHost.DeviceA), "ws", "p1");
        var epoch = new ConnectionEpoch(3);
        AppTestHost.Check(host.BeginObserve(pane, epoch) == epoch);
        AppTestHost.Check(!host.InputReplayed);
        AppTestHost.Check(!host.ControlRestored);
        var recorded = new RecordingPaneHost();
        var layout = new WorkbenchLayout(
            new PaneVisibilityCoordinator(new ConnectionAdmissionPolicy(ResourceBudgets.Product), recorded));
        var snapshot = AppTestHost.TwoTabSplitSnapshot();
        var session = snapshot.Devices[0].Sessions[0];
        layout.Project(session, "ws");
        AppTestHost.Check(recorded.Observed.Count == 2);
        AppTestHost.Check(!recorded.MarkedReady);
        AppTestHost.Check(!recorded.InputReplayed);
        AppTestHost.Check(!recorded.ControlRestored);
        var catalog = new ProjectionCatalog { Snapshot = snapshot };
        foreach (var slot in layout.Slots)
            AppTestHost.Check(!catalog.RendererReadyFor(slot.Pane));
        AppTestHost.Check(layout.Slots.All(item => item.BindHost));
        AppTestHost.Check(layout.Slots.All(item => !item.NeedsPlaceholder));
    }

    static void UniqueFocusedSlot()
    {
        var snapshot = AppTestHost.TwoTabSplitSnapshot();
        var session = snapshot.Devices[0].Sessions[0];
        var layout = WorkbenchLayout.CreateProduct();
        layout.Project(session, "ws", new PaneKey(session.Session, "ws", "p2"));
        AppTestHost.Check(layout.Slots.Count(item => item.Focused) == 1);
        AppTestHost.Check(layout.Slots.Single(item => item.Focused).Pane.PaneId == "p2");
        layout.Project(session, "ws");
        AppTestHost.Check(layout.Tabs[0].Selected);
        AppTestHost.Check(layout.Slots.Count(item => item.Focused) == 1);
        AppTestHost.Check(layout.Slots.Single(item => item.Focused).Pane.PaneId == "p1");
    }

    static void ZoomedOneSlot()
    {
        var session = AppTestHost.SessionOf(AppTestHost.DeviceA);
        var workspace = AppTestHost.Workspace(session, "ws", "lab", 2);
        var state = AppTestHost.SessionState(
            session,
            [workspace],
            [
                AppTestHost.Pane(new PaneKey(session, "ws", "p1"), "a", tabId: "t1"),
                AppTestHost.Pane(new PaneKey(session, "ws", "p2"), "b", tabId: "t1", focused: true)
            ],
            [AppTestHost.Tab(session, "t1", "ws", 1, "main", true, 2)],
            [
                AppTestHost.Layout(
                    session, "ws", "t1", true, "p2",
                    AppTestHost.LayoutPane("p1", false, 0, 0, 40, 24),
                    AppTestHost.LayoutPane("p2", true, 40, 0, 40, 24))
            ]);
        var layout = WorkbenchLayout.CreateProduct();
        layout.Project(state, "ws");
        AppTestHost.Check(layout.Zoomed);
        AppTestHost.Check(layout.Slots.Count == 1);
        AppTestHost.Check(layout.Slots[0].Pane.PaneId == "p2");
        AppTestHost.Check(layout.Slots[0].Focused);
        AppTestHost.Check(layout.Slots[0].Rect.Width == 1);
        AppTestHost.Check(layout.Slots[0].Rect.Height == 1);
        AppTestHost.Check(layout.Visibility.VisibleCount == 1);
    }

    static void TabSwitchNewEpoch()
    {
        var snapshot = AppTestHost.TwoTabSplitSnapshot();
        var session = snapshot.Devices[0].Sessions[0];
        var host = new RecordingPaneHost();
        var layout = new WorkbenchLayout(
            new PaneVisibilityCoordinator(new ConnectionAdmissionPolicy(ResourceBudgets.Product), host));
        layout.Project(session, "ws");
        var first = layout.Slots[0].Epoch;
        AppTestHost.Check(first.Value >= 1);
        AppTestHost.Check(host.Observed.Count == 2);
        var left = layout.Slots.Select(item => item.Pane).ToArray();
        layout.SelectTab("t2");
        AppTestHost.Check(layout.SelectedTabId == "t2");
        AppTestHost.Check(layout.Slots.Count == 1);
        AppTestHost.Check(layout.Slots[0].Pane.PaneId == "p3");
        foreach (var pane in left)
            AppTestHost.Check(layout.Visibility.VisibilityOf(pane) == PaneVisibilityKind.Hidden);
        AppTestHost.Check(host.Hidden.Count >= 2);
        var secondEpoch = layout.Slots[0].Epoch;
        layout.SelectTab("t1");
        AppTestHost.Check(layout.Slots.Count == 2);
        AppTestHost.Check(layout.Slots[0].Epoch.Value > first.Value);
        AppTestHost.Check(layout.Slots[0].Epoch.Value >= secondEpoch.Value);
        AppTestHost.Check(!layout.Visibility.InputReplayed);
        AppTestHost.Check(!layout.Visibility.ControlRestored);
        AppTestHost.Check(!host.MarkedReady);
    }

    static void NoLayoutFallback()
    {
        var snapshot = AppTestHost.TwoNamedPanes();
        var session = snapshot.Devices[0].Sessions[0];
        var layout = WorkbenchLayout.CreateProduct();
        layout.Project(session, "ws", session.Panes[0].Key, _ => true);
        AppTestHost.Check(!layout.HasLayout);
        AppTestHost.Check(layout.Slots.Count == 1);
        AppTestHost.Check(layout.Slots[0].BindHost);
        AppTestHost.Check(!layout.Slots[0].NeedsPlaceholder);
        AppTestHost.Check(layout.Slots[0].Pane == session.Panes[0].Key);
    }

    sealed class RecordingPaneHost : IPaneVisibilityHost
    {
        public List<PaneKey> Observed { get; } = [];
        public List<PaneKey> Hidden { get; } = [];
        public bool MarkedReady => false;
        public bool InputReplayed => false;
        public bool ControlRestored => false;

        public void SetReadOnly(PaneKey pane, bool readOnly) => _ = (pane, readOnly);

        public void CancelTerminal(PaneKey pane) => Hidden.Add(pane);

        public void DestroyRenderer(PaneKey pane) => _ = pane;

        public bool TryKeepRpcPair(SessionKey session)
        {
            _ = session;
            return false;
        }

        public ConnectionEpoch BeginObserve(PaneKey pane, ConnectionEpoch epoch)
        {
            Observed.Add(pane);
            return epoch;
        }
    }
}
