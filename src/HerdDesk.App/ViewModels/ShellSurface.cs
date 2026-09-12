namespace HerdDesk.App;

public static class ShellSurface
{
    public const string WindowTypeName = "MainWindow";
    public const string WindowTypeFullName = "HerdDesk.App.MainWindow";

    public static readonly string[] Pages =
    [
        "App.xaml",
        "Controls/ControlBar.xaml",
        "Controls/DeviceSessionRail.xaml",
        "Controls/SearchPalette.xaml",
        "Controls/TerminalHost.xaml",
        "Controls/WorkspacePaneTree.xaml",
        "MainWindow.xaml",
        "Views/AboutPage.xaml",
        "Views/DiagnosticsPage.xaml",
        "Views/SettingsPage.xaml",
        "Views/ShellPage.xaml"
    ];

    public static readonly string[] AutomationNames =
    [
        ShellStrings.DeviceRail,
        ShellStrings.WorkspaceTree,
        ShellStrings.WorkspaceHeader,
        ShellStrings.TerminalHost,
        ShellStrings.TerminalPlaceholder,
        ShellStrings.TabStrip,
        ShellStrings.MosaicHost,
        ShellStrings.ControlBar,
        ShellStrings.RequestControl,
        ShellStrings.TakeOver,
        ShellStrings.Connect,
        ShellStrings.ConnectStatus,
        ShellStrings.ReturnToWorkbench,
        ShellStrings.EmptyBanner,
        ShellStrings.EmptyWorkspace,
        ShellStrings.NoDevices,
        ShellStrings.Details,
        ShellStrings.Search,
        ShellStrings.ConnectionStatus,
        ShellStrings.Settings,
        ShellStrings.Diagnostics,
        ShellStrings.About,
        ShellStrings.AddDevice,
        ShellStrings.OpenNavigation,
        ShellStrings.ExpandDetails
    ];
}
