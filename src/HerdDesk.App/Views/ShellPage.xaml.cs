using HerdDesk.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace HerdDesk.App.Views;

public sealed partial class ShellPage : UserControl
{
    public ShellPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        var search = new KeyboardAccelerator
        {
            Key = VirtualKey.K,
            Modifiers = VirtualKeyModifiers.Control
        };
        search.Invoked += OnOpenSearch;
        KeyboardAccelerators.Add(search);
        DeviceRail.ItemChosen += OnRailChosen;
        OverlayRail.ItemChosen += OnRailChosen;
        DeviceRail.AddDeviceRequested += OnRailAddDevice;
        DeviceRail.DiagnosticsRequested += OnRailDiagnostics;
        OverlayRail.AddDeviceRequested += OnRailAddDevice;
        OverlayRail.DiagnosticsRequested += OnRailDiagnostics;
        PaneTree.ItemChosen += OnTreeChosen;
        PaneTree.AddDeviceRequested += OnRailAddDevice;
        PaneTree.DiagnosticsRequested += OnRailDiagnostics;
        SearchControl.Activated += OnSearchActivated;
        ControlBar.Changed += OnControlBarChanged;
    }

    public ShellViewModel? Shell { get; private set; }

    public void Bind(ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        Shell = shell;
        SearchControl.Bind(shell);
        Refresh();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        _ = (sender, args);
        Refresh();
    }

    public void Refresh()
    {
        if (Shell is null)
            return;
        TitleText.Text = Shell.TitleSummary;
        BreadcrumbText.Text = Shell.Breadcrumb;
        StatusText.Text = Shell.StatusLine;
        var selected = Shell.Selection.Kind == SelectionKind.None
            ? null
            : Shell.VisibleItems.FirstOrDefault(item =>
                item.Kind == ToKind(Shell.Selection.Kind) &&
                item.Device == Shell.Selection.Device &&
                item.Session == Shell.Selection.Session &&
                item.WorkspaceId == Shell.Selection.WorkspaceId &&
                item.Pane == Shell.Selection.Pane)?.IdentityKey;
        DeviceRail.SetItems(
            Shell.VisibleItems.Where(item =>
                item.Kind is NavigationKind.Device or NavigationKind.Session),
            selected);
        OverlayRail.SetItems(
            Shell.VisibleItems.Where(item =>
                item.Kind is NavigationKind.Device or NavigationKind.Session),
            selected);
        var treeSelected = TreeSelectedKey(Shell, selected);
        PaneTree.SetItems(
            Shell.VisibleItems.Where(item => item.Kind == NavigationKind.Workspace),
            treeSelected);
        SearchHost.Visibility = Shell.Search.IsOpen ? Visibility.Visible : Visibility.Collapsed;
        if (Shell.Search.IsOpen)
        {
            SearchControl.Bind(Shell);
            SearchControl.FocusQuery();
        }

        ApplyLayout();
        ShowRoute();
        DetailsText.Text = Shell.Breadcrumb + " · " + Shell.AccessLabel;
        DetailsToggle.Content = Shell.Details == DetailsPaneKind.Collapsed
            ? ShellStrings.ExpandDetails
            : ShellStrings.CollapseDetails;
        AddDeviceButton.Content = Shell.AddDeviceLabel;
        WelcomeText.Text = WelcomeMessage(Shell);
        var paneSelected = Shell.Selection.Kind == SelectionKind.Pane && !Shell.Selection.IsExpired;
        ControlBar.Bind(Shell.TerminalControl, paneSelected);
        ReturnButton.Visibility = OverlayVisible(Shell.Route) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyLayout()
    {
        if (Shell is null)
            return;
        var narrow = Shell.Layout == LayoutBreakpoint.Narrow;
        var wide = Shell.Layout == LayoutBreakpoint.Wide;
        RailColumn.Width = narrow ? new GridLength(0) : new GridLength(220);
        TreeColumn.Width = narrow ? new GridLength(0) : new GridLength(240);
        DetailsColumn.Width = wide && Shell.Details != DetailsPaneKind.Collapsed
            ? new GridLength(280)
            : new GridLength(0);
        NavOverlayButton.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
        NavOverlay.Visibility = narrow && Shell.NavigationOverlayOpen
            ? Visibility.Visible
            : Visibility.Collapsed;
        DeviceRail.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        PaneTree.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        DetailsHost.Visibility = wide && Shell.Details != DetailsPaneKind.Collapsed
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ShowRoute()
    {
        if (Shell is null)
            return;
        EmptyBanner.Visibility = Shell.Route == ShellRoute.Welcome ? Visibility.Visible : Visibility.Collapsed;
        TerminalPlaceholder.Visibility = Shell.Route == ShellRoute.Pane
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (Shell.Route != ShellRoute.Pane)
            TerminalSurface.SetVisible(false);
        SettingsHost.Visibility = Visibility.Collapsed;
        DiagnosticsHost.Visibility = Visibility.Collapsed;
        AboutHost.Visibility = Visibility.Collapsed;
        switch (Shell.Route)
        {
            case ShellRoute.Settings:
                SettingsHost.Visibility = Visibility.Visible;
                SettingsHost.Bind(Shell);
                break;
            case ShellRoute.Diagnostics:
                DiagnosticsHost.Visibility = Visibility.Visible;
                DiagnosticsHost.Bind(Shell);
                break;
            case ShellRoute.About:
                AboutHost.Visibility = Visibility.Visible;
                break;
            case ShellRoute.Pane:
                TerminalSurface.SetVisible(true);
                BindTerminal();
                break;
        }
    }

    private void BindTerminal()
    {
        if (Shell is null || Shell.Selection.Pane is not { } pane)
            return;
        var readOnly = Shell.Access != TerminalAccess.Controlling || !Shell.ControlVerified;
        TerminalSurface.Bind(pane, Shell.Selection.Epoch, readOnly, Shell.Display.Preview);
    }

    private static string WelcomeMessage(ShellViewModel shell) =>
        shell.Lifecycle switch
        {
            ShellLifecycle.Starting => ShellStrings.Starting,
            ShellLifecycle.NoDevices => ShellStrings.NoDevices,
            ShellLifecycle.DaemonUnavailable => ShellStrings.DaemonUnavailable,
            ShellLifecycle.Failed => shell.ErrorCategory ?? ShellStrings.Failed,
            _ => ProductInfo.Name
        };

    private static bool OverlayVisible(ShellRoute route) =>
        route is ShellRoute.Settings or ShellRoute.Diagnostics or ShellRoute.About;

    private static string? TreeSelectedKey(ShellViewModel shell, string? selected)
    {
        if (shell.Selection.Kind != SelectionKind.Pane ||
            shell.Selection.Device is not { } device ||
            shell.Selection.Session is not { } session ||
            string.IsNullOrWhiteSpace(shell.Selection.WorkspaceId))
            return selected;
        return NavigationIdentity.Format(
            NavigationKind.Workspace, device, session, shell.Selection.WorkspaceId, null);
    }

    private static NavigationKind ToKind(SelectionKind kind) => kind switch
    {
        SelectionKind.Device => NavigationKind.Device,
        SelectionKind.Session => NavigationKind.Session,
        SelectionKind.Workspace => NavigationKind.Workspace,
        SelectionKind.Pane => NavigationKind.Pane,
        _ => NavigationKind.Device
    };

    private void OnRailChosen(object? sender, NavigationItem item)
    {
        _ = sender;
        if (Shell is null)
            return;
        Shell.Select(item);
        if (item.Kind is NavigationKind.Device or NavigationKind.Session)
            Shell.ToggleExpanded(item);
        Refresh();
    }

    private void OnTreeChosen(object? sender, NavigationItem item)
    {
        _ = sender;
        if (Shell is null)
            return;
        Shell.Select(item);
        Refresh();
    }

    private void OnOpenSearch(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        _ = sender;
        if (Shell is null)
        {
            args.Handled = false;
            return;
        }

        if (TerminalSurface.Session.IsComposing)
        {
            args.Handled = true;
            return;
        }

        args.Handled = Shell.HandleAccelerator(ShellAccelerator.OpenSearch);
        Refresh();
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs args)
    {
        _ = sender;
        if (Shell is null)
            return;
        if (args.Key == VirtualKey.F6)
        {
            Shell.CycleRegion();
            args.Handled = true;
            Refresh();
            return;
        }

        if (args.Key == VirtualKey.Escape && TerminalSurface.Session.IsComposing)
        {
            args.Handled = true;
            return;
        }

        if (args.Key == VirtualKey.Escape && Shell.Search.IsOpen)
        {
            Shell.CloseSearch();
            args.Handled = true;
            Refresh();
        }
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs args)
    {
        _ = sender;
        if (Shell is null)
            return;
        Shell.SetWidth((int)args.NewSize.Width);
        Refresh();
    }

    private void OnToggleNav(object sender, RoutedEventArgs args)
    {
        _ = (sender, args);
        Shell?.ToggleNavigationOverlay();
        Refresh();
    }

    private void OnOpenSettings(object sender, RoutedEventArgs args)
    {
        _ = (sender, args);
        Shell?.OpenSettings();
        Refresh();
    }

    private void OnOpenDiagnostics(object sender, RoutedEventArgs args)
    {
        _ = (sender, args);
        Shell?.OpenDiagnostics();
        Refresh();
    }

    private void OnOpenAbout(object sender, RoutedEventArgs args)
    {
        _ = (sender, args);
        Shell?.OpenAbout();
        Refresh();
    }

    private void OnReturnToWorkbench(object sender, RoutedEventArgs args)
    {
        _ = (sender, args);
        Shell?.ReturnToWorkbench();
        Refresh();
    }

    private void OnControlBarChanged(object? sender, EventArgs args)
    {
        _ = (sender, args);
        Refresh();
    }

    private void OnRailAddDevice(object? sender, EventArgs args)
    {
        _ = sender;
        _ = args;
        Shell?.RequestAddDevice();
        Refresh();
    }

    private void OnRailDiagnostics(object? sender, EventArgs args)
    {
        _ = (sender, args);
        Shell?.OpenDiagnostics();
        Refresh();
    }

    private void OnToggleDetails(object sender, RoutedEventArgs args)
    {
        _ = (sender, args);
        Shell?.ToggleDetails();
        Refresh();
    }

    private void OnAddDevice(object sender, RoutedEventArgs args)
    {
        _ = (sender, args);
        Shell?.RequestAddDevice();
        Refresh();
    }

    private void OnSearchActivated(object? sender, EventArgs args)
    {
        _ = (sender, args);
        Refresh();
    }
}
