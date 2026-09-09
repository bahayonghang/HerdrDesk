using System.Text.Json;
using HerdDesk.App;
using HerdDesk.Core;
using HerdDesk.Terminal.Web;

internal static class Hd033Cases
{
    public static (string Name, Action Run)[] All =>
    [
        ("hd-033 catalog and support matrix stay not_run", CatalogAndMatrix),
        ("hd-033 l3 narrator dpi l4 soak and product acs stay unverified", ResidualJson)
    ];

    static readonly string[] PassKeys =
    [
        "ac27_passed", "ac28_passed", "ac29_passed", "ac37_passed",
        "ac38_passed", "ac46_passed", "g0_passed"
    ];

    static readonly string[] L3L4Keys =
    [
        "l3_ime", "l3_narrator", "l3_dpi", "l4_soak"
    ];

    static readonly string[] CardIds =
    [
        "cold-start", "input-to-visible-pixel", "search-p95",
        "working-set-1-4-pane", "hide-show-100", "narrator",
        "dpi-100-150-200", "eight-hour-soak"
    ];

    static readonly Dictionary<string, string[]> CardOwners = new()
    {
        ["cold-start"] = ["HD-011"],
        ["input-to-visible-pixel"] = ["HD-014", "HD-015"],
        ["search-p95"] = ["HD-011", "HD-023"],
        ["working-set-1-4-pane"] = ["HD-025"],
        ["hide-show-100"] = ["HD-025", "HD-011"],
        ["narrator"] = ["HD-011"],
        ["dpi-100-150-200"] = ["HD-011", "HD-014"],
        ["eight-hour-soak"] = ["HD-025", "HD-018"]
    };

    static readonly Dictionary<string, string[]> CardAcs = new()
    {
        ["cold-start"] = ["AC28"],
        ["input-to-visible-pixel"] = ["AC28"],
        ["search-p95"] = ["AC28"],
        ["working-set-1-4-pane"] = ["AC27", "AC28"],
        ["hide-show-100"] = ["AC29"],
        ["narrator"] = ["AC37"],
        ["dpi-100-150-200"] = ["AC38"],
        ["eight-hour-soak"] = ["AC46"]
    };

    static readonly Dictionary<string, string> CardGrants = new()
    {
        ["cold-start"] = "no_authorized_interactive_desktop_cold_start",
        ["input-to-visible-pixel"] = "no_authorized_visible_pixel_latency_probe",
        ["search-p95"] = "no_authorized_live_search_p95",
        ["working-set-1-4-pane"] = "no_authorized_working_set_process_sample",
        ["hide-show-100"] = "no_authorized_pane_hide_show_handle_lab",
        ["narrator"] = "no_authorized_narrator_desktop",
        ["dpi-100-150-200"] = "no_authorized_dpi_theme_monitor_matrix",
        ["eight-hour-soak"] = "no_authorized_eight_hour_soak"
    };

    static readonly Dictionary<string, string> LiveGrants = new()
    {
        ["live-cold-start"] = "no_authorized_interactive_desktop_cold_start",
        ["live-input-pixel"] = "no_authorized_visible_pixel_latency_probe",
        ["live-search-p95"] = "no_authorized_live_search_p95",
        ["live-working-set"] = "no_authorized_working_set_process_sample",
        ["live-handle-reclaim"] = "no_authorized_pane_hide_show_handle_lab",
        ["live-narrator"] = "no_authorized_narrator_desktop",
        ["live-dpi"] = "no_authorized_dpi_theme_monitor_matrix",
        ["live-soak"] = "no_authorized_eight_hour_soak"
    };

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void CatalogAndMatrix()
    {
        Check(typeof(ShellViewModel).IsClass);
        Check(typeof(GlobalSearchViewModel).IsClass);
        Check(typeof(GlobalSearchIndex).IsClass);
        Check(typeof(TerminalQueueBudget).IsClass);
        Check(typeof(PaneVisibilityCoordinator).IsClass);
        Check(typeof(RenderFlowController).IsClass);
        Check(typeof(TerminalInputController).IsClass);

        var root = FindRepoRoot();
        AppXamlSurface.CheckIntegrationWindowsProject(root);
        AppXamlSurface.CheckBlankContainerOnly(root);

        using var catalogDoc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "evidence", "quality", "catalog.json")));
        var catalog = catalogDoc.RootElement;
        Check(catalog.GetProperty("document_kind").GetString() == "hd033_performance_a11y_soak_catalog");
        foreach (var key in L3L4Keys)
            Check(catalog.GetProperty(key).GetString() == "UNVERIFIED");
        Check(catalog.GetProperty("live_soak").GetBoolean() is false);
        Check(catalog.GetProperty("live_input_pixel").GetBoolean() is false);
        Check(catalog.GetProperty("live_working_set").GetBoolean() is false);
        Check(catalog.GetProperty("live_narrator").GetBoolean() is false);
        Check(catalog.GetProperty("live_dpi").GetBoolean() is false);
        Check(catalog.GetProperty("eight_hour_soak_executed").GetBoolean() is false);
        Check(catalog.GetProperty("invented_timings").GetBoolean() is false);
        Check(catalog.GetProperty("parser_consumed_is_not_presentation").GetBoolean());
        Check(catalog.GetProperty("parser_callback_cannot_pass_input_to_pixel").GetBoolean());
        Check(catalog.GetProperty("derive_process_memory_from_q_p").GetBoolean() is false);
        Check(catalog.GetProperty("mib_bytes").GetInt32() == 1048576);
        Check(catalog.GetProperty("hosted_ci_is_interactive_desktop").GetBoolean() is false);
        Check(catalog.GetProperty("winui_admitted").GetBoolean() is false);
        Check(catalog.GetProperty("herdr_executed").GetBoolean() is false);
        Check(catalog.GetProperty("redaction").GetProperty("credential").GetString() == "omitted");
        Check(catalog.GetProperty("redaction").GetProperty("host").GetString() == "omitted");
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
            if (rel.GetString()!.Contains("soak", StringComparison.Ordinal))
            {
                Check(template.GetProperty("eight_hour_soak_executed").GetBoolean() is false);
                Check(template.GetProperty("soak_hours").ValueKind == JsonValueKind.Null);
            }
        }

        using var soakDoc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "evidence", "quality", "live-soak.not-run.json")));
        var soak = soakDoc.RootElement;
        Check(soak.GetProperty("kind").GetString() == "live_eight_hour_soak");
        Check(soak.GetProperty("result").GetString() == "not_run");
        Check(soak.GetProperty("eight_hour_soak_executed").GetBoolean() is false);
        Check(soak.GetProperty("soak_hours").ValueKind == JsonValueKind.Null);
        Check(soak.GetProperty("invented_timings").GetBoolean() is false);

        using var pixelDoc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "evidence", "quality", "live-input-pixel.not-run.json")));
        var pixel = pixelDoc.RootElement;
        Check(pixel.GetProperty("parser_consumed_is_not_presentation").GetBoolean());
        Check(pixel.GetProperty("parser_callback_cannot_pass_input_to_pixel").GetBoolean());
        Check(pixel.GetProperty("visible_pixel_ms").ValueKind == JsonValueKind.Null);
        Check(pixel.GetProperty("parser_consumed_ms").ValueKind == JsonValueKind.Null);

        using var workingDoc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "evidence", "quality", "live-working-set.not-run.json")));
        var working = workingDoc.RootElement;
        Check(working.GetProperty("derive_process_memory_from_q_p").GetBoolean() is false);
        Check(working.GetProperty("mib_bytes").GetInt32() == 1048576);
        Check(working.GetProperty("working_set_bytes").ValueKind == JsonValueKind.Null);
        Check(working.GetProperty("q_p_bytes").ValueKind == JsonValueKind.Null);

        using var matrixDoc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "evidence", "quality", "support-matrix.json")));
        var matrix = matrixDoc.RootElement;
        Check(matrix.GetProperty("document_kind").GetString() == "hd033_support_matrix");
        Check(matrix.GetProperty("copy_windows_fields_onto_linux").GetBoolean() is false);
        Check(matrix.GetProperty("extrapolate_macos_arm64").GetBoolean() is false);
        Check(matrix.GetProperty("eight_hour_soak_executed").GetBoolean() is false);
        Check(matrix.GetProperty("parser_consumed_is_not_presentation").GetBoolean());
        Check(matrix.GetProperty("mib_bytes").GetInt32() == 1048576);
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
            "cold_start", "input_to_visible_pixel", "search_p95", "working_set",
            "hide_show_100", "narrator", "dpi_theme", "eight_hour_soak"
        ]));
    }

    static void ResidualJson()
    {
        var path = Path.Combine(FindRepoRoot(), "implementation", "hd-033-l2.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        foreach (var key in L3L4Keys)
            Check(root.GetProperty(key).GetString() == "UNVERIFIED");
        foreach (var key in PassKeys)
            Check(root.GetProperty(key).GetBoolean() is false);
        Check(root.GetProperty("phase_gate").GetString() != "passed");
        Check(root.GetProperty("live_soak").GetBoolean() is false);
        Check(root.GetProperty("live_input_pixel").GetBoolean() is false);
        Check(root.GetProperty("live_working_set").GetBoolean() is false);
        Check(root.GetProperty("live_narrator").GetBoolean() is false);
        Check(root.GetProperty("live_dpi").GetBoolean() is false);
        Check(root.GetProperty("eight_hour_soak_executed").GetBoolean() is false);
        Check(root.GetProperty("parser_consumed_is_not_presentation").GetBoolean());
        Check(root.GetProperty("parser_callback_cannot_pass_input_to_pixel").GetBoolean());
        Check(root.GetProperty("derive_process_memory_from_q_p").GetBoolean() is false);
        Check(root.GetProperty("mib_bytes").GetInt32() == 1048576);
        Check(root.GetProperty("winui_admitted").GetBoolean() is false);
        Check(root.GetProperty("integration_windows_project").GetBoolean() is false);
        Check(root.GetProperty("herdr_executed").GetBoolean() is false);
        Check(root.GetProperty("hosted_ci_is_interactive_desktop").GetBoolean() is false);
        Check(root.GetProperty("missing").GetProperty("eight_hour_soak").GetBoolean());
        Check(root.GetProperty("missing").GetProperty("visible_pixel_probe").GetBoolean());
        Check(root.GetProperty("missing").GetProperty("narrator_desktop").GetBoolean());
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
