using System.Text.Json;
using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Ssh;

internal static class Hd021Cases
{
    public static (string Name, Action Run)[] All =>
    [
        ("hd-021 helper install ships without winui live ssh or always-allow", Surface),
        ("hd-021 l2 live deploy and ac25 stay unverified", ResidualJson)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void Surface()
    {
        Check(typeof(RemoteHelperDeploymentService).IsClass);
        Check(typeof(HelperInstallViewModel).IsClass);
        Check(typeof(TrustedHelperManifestProvider).IsClass);
        Check(typeof(HelperInstallViewModel).GetProperty("Argv") is null);
        Check(typeof(HelperInstallViewModel).GetProperty("ArgumentList") is null);
        Check(typeof(HelperInstallViewModel).GetProperty("AlwaysInstall") is null);
        Check(typeof(HelperDeploymentResult).GetProperty("Stderr") is null);
        Check(typeof(HelperDeploymentResult).GetProperty("Argv") is null);
        var vm = new HelperInstallViewModel(new UnavailableHelperService());
        Check(vm.AlwaysAllow is false);
        var root = FindRepoRoot();
        Check(!Directory.Exists(Path.Combine(root, "tests", "Integration.Ssh")));
        Check(!File.Exists(Path.Combine(root, "src", "HerdDesk.App", "Devices", "HelperInstallDialog.xaml")));
        Check(!Directory.EnumerateFiles(Path.Combine(root, "src", "HerdDesk.App"), "*.xaml",
            SearchOption.AllDirectories).Any());
    }

    static void ResidualJson()
    {
        var path = Path.Combine(FindRepoRoot(), "implementation", "hd-021-l2.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Check(root.GetProperty("l2_live_helper_deploy").GetString() == "UNVERIFIED");
        Check(root.GetProperty("ac25_passed").GetBoolean() is false);
        Check(root.GetProperty("g0_passed").GetBoolean() is false);
        Check(root.GetProperty("phase_gate").GetString() != "passed");
        Check(root.GetProperty("live_ssh").GetBoolean() is false);
        Check(root.GetProperty("live_remote_install").GetBoolean() is false);
        Check(root.GetProperty("sudo").GetBoolean() is false);
        Check(root.GetProperty("winui_admitted").GetBoolean() is false);
        Check(root.GetProperty("integration_ssh_project").GetBoolean() is false);
        foreach (var target in root.GetProperty("targets").EnumerateObject())
            Check(target.Value.GetString() is "not_run" or "UNVERIFIED");
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

    sealed class UnavailableHelperService : IHelperDeploymentService
    {
        public HelperDeploymentPhase Phase => HelperDeploymentPhase.Idle;
        public DeploymentPlan? Plan => null;

        public ValueTask<HelperDeploymentResult> PlanAsync(
            DeviceId device, SessionKey session, SshDeviceSettings settings,
            CancellationToken cancellationToken = default)
        {
            _ = (device, session, settings, cancellationToken);
            return new(new HelperDeploymentResult(
                HelperDeploymentPhase.Failed, HelperCodes.PlatformUnsupported, null, null, []));
        }

        public ValueTask<HelperDeploymentResult> ConfirmAsync(
            HelperConsentValues consent, CancellationToken cancellationToken = default)
        {
            _ = (consent, cancellationToken);
            return new(new HelperDeploymentResult(
                HelperDeploymentPhase.Failed, HelperCodes.ConsentRequired, null, null, []));
        }

        public ValueTask<HelperDeploymentResult> CancelAsync() =>
            new(new HelperDeploymentResult(HelperDeploymentPhase.Cancelled, HelperCodes.Cancelled, null, null, []));

        public ValueTask<HelperDeploymentResult> RollbackAsync(
            HelperReceiptKey key, CancellationToken cancellationToken = default)
        {
            _ = (key, cancellationToken);
            return new(new HelperDeploymentResult(
                HelperDeploymentPhase.Failed, HelperCodes.ActivationFailed, null, null, []));
        }
    }
}
