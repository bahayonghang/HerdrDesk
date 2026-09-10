using System.Text.Json;
using HerdDesk.App;
using HerdDesk.Core;
using HerdDesk.Terminal.Web;

internal static class Hd036Cases
{
    public static (string Name, Action Run)[] All =>
    [
        ("hd-036 catalog ac-index and support matrix stay not_run", CatalogAndMatrix),
        ("hd-036 l2 l3 l4 walkthrough publish and product acs stay unverified", ResidualJson)
    ];

    static readonly string[] PassKeys =
    [
        "ac39_passed", "ac40_passed", "ac45_passed", "ac47_passed", "ac48_passed",
        "g0_passed"
    ];

    static readonly string[] L2L3L4Keys =
    [
        "l2_independent_user_walkthrough", "l2_live_platform_matrix",
        "l2_final_candidate_sha_evidence_bind", "l2_clean_machine_locked_restore",
        "l2_github_required_check_on_head", "l2_final_sha_module_graph_rerun",
        "l2_external_publish", "l3_signed_msix_hash", "l4_complete_1_0_release"
    ];

    static readonly string[] CardIds =
    [
        "user-guide", "support-matrix", "ac48-trace", "ac39-clean-restore",
        "ac40-hosted-required-check", "ac47-dep-graph", "unpublished-candidate",
        "signed-hash-sbom"
    ];

    static readonly Dictionary<string, string[]> CardOwners = new()
    {
        ["user-guide"] = ["HD-011", "HD-016", "HD-030", "HD-031"],
        ["support-matrix"] = ["HD-001", "HD-026", "HD-033", "HD-034"],
        ["ac48-trace"] = ["HD-026", "HD-032", "HD-033", "HD-034", "HD-035"],
        ["ac39-clean-restore"] = ["HD-007"],
        ["ac40-hosted-required-check"] = ["HD-007"],
        ["ac47-dep-graph"] = ["HD-007"],
        ["unpublished-candidate"] = ["HD-007"],
        ["signed-hash-sbom"] = ["HD-034", "HD-035"]
    };

    static readonly Dictionary<string, string[]> CardAcs = new()
    {
        ["user-guide"] = ["AC45"],
        ["support-matrix"] = ["AC45"],
        ["ac48-trace"] = ["AC48"],
        ["ac39-clean-restore"] = ["AC39"],
        ["ac40-hosted-required-check"] = ["AC40"],
        ["ac47-dep-graph"] = ["AC47"],
        ["unpublished-candidate"] = ["AC48"],
        ["signed-hash-sbom"] = ["AC45"]
    };

    static readonly Dictionary<string, string> CardGrants = new()
    {
        ["user-guide"] = "no_authorized_independent_user_walkthrough",
        ["support-matrix"] = "no_authorized_live_platform_matrix",
        ["ac48-trace"] = "no_authorized_final_candidate_sha_evidence_bind",
        ["ac39-clean-restore"] = "no_authorized_clean_machine_locked_restore",
        ["ac40-hosted-required-check"] = "no_authorized_github_required_check_on_head",
        ["ac47-dep-graph"] = "no_authorized_final_sha_module_graph_rerun",
        ["unpublished-candidate"] = "no_authorized_external_publish",
        ["signed-hash-sbom"] = "no_authorized_signed_msix_hash"
    };

    static readonly Dictionary<string, string> LiveGrants = new()
    {
        ["live-user-guide"] = "no_authorized_independent_user_walkthrough",
        ["live-support-matrix"] = "no_authorized_live_platform_matrix",
        ["live-ac48-trace"] = "no_authorized_final_candidate_sha_evidence_bind",
        ["live-ac39-clean-restore"] = "no_authorized_clean_machine_locked_restore",
        ["live-ac40-hosted-required-check"] = "no_authorized_github_required_check_on_head",
        ["live-ac47-dep-graph"] = "no_authorized_final_sha_module_graph_rerun",
        ["live-unpublished-candidate"] = "no_authorized_external_publish",
        ["live-signed-hash-sbom"] = "no_authorized_signed_msix_hash"
    };

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void CatalogAndMatrix()
    {
        Check(typeof(ControlLeaseCoordinator).IsClass);
        Check(typeof(PasteCoordinator).IsClass);
        Check(typeof(AttachmentCoordinator).IsClass);
        Check(typeof(OscClipboardPolicy).IsClass);
        Check(typeof(ShellViewModel).IsClass);
        Check(typeof(TerminalControlViewModel).IsClass);
        Check(typeof(AttachToAgentViewModel).IsClass);
        Check(typeof(PastePreviewViewModel).IsClass);

        var root = FindRepoRoot();
        AppXamlSurface.CheckIntegrationWindowsProject(root);
        Check(File.Exists(Path.Combine(root, "docs", "user-guide", "index.md")));
        Check(File.Exists(Path.Combine(root, "docs", "release", "notes.md")));
        Check(File.Exists(Path.Combine(root, "docs", "testing", "release-checklist.md")));
        Check(File.Exists(Path.Combine(root, "planning", "acceptance.json")));
        Check(File.Exists(Path.Combine(root, "scripts", "bind_release_candidate.py")));
        Check(File.Exists(Path.Combine(root, "evidence", "releases", "hosted-workflow-pointer.json")));

        using var catalogDoc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "evidence", "releases", "catalog.json")));
        var catalog = catalogDoc.RootElement;
        Check(catalog.GetProperty("document_kind").GetString() == "hd036_release_docs_catalog");
        foreach (var key in L2L3L4Keys)
            Check(catalog.GetProperty(key).GetString() == "UNVERIFIED");
        Check(catalog.GetProperty("published").GetBoolean() is false);
        Check(catalog.GetProperty("complete_1_0_claimed").GetBoolean() is false);
        Check(catalog.GetProperty("independent_user_walkthrough_executed").GetBoolean() is false);
        Check(catalog.GetProperty("screenshot_as_evidence").GetBoolean() is false);
        Check(catalog.GetProperty("winui_admitted").GetBoolean() is false);
        Check(catalog.GetProperty("herdr_executed").GetBoolean() is false);
        Check(catalog.GetProperty("integration_windows_project").GetBoolean() is false);
        Check(catalog.GetProperty("github_required_check").GetString() == "UNVERIFIED");
        Check(catalog.GetProperty("windows_desktop_restore").GetString() == "not_admitted");
        Check(catalog.GetProperty("core_1_0_does_not_impersonate_1_x_extensions").GetBoolean());
        Check(catalog.GetProperty("unpublished_candidate_is_not_published").GetBoolean());
        Check(catalog.GetProperty("hosted_workflow_pointer").GetString()
              == "evidence/releases/hosted-workflow-pointer.json");
        using var pointerDoc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "evidence", "releases", "hosted-workflow-pointer.json")));
        var pointer = pointerDoc.RootElement;
        Check(pointer.GetProperty("document_kind").GetString() == "hd036_hosted_workflow_pointer");
        Check(pointer.GetProperty("result").GetString() == "not_run");
        Check(pointer.GetProperty("bound_sha").GetString() == "602c252ae10303b63c6d8fc584e193e1ed5654c4");
        Check(pointer.GetProperty("hosted_workflow_run_id").GetString() == "34438599236");
        Check(pointer.GetProperty("hosted_workflow_conclusion").GetString() == "success");
        Check(pointer.GetProperty("github_required_check").GetString() == "UNVERIFIED");
        Check(pointer.GetProperty("hosted_workflow_is_not_required_check_ruleset").GetBoolean());
        Check(pointer.GetProperty("published").GetBoolean() is false);
        Check(pointer.GetProperty("complete_1_0_claimed").GetBoolean() is false);
        Check(pointer.GetProperty("ac40_passed").GetBoolean() is false);
        Check(pointer.GetProperty("g0_passed").GetBoolean() is false);
        Check(pointer.GetProperty("candidate_sha").ValueKind == JsonValueKind.Null);
        Check(pointer.GetProperty("hosted_check_run_id").ValueKind == JsonValueKind.Null);
        Check(catalog.GetProperty("redaction").GetProperty("credential").GetString() == "omitted");
        Check(catalog.GetProperty("package_sha256").ValueKind == JsonValueKind.Null);
        Check(catalog.GetProperty("screenshot_path").ValueKind == JsonValueKind.Null);
        Check(catalog.GetProperty("candidate_sha").ValueKind == JsonValueKind.Null);
        Check(catalog.GetProperty("hosted_check_run_id").ValueKind == JsonValueKind.Null);
        foreach (var key in PassKeys)
            Check(catalog.GetProperty(key).GetBoolean() is false);

        var cards = catalog.GetProperty("execution_cards").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()!).ToArray();
        Check(cards.SequenceEqual(CardIds));
        var grants = new HashSet<string>(StringComparer.Ordinal);
        foreach (var card in catalog.GetProperty("execution_cards").EnumerateArray())
        {
            var id = card.GetProperty("id").GetString()!;
            Check(card.GetProperty("live_status").GetString() == "UNVERIFIED");
            Check(card.GetProperty("live_result").GetString() == "not_run");
            Check(card.GetProperty("l1_status").GetString() == "shipped");
            Check(card.GetProperty("required_evidence").GetString() is "L2" or "L3" or "L4");
            Check(card.GetProperty("required_evidence").GetString() != "L1");
            Check(card.GetProperty("missing_grant").GetString() == CardGrants[id]);
            Check(grants.Add(card.GetProperty("missing_grant").GetString()!));
            Check(card.GetProperty("owner_children").EnumerateArray()
                .Select(item => item.GetString()!).SequenceEqual(CardOwners[id]));
            Check(card.GetProperty("ac_ids").EnumerateArray()
                .Select(item => item.GetString()!).SequenceEqual(CardAcs[id]));
            Check(card.GetProperty("l1_artifacts").GetArrayLength() > 0);
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
            Check(template.GetProperty("published").GetBoolean() is false);
            Check(template.GetProperty("screenshot_as_evidence").GetBoolean() is false);
            Check(template.GetProperty("github_required_check").GetString() == "UNVERIFIED");
            Check(!template.TryGetProperty("stdout", out _));
            Check(!template.TryGetProperty("stderr", out _));
        }

        using var indexDoc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "evidence", "releases", "ac-index.json")));
        var index = indexDoc.RootElement;
        Check(index.GetProperty("document_kind").GetString() == "hd036_ac_index");
        Check(index.GetProperty("published").GetBoolean() is false);
        Check(index.GetProperty("complete_1_0_claimed").GetBoolean() is false);
        Check(index.GetProperty("trace_index_is_not_ac_pass_evidence").GetBoolean());
        Check(index.GetProperty("entries").GetArrayLength() == 48);
        foreach (var entry in index.GetProperty("entries").EnumerateArray())
        {
            Check(entry.GetProperty("status").GetString() == "not_run");
            Check(entry.GetProperty("live_result").GetString() == "not_run");
            Check(entry.GetProperty("candidate_sha").ValueKind == JsonValueKind.Null);
        }

        using var matrixDoc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "evidence", "releases", "support-matrix.json")));
        var matrix = matrixDoc.RootElement;
        Check(matrix.GetProperty("document_kind").GetString() == "hd036_support_matrix");
        Check(matrix.GetProperty("copy_windows_fields_onto_linux").GetBoolean() is false);
        Check(matrix.GetProperty("extrapolate_macos_arm64").GetBoolean() is false);
        Check(matrix.GetProperty("linux_msix_client").GetBoolean() is false);
        Check(matrix.GetProperty("linux_x64_is_not_windows_client_substitute").GetBoolean());
        Check(matrix.GetProperty("core_1_0_does_not_impersonate_1_x_extensions").GetBoolean());
        foreach (var key in PassKeys)
            Check(matrix.GetProperty(key).GetBoolean() is false);
        var platforms = matrix.GetProperty("platforms").EnumerateArray()
            .ToDictionary(item => item.GetProperty("id").GetString()!, item => item);
        Check(platforms["windows-11-x64-client"].GetProperty("support").GetString() == "promised_not_run");
        Check(platforms["linux-x64-remote"].GetProperty("support").GetString() == "promised_not_run");
        Check(platforms["linux-x64-remote"].GetProperty("promise").GetString() == "promised");
        Check(platforms["linux-x64-remote"].GetProperty("linux_msix").GetBoolean() is false);
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

        foreach (var cell in matrix.GetProperty("cells").EnumerateArray())
        {
            Check(cell.GetProperty("live_status").GetString() == "not_run");
            Check(cell.GetProperty("live_result").GetString() == "not_run");
            Check(cell.GetProperty("compatible").GetBoolean() is false);
        }

        Check(platforms["windows-11-x64-client"].GetProperty("missing_grant").GetString()
              != platforms["linux-x64-remote"].GetProperty("missing_grant").GetString());
        var scenarios = matrix.GetProperty("scenarios").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()!).ToArray();
        Check(scenarios.SequenceEqual(
        [
            "user_guide", "support_matrix", "ac48_trace", "ac39_clean_restore",
            "ac40_hosted_required_check", "ac47_dep_graph", "unpublished_candidate",
            "signed_hash_sbom"
        ]));
        var promised = matrix.GetProperty("promised_range").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()!).ToArray();
        Check(promised.SequenceEqual(["windows-11-x64-client", "linux-x64-remote"]));
    }

    static void ResidualJson()
    {
        var path = Path.Combine(FindRepoRoot(), "implementation", "hd-036-l2.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        foreach (var key in L2L3L4Keys)
            Check(root.GetProperty(key).GetString() == "UNVERIFIED");
        foreach (var key in PassKeys)
            Check(root.GetProperty(key).GetBoolean() is false);
        Check(root.GetProperty("phase_gate").GetString() != "passed");
        Check(root.GetProperty("published").GetBoolean() is false);
        Check(root.GetProperty("complete_1_0_claimed").GetBoolean() is false);
        Check(root.GetProperty("independent_user_walkthrough_executed").GetBoolean() is false);
        Check(root.GetProperty("screenshot_as_evidence").GetBoolean() is false);
        Check(root.GetProperty("winui_admitted").GetBoolean() is false);
        Check(root.GetProperty("integration_windows_project").GetBoolean() is false);
        Check(root.GetProperty("herdr_executed").GetBoolean() is false);
        Check(root.GetProperty("github_required_check").GetString() == "UNVERIFIED");
        Check(root.GetProperty("windows_desktop_restore").GetString() == "not_admitted");
        Check(root.GetProperty("missing").GetProperty("independent_user_walkthrough").GetBoolean());
        Check(root.GetProperty("missing").GetProperty("github_required_check_on_head").GetBoolean());
        Check(root.GetProperty("missing").GetProperty("external_publish").GetBoolean());
        Check(root.GetProperty("missing").GetProperty("signed_msix_hash").GetBoolean());
        Check(root.GetProperty("hosted_workflow_pointer").GetString()
              == "evidence/releases/hosted-workflow-pointer.json");
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
