using System.Text.Json;
using HerdDesk.Infrastructure.Files;

internal static class Hd027Cases
{
    public static (string Name, Action Run)[] All =>
    [
        ("hd-027 l2 filesystem ssh toctou stay unverified", ResidualJson),
        ("hd-027 has no binary entrypoint", NoBinary)
    ];

    static readonly string[] PassKeys =
    [
        "ac30_passed", "ac34_passed", "g0_passed"
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void ResidualJson()
    {
        Check(typeof(FileBridgeProtocolCodec).IsClass);
        var root = FindRepoRoot();
        Check(!Directory.Exists(Path.Combine(root, "tests", "Integration.Ssh")));
        Check(!Directory.Exists(Path.Combine(root, "tests", "Integration.Windows")));
        using var doc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "implementation", "hd-027-l2.json")));
        var obj = doc.RootElement;
        Check(obj.GetProperty("document_kind").GetString() == "hd027_l2_status");
        Check(obj.GetProperty("l2_filesystem").GetString() == "UNVERIFIED");
        Check(obj.GetProperty("l2_ssh").GetString() == "UNVERIFIED");
        Check(obj.GetProperty("l2_toctou").GetString() == "UNVERIFIED");
        Check(obj.GetProperty("live_file_ops").GetBoolean() is false);
        Check(obj.GetProperty("binary_implemented").GetBoolean() is false);
        Check(obj.GetProperty("main_rs").GetBoolean() is false);
        foreach (var key in PassKeys)
            Check(obj.GetProperty(key).GetBoolean() is false);
        Check(obj.GetProperty("phase_gate").GetString() != "passed");
        Check(obj.GetProperty("adr_status").GetString() == "proposed");
        Check(obj.GetProperty("command_not_implemented").GetString() ==
              "herddesk-filebridge serve --stdio --protocol 1.0");
    }

    static void NoBinary()
    {
        var root = FindRepoRoot();
        Check(!File.Exists(Path.Combine(root, "filebridge", "src", "main.rs")));
        var cargo = File.ReadAllText(Path.Combine(root, "filebridge", "Cargo.toml"));
        Check(!cargo.Contains("[[bin]]"));
        Check(!Directory.Exists(Path.Combine(root, "tests", "Integration.Ssh")));
        Check(!Directory.Exists(Path.Combine(root, "tests", "Integration.Windows")));
        var adr = File.ReadAllText(Path.Combine(root, "docs", "adr", "0008-filebridge-protocol-v1.md"));
        Check(adr.Contains("proposed"));
        Check(!adr.Contains("status: accepted"));
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
