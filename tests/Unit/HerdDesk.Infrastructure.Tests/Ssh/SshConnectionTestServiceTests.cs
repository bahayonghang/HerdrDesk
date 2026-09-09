using System.Diagnostics;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Configuration;
using HerdDesk.Infrastructure.Process;
using HerdDesk.Infrastructure.Ssh;

internal static class SshConnectionTestServiceTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("stages stop at unknown host and confirm then probe", UnknownThenProbe),
        ("changed host key fails closed without probe", ChangedBlocks),
        ("cancel stops a hanging stage within three seconds", CancelStops),
        ("cancel stops owned fake-ssh child within three seconds", CancelStopsOwnedChild),
        ("unreadable trust store fails closed and keeps bytes", UnreadableTrustStays),
        ("unsupported prompt is classified without stderr leak", UnsupportedNoLeak)
    ];

    static void UnknownThenProbe()
    {
        var root = SshFixtures.TempRoot();
        try
        {
            var runner = new FakeSshProcessRunner();
            SshFixtures.EnqueueUntilHostKey(runner);
            var service = SshFixtures.Service(root, runner);
            var first = service.TestUntilHostKeyAsync(SshFixtures.Settings(jump: null)).AsTask().GetAwaiter().GetResult();
            SshFixtures.Check(first.Phase == SshConnectionTestPhase.AwaitingHostVerification);
            SshFixtures.Check(first.Code == SshCodes.HostKeyUnknown);
            SshFixtures.Check(first.HostKey!.Status == HostKeyStatus.UnknownCandidate);
            SshFixtures.Check(first.Stages.All(item => item.Code is SshCodes.Ok or SshCodes.HostKeyUnknown));
            SshFixtures.Check(!ResultText(first).Contains("BEGIN", StringComparison.Ordinal));
            var confirmed = service.ConfirmUnknownHostAsync(first.HostKey.Candidate!)
                .AsTask().GetAwaiter().GetResult();
            SshFixtures.Check(confirmed.HostKey!.Status == HostKeyStatus.Trusted);
            runner.Responses.Clear();
            SshFixtures.EnqueueAuthenticate(runner);
            var authed = service.AuthenticateAsync(SshFixtures.Settings(jump: null)).AsTask().GetAwaiter().GetResult();
            SshFixtures.Check(authed.Succeeded);
            SshFixtures.Check(authed.Phase == SshConnectionTestPhase.Succeeded);
            SshFixtures.Check(runner.Started.Any(item => item.Kind == SshProcessKind.AuthProbe));
            SshFixtures.Check(runner.Started.All(item => !item.Arguments.Contains("-tt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void ChangedBlocks()
    {
        var root = SshFixtures.TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            Directory.CreateDirectory(paths.SettingsDirectory);
            var trust = new HostKeyTrustStore(paths);
            var firstScan = ParseCandidate(SshFixtures.KeyScanStdout("example.test", 1), "example.test");
            SshFixtures.Check(trust.ConfirmUnknownAsync(firstScan).AsTask().GetAwaiter().GetResult().Succeeded);
            var original = File.ReadAllBytes(paths.KnownHostsFile);
            var runner = new FakeSshProcessRunner();
            runner.Responses.Enqueue(SshFixtures.VersionOk());
            runner.Responses.Enqueue(SshFixtures.ConfigOk());
            runner.Responses.Enqueue(SshFixtures.ScanOk("jump.example", 1));
            SshFixtures.Check(trust.ConfirmUnknownAsync(
                    ParseCandidate(SshFixtures.KeyScanStdout("jump.example", 1), "jump.example", 22))
                .AsTask().GetAwaiter().GetResult().Succeeded);
            original = File.ReadAllBytes(paths.KnownHostsFile);
            runner.Responses.Enqueue(SshFixtures.ScanOk("example.test", 9));
            var service = SshFixtures.Service(root, runner, trust);
            var result = service.TestUntilHostKeyAsync(SshFixtures.Settings()).AsTask().GetAwaiter().GetResult();
            SshFixtures.Check(result.Phase == SshConnectionTestPhase.Failed);
            SshFixtures.Check(result.Code == SshCodes.HostKeyChanged);
            SshFixtures.Check(result.HostKey!.Status == HostKeyStatus.Changed);
            SshFixtures.Check(!runner.Started.Any(item => item.Kind == SshProcessKind.AuthProbe));
            SshFixtures.Check(File.ReadAllBytes(paths.KnownHostsFile).SequenceEqual(original));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void CancelStops()
    {
        var root = SshFixtures.TempRoot();
        try
        {
            var runner = new FakeSshProcessRunner { Delay = TimeSpan.FromSeconds(30) };
            runner.Responses.Enqueue(SshFixtures.VersionOk());
            var service = SshFixtures.Service(root, runner);
            using var cts = new CancellationTokenSource();
            var task = service.TestUntilHostKeyAsync(SshFixtures.Settings(jump: null), cts.Token).AsTask();
            Thread.Sleep(50);
            var sw = Stopwatch.StartNew();
            cts.Cancel();
            var result = task.GetAwaiter().GetResult();
            sw.Stop();
            SshFixtures.Check(sw.Elapsed < TimeSpan.FromSeconds(3));
            SshFixtures.Check(result.Phase == SshConnectionTestPhase.Cancelled);
            SshFixtures.Check(result.Code == SshCodes.TestCancelled);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void CancelStopsOwnedChild()
    {
        var root = SshFixtures.TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            Directory.CreateDirectory(paths.SettingsDirectory);
            var locator = new OpenSshLocator(
                SshFixtures.SelfExe(), SshFixtures.SelfExe(), ["--fake-ssh", "hang"]);
            var runner = new SshOwnedProcessRunner();
            var service = new SshConnectionTestService(
                locator, runner, new HostKeyTrustStore(paths), paths.KnownHostsFile);
            using var cts = new CancellationTokenSource();
            var beforeStarted = OwnedChildProcessKillLedger.StartedProcessIds.ToArray();
            var task = service.TestUntilHostKeyAsync(SshFixtures.Settings(jump: null), cts.Token).AsTask();
            if (!SpinWait.SpinUntil(
                    () => OwnedChildProcessKillLedger.StartedProcessIds.Except(beforeStarted).Any()
                        || task.IsCompleted,
                    TimeSpan.FromSeconds(30)))
                throw new Exception("child_start_timeout");
            SshFixtures.Check(OwnedChildProcessKillLedger.StartedProcessIds.Except(beforeStarted).Any());
            var before = OwnedChildProcessKillLedger.KilledProcessIds.ToArray();
            var sw = Stopwatch.StartNew();
            cts.Cancel();
            if (!task.Wait(TimeSpan.FromSeconds(8)))
                throw new Exception("cancel_did_not_complete");
            var result = task.GetAwaiter().GetResult();
            sw.Stop();
            SshFixtures.Check(sw.Elapsed < TimeSpan.FromSeconds(5));
            SshFixtures.Check(result.Phase == SshConnectionTestPhase.Cancelled);
            SshFixtures.Check(result.Code == SshCodes.TestCancelled);
            var killed = OwnedChildProcessKillLedger.KilledProcessIds.Except(before).ToArray();
            SshFixtures.Check(killed.Length >= 1);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void UnreadableTrustStays()
    {
        var root = SshFixtures.TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            Directory.CreateDirectory(paths.SettingsDirectory);
            var damaged = "{not-json"u8.ToArray();
            File.WriteAllBytes(paths.KnownHostsFile, damaged);
            var runner = new FakeSshProcessRunner();
            SshFixtures.EnqueueUntilHostKey(runner);
            var service = SshFixtures.Service(root, runner);
            var result = service.TestUntilHostKeyAsync(SshFixtures.Settings(jump: null))
                .AsTask().GetAwaiter().GetResult();
            SshFixtures.Check(result.Phase == SshConnectionTestPhase.Failed);
            SshFixtures.Check(result.Code == SshCodes.PersistenceFailed);
            SshFixtures.Check(result.HostKey!.Status == HostKeyStatus.Unavailable);
            SshFixtures.Check(!runner.Started.Any(item => item.Kind == SshProcessKind.AuthProbe));
            SshFixtures.Check(File.ReadAllBytes(paths.KnownHostsFile).SequenceEqual(damaged));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void UnsupportedNoLeak()
    {
        var root = SshFixtures.TempRoot();
        try
        {
            var runner = new FakeSshProcessRunner();
            SshFixtures.EnqueueUntilHostKey(runner);
            var service = SshFixtures.Service(root, runner);
            var until = service.TestUntilHostKeyAsync(SshFixtures.Settings(jump: null)).AsTask().GetAwaiter().GetResult();
            SshFixtures.Check(service.ConfirmUnknownHostAsync(until.HostKey!.Candidate!)
                .AsTask().GetAwaiter().GetResult().HostKey!.Status == HostKeyStatus.Trusted);
            runner.Responses.Clear();
            SshFixtures.EnqueueUntilHostKey(runner);
            runner.Responses.Enqueue(new SshProcessRunResult(
                255, "", "Password: supersecret-password\nkeyboard-interactive", false, false, 15));
            var result = service.AuthenticateAsync(SshFixtures.Settings(jump: null)).AsTask().GetAwaiter().GetResult();
            SshFixtures.Check(result.Code == SshCodes.AuthUnsupported);
            var text = ResultText(result);
            SshFixtures.Check(!text.Contains("supersecret-password", StringComparison.Ordinal));
            SshFixtures.Check(!text.Contains("Password:", StringComparison.Ordinal));
            SshFixtures.Check(!result.Stages.Any(item => item.Metadata.Any(meta =>
                meta.Value.Contains("password", StringComparison.OrdinalIgnoreCase))));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static HostKeyCandidate ParseCandidate(string stdout, string host, int port = 2222)
    {
        SshFixtures.Check(OpenSshGParser.TryParseKeyScan(
            stdout, host, port, host == "jump.example" ? SshHopKind.ProxyJump : SshHopKind.Target,
            DateTimeOffset.UtcNow, out var candidate, out _));
        return candidate;
    }

    static string ResultText(SshConnectionTestResult result)
    {
        var preview = result.Preview;
        return string.Join("|",
        [
            result.Code ?? "",
            preview?.Hostname ?? "",
            preview?.User ?? "",
            string.Join(",", result.Stages.Select(item => item.Code))
        ]);
    }
}
