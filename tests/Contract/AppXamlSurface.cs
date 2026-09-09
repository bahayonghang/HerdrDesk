internal static class AppXamlSurface
{
    internal static readonly string[] ShellPages = HerdDesk.App.ShellSurface.Pages;

    internal static void CheckBlankContainerOnly(string repoRoot) => CheckShellSurface(repoRoot);

    internal static void CheckShellSurface(string repoRoot)
    {
        var app = Path.Combine(repoRoot, "src", "HerdDesk.App");
        var xaml = Directory.EnumerateFiles(app, "*.xaml", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(app, path).Replace('\\', '/'))
            .Where(rel => rel.Split('/').All(part =>
                !part.Equals("bin", StringComparison.OrdinalIgnoreCase) &&
                !part.Equals("obj", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        if (!xaml.SequenceEqual(ShellPages))
            throw new Exception("assertion_failed");
        if (File.Exists(Path.Combine(app, "Devices", "EditDevicePage.xaml")))
            throw new Exception("assertion_failed");
        if (File.Exists(Path.Combine(app, "Devices", "HelperInstallDialog.xaml")))
            throw new Exception("assertion_failed");
        var files = Path.Combine(app, "Files");
        if (Directory.Exists(files) && Directory.EnumerateFiles(files, "*.xaml").Any())
            throw new Exception("assertion_failed");
    }

    internal static void CheckIntegrationWindowsProject(string repoRoot)
    {
        var path = Path.Combine(repoRoot, "tests", "Integration.Windows",
            "HerdDesk.Integration.Windows.csproj");
        if (!File.Exists(path))
            throw new Exception("assertion_failed");
        var text = File.ReadAllText(path);
        if (text.Contains("PackageReference", StringComparison.OrdinalIgnoreCase))
            throw new Exception("assertion_failed");
        if (text.Contains("Microsoft.NET.Test.Sdk", StringComparison.Ordinal))
            throw new Exception("assertion_failed");
        if (text.Contains("Microsoft.WindowsAppSDK", StringComparison.Ordinal))
            throw new Exception("assertion_failed");
        if (text.Contains("2.4.0", StringComparison.Ordinal))
            throw new Exception("assertion_failed");
        var program = Path.Combine(repoRoot, "tests", "Integration.Windows", "Program.cs");
        var src = File.ReadAllText(program);
        if (src.Contains("Application.Start", StringComparison.Ordinal) ||
            src.Contains("new MainWindow", StringComparison.Ordinal))
            throw new Exception("assertion_failed");
    }
}
