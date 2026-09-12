namespace HerdDesk.App.Quality;

public static class AccessibilityNameCatalog
{
    public static IReadOnlyList<string> ShellAutomationNames => ShellSurface.AutomationNames;

    public static IReadOnlyList<string> KeyboardAndNarratorNames { get; } =
    [
        ShellStrings.DeviceRail,
        ShellStrings.WorkspaceTree,
        ShellStrings.WorkspaceHeader,
        ShellStrings.TerminalHost,
        ShellStrings.TerminalPlaceholder,
        ShellStrings.TabStrip,
        ShellStrings.MosaicHost,
        ShellStrings.ControlBar,
        ShellStrings.Connect,
        ShellStrings.ConnectStatus,
        ShellStrings.ReturnToWorkbench,
        ShellStrings.EmptyBanner,
        ShellStrings.Details,
        ShellStrings.Search,
        ShellStrings.ConnectionStatus,
        ShellStrings.Settings,
        ShellStrings.Diagnostics,
        ShellStrings.About,
        ShellStrings.AddDevice,
        ShellStrings.OpenNavigation,
        ShellStrings.ExpandDetails,
        ShellStrings.RequestControl,
        ShellStrings.TakeOver,
        ShellStrings.ReleaseControl,
        ShellStrings.ConfirmClose,
        ShellStrings.Loading,
        ShellStrings.Empty,
        ShellStrings.Error,
        ShellStrings.Offline,
        ShellStrings.Expired,
        ShellStrings.PermissionDenied,
        ShellStrings.Stale,
        ShellStrings.NoDevices,
        ShellStrings.EmptyWorkspace
    ];

    public static string JoinedXaml(string repoRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        var app = Path.Combine(repoRoot, "src", "HerdDesk.App");
        var files = Directory.EnumerateFiles(app, "*.xaml", SearchOption.AllDirectories)
            .Where(path =>
            {
                var relative = Path.GetRelativePath(app, path).Replace('\\', '/');
                return relative.Split('/').All(part =>
                    !part.Equals("bin", StringComparison.OrdinalIgnoreCase) &&
                    !part.Equals("obj", StringComparison.OrdinalIgnoreCase));
            })
            .OrderBy(item => item, StringComparer.Ordinal);
        return string.Join('\n', files.Select(File.ReadAllText));
    }

    public static bool XamlDeclares(string repoRoot, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return JoinedXaml(repoRoot).Contains(
            "AutomationProperties.Name=\"" + name + "\"", StringComparison.Ordinal);
    }
}
