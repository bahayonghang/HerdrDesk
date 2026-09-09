namespace HerdDesk.App;

public static class ShellSurface
{
    public const string WindowTypeName = "MainWindow";
    public const string WindowTypeFullName = "HerdDesk.App.MainWindow";

    public static readonly string[] Pages =
    [
        "App.xaml",
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
        ShellStrings.TerminalHost,
        ShellStrings.Details,
        ShellStrings.Search,
        ShellStrings.ConnectionStatus
    ];
}
