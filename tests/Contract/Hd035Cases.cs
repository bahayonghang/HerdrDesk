using System.Collections.Generic;
using System.Text.Json;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Clipboard;
using HerdDesk.Infrastructure.Diagnostics;
using HerdDesk.Infrastructure.Ssh;
using HerdDesk.Terminal.Web;

internal static class Hd035Cases
{
    public static (string Name, Action Run)[] All =>
    [
        ("hd-035 catalog inventory and support matrix stay not_run", CatalogAndMatrix),
        ("hd-035 l2 l3 scans renderer canary signed package and product acs stay unverified", ResidualJson)
    ];

    static readonly string[] PassKeys =
    [
        "ac02_passed", "ac43_passed", "ac44_passed", "g0_passed"
    ];

    static readonly string[] L2L3Keys =
    [
        "l2_nuget_scan", "l2_cargo_advisory", "l2_npm_audit",
        "l2_live_renderer_process", "l2_canary_export", "l2_signed_package_unpack",
        "l3_signed_package_reverse_audit"
    ];

    static readonly string[] CardIds =
    [
        "license-inventory", "herdrm-not-copied", "nuget-scan", "cargo-scan",
        "npm-scan", "renderer-boundary", "diagnostic-canary",
        "signed-package-reverse-audit"
    ];

    static readonly Dictionary<string, string[]> CardOwners = new()
    {
        ["license-inventory"] = ["HD-002"],
        ["herdrm-not-copied"] = ["HD-002", "HD-006"],
        ["nuget-scan"] = ["HD-007"],
        ["cargo-scan"] = ["HD-008", "HD-027"],
        ["npm-scan"] = ["HD-014"],
        ["renderer-boundary"] = ["HD-014", "HD-020", "HD-024"],
        ["diagnostic-canary"] = ["HD-031"],
        ["signed-package-reverse-audit"] = ["HD-032", "HD-034"]
    };

    static readonly Dictionary<string, string[]> CardAcs = new()
    {
        ["license-inventory"] = ["AC02"],
        ["herdrm-not-copied"] = ["AC02"],
        ["nuget-scan"] = ["AC43"],
        ["cargo-scan"] = ["AC43"],
        ["npm-scan"] = ["AC43"],
        ["renderer-boundary"] = ["AC44"],
        ["diagnostic-canary"] = ["AC44"],
        ["signed-package-reverse-audit"] = ["AC43"]
    };

    static readonly Dictionary<string, string> CardGrants = new()
    {
        ["license-inventory"] = "no_authorized_maintainer_license_decision",
        ["herdrm-not-copied"] = "no_authorized_herdrm_reverse_audit_of_release_inputs",
        ["nuget-scan"] = "no_authorized_nuget_advisory_scan",
        ["cargo-scan"] = "no_authorized_cargo_advisory_scan",
        ["npm-scan"] = "no_authorized_npm_audit",
        ["renderer-boundary"] = "no_authorized_webview_process_observation",
        ["diagnostic-canary"] = "no_authorized_canary_diagnostic_export",
        ["signed-package-reverse-audit"] = "no_authorized_signed_package_unpack"
    };

    static readonly Dictionary<string, string> LiveGrants = new()
    {
        ["live-license-inventory"] = "no_authorized_maintainer_license_decision",
        ["live-herdrm-not-copied"] = "no_authorized_herdrm_reverse_audit_of_release_inputs",
        ["live-nuget-scan"] = "no_authorized_nuget_advisory_scan",
        ["live-cargo-scan"] = "no_authorized_cargo_advisory_scan",
        ["live-npm-scan"] = "no_authorized_npm_audit",
        ["live-renderer-boundary"] = "no_authorized_webview_process_observation",
        ["live-diagnostic-canary"] = "no_authorized_canary_diagnostic_export",
        ["live-signed-package-reverse-audit"] = "no_authorized_signed_package_unpack"
    };

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void CatalogAndMatrix()
    {
        Check(typeof(WebMessagePolicy).IsClass);
        Check(typeof(WebMessageValidator).IsClass);
        Check(typeof(WebViewSecurityPolicy).IsClass);
        Check(typeof(OscClipboardPolicy).IsClass);
        Check(typeof(PasteCoordinator).IsClass);
        Check(typeof(AttachmentCache).IsClass);
        Check(typeof(DiagnosticEventValidator).IsClass);
        Check(typeof(SshFailureClassifier).IsClass);
        Check(typeof(SshProcessSpecFactory).IsClass);

        var root = FindRepoRoot();
        AppXamlSurface.CheckIntegrationWindowsProject(root);
        Check(!File.Exists(Path.Combine(root, "web", "terminal", "package-lock.json")));
        Check(!File.Exists(Path.Combine(root, "packages.lock.json")));
        Check(File.Exists(Path.Combine(root, "bridge", "Cargo.lock")));
        Check(File.Exists(Path.Combine(root, "filebridge", "Cargo.lock")));
        Check(File.Exists(Path.Combine(root, "docs", "licensing", "register.json")));
        Check(File.Exists(Path.Combine(root, "docs", "licensing-register.md")));

        using var catalogDoc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "evidence", "security-release", "catalog.json")));
        var catalog = catalogDoc.RootElement;
        Check(catalog.GetProperty("document_kind").GetString() == "hd035_security_release_catalog");
        foreach (var key in L2L3Keys)
            Check(catalog.GetProperty(key).GetString() == "UNVERIFIED");
        Check(catalog.GetProperty("nuget_scan_executed").GetBoolean() is false);
        Check(catalog.GetProperty("cargo_advisory_executed").GetBoolean() is false);
        Check(catalog.GetProperty("npm_audit_executed").GetBoolean() is false);
        Check(catalog.GetProperty("live_renderer_process_observed").GetBoolean() is false);
        Check(catalog.GetProperty("canary_export_executed").GetBoolean() is false);
        Check(catalog.GetProperty("signed_package_unpacked").GetBoolean() is false);
        Check(catalog.GetProperty("project_license_selected").GetBoolean() is false);
        Check(catalog.GetProperty("herdrm_copied").GetBoolean() is false);
        Check(catalog.GetProperty("public_visibility_is_not_license_grant").GetBoolean());
        Check(catalog.GetProperty("missing_scan_is_not_zero_vuln").GetBoolean());
        Check(catalog.GetProperty("confirmed_exploitable_critical_high_must_not_be_hidden_by_exception").GetBoolean());
        Check(catalog.GetProperty("winui_admitted").GetBoolean() is false);
        Check(catalog.GetProperty("herdr_executed").GetBoolean() is false);
        Check(catalog.GetProperty("integration_windows_project").GetBoolean() is false);
        Check(catalog.GetProperty("redaction").GetProperty("credential").GetString() == "omitted");
        Check(catalog.GetProperty("redaction").GetProperty("host").GetString() == "omitted");
        Check(catalog.GetProperty("scan_tool_version").ValueKind == JsonValueKind.Null);
        Check(catalog.GetProperty("advisory_database_date").ValueKind == JsonValueKind.Null);
        Check(catalog.GetProperty("confirmed_exploitable_critical_high").ValueKind == JsonValueKind.Null);
        Check(catalog.GetProperty("package_sha256").ValueKind == JsonValueKind.Null);
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
            Check(template.GetProperty("scan_tool_version").ValueKind == JsonValueKind.Null);
            Check(template.GetProperty("advisory_database_date").ValueKind == JsonValueKind.Null);
            Check(template.GetProperty("herdr_executed").GetBoolean() is false);
            Check(!template.TryGetProperty("stdout", out _));
            Check(!template.TryGetProperty("stderr", out _));
        }

        using var inventoryDoc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "evidence", "security-release", "inventory.json")));
        var inventory = inventoryDoc.RootElement;
        Check(inventory.GetProperty("document_kind").GetString() == "hd035_security_release_inventory");
        Check(inventory.GetProperty("final_unpacked_msix").GetBoolean() is false);
        Check(inventory.GetProperty("project_license_selected").GetBoolean() is false);
        Check(inventory.GetProperty("herdrm_copied").GetBoolean() is false);
        Check(inventory.GetProperty("ac02_passed").GetBoolean() is false);
        var allowedLock = new HashSet<string>(StringComparer.Ordinal)
        {
            "Microsoft.WindowsAppSDK.WinUI",
            "Microsoft.WindowsAppSDK.Base",
            "Microsoft.WindowsAppSDK.Foundation",
            "Microsoft.WindowsAppSDK.InteractiveExperiences",
            "Microsoft.Web.WebView2",
            "Microsoft.Windows.SDK.BuildTools",
            "Microsoft.Windows.SDK.BuildTools.MSIX",
        };
        foreach (var unit in inventory.GetProperty("units").EnumerateArray())
        {
            var admission = unit.GetProperty("admission").GetString();
            var name = unit.GetProperty("name").GetString();
            Check(admission is "pending" or "blocked" or "approved");
            if (admission == "approved")
            {
                Check(!string.IsNullOrEmpty(name));
                Check(name != "herdrm");
                Check(unit.GetProperty("artifact_kind").GetString() == "prebuilt_binary");
                Check(allowedLock.Contains(name!));
            }
        }

        using var matrixDoc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "evidence", "security-release", "support-matrix.json")));
        var matrix = matrixDoc.RootElement;
        Check(matrix.GetProperty("document_kind").GetString() == "hd035_support_matrix");
        Check(matrix.GetProperty("copy_windows_fields_onto_linux").GetBoolean() is false);
        Check(matrix.GetProperty("extrapolate_macos_arm64").GetBoolean() is false);
        Check(matrix.GetProperty("linux_msix_client").GetBoolean() is false);
        Check(matrix.GetProperty("linux_x64_is_not_windows_renderer_substitute").GetBoolean());
        foreach (var key in PassKeys)
            Check(matrix.GetProperty(key).GetBoolean() is false);
        var platforms = matrix.GetProperty("platforms").EnumerateArray()
            .ToDictionary(item => item.GetProperty("id").GetString()!, item => item);
        Check(platforms["windows-11-x64-client"].GetProperty("support").GetString() == "promised_not_run");
        Check(platforms["linux-x64-remote"].GetProperty("support").GetString() == "unsupported");
        Check(platforms["linux-x64-remote"].GetProperty("promise").GetString() == "none");
        Check(platforms["linux-x64-remote"].GetProperty("linux_renderer_observation").GetBoolean() is false);
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
            Check(cell.GetProperty("platform").GetString() == "windows-11-x64-client");
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
            "license_inventory", "herdrm_not_copied", "nuget_scan", "cargo_scan",
            "npm_scan", "renderer_boundary", "diagnostic_canary",
            "signed_package_reverse_audit"
        ]));
        var promised = matrix.GetProperty("promised_range").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()!).ToArray();
        Check(promised.SequenceEqual(["windows-11-x64-client"]));
    }

    static void ResidualJson()
    {
        var path = Path.Combine(FindRepoRoot(), "implementation", "hd-035-l2.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        foreach (var key in L2L3Keys)
            Check(root.GetProperty(key).GetString() == "UNVERIFIED");
        foreach (var key in PassKeys)
            Check(root.GetProperty(key).GetBoolean() is false);
        Check(root.GetProperty("phase_gate").GetString() != "passed");
        Check(root.GetProperty("nuget_scan_executed").GetBoolean() is false);
        Check(root.GetProperty("live_renderer_process_observed").GetBoolean() is false);
        Check(root.GetProperty("canary_export_executed").GetBoolean() is false);
        Check(root.GetProperty("signed_package_unpacked").GetBoolean() is false);
        Check(root.GetProperty("project_license_selected").GetBoolean() is false);
        Check(root.GetProperty("herdrm_copied").GetBoolean() is false);
        Check(root.GetProperty("winui_admitted").GetBoolean() is false);
        Check(root.GetProperty("integration_windows_project").GetBoolean() is false);
        Check(root.GetProperty("herdr_executed").GetBoolean() is false);
        Check(root.GetProperty("missing").GetProperty("maintainer_license_decision").GetBoolean());
        Check(root.GetProperty("missing").GetProperty("nuget_advisory_scan").GetBoolean());
        Check(root.GetProperty("missing").GetProperty("signed_package_unpack").GetBoolean());
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
