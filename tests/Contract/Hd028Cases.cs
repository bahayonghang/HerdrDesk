using System.Text.Json;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Files;

internal static class Hd028Cases
{
    public static (string Name, Action Run)[] All =>
    [
        ("hd-028 l2 live fs ssh stay unverified", ResidualJson),
        ("hd-028 does not add integration projects", NoIntegration)
    ];

    static readonly string[] PassKeys =
    [
        "ac30_passed", "ac31_passed", "ac32_passed", "g0_passed"
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void ResidualJson()
    {
        Check(typeof(TransferCoordinator).IsClass);
        Check(typeof(LocalFileEndpoint).IsClass);
        Check(typeof(FileBridgeClient).IsClass);
        var root = FindRepoRoot();
        using var doc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "implementation", "hd-028-l2.json")));
        var obj = doc.RootElement;
        Check(obj.GetProperty("document_kind").GetString() == "hd028_l2_status");
        Check(obj.GetProperty("l2_filesystem").GetString() == "UNVERIFIED");
        Check(obj.GetProperty("l2_ssh").GetString() == "UNVERIFIED");
        Check(obj.GetProperty("l2_toctou").GetString() == "UNVERIFIED");
        Check(obj.GetProperty("live_ssh").GetBoolean() is false);
        Check(obj.GetProperty("integration_ssh_project").GetBoolean() is false);
        Check(obj.GetProperty("integration_windows_project").GetBoolean() is false);
        foreach (var key in PassKeys)
            Check(obj.GetProperty(key).GetBoolean() is false);
        Check(obj.GetProperty("phase_gate").GetString() != "passed");
        Check(typeof(IFileEndpoint).IsInterface);
        Check(typeof(IRemoteFileService).IsInterface);
    }

    static void NoIntegration()
    {
        var root = FindRepoRoot();
        Check(!Directory.Exists(Path.Combine(root, "tests", "Integration.Ssh")));
        Check(!Directory.Exists(Path.Combine(root, "tests", "Integration.Windows")));
        Check(File.Exists(Path.Combine(root, "filebridge", "src", "main.rs")));
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
