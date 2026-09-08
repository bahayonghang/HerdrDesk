using System.Text.Json;
using HerdDesk.App;
using HerdDesk.Core;

internal static class Hd026Cases
{
    public static (string Name, Action Run)[] All =>
    [
        ("hd-026 catalog and support matrix stay not_run", CatalogAndMatrix),
        ("hd-026 l2 live ssh and product acs stay unverified", ResidualJson)
    ];

    static readonly string[] PassKeys =
    [
        "ac13_passed", "ac14_passed", "ac15_passed", "ac19_passed",
        "ac21_passed", "ac22_passed", "ac23_passed", "ac24_passed",
        "ac26_passed", "ac27_passed", "g0_passed"
    ];

    static readonly string[] CardIds =
    [
        "ac21-identity-collision", "ac22-ac23-ssh-identity-config",
        "ac24-transparent-stream", "ac13-recovery", "ac14-no-replay",
        "ac15-ownership", "ac26-isolation-retry", "ac19-search",
        "budget-platform"
    ];

    static readonly Dictionary<string, string[]> CardOwners = new()
    {
        ["ac21-identity-collision"] = ["HD-023"],
        ["ac22-ac23-ssh-identity-config"] = ["HD-020", "HD-024"],
        ["ac24-transparent-stream"] = ["HD-022"],
        ["ac13-recovery"] = ["HD-018", "HD-019", "HD-022", "HD-024"],
        ["ac14-no-replay"] = ["HD-016", "HD-018", "HD-022"],
        ["ac15-ownership"] = ["HD-013", "HD-018", "HD-019", "HD-022"],
        ["ac26-isolation-retry"] = ["HD-024"],
        ["ac19-search"] = ["HD-011", "HD-023"],
        ["budget-platform"] = ["HD-025", "HD-026"]
    };

    static readonly Dictionary<string, string> LiveGrants = new()
    {
        ["live-ssh"] = "no_authorized_isolated_windows_openssh_lab",
        ["live-winui"] = "no_winui_admission",
        ["live-three-device"] = "no_authorized_third_device_id",
        ["live-crash"] = "no_authorized_supervised_gui_crash"
    };

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void CatalogAndMatrix()
    {
        Check(typeof(WriteIntentGuard).IsClass);
        Check(typeof(GlobalProjectionStore).IsClass);
        Check(typeof(GlobalSearchIndex).IsClass);
        Check(typeof(ControlLeaseCoordinator).IsClass);
        Check(typeof(RecoveryPolicy).IsClass);
        Check(typeof(AppExitCoordinator).IsClass);
        Check(typeof(EditDeviceViewModel).IsClass);
        Check(typeof(ConnectionAdmissionPolicy).IsClass);
        Check(typeof(PaneVisibilityCoordinator).IsClass);

        var root = FindRepoRoot();
        Check(!Directory.Exists(Path.Combine(root, "tests", "Integration.Ssh")));
        Check(!Directory.Exists(Path.Combine(root, "tests", "Integration.Windows")));
        Check(!Directory.EnumerateFiles(Path.Combine(root, "src", "HerdDesk.App"), "*.xaml",
            SearchOption.AllDirectories).Any());

        using var catalogDoc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "evidence", "multi-device-mvp", "catalog.json")));
        var catalog = catalogDoc.RootElement;
        Check(catalog.GetProperty("document_kind").GetString() == "hd026_multi_device_mvp_catalog");
        Check(catalog.GetProperty("l2_live_ssh").GetString() == "UNVERIFIED");
        Check(catalog.GetProperty("live_ssh").GetBoolean() is false);
        Check(catalog.GetProperty("winui_admitted").GetBoolean() is false);
        Check(catalog.GetProperty("third_device_id_authorized").GetBoolean() is false);
        Check(catalog.GetProperty("two_sessions_on_one_host_are_not_three_devices").GetBoolean());
        Check(catalog.GetProperty("integration_ssh_project").GetBoolean() is false);
        Check(catalog.GetProperty("herdr_executed").GetBoolean() is false);
        foreach (var key in PassKeys)
            Check(catalog.GetProperty(key).GetBoolean() is false);

        var cards = catalog.GetProperty("execution_cards").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()!).ToArray();
        Check(cards.SequenceEqual(CardIds));
        foreach (var card in catalog.GetProperty("execution_cards").EnumerateArray())
        {
            var id = card.GetProperty("id").GetString()!;
            Check(card.GetProperty("live_status").GetString() == "UNVERIFIED");
            Check(card.GetProperty("live_result").GetString() == "not_run");
            Check(card.GetProperty("l1_status").GetString() == "shipped");
            Check(!string.IsNullOrWhiteSpace(card.GetProperty("missing_grant").GetString()));
            Check(card.GetProperty("owner_children").EnumerateArray()
                .Select(item => item.GetString()!).SequenceEqual(CardOwners[id]));
            foreach (var rel in card.GetProperty("l1_artifacts").EnumerateArray())
                Check(File.Exists(Path.Combine(root, rel.GetString()!.Replace('/', Path.DirectorySeparatorChar))));
        }

        foreach (var row in catalog.GetProperty("live_rows").EnumerateArray())
        {
            var id = row.GetProperty("id").GetString()!;
            Check(row.GetProperty("status").GetString() == "UNVERIFIED");
            Check(row.GetProperty("result").GetString() == "not_run");
            Check(row.GetProperty("template").GetBoolean() is false);
            Check(row.GetProperty("owner_children").GetArrayLength() > 0);
            Check(row.GetProperty("missing_grant").GetString() == LiveGrants[id]);
        }

        foreach (var rel in catalog.GetProperty("templates").EnumerateArray())
        {
            var path = Path.Combine(root, rel.GetString()!.Replace('/', Path.DirectorySeparatorChar));
            using var templateDoc = JsonDocument.Parse(File.ReadAllText(path));
            var template = templateDoc.RootElement;
            Check(template.GetProperty("template").GetBoolean());
            Check(template.GetProperty("document_kind").GetString() == "template");
            Check(template.GetProperty("exit_code").ValueKind == JsonValueKind.Null);
            Check(template.GetProperty("stdout_sha256").ValueKind == JsonValueKind.Null);
            Check(template.GetProperty("stderr_sha256").ValueKind == JsonValueKind.Null);
            Check(template.GetProperty("captured_at_utc").ValueKind == JsonValueKind.Null);
            Check(template.GetProperty("herdr_executed").GetBoolean() is false);
            Check(!template.TryGetProperty("stdout", out _));
            Check(!template.TryGetProperty("stderr", out _));
        }

        using var matrixDoc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "evidence", "multi-device-mvp", "support-matrix.json")));
        var matrix = matrixDoc.RootElement;
        Check(matrix.GetProperty("document_kind").GetString() == "hd026_support_matrix");
        Check(matrix.GetProperty("copy_windows_fields_onto_linux").GetBoolean() is false);
        Check(matrix.GetProperty("extrapolate_macos_arm64").GetBoolean() is false);
        Check(matrix.GetProperty("third_device_id_authorized").GetBoolean() is false);
        foreach (var key in PassKeys)
            Check(matrix.GetProperty(key).GetBoolean() is false);
        var platforms = matrix.GetProperty("platforms").EnumerateArray()
            .ToDictionary(item => item.GetProperty("id").GetString()!, item => item);
        Check(platforms["windows-11-x64-client"].GetProperty("support").GetString() == "promised_not_run");
        Check(platforms["linux-x64-remote"].GetProperty("support").GetString() == "promised_not_run");
        Check(platforms["macos-x64"].GetProperty("support").GetString() == "unsupported");
        foreach (var id in new[] { "macos-arm64", "linux-arm64", "windows-arm64" })
        {
            var support = platforms[id].GetProperty("support").GetString();
            Check(support is "unsupported" or "experimental");
        }

        foreach (var row in matrix.GetProperty("platforms").EnumerateArray())
        {
            Check(row.GetProperty("live_status").GetString() == "not_run");
            Check(row.GetProperty("compatible").GetBoolean() is false);
            var support = row.GetProperty("support").GetString();
            Check(support != "supported" && support != "stable" && support != "passed");
        }

        Check(platforms["windows-11-x64-client"].GetProperty("missing_grant").GetString()
              != platforms["linux-x64-remote"].GetProperty("missing_grant").GetString());
    }

    static void ResidualJson()
    {
        var path = Path.Combine(FindRepoRoot(), "implementation", "hd-026-l2.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Check(root.GetProperty("l2_live_ssh").GetString() == "UNVERIFIED");
        foreach (var key in PassKeys)
            Check(root.GetProperty(key).GetBoolean() is false);
        Check(root.GetProperty("phase_gate").GetString() != "passed");
        Check(root.GetProperty("live_ssh").GetBoolean() is false);
        Check(root.GetProperty("winui_admitted").GetBoolean() is false);
        Check(root.GetProperty("integration_ssh_project").GetBoolean() is false);
        Check(root.GetProperty("third_device_id_authorized").GetBoolean() is false);
        Check(root.GetProperty("herdr_executed").GetBoolean() is false);
        Check(root.GetProperty("two_sessions_on_one_host_are_not_three_devices").GetBoolean());
        Check(root.GetProperty("missing").GetProperty("authorized_third_device").GetBoolean());
        Check(root.GetProperty("missing").GetProperty("isolated_linux_herdr").GetBoolean());
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
