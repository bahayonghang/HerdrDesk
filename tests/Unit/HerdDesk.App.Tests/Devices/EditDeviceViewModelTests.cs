using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Configuration;

internal static class EditDeviceViewModelTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("device id is stable across save reopen and label edit", DeviceIdRoundTrip),
        ("viewmodel has no raw argv surface", NoRawArgv),
        ("unsupported auth is visible and does not start a probe", UnsupportedVisible),
        ("unknown host waits for confirm; cancel does not write", UnknownCancel),
        ("changed key is hard blocked", ChangedBlocked),
        ("cancel after idle does not overwrite a succeeded result", CancelIdleKeepsSuccess)
    ];

    static void DeviceIdRoundTrip()
    {
        var root = AppTestHost.TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            Directory.CreateDirectory(paths.SettingsDirectory);
            var store = new AtomicConfigurationStore(paths);
            var tester = new FakeSshTester();
            var vm = new EditDeviceViewModel(store, tester);
            vm.LoadAsync().AsTask().GetAwaiter().GetResult();
            var device = AppTestHost.DeviceA;
            vm.BeginNewDevice(device);
            vm.SetLabel("ssh-lab");
            vm.SetHerdrPath(Path.Combine(paths.Root, "herdr"));
            vm.SetHostAlias("lab");
            vm.SetUser("git");
            vm.SetPortText("2222");
            vm.SetRemoteHerdrPath("/usr/bin/herdr");
            vm.SetAuthModeRaw("open_ssh_config");
            vm.AddNamedSession("dev");
            vm.AddExplicitEndpoint(AppTestHost.ExplicitLocation(), AppTestHost.ExplicitKind());
            vm.SaveAsync().AsTask().GetAwaiter().GetResult();
            AppTestHost.Check(vm.Lifecycle == SettingsLifecycle.Saved);
            AppTestHost.Check(vm.CommittedDevice!.Device == device);
            AppTestHost.Check(vm.CommittedDevice.Sessions.Count == 2);
            var first = vm.CommittedDevice.Sessions[0].ToSessionKey(device);
            var second = vm.CommittedDevice.Sessions[1].ToSessionKey(device);
            AppTestHost.Check(first != second);
            var reopened = new EditDeviceViewModel(store, tester);
            reopened.LoadAsync().AsTask().GetAwaiter().GetResult();
            AppTestHost.Check(reopened.Draft.Device == device);
            reopened.SetLabel("renamed");
            reopened.SaveAsync().AsTask().GetAwaiter().GetResult();
            AppTestHost.Check(reopened.CommittedDevice!.Device == device);
            AppTestHost.Check(reopened.CommittedDevice.Label == "renamed");
            AppTestHost.Check(reopened.CommittedDevice.Ssh!.HostAlias == "lab");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void NoRawArgv()
    {
        var type = typeof(EditDeviceViewModel);
        AppTestHost.Check(type.GetProperty("Argv") is null);
        AppTestHost.Check(type.GetProperty("ArgumentList") is null);
        AppTestHost.Check(type.GetProperty("CommandLine") is null);
        AppTestHost.Check(type.GetProperty("RawStderr") is null);
        foreach (var method in type.GetMethods())
        {
            AppTestHost.Check(method.Name is not "SetArgv" and not "RunArgv");
            foreach (var parameter in method.GetParameters())
                AppTestHost.Check(parameter.Name is not "argv" and not "argumentList");
        }

        var store = new MemoryDeviceProfileStore();
        var tester = new FakeSshTester();
        var vm = new EditDeviceViewModel(store, tester);
        AppTestHost.Check(vm.Unsupported.Count == 6);
        AppTestHost.Check(vm.Unsupported.All(item => item.Status == "未支持"));
    }

    static void UnsupportedVisible()
    {
        var store = new MemoryDeviceProfileStore();
        var tester = new FakeSshTester();
        var vm = NewDraft(store, tester);
        vm.SetAuthModeRaw("password");
        vm.TestUntilHostKeyAsync().AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(vm.ErrorCode == SshCodes.AuthUnsupported);
        AppTestHost.Check(tester.AuthenticateCalls == 0);
        AppTestHost.Check(tester.TestCalls == 0);
        AppTestHost.Check(vm.Unsupported.Any(item => item.Kind == SshUnsupportedAuthKind.PasswordArgvOrStdin));
    }

    static void UnknownCancel()
    {
        var store = new MemoryDeviceProfileStore();
        var tester = new FakeSshTester
        {
            Next = UnknownResult()
        };
        var vm = NewDraft(store, tester);
        vm.TestUntilHostKeyAsync().AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(vm.Lifecycle == SettingsLifecycle.AwaitingHostKey);
        AppTestHost.Check(vm.HostKeyReview!.CanConfirm);
        AppTestHost.Check(vm.HostKeyReview.Prompt.Contains("侧信道", StringComparison.Ordinal));
        vm.CancelHostKeyReview();
        AppTestHost.Check(tester.ConfirmCalls == 0);
        AppTestHost.Check(vm.LastTest!.Phase == SshConnectionTestPhase.Cancelled);
    }

    static void ChangedBlocked()
    {
        var store = new MemoryDeviceProfileStore();
        var tester = new FakeSshTester { Next = ChangedResult() };
        var vm = NewDraft(store, tester);
        vm.TestUntilHostKeyAsync().AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(vm.HostKeyReview!.IsHardBlocked);
        AppTestHost.Check(!vm.HostKeyReview.CanConfirm);
        vm.AuthenticateAsync().AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(tester.AuthenticateCalls == 0);
        AppTestHost.Check(vm.ErrorCode == SshCodes.HostKeyChanged);
    }

    static void CancelIdleKeepsSuccess()
    {
        var store = new MemoryDeviceProfileStore();
        var tester = new FakeSshTester();
        var vm = NewDraft(store, tester);
        vm.TestUntilHostKeyAsync().AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(vm.LastTest!.Phase == SshConnectionTestPhase.Succeeded);
        vm.CancelTestAsync().AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(vm.LastTest.Phase == SshConnectionTestPhase.Succeeded);
        AppTestHost.Check(vm.ErrorCode is null);
    }

    static EditDeviceViewModel NewDraft(IDeviceProfileStore store, FakeSshTester tester)
    {
        var vm = new EditDeviceViewModel(store, tester);
        vm.BeginNewDevice(AppTestHost.DeviceA);
        vm.SetLabel("ssh-lab");
        vm.SetHerdrPath(OperatingSystem.IsWindows() ? @"C:\HerdDesk\herdr.exe" : "/tmp/herdr");
        vm.SetHostAlias("lab");
        vm.SetRemoteHerdrPath("/usr/bin/herdr");
        vm.SetAuthModeRaw("open_ssh_config");
        return vm;
    }

    static HostKeyCandidate Candidate(byte seed = 1) =>
        new("example.test", 22, SshHopKind.Target, "ssh-ed25519", "abc" + seed, "SHA256:fixture",
            DateTimeOffset.UtcNow);

    static SshConnectionTestResult UnknownResult() =>
        new(
            SshConnectionTestPhase.AwaitingHostVerification,
            SshCodes.HostKeyUnknown,
            [],
            null,
            new HostKeyAssessment(HostKeyStatus.UnknownCandidate, Candidate(), 0));

    static SshConnectionTestResult ChangedResult() =>
        new(
            SshConnectionTestPhase.Failed,
            SshCodes.HostKeyChanged,
            [],
            null,
            new HostKeyAssessment(HostKeyStatus.Changed, Candidate(9), 1));

    sealed class FakeSshTester : ISshConnectionTester, ISshConfigPreview
    {
        public SshConnectionTestResult Next { get; set; } =
            new(SshConnectionTestPhase.Succeeded, null, [], null, null);
        public int TestCalls { get; private set; }
        public int ConfirmCalls { get; private set; }
        public int AuthenticateCalls { get; private set; }

        public ValueTask<SshPreviewResult> PreviewAsync(
            SshDeviceSettings settings, CancellationToken cancellationToken = default)
        {
            _ = (settings, cancellationToken);
            return new(new SshPreviewResult(
                new SshResolvedConfiguration(
                    "example.test", SshValueSource.OpenSshConfig, "git", SshValueSource.ExplicitField, 22,
                    SshValueSource.ExplicitField, [], null, null, null, null),
                null));
        }

        public ValueTask<SshConnectionTestResult> TestUntilHostKeyAsync(
            SshDeviceSettings settings, CancellationToken cancellationToken = default)
        {
            _ = (settings, cancellationToken);
            TestCalls++;
            return new(Next);
        }

        public ValueTask<SshConnectionTestResult> ConfirmUnknownHostAsync(
            HostKeyCandidate candidate, CancellationToken cancellationToken = default)
        {
            _ = (candidate, cancellationToken);
            ConfirmCalls++;
            return new(Next);
        }

        public ValueTask<SshConnectionTestResult> AuthenticateAsync(
            SshDeviceSettings settings, CancellationToken cancellationToken = default)
        {
            _ = (settings, cancellationToken);
            AuthenticateCalls++;
            return new(Next);
        }

        public ValueTask<SshConnectionTestResult> CancelAsync() =>
            new(new SshConnectionTestResult(
                SshConnectionTestPhase.Cancelled, SshCodes.TestCancelled, [], null, null));
    }
}
