using System.Text.Json;
using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class Hd030Cases
{
    public static (string Name, Action Run)[] All =>
    [
        ("hd-030 attachment coordinators are compiled", Surface),
        ("hd-030 l2 live agent ime ssh and acs stay unverified", ResidualJson)
    ];

    static readonly string[] PassKeys =
    [
        "ac35_passed", "ac31_passed", "ac32_passed", "ac36_passed", "g0_passed"
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void Surface()
    {
        Check(typeof(AttachmentCoordinator).IsClass);
        Check(typeof(AttachmentCapabilityCatalog).IsClass);
        Check(typeof(AttachToAgentViewModel).IsClass);
        Check(typeof(AttachmentDraft).IsClass);
        Check(typeof(AttachmentTargetLease).IsClass);
        Check(typeof(IAttachmentInputSink).IsInterface);
        Check(!Enum.GetNames<DeliveryState>().Contains("AgentAccepted"));
        Check(typeof(InputPolicy).IsClass);
        Check(typeof(TransferCoordinator).IsClass);
        Check(typeof(UntrustedText).IsClass);
        var root = FindRepoRoot();
        Check(!Directory.Exists(Path.Combine(root, "tests", "Integration.Ssh")));
        AppXamlSurface.CheckIntegrationWindowsProject(root);
        AppXamlSurface.CheckBlankContainerOnly(root);
        Check(File.Exists(Path.Combine(root, "src", "HerdDesk.Core", "Attachments", "AttachmentCoordinator.cs")));
        Check(File.Exists(Path.Combine(root, "src", "HerdDesk.App", "ViewModels", "AttachToAgentViewModel.cs")));
        var refs = typeof(AttachmentCoordinator).Assembly.GetReferencedAssemblies()
            .Select(item => item.Name!).ToArray();
        foreach (var name in refs)
        {
            var lower = name.ToLowerInvariant();
            Check(!lower.Contains("windowsappsdk"));
            Check(!lower.Contains("winui"));
            Check(!lower.Contains("webview2"));
            Check(!lower.Contains("ssh"));
        }
    }

    static void ResidualJson()
    {
        var path = Path.Combine(FindRepoRoot(), "implementation", "hd-030-l2.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Check(root.GetProperty("document_kind").GetString() == "hd030_l2_status");
        Check(root.GetProperty("l2_live_agent").GetString() == "UNVERIFIED");
        Check(root.GetProperty("l2_live_ime").GetString() == "UNVERIFIED");
        Check(root.GetProperty("l2_live_ssh").GetString() == "UNVERIFIED");
        Check(root.GetProperty("live_ssh").GetBoolean() is false);
        Check(root.GetProperty("winui_admitted").GetBoolean() is false);
        Check(root.GetProperty("integration_windows").GetBoolean() is false);
        Check(root.GetProperty("integration_windows_project").GetBoolean() is false);
        Check(root.GetProperty("auto_submit").GetBoolean() is false);
        foreach (var key in PassKeys)
            Check(root.GetProperty(key).GetBoolean() is false);
        Check(root.GetProperty("phase_gate").GetString() != "passed");
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
