internal static class AppXamlSurface
{
    internal static readonly string[] BlankContainer =
        ["App.xaml", "MainWindow.xaml"];

    internal static void CheckBlankContainerOnly(string repoRoot)
    {
        var app = Path.Combine(repoRoot, "src", "HerdDesk.App");
        var xaml = Directory.EnumerateFiles(app, "*.xaml", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(app, path).Replace('\\', '/'))
            .Where(rel => rel.Split('/').All(part =>
                !part.Equals("bin", StringComparison.OrdinalIgnoreCase) &&
                !part.Equals("obj", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        if (!xaml.SequenceEqual(BlankContainer))
            throw new Exception("assertion_failed");
        if (File.Exists(Path.Combine(app, "Shell.xaml")))
            throw new Exception("assertion_failed");
        if (File.Exists(Path.Combine(app, "Devices", "EditDevicePage.xaml")))
            throw new Exception("assertion_failed");
        if (File.Exists(Path.Combine(app, "Devices", "HelperInstallDialog.xaml")))
            throw new Exception("assertion_failed");
    }
}
