using System.Text.Json;
using HerdDesk.App;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Configuration;
using HerdDesk.Infrastructure.Ssh;

internal static class Hd034Cases
{
    public static (string Name, Action Run)[] All =>
    [
        ("hd-034 catalog and support matrix stay not_run", CatalogAndMatrix),
        ("hd-034 l2 l3 live install sign update rollback and product acs stay unverified", ResidualJson)
    ];

    static readonly string[] PassKeys =
    [
        "ac41_passed", "ac42_passed", "g0_passed"
    ];

    static readonly string[] L2L3Keys =
    [
        "l2_live_install", "l2_live_sign", "l2_live_update", "l2_live_rollback",
        "l3_clean_machine"
    ];

    static readonly string[] CardIds =
    [
        "clean-install", "runtime-missing", "signed-update",
        "bad-publisher-or-tamper", "signed-rollback", "config-backup-restore",
        "file-job-defer", "unsigned-local-build"
    ];

    static readonly Dictionary<string, string[]> CardOwners = new()
    {
        ["clean-install"] = ["HD-007", "HD-011"],
        ["runtime-missing"] = ["HD-007"],
        ["signed-update"] = ["HD-007"],
        ["bad-publisher-or-tamper"] = ["HD-021", "HD-007"],
        ["signed-rollback"] = ["HD-007"],
        ["config-backup-restore"] = ["HD-007"],
        ["file-job-defer"] = ["HD-028"],
        ["unsigned-local-build"] = ["HD-007"]
    };

    static readonly Dictionary<string, string[]> CardAcs = new()
    {
        ["clean-install"] = ["AC41"],
        ["runtime-missing"] = ["AC41"],
        ["signed-update"] = ["AC42"],
        ["bad-publisher-or-tamper"] = ["AC42"],
        ["signed-rollback"] = ["AC42"],
        ["config-backup-restore"] = ["AC42"],
        ["file-job-defer"] = ["AC42"],
        ["unsigned-local-build"] = ["AC41"]
    };

    static readonly Dictionary<string, string> CardGrants = new()
    {
        ["clean-install"] = "no_authorized_clean_machine_install",
        ["runtime-missing"] = "no_authorized_runtime_missing_vm",
        ["signed-update"] = "no_authorized_signed_update_channel",
        ["bad-publisher-or-tamper"] = "no_authorized_publisher_identity",
        ["signed-rollback"] = "no_authorized_signed_rollback",
        ["config-backup-restore"] = "no_authorized_config_restore_on_install",
        ["file-job-defer"] = "no_authorized_file_job_defer_during_update",
        ["unsigned-local-build"] = "no_authorized_signing_service"
    };

    static readonly Dictionary<string, string> LiveGrants = new()
    {
        ["live-clean-install"] = "no_authorized_clean_machine_install",
        ["live-runtime-missing"] = "no_authorized_runtime_missing_vm",
        ["live-signed-update"] = "no_authorized_signed_update_channel",
        ["live-bad-publisher-or-tamper"] = "no_authorized_publisher_identity",
        ["live-signed-rollback"] = "no_authorized_signed_rollback",
        ["live-config-backup-restore"] = "no_authorized_config_restore_on_install",
        ["live-file-job-defer"] = "no_authorized_file_job_defer_during_update",
        ["live-unsigned-local-build"] = "no_authorized_signing_service"
    };

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void CatalogAndMatrix()
    {
        Check(typeof(ShellViewModel).IsClass);
        Check(typeof(SettingsViewModel).IsClass);
        Check(typeof(AtomicConfigurationStore).IsClass);
        Check(typeof(TransferCoordinator).IsClass);
        Check(typeof(HelperInstallViewModel).IsClass);
        Check(typeof(TrustedHelperManifestProvider).IsClass);
        Check(typeof(ProductInfo).IsClass);

        var root = FindRepoRoot();
        AppXamlSurface.CheckIntegrationWindowsProject(root);
        Check(Directory.Exists(Path.Combine(root, "packaging")));
        Check(File.Exists(Path.Combine(root, "packaging", "Package.appxmanifest")));
        Check(File.Exists(Path.Combine(root, "packaging", "runtime.json")));
        Check(File.Exists(Path.Combine(root, "scripts", "package_release.ps1")));
        Check(File.Exists(Path.Combine(root, "scripts", "new_lab_certificate.ps1")));
        Check(File.Exists(Path.Combine(root, "scripts", "record_lab_msix.py")));
        Check(File.Exists(Path.Combine(root, "evidence", "packaging", "lab-sign-overlay-pointer.json")));
        Check(File.Exists(Path.Combine(root, "src", "HerdDesk.App", "App.xaml")));
        Check(!File.Exists(Path.Combine(root, "packaging", "HerdDesk.Package.wapproj")));
        AppXamlSurface.CheckBlankContainerOnly(root);

        using var catalogDoc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "evidence", "packaging", "catalog.json")));
        var catalog = catalogDoc.RootElement;
        Check(catalog.GetProperty("document_kind").GetString() == "hd034_packaging_catalog");
        foreach (var key in L2L3Keys)
            Check(catalog.GetProperty(key).GetString() == "UNVERIFIED");
        Check(catalog.GetProperty("live_install").GetBoolean() is false);
        Check(catalog.GetProperty("live_sign").GetBoolean() is false);
        Check(catalog.GetProperty("live_update").GetBoolean() is false);
        Check(catalog.GetProperty("live_rollback").GetBoolean() is false);
        Check(catalog.GetProperty("publisher_identity_confirmed").GetBoolean() is false);
        Check(catalog.GetProperty("signed_msix_built").GetBoolean() is false);
        Check(catalog.GetProperty("clean_machine_install_executed").GetBoolean() is false);
        Check(catalog.GetProperty("unsigned_local_build_is_release").GetBoolean() is false);
        Check(catalog.GetProperty("exe_copy_is_rollback").GetBoolean() is false);
        Check(catalog.GetProperty("linux_msix_client").GetBoolean() is false);
        Check(catalog.GetProperty("fake_publisher_cannot_pass_ac41").GetBoolean());
        Check(catalog.GetProperty("unsigned_local_build_cannot_pass_release_install").GetBoolean());
        Check(catalog.GetProperty("exe_copy_cannot_pass_rollback").GetBoolean());
        Check(catalog.GetProperty("winui_admitted").GetBoolean() is false);
        Check(catalog.GetProperty("herdr_executed").GetBoolean() is false);
        Check(catalog.GetProperty("integration_windows_project").GetBoolean() is false);
        Check(catalog.GetProperty("packaging_project").GetBoolean() is false);
        Check(catalog.GetProperty("lab_sign_overlay_pointer").GetString()
              == "evidence/packaging/lab-sign-overlay-pointer.json");
        Check(catalog.GetProperty("lab_msix_capture").GetString()
              == "evidence/packaging/live-lab-msix.json");
        using var pointerDoc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "evidence", "packaging", "lab-sign-overlay-pointer.json")));
        var pointer = pointerDoc.RootElement;
        Check(pointer.GetProperty("document_kind").GetString() == "hd034_lab_sign_overlay_pointer");
        Check(pointer.GetProperty("result").GetString() == "not_run");
        Check(pointer.GetProperty("l2_live_sign").GetString() == "UNVERIFIED");
        Check(pointer.GetProperty("script").GetString() == "scripts/new_lab_certificate.ps1");
        Check(pointer.GetProperty("is_release_install").GetBoolean() is false);
        Check(pointer.GetProperty("signed_msix_built").GetBoolean() is false);
        Check(pointer.GetProperty("publisher_identity_confirmed").GetBoolean() is false);
        Check(pointer.GetProperty("ac41_passed").GetBoolean() is false);
        Check(pointer.GetProperty("ac42_passed").GetBoolean() is false);
        Check(pointer.GetProperty("g0_passed").GetBoolean() is false);
        Check(pointer.GetProperty("pfx_in_git").GetBoolean() is false);
        Check(pointer.GetProperty("pfx_written").GetBoolean() is false);
        Check(pointer.GetProperty("lab_certificate_script_is_not_signed_release_msix").GetBoolean());
        Check(pointer.GetProperty("live_sign").GetBoolean() is false);
        Check(pointer.GetProperty("pfx_written").GetBoolean() is false);
        var overlayPath = Path.Combine(root, "evidence", "packaging", "live-lab-msix.json");
        if (File.Exists(overlayPath))
        {
            using var overlayDoc = JsonDocument.Parse(File.ReadAllText(overlayPath));
            var overlay = overlayDoc.RootElement;
            Check(overlay.GetProperty("document_kind").GetString() == "hd034_lab_msix");
            Check(overlay.GetProperty("result").GetString() == "not_run");
            Check(overlay.GetProperty("ac41_passed").GetBoolean() is false);
            Check(overlay.GetProperty("ac42_passed").GetBoolean() is false);
            Check(overlay.GetProperty("g0_passed").GetBoolean() is false);
            Check(overlay.GetProperty("signed_msix_built").GetBoolean() is false);
            Check(overlay.GetProperty("live_sign").GetBoolean() is false);
            Check(overlay.GetProperty("live_install").GetBoolean() is false);
            Check(overlay.GetProperty("lab_msix_packed").GetBoolean());
            Check(overlay.GetProperty("lab_signature_applied").GetBoolean());
            Check(overlay.GetProperty("publisher").ValueKind == JsonValueKind.Null);
            Check(overlay.GetProperty("msix_sha256").ValueKind == JsonValueKind.Null);
            Check(overlay.GetProperty("package_sha256").ValueKind == JsonValueKind.Null);
            Check(overlay.GetProperty("certificate_subject").ValueKind == JsonValueKind.Null);
            Check(overlay.GetProperty("certificate_thumbprint").ValueKind == JsonValueKind.Null);
            Check(overlay.GetProperty("timestamp_url").ValueKind == JsonValueKind.Null);
        }

        Check(catalog.GetProperty("redaction").GetProperty("credential").GetString() == "omitted");
        Check(catalog.GetProperty("redaction").GetProperty("host").GetString() == "omitted");
        Check(catalog.GetProperty("publisher").ValueKind == JsonValueKind.Null);
        Check(catalog.GetProperty("package_sha256").ValueKind == JsonValueKind.Null);
        Check(catalog.GetProperty("install_hours").ValueKind == JsonValueKind.Null);
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
            Check(template.GetProperty("package_sha256").ValueKind == JsonValueKind.Null);
            Check(template.GetProperty("publisher").ValueKind == JsonValueKind.Null);
            Check(!template.TryGetProperty("stdout", out _));
            Check(!template.TryGetProperty("stderr", out _));
            if (rel.GetString()!.Contains("unsigned-local-build", StringComparison.Ordinal))
            {
                Check(template.GetProperty("unsigned_local_build_is_release").GetBoolean() is false);
                Check(template.GetProperty("unsigned_local_build_cannot_pass_release_install").GetBoolean());
            }

            if (rel.GetString()!.Contains("signed-rollback", StringComparison.Ordinal))
            {
                Check(template.GetProperty("exe_copy_is_rollback").GetBoolean() is false);
                Check(template.GetProperty("exe_copy_cannot_pass_rollback").GetBoolean());
            }
        }

        using var unsignedDoc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "evidence", "packaging", "live-unsigned-local-build.not-run.json")));
        var unsigned = unsignedDoc.RootElement;
        Check(unsigned.GetProperty("kind").GetString() == "live_unsigned_local_build");
        Check(unsigned.GetProperty("result").GetString() == "not_run");
        Check(unsigned.GetProperty("unsigned_local_build_is_release").GetBoolean() is false);
        Check(unsigned.GetProperty("package_sha256").ValueKind == JsonValueKind.Null);

        using var rollbackDoc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "evidence", "packaging", "live-signed-rollback.not-run.json")));
        var rollback = rollbackDoc.RootElement;
        Check(rollback.GetProperty("exe_copy_is_rollback").GetBoolean() is false);
        Check(rollback.GetProperty("exe_copy_cannot_pass_rollback").GetBoolean());

        using var matrixDoc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "evidence", "packaging", "support-matrix.json")));
        var matrix = matrixDoc.RootElement;
        Check(matrix.GetProperty("document_kind").GetString() == "hd034_support_matrix");
        Check(matrix.GetProperty("copy_windows_fields_onto_linux").GetBoolean() is false);
        Check(matrix.GetProperty("extrapolate_macos_arm64").GetBoolean() is false);
        Check(matrix.GetProperty("linux_msix_client").GetBoolean() is false);
        foreach (var key in PassKeys)
            Check(matrix.GetProperty(key).GetBoolean() is false);
        var platforms = matrix.GetProperty("platforms").EnumerateArray()
            .ToDictionary(item => item.GetProperty("id").GetString()!, item => item);
        Check(platforms["windows-11-x64-client"].GetProperty("support").GetString() == "promised_not_run");
        Check(platforms["linux-x64-remote"].GetProperty("support").GetString() == "unsupported");
        Check(platforms["linux-x64-remote"].GetProperty("promise").GetString() == "none");
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
            "clean_install", "runtime_missing", "signed_update",
            "bad_publisher_or_tamper", "signed_rollback", "config_backup_restore",
            "file_job_defer", "unsigned_local_build"
        ]));
        var promised = matrix.GetProperty("promised_range").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()!).ToArray();
        Check(promised.SequenceEqual(["windows-11-x64-client"]));
    }

    static void ResidualJson()
    {
        var path = Path.Combine(FindRepoRoot(), "implementation", "hd-034-l2.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        foreach (var key in L2L3Keys)
            Check(root.GetProperty(key).GetString() == "UNVERIFIED");
        foreach (var key in PassKeys)
            Check(root.GetProperty(key).GetBoolean() is false);
        Check(root.GetProperty("phase_gate").GetString() != "passed");
        Check(root.GetProperty("live_install").GetBoolean() is false);
        Check(root.GetProperty("live_sign").GetBoolean() is false);
        Check(root.GetProperty("live_update").GetBoolean() is false);
        Check(root.GetProperty("live_rollback").GetBoolean() is false);
        Check(root.GetProperty("publisher_identity_confirmed").GetBoolean() is false);
        Check(root.GetProperty("signed_msix_built").GetBoolean() is false);
        Check(root.GetProperty("winui_admitted").GetBoolean() is false);
        Check(root.GetProperty("integration_windows_project").GetBoolean() is false);
        Check(root.GetProperty("herdr_executed").GetBoolean() is false);
        Check(root.GetProperty("linux_msix_client").GetBoolean() is false);
        Check(root.GetProperty("packaging_project").GetBoolean() is false);
        Check(root.GetProperty("missing").GetProperty("clean_machine_install").GetBoolean());
        Check(root.GetProperty("missing").GetProperty("publisher_identity").GetBoolean());
        Check(root.GetProperty("missing").GetProperty("signing_service").GetBoolean());
        Check(root.GetProperty("missing").GetProperty("packaging_project").GetBoolean());
        Check(root.GetProperty("unsigned_local_build_is_release").GetBoolean() is false);
        Check(root.GetProperty("invented_publisher_identity").GetBoolean() is false);
        Check(root.GetProperty("lab_sign_overlay_pointer").GetString()
              == "evidence/packaging/lab-sign-overlay-pointer.json");
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
