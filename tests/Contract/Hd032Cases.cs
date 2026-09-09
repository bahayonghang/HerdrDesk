using System.Text.Json;
using HerdDesk.App;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Files;

internal static class Hd032Cases
{
    public static (string Name, Action Run)[] All =>
    [
        ("hd-032 catalog and support matrix stay not_run", CatalogAndMatrix),
        ("hd-032 l2 live fs ssh toctou attack and product acs stay unverified", ResidualJson)
    ];

    static readonly string[] PassKeys =
    [
        "ac31_passed", "ac32_passed", "ac33_passed", "ac34_passed",
        "ac35_passed", "g0_passed"
    ];

    static readonly string[] L2Keys =
    [
        "l2_live_fs", "l2_live_ssh", "l2_live_toctou", "l2_live_attack",
        "l2_live_ui"
    ];

    static readonly string[] CardIds =
    [
        "payload-integrity", "permission-enospc",
        "ssh-link-interrupt", "ssh-kill", "helper-kill",
        "symlink-junction-reparse-swap", "keepboth-32-way",
        "fail-replace-conflict", "dual-pane-ui-switch", "attach-no-enter"
    ];

    static readonly string[] InterruptKinds =
    [
        "ssh-link-interrupt", "ssh-kill", "helper-kill"
    ];

    static readonly Dictionary<string, string[]> CardOwners = new()
    {
        ["payload-integrity"] = ["HD-028", "HD-029"],
        ["permission-enospc"] = ["HD-028"],
        ["ssh-link-interrupt"] = ["HD-028"],
        ["ssh-kill"] = ["HD-028"],
        ["helper-kill"] = ["HD-028"],
        ["symlink-junction-reparse-swap"] = ["HD-027", "HD-028"],
        ["keepboth-32-way"] = ["HD-028", "HD-029"],
        ["fail-replace-conflict"] = ["HD-028", "HD-029"],
        ["dual-pane-ui-switch"] = ["HD-029"],
        ["attach-no-enter"] = ["HD-030", "HD-031"]
    };

    static readonly Dictionary<string, string[]> CardAcs = new()
    {
        ["payload-integrity"] = ["AC31"],
        ["permission-enospc"] = ["AC32"],
        ["ssh-link-interrupt"] = ["AC32"],
        ["ssh-kill"] = ["AC32"],
        ["helper-kill"] = ["AC32"],
        ["symlink-junction-reparse-swap"] = ["AC34"],
        ["keepboth-32-way"] = ["AC33"],
        ["fail-replace-conflict"] = ["AC33"],
        ["dual-pane-ui-switch"] = ["AC31", "AC33"],
        ["attach-no-enter"] = ["AC35"]
    };

    static readonly Dictionary<string, string> CardGrants = new()
    {
        ["payload-integrity"] = "no_authorized_disposable_fs_payload_lab",
        ["permission-enospc"] = "no_authorized_acl_quota_volume",
        ["ssh-link-interrupt"] = "no_authorized_ssh_link_interrupt",
        ["ssh-kill"] = "no_authorized_owned_ssh_pid_kill",
        ["helper-kill"] = "no_authorized_owned_filebridge_pid_kill",
        ["symlink-junction-reparse-swap"] = "no_authorized_second_process_symlink_attack",
        ["keepboth-32-way"] = "no_authorized_keepboth_race_lab",
        ["fail-replace-conflict"] = "no_authorized_replace_target_changed_lab",
        ["dual-pane-ui-switch"] = "no_winui_admission",
        ["attach-no-enter"] = "no_authorized_live_agent_path_insert"
    };

    static readonly Dictionary<string, string> LiveGrants = new()
    {
        ["live-fs"] = "no_authorized_disposable_fs_payload_lab",
        ["live-ssh"] = "no_authorized_ssh_interrupt_or_owned_pid_kill",
        ["live-toctou"] = "no_authorized_second_process_symlink_attack",
        ["live-attack"] = "no_authorized_keepboth_replace_race_lab",
        ["live-ui"] = "no_winui_admission"
    };

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void CatalogAndMatrix()
    {
        Check(typeof(TransferCoordinator).IsClass);
        Check(typeof(KeepBothNames).IsClass);
        Check(typeof(LocalFileEndpoint).IsClass);
        Check(typeof(ConflictDialogViewModel).IsClass);
        Check(typeof(FileWorkspaceViewModel).IsClass);
        Check(typeof(AttachmentCoordinator).IsClass);
        Check(typeof(PasteCoordinator).IsClass);

        var root = FindRepoRoot();
        Check(!Directory.Exists(Path.Combine(root, "tests", "Integration.Ssh")));
        AppXamlSurface.CheckIntegrationWindowsProject(root);
        AppXamlSurface.CheckBlankContainerOnly(root);

        using var catalogDoc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "evidence", "files", "catalog.json")));
        var catalog = catalogDoc.RootElement;
        Check(catalog.GetProperty("document_kind").GetString() == "hd032_file_fault_security_catalog");
        foreach (var key in L2Keys)
            Check(catalog.GetProperty(key).GetString() == "UNVERIFIED");
        Check(catalog.GetProperty("live_fs").GetBoolean() is false);
        Check(catalog.GetProperty("live_ssh").GetBoolean() is false);
        Check(catalog.GetProperty("live_toctou").GetBoolean() is false);
        Check(catalog.GetProperty("live_attack").GetBoolean() is false);
        Check(catalog.GetProperty("live_ui").GetBoolean() is false);
        Check(catalog.GetProperty("winui_admitted").GetBoolean() is false);
        Check(catalog.GetProperty("silent_overwrite_tested").GetBoolean() is false);
        Check(catalog.GetProperty("fake_fs_cannot_pass_toctou").GetBoolean());
        Check(catalog.GetProperty("cannot_merge_interrupt_kinds").GetBoolean());
        Check(catalog.GetProperty("herdr_executed").GetBoolean() is false);
        Check(catalog.GetProperty("redaction").GetProperty("credential").GetString() == "omitted");
        Check(catalog.GetProperty("redaction").GetProperty("file_body").GetString() == "omitted");
        Check(catalog.GetProperty("redaction").GetProperty("host").GetString() == "omitted");
        Check(catalog.GetProperty("redaction").GetProperty("path").GetString() == "omitted");
        Check(catalog.GetProperty("integration_ssh_project").GetBoolean() is false);
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
            Check(card.GetProperty("required_evidence").GetString() is "L2" or "L3" or "L4");
            Check(card.GetProperty("required_evidence").GetString() != "L1");
            Check(card.GetProperty("missing_grant").GetString() == CardGrants[id]);
            Check(card.GetProperty("owner_children").EnumerateArray()
                .Select(item => item.GetString()!).SequenceEqual(CardOwners[id]));
            Check(card.GetProperty("ac_ids").EnumerateArray()
                .Select(item => item.GetString()!).SequenceEqual(CardAcs[id]));
            Check(card.GetProperty("l1_artifacts").GetArrayLength() > 0);
            foreach (var rel in card.GetProperty("l1_artifacts").EnumerateArray())
                Check(File.Exists(Path.Combine(root, rel.GetString()!.Replace('/', Path.DirectorySeparatorChar))));
        }

        Check(!cards.Contains("ssh-interrupt-kill-helper-kill"));
        var interruptGrants = InterruptKinds.Select(id =>
        {
            var card = catalog.GetProperty("execution_cards").EnumerateArray()
                .First(item => item.GetProperty("id").GetString() == id);
            return card.GetProperty("missing_grant").GetString()!;
        }).ToArray();
        Check(interruptGrants.Distinct().Count() == 3);

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
            File.ReadAllText(Path.Combine(root, "evidence", "files", "support-matrix.json")));
        var matrix = matrixDoc.RootElement;
        Check(matrix.GetProperty("document_kind").GetString() == "hd032_support_matrix");
        Check(matrix.GetProperty("copy_windows_fields_onto_linux").GetBoolean() is false);
        Check(matrix.GetProperty("extrapolate_macos_arm64").GetBoolean() is false);
        Check(matrix.GetProperty("silent_overwrite_tested").GetBoolean() is false);
        Check(matrix.GetProperty("fake_fs_cannot_pass_toctou").GetBoolean());
        Check(matrix.GetProperty("cannot_merge_interrupt_kinds").GetBoolean());
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
        var scenarios = matrix.GetProperty("scenarios").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()!).ToArray();
        Check(scenarios.SequenceEqual(
        [
            "payload", "permission_enospc", "ssh_link_interrupt", "ssh_kill",
            "helper_kill", "toctou", "keepboth", "fail_replace", "dual_pane",
            "attach_no_enter"
        ]));
        Check(!scenarios.Contains("ssh_interrupt_kill"));
    }

    static void ResidualJson()
    {
        var path = Path.Combine(FindRepoRoot(), "implementation", "hd-032-l2.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        foreach (var key in L2Keys)
            Check(root.GetProperty(key).GetString() == "UNVERIFIED");
        foreach (var key in PassKeys)
            Check(root.GetProperty(key).GetBoolean() is false);
        Check(root.GetProperty("phase_gate").GetString() != "passed");
        Check(root.GetProperty("live_fs").GetBoolean() is false);
        Check(root.GetProperty("live_ssh").GetBoolean() is false);
        Check(root.GetProperty("live_toctou").GetBoolean() is false);
        Check(root.GetProperty("live_attack").GetBoolean() is false);
        Check(root.GetProperty("live_ui").GetBoolean() is false);
        Check(root.GetProperty("winui_admitted").GetBoolean() is false);
        Check(root.GetProperty("cannot_merge_interrupt_kinds").GetBoolean());
        Check(root.GetProperty("fake_fs_cannot_pass_toctou").GetBoolean());
        Check(root.GetProperty("integration_ssh_project").GetBoolean() is false);
        Check(root.GetProperty("integration_windows_project").GetBoolean() is false);
        Check(root.GetProperty("herdr_executed").GetBoolean() is false);
        Check(root.GetProperty("silent_overwrite_tested").GetBoolean() is false);
        Check(root.GetProperty("missing").GetProperty("toctou_lab").GetBoolean());
        Check(root.GetProperty("missing").GetProperty("attack_lab").GetBoolean());
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
