internal static class Hd007Cases
{
    public static (string Name, Action Run)[] All =>
    [
        ("app windows tfm admits winui lock; net10.0 assembly stays bcl", AppLockShape),
        ("core contracts infrastructure and terminal.web stay without packagereference", BclProjects)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void AppLockShape()
    {
        var root = FindRepoRoot();
        AppXamlSurface.CheckBlankContainerOnly(root);
        var csproj = File.ReadAllText(Path.Combine(root, "src", "HerdDesk.App", "HerdDesk.App.csproj"));
        Check(csproj.Contains("Microsoft.WindowsAppSDK.WinUI", StringComparison.Ordinal));
        Check(csproj.Contains("net10.0-windows10.0.19041.0", StringComparison.Ordinal));
        Check(csproj.Contains("WindowsPackageType", StringComparison.Ordinal));
        Check(csproj.Contains("None", StringComparison.Ordinal));
        Check(!csproj.Contains("Include=\"Microsoft.WindowsAppSDK\"", StringComparison.Ordinal));
        Check(!csproj.Contains("2.4.0", StringComparison.Ordinal));
        Check(File.Exists(Path.Combine(root, "src", "HerdDesk.App", "packages.lock.json")));
        Check(File.Exists(Path.Combine(root, "Directory.Packages.props")));
        var assembly = typeof(HerdDesk.App.ProductInfo).Assembly;
        foreach (var name in assembly.GetReferencedAssemblies().Select(item => item.Name!))
        {
            var lower = name.ToLowerInvariant();
            Check(!lower.Contains("windowsappsdk"));
            Check(!lower.Contains("webview2"));
            Check(!lower.Contains("winui"));
            Check(!lower.Contains("ssh"));
        }
    }

    static void BclProjects()
    {
        var root = FindRepoRoot();
        foreach (var rel in new[]
                 {
                     Path.Combine("src", "HerdDesk.Contracts", "HerdDesk.Contracts.csproj"),
                     Path.Combine("src", "HerdDesk.Core", "HerdDesk.Core.csproj"),
                     Path.Combine("src", "HerdDesk.Infrastructure", "HerdDesk.Infrastructure.csproj"),
                     Path.Combine("src", "HerdDesk.Terminal.Web", "HerdDesk.Terminal.Web.csproj"),
                     Path.Combine("tests", "Unit", "HerdDesk.App.Tests", "HerdDesk.App.Tests.csproj")
                 })
        {
            var csproj = File.ReadAllText(Path.Combine(root, rel));
            Check(!csproj.Contains("PackageReference", StringComparison.OrdinalIgnoreCase));
            Check(!csproj.Contains("WindowsAppSDK", StringComparison.OrdinalIgnoreCase));
            Check(!csproj.Contains("WebView2", StringComparison.OrdinalIgnoreCase));
        }
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
