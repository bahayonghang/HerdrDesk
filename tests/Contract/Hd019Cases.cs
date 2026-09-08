using System.Text.Json;
using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Terminal.Web;

internal static class Hd019Cases
{
    public static (string Name, Action Run)[] All =>
    [
        ("local mvp catalog and coordinators ship without winui", CatalogAndSurface),
        ("hd-019 l2 l3 live local mvp and ac06 ac07 ac10 ac15 stay unverified", ResidualJson)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void CatalogAndSurface()
    {
        Check(typeof(DeviceSession).IsClass);
        Check(typeof(ControlLeaseCoordinator).IsClass);
        Check(typeof(RecoveryPolicy).IsAbstract);
        Check(typeof(ResourceCommandCoordinator).IsClass);
        Check(typeof(AttentionReducer).IsClass);
        Check(typeof(InputPolicy).IsAbstract);
        Check(typeof(ShellViewModel).IsClass);
        Check(typeof(TerminalInputController).IsClass);
        Check(typeof(AppExitCoordinator).IsClass);
        Check(!Enum.GetNames<KnownAgentKind>().Contains("Muse"));
        Check(VerifiedAgentWires.Wire(KnownAgentKind.Claude) == "claude");
        Check(VerifiedAgentWires.Wire(KnownAgentKind.Codex) == "codex");
        Check(VerifiedAgentWires.Wire(KnownAgentKind.OpenCode) == "opencode");
        Check(AgentInputProfiles.Resolve("muse").IsUnknown);
        Check(!AgentInputProfiles.Resolve("claude-code").LiveVerified);
        var root = FindRepoRoot();
        Check(!Directory.Exists(Path.Combine(root, "tests", "Integration.Windows")));
        Check(!File.Exists(Path.Combine(root, "tests", "Integration.Windows",
            "HerdDesk.Integration.Windows.csproj")));
        foreach (var rel in new[]
                 {
                     Path.Combine("src", "HerdDesk.App", "HerdDesk.App.csproj"),
                     Path.Combine("src", "HerdDesk.Core", "HerdDesk.Core.csproj"),
                     Path.Combine("src", "HerdDesk.Terminal.Web", "HerdDesk.Terminal.Web.csproj")
                 })
        {
            var csproj = File.ReadAllText(Path.Combine(root, rel));
            Check(!csproj.Contains("PackageReference", StringComparison.OrdinalIgnoreCase));
            Check(!csproj.Contains("WebView2", StringComparison.OrdinalIgnoreCase));
            Check(!csproj.Contains("WindowsAppSDK", StringComparison.OrdinalIgnoreCase));
        }

        var catalogPath = Path.Combine(root, "evidence", "local-mvp", "catalog.json");
        using var document = JsonDocument.Parse(File.ReadAllText(catalogPath));
        var catalog = document.RootElement;
        Check(catalog.GetProperty("document_kind").GetString() == "hd019_local_mvp_catalog");
        Check(catalog.GetProperty("ac06_passed").GetBoolean() is false);
        Check(catalog.GetProperty("ac07_passed").GetBoolean() is false);
        Check(catalog.GetProperty("ac10_passed").GetBoolean() is false);
        Check(catalog.GetProperty("ac15_passed").GetBoolean() is false);
        Check(catalog.GetProperty("g0_passed").GetBoolean() is false);
        Check(catalog.GetProperty("live_herdr").GetBoolean() is false);
        Check(catalog.GetProperty("integration_windows_project").GetBoolean() is false);
        Check(catalog.GetProperty("missing").GetProperty("disposable_pane").GetBoolean());
        Check(catalog.GetProperty("missing").GetProperty("webview2").GetBoolean());
        Check(catalog.GetProperty("missing").GetProperty("ime_desktop").GetBoolean());
        Check(catalog.GetProperty("missing").GetProperty("agent_tui_versions").GetBoolean());
        var targets = catalog.GetProperty("targets").EnumerateArray().Select(item => item.GetProperty("id").GetString())
            .ToArray();
        Check(targets.SequenceEqual(["powershell", "claude-code", "codex", "opencode"]));
        Check(!targets.Contains("muse"));
        foreach (var row in catalog.GetProperty("live_rows").EnumerateArray())
        {
            Check(row.GetProperty("status").GetString() == "UNVERIFIED");
            var evidence = row.GetProperty("required_evidence").GetString();
            Check(evidence is "L2" or "L3");
        }
    }

    static void ResidualJson()
    {
        var root = FindRepoRoot();
        using var l2 = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "implementation", "hd-019-l2.json")));
        using var l3 = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "implementation", "hd-019-l3.json")));
        Check(l2.RootElement.GetProperty("l2_live_local_mvp").GetString() == "UNVERIFIED");
        Check(l3.RootElement.GetProperty("l3_ime_desktop").GetString() == "UNVERIFIED");
        Check(l3.RootElement.GetProperty("l3_agent_tui").GetString() == "UNVERIFIED");
        Check(l3.RootElement.GetProperty("agent_tui_versions").GetString() == "UNVERIFIED");
        foreach (var doc in new[] { l2.RootElement, l3.RootElement })
        {
            Check(doc.GetProperty("ac06_passed").GetBoolean() is false);
            Check(doc.GetProperty("ac07_passed").GetBoolean() is false);
            Check(doc.GetProperty("ac10_passed").GetBoolean() is false);
            Check(doc.GetProperty("ac15_passed").GetBoolean() is false);
            Check(doc.GetProperty("g0_passed").GetBoolean() is false);
            Check(doc.GetProperty("phase_gate").GetString() != "passed");
            Check(doc.GetProperty("live_herdr").GetBoolean() is false);
            Check(doc.GetProperty("winui_admitted").GetBoolean() is false);
            Check(doc.GetProperty("webview2_admitted").GetBoolean() is false);
            Check(doc.GetProperty("integration_windows_project").GetBoolean() is false);
            Check(doc.GetProperty("missing").GetProperty("disposable_pane").GetBoolean());
            Check(doc.GetProperty("missing").GetProperty("webview2").GetBoolean());
            Check(doc.GetProperty("missing").GetProperty("ime_desktop").GetBoolean());
            Check(doc.GetProperty("missing").GetProperty("agent_tui_versions").GetBoolean());
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
