using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Configuration;
using HerdDesk.Terminal.Web;

internal static class TerminalInputTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("terminal composition blocks ctrl+k", CompositionBlocksSearch),
        ("search and notification wait for renderer ready", FocusWaitsReady),
        ("status strip stays explainable and request does not grant", StatusAndLease),
        ("paste and observe resize stay on the view model", PasteAndResize)
    ];

    static void CompositionBlocksSearch()
    {
        var snapshot = AppTestHost.TwoNamedPanes();
        var pane = snapshot.Devices[0].Sessions[0].Panes[0].Key;
        var catalog = new ProjectionCatalog { Snapshot = snapshot, DaemonAvailable = true, RendererReadyDefault = true };
        var controller = new TerminalInputController(
            new InputContext(pane, snapshot.Epoch, TerminalAccess.Controlling, true),
            CompositionPolicy.Evaluate, InputPolicy.Evaluate);
        controller.Bind(pane, snapshot.Epoch);
        controller.SetReadOnly(false);
        controller.SetRendererReady(true);
        var input = new TerminalInputViewModel(controller);
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
            input: input);
        shell.StartAsync().AsTask().GetAwaiter().GetResult();
        input.StartComposition();
        AppTestHost.Check(input.IsComposing);
        AppTestHost.Check(!shell.HandleAccelerator(ShellAccelerator.OpenSearch));
        AppTestHost.Check(!shell.Search.IsOpen);
        var token = controller.ActiveCommitToken!;
        AppTestHost.Check(input.Commit(token, "词").Allowed);
        AppTestHost.Check(shell.HandleAccelerator(ShellAccelerator.OpenSearch));
        AppTestHost.Check(shell.Search.IsOpen);
    }

    static void FocusWaitsReady()
    {
        var snapshot = AppTestHost.TwoNamedPanes();
        var paneA = snapshot.Devices[0].Sessions[0].Panes[0].Key;
        var paneB = snapshot.Devices[1].Sessions[0].Panes[0].Key;
        var catalog = new ProjectionCatalog { Snapshot = snapshot, DaemonAvailable = true };
        catalog.SetRendererReady(paneA, false);
        catalog.SetRendererReady(paneB, false);
        var shell = AppTestHost.Shell(
            new MemoryDeviceProfileStore
            {
                Snapshot = new ConfigurationSnapshot(
                    1, 1,
                    [
                        new DeviceProfile(
                            AppTestHost.DeviceA, "lab", ConnectionKinds.Local, "/tmp/herdr",
                            [SessionProfile.Named("dev")]),
                        new DeviceProfile(
                            AppTestHost.DeviceB, "lab", ConnectionKinds.Local, "/tmp/herdr",
                            [SessionProfile.Named("dev")])
                    ])
            },
            catalog);
        shell.StartAsync().AsTask().GetAwaiter().GetResult();
        shell.ExpandAll();
        var itemA = shell.VisibleItems.First(item => item.Pane == paneA);
        shell.Select(itemA);
        AppTestHost.Check(!shell.ContentFocused);
        AppTestHost.Check(shell.FocusRestore.State == HostFocusState.FocusRequested);
        AppTestHost.Check(!shell.FocusRestore.ControlVerified);
        catalog.SetRendererReady(paneA, true);
        shell.RefreshFromCatalog();
        AppTestHost.Check(shell.ContentFocused);
        AppTestHost.Check(shell.FocusRestore.FocusedPane == paneA);
        AppTestHost.Check(!shell.FocusRestore.ControlVerified);
        catalog.SetRendererReady(paneA, false);
        catalog.SetRendererReady(paneB, false);
        var itemB = shell.VisibleItems.First(item => item.Pane == paneB);
        shell.Select(itemB);
        catalog.SetRendererReady(paneA, true);
        shell.RefreshFromCatalog();
        AppTestHost.Check(shell.FocusRestore.FocusedPane != paneA || !shell.ContentFocused);
        catalog.SetRendererReady(paneB, true);
        shell.RefreshFromCatalog();
        AppTestHost.Check(shell.ContentFocused);
        AppTestHost.Check(shell.FocusRestore.FocusedPane == paneB);
        AppTestHost.Check(!shell.ControlVerified);
        shell.FocusRestore.Blur();
        AppTestHost.Check(!shell.FocusRestore.ContentFocused);
        AppTestHost.Check(!shell.FocusRestore.ControlVerified);
    }

    static void StatusAndLease()
    {
        var pane = AppTestHost.TwoNamedPanes().Devices[0].Sessions[0].Panes[0].Key;
        var controller = new TerminalInputController(
            new InputContext(pane, new ConnectionEpoch(1), TerminalAccess.Observing, false),
            CompositionPolicy.Evaluate, InputPolicy.Evaluate);
        controller.Bind(pane, new ConnectionEpoch(1));
        var vm = new TerminalInputViewModel(controller);
        AppTestHost.Check(vm.AccessStrip == ShellStrings.Observing);
        AppTestHost.Check(vm.CopyAutomationName == "复制");
        AppTestHost.Check(vm.RequestControlAutomationName == "申请控制");
        var request = vm.RequestControlFromScreenReader();
        AppTestHost.Check(!request.Allowed);
        AppTestHost.Check(!vm.ControlVerified);
        AppTestHost.Check(controller.Context.Access == TerminalAccess.Observing);
        controller.SetInputContext(new InputContext(pane, new ConnectionEpoch(1), TerminalAccess.Acquiring, false));
        AppTestHost.Check(vm.AccessStrip == ShellStrings.AcquiringControl);
        controller.SetInputContext(new InputContext(pane, new ConnectionEpoch(1), TerminalAccess.Controlling, true));
        AppTestHost.Check(vm.AccessStrip == ShellStrings.Controlling);
        controller.StartComposition();
        AppTestHost.Check(vm.AccessStrip == ShellStrings.InputPaused);
        controller.Suspend("stale_epoch");
        AppTestHost.Check(vm.AccessStrip == ShellStrings.ConnectionExpired);
    }

    static void PasteAndResize()
    {
        var snapshot = AppTestHost.TwoNamedPanes();
        var pane = snapshot.Devices[0].Sessions[0].Panes[0].Key;
        var controller = new TerminalInputController(
            new InputContext(pane, snapshot.Epoch, TerminalAccess.Observing, false),
            CompositionPolicy.Evaluate, InputPolicy.Evaluate);
        controller.Bind(pane, snapshot.Epoch);
        controller.SetReadOnly(true);
        var vm = new TerminalInputViewModel(controller);
        var paste = vm.Paste("ab\ncd");
        AppTestHost.Check(!paste.Allowed);
        AppTestHost.Check(controller.TransportByteCount == 0);
        for (var i = 0; i < 20; i++)
            controller.ApplyLocalDisplay("Cascadia Mono", 12, 100);
        AppTestHost.Check(!controller.TryRequestResize(80, 24));
        AppTestHost.Check(controller.UpstreamResizeCount == 0);
        AppTestHost.Check(vm.RetryFocus().Code == "renderer_not_ready");
    }
}
