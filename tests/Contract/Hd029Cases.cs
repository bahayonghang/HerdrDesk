using System.Text.Json;
using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class Hd029Cases
{
    public static (string Name, Action Run)[] All =>
    [
        ("hd-029 file workspace viewmodels are compiled", Surface),
        ("hd-029 l2 live ui ssh and acs stay unverified", ResidualJson)
    ];

    static readonly string[] PassKeys =
    [
        "ac31_passed", "ac33_passed", "g0_passed"
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void Surface()
    {
        Check(typeof(FileWorkspaceViewModel).IsClass);
        Check(typeof(FilePaneViewModel).IsClass);
        Check(typeof(TransferQueueViewModel).IsClass);
        Check(typeof(ConflictDialogViewModel).IsClass);
        Check(typeof(TransferDraft).IsClass);
        Check(typeof(TransferLease).IsClass);
        Check(typeof(UntrustedText).IsClass);
        Check(typeof(TransferCoordinator).IsClass);
        Check(typeof(IFileEndpoint).IsInterface);
        Check(UntrustedText.TryAsPath("javascript:alert(1)") is null);
        Check(!UntrustedText.TryAsCommand("rm -rf /"));
        var root = FindRepoRoot();
        Check(!Directory.Exists(Path.Combine(root, "tests", "Integration.Ssh")));
        AppXamlSurface.CheckIntegrationWindowsProject(root);
        AppXamlSurface.CheckBlankContainerOnly(root);
        Check(File.Exists(Path.Combine(root, "src", "HerdDesk.App", "Files", "FileWorkspaceViewModel.cs")));
        Check(File.Exists(Path.Combine(root, "src", "HerdDesk.App", "Files", "FilePaneViewModel.cs")));
        var refs = typeof(FileWorkspaceViewModel).Assembly.GetReferencedAssemblies()
            .Select(item => item.Name!).ToArray();
        foreach (var name in refs)
        {
            var lower = name.ToLowerInvariant();
            Check(!lower.Contains("windowsappsdk"));
            Check(!lower.Contains("winui"));
            Check(!lower.Contains("webview2"));
        }
    }

    static void ResidualJson()
    {
        var path = Path.Combine(FindRepoRoot(), "implementation", "hd-029-l2.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Check(root.GetProperty("document_kind").GetString() == "hd029_l2_status");
        Check(root.GetProperty("l2_live_ui").GetString() == "UNVERIFIED");
        Check(root.GetProperty("l2_live_ssh").GetString() == "UNVERIFIED");
        Check(root.GetProperty("live_ssh").GetBoolean() is false);
        Check(root.GetProperty("winui_admitted").GetBoolean() is false);
        Check(root.GetProperty("integration_windows").GetBoolean() is false);
        Check(root.GetProperty("integration_windows_project").GetBoolean() is false);
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
