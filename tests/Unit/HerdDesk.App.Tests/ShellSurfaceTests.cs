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
        var shellXaml = File.ReadAllText(Path.Combine(app, "Views", "ShellPage.xaml"));
        AppTestHost.Check(CountNamed(shellXaml, "<controls:TerminalHost") == 4);
        AppTestHost.Check(!shellXaml.Contains("x:Name=\"TerminalSlot4\"", StringComparison.Ordinal));
        AppTestHost.Check(shellXaml.Contains("x:Name=\"TabStrip\"", StringComparison.Ordinal));
        AppTestHost.Check(shellXaml.Contains("x:Name=\"MosaicHost\"", StringComparison.Ordinal));
        AppTestHost.Check(shellXaml.Contains("x:Name=\"RailHost\"", StringComparison.Ordinal));
        AppTestHost.Check(shellXaml.Contains("x:Name=\"TreeHost\"", StringComparison.Ordinal));
        AppTestHost.Check(shellXaml.Contains("x:Name=\"DetailsHost\"", StringComparison.Ordinal));
        AppTestHost.Check(shellXaml.Contains("x:Name=\"EmptyBanner\"", StringComparison.Ordinal));
        AppTestHost.Check(shellXaml.Contains("x:Name=\"MosaicCanvas\"", StringComparison.Ordinal));
        var doc = XDocument.Load(Path.Combine(app, "Views", "ShellPage.xaml"));
        AppTestHost.Check(doc.Root is not null);
        var emptyBanner = doc.Descendants().FirstOrDefault(item =>
            (string?)item.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "EmptyBanner");
        AppTestHost.Check(emptyBanner?.Attribute("Grid.Row")?.Value == "0");
    }

    static int CountNamed(string xaml, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = xaml.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
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
