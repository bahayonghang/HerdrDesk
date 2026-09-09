using System.Xml.Linq;
using HerdDesk.App;
using HerdDesk.Contracts;

internal static class ShellSurfaceTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("shell xaml names four zones and automation", XamlNames),
        ("selection does not grant control", SelectionNoControl)
    ];

    static void XamlNames()
    {
        var root = FindRepoRoot();
        var app = Path.Combine(root, "src", "HerdDesk.App");
        var found = Directory.EnumerateFiles(app, "*.xaml", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(app, path).Replace('\\', '/'))
            .Where(rel => rel.Split('/').All(part =>
                !part.Equals("bin", StringComparison.OrdinalIgnoreCase) &&
                !part.Equals("obj", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        AppTestHost.Check(found.SequenceEqual(ShellSurface.Pages));
        var joined = string.Join('\n', found.Select(rel => File.ReadAllText(Path.Combine(app, rel))));
        foreach (var name in ShellSurface.AutomationNames)
            AppTestHost.Check(joined.Contains("AutomationProperties.Name=\"" + name + "\"", StringComparison.Ordinal));
        AppTestHost.Check(joined.Contains("x:Class=\"HerdDesk.App.MainWindow\"", StringComparison.Ordinal));
        AppTestHost.Check(!joined.Contains("EditDevicePage", StringComparison.Ordinal));
        AppTestHost.Check(!File.Exists(Path.Combine(app, "Devices", "EditDevicePage.xaml")));
        AppTestHost.Check(!File.Exists(Path.Combine(app, "Files", "FileWorkspaceView.xaml")));
        var doc = XDocument.Load(Path.Combine(app, "Views", "ShellPage.xaml"));
        AppTestHost.Check(doc.Root is not null);
    }

    static void SelectionNoControl()
    {
        var catalog = new ProjectionCatalog
        {
            Snapshot = AppTestHost.TwoNamedPanes(),
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
        shell.ExpandAll();
        var pane = shell.VisibleItems.First(item => item.Kind == NavigationKind.Pane);
        AppTestHost.Check(!pane.ControlVerified);
        shell.Select(pane);
        AppTestHost.Check(shell.Selection.Pane == pane.Pane);
        AppTestHost.Check(!shell.ControlVerified);
        AppTestHost.Check(shell.HiddenTerminalBridgeCount == 0);
        AppTestHost.Check(shell.SshAvailability.Kind == RouteAvailabilityKind.Disabled);
    }

    static string FindRepoRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "HerdDesk.slnx")))
                return directory;
            directory = Directory.GetParent(directory)?.FullName ?? "";
        }

        throw new Exception("repo_root_missing");
    }
}
