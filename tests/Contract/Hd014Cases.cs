using System.Text.Json;
using HerdDesk.App.Composition;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Configuration;
using HerdDesk.Terminal.Web;

internal static class Hd014Cases
{
    public static (string Name, Action Run)[] All =>
    [
        ("terminal renderer port is compiled without webview2", RendererPortShape),
        ("production renderer factory stays unavailable", ProductionUnavailable),
        ("hd-014 l2 l3 and ac08 stay unverified", ResidualJson)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void RendererPortShape()
    {
        Check(typeof(ITerminalRenderer).IsInterface);
        Check(typeof(IRenderFlowController).IsInterface);
        Check(typeof(RenderConsumption).IsClass);
        var assembly = typeof(WebRendererHost).Assembly;
        foreach (var name in assembly.GetReferencedAssemblies().Select(item => item.Name!))
        {
            var lower = name.ToLowerInvariant();
            Check(!lower.Contains("windowsappsdk"));
            Check(!lower.Contains("webview2"));
            Check(!lower.Contains("winui"));
        }

        var csproj = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "HerdDesk.Terminal.Web",
            "HerdDesk.Terminal.Web.csproj"));
        Check(!csproj.Contains("PackageReference", StringComparison.OrdinalIgnoreCase));
        Check(!csproj.Contains("WebView2", StringComparison.OrdinalIgnoreCase));
        Check(!Directory.Exists(Path.Combine(FindRepoRoot(), "web", "terminal")));
        Check(!File.Exists(Path.Combine(FindRepoRoot(), "web", "terminal", "package-lock.json")));
        Check(WebRendererHost.PackageStatus == "UNVERIFIED");
        Check(WebRendererHost.L2WebViewProcess == "UNVERIFIED");
        Check(WebRendererHost.L3DpiThemeFocus == "UNVERIFIED");
    }

    static void ProductionUnavailable()
    {
        var root = Path.Combine(Path.GetTempPath(), "herddesk-hd014-compose-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var services = AppServices.CreateProduction(AppDataPaths.FromRoot(root));
            try
            {
                Check(!services.TerminalRenderers.Available);
                Check(services.TerminalRenderers.CreateAsync().AsTask().GetAwaiter().GetResult() is null);
                Check(services.Unavailable.Any(item => item.Name == "terminal-renderer-web"));
            }
            finally
            {
                services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void ResidualJson()
    {
        var path = Path.Combine(FindRepoRoot(), "implementation", "hd-014-l2.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Check(root.GetProperty("l2_webview_process").GetString() == "UNVERIFIED");
        Check(root.GetProperty("l3_dpi_theme_focus").GetString() == "UNVERIFIED");
        Check(root.GetProperty("webview2_admitted").GetBoolean() is false);
        Check(root.GetProperty("npm_xterm_admitted").GetBoolean() is false);
        Check(root.GetProperty("ac08_passed").GetBoolean() is false);
        Check(root.GetProperty("ac27_passed").GetBoolean() is false);
        Check(root.GetProperty("g0_passed").GetBoolean() is false);
        Check(root.GetProperty("phase_gate").GetString() != "passed");
        Check(root.GetProperty("windows_desktop_restore").GetString() == "not_admitted");
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
