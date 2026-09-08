using HerdDesk.App;
using HerdDesk.Contracts;

internal static class HelperInstallViewModelTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("security text includes target version hash and directory", SecurityText),
        ("cancel does not leave always-allow consent", CancelNoAlwaysAllow),
        ("confirm without a plan does not call deploy", NoPlanNoConfirm)
    ];

    static void SecurityText()
    {
        var service = new FakeHelperService();
        var vm = new HelperInstallViewModel(service);
        vm.PlanAsync(AppTestHost.DeviceA, AppTestHost.SessionOf(AppTestHost.DeviceA), SampleSettings())
            .AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(vm.CanConfirm);
        AppTestHost.Check(vm.Phase == HelperDeploymentPhase.AwaitingConsent);
        AppTestHost.Check(vm.SecurityText.Contains("x86_64-unknown-linux-gnu", StringComparison.Ordinal));
        AppTestHost.Check(vm.SecurityText.Contains("0.1.0", StringComparison.Ordinal));
        AppTestHost.Check(vm.SecurityText.Contains("/home/lab/.herddesk/helper/", StringComparison.Ordinal));
        AppTestHost.Check(vm.SecurityText.Contains("hardlink-noclobber", StringComparison.Ordinal));
        vm.ConfirmAsync().AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(service.ConfirmCalls == 1);
        AppTestHost.Check(vm.Phase == HelperDeploymentPhase.Succeeded);
    }

    static void CancelNoAlwaysAllow()
    {
        var service = new FakeHelperService();
        var vm = new HelperInstallViewModel(service);
        AppTestHost.Check(vm.AlwaysAllow is false);
        AppTestHost.Check(typeof(HelperInstallViewModel).GetProperty("AlwaysInstall") is null);
        vm.PlanAsync(AppTestHost.DeviceA, AppTestHost.SessionOf(AppTestHost.DeviceA), SampleSettings())
            .AsTask().GetAwaiter().GetResult();
        vm.CancelAsync().AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(service.CancelCalls == 1);
        AppTestHost.Check(vm.AlwaysAllow is false);
        AppTestHost.Check(vm.Phase == HelperDeploymentPhase.Cancelled);
    }

    static void NoPlanNoConfirm()
    {
        var service = new FakeHelperService();
        var vm = new HelperInstallViewModel(service);
        vm.ConfirmAsync().AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(service.ConfirmCalls == 0);
        AppTestHost.Check(vm.ErrorCode == HelperCodes.ConsentRequired);
    }

    static SshDeviceSettings SampleSettings() =>
        SshDeviceSettings.Create("lab", SshAuthMode.OpenSshConfig, "/usr/bin/herdr");

    sealed class FakeHelperService : IHelperDeploymentService
    {
        public int ConfirmCalls { get; private set; }
        public int CancelCalls { get; private set; }
        public HelperDeploymentPhase Phase { get; private set; } = HelperDeploymentPhase.Idle;
        public DeploymentPlan? Plan { get; private set; }

        public ValueTask<HelperDeploymentResult> PlanAsync(
            DeviceId device, SessionKey session, SshDeviceSettings settings,
            CancellationToken cancellationToken = default)
        {
            _ = (settings, cancellationToken);
            var hash = new string('a', 64);
            Plan = new DeploymentPlan(
                device,
                session,
                new HelperTarget(
                    "x86_64-unknown-linux-gnu", "linux", "x86_64", "linux", "x86_64",
                    "herddesk-bridge-x86_64-unknown-linux-gnu", true, "hardlink_noclobber"),
                "0.1.0",
                hash,
                12,
                "/home/lab/.herddesk/helper/0.1.0/x86_64-unknown-linux-gnu",
                new string('b', 64),
                "sha256sum",
                ["probe-os-arch", "write-private-staging", "hardlink-noclobber"],
                "a*******",
                hash[..12]);
            Phase = HelperDeploymentPhase.AwaitingConsent;
            return new(new HelperDeploymentResult(Phase, HelperCodes.ConsentRequired, Plan, null, []));
        }

        public ValueTask<HelperDeploymentResult> ConfirmAsync(
            HelperConsentValues consent, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            ConfirmCalls++;
            AppTestHost.Check(Plan is not null);
            AppTestHost.Check(consent.Device == Plan!.Device);
            AppTestHost.Check(consent.Version == Plan.Version);
            AppTestHost.Check(consent.Sha256 == Plan.Sha256);
            Phase = HelperDeploymentPhase.Succeeded;
            return new(new HelperDeploymentResult(Phase, null, Plan, null, []));
        }

        public ValueTask<HelperDeploymentResult> CancelAsync()
        {
            CancelCalls++;
            Phase = HelperDeploymentPhase.Cancelled;
            return new(new HelperDeploymentResult(Phase, HelperCodes.Cancelled, Plan, null, []));
        }

        public ValueTask<HelperDeploymentResult> RollbackAsync(
            HelperReceiptKey key, CancellationToken cancellationToken = default)
        {
            _ = (key, cancellationToken);
            Phase = HelperDeploymentPhase.RolledBack;
            return new(new HelperDeploymentResult(Phase, null, Plan, null, []));
        }
    }
}
