using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Ssh;

internal static class RemotePlatformProbeTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("linux x64 maps to closed allowlist", LinuxX64),
        ("banner and extra lines fail closed", BannerFails),
        ("control characters fail closed", ControlFails),
        ("unknown and remote windows fail closed", UnknownAndWindows),
        ("timeout fails closed", TimeoutFails)
    ];

    static void LinuxX64()
    {
        var runner = new FakeSshProcessRunner();
        runner.Responses.Enqueue(new(0, "linux\nx86_64\n/home/lab\n", "", false, false, 3));
        var probe = new RemotePlatformProbe(
            SshFixtures.Locator(),
            runner,
            OperatingSystem.IsWindows() ? @"C:\HerdDesk\settings\ssh-known-hosts.json" : "/tmp/herddesk/known",
            new TrustedHelperManifestProvider(HelperFixtures.AppVersion).Targets);
        var result = probe.ProbeAsync(HelperFixtures.Settings()).AsTask().GetAwaiter().GetResult();
        HelperFixtures.Check(result.Succeeded);
        HelperFixtures.Check(result.Target!.Triple == HelperFixtures.LinuxX64);
        HelperFixtures.Check(result.Home == "/home/lab");
        HelperFixtures.Check(runner.Started[0].Kind == SshProcessKind.PlatformProbe);
        HelperFixtures.Check(runner.Started[0].Arguments.Contains("-T"));
        HelperFixtures.Check(runner.Started[0].Arguments.Contains(HelperRemoteScripts.Probe));
        HelperFixtures.Check(!runner.Started[0].Arguments.Any(RemoteHelperPublisher.ForbiddenArgument));
    }

    static void BannerFails()
    {
        var runner = new FakeSshProcessRunner();
        runner.Responses.Enqueue(new(0, "Welcome to host\nlinux\nx86_64\n/home/lab\n", "", false, false, 3));
        var result = Probe(runner);
        HelperFixtures.Check(!result.Succeeded);
        HelperFixtures.Check(result.Code == HelperCodes.PlatformUnsupported);
        runner.Responses.Enqueue(new(0, "linux\nx86_64\n/home/lab\n/extra\n", "", false, false, 3));
        var extra = Probe(runner);
        HelperFixtures.Check(extra.Code == HelperCodes.PlatformUnsupported);
        runner.Responses.Enqueue(new(0, "linux\nx86_64\n/home/lab\n", "banner-stderr\n", false, false, 3));
        var stderr = Probe(runner);
        HelperFixtures.Check(stderr.Code == HelperCodes.PlatformUnsupported);
    }

    static void ControlFails()
    {
        var runner = new FakeSshProcessRunner();
        runner.Responses.Enqueue(new(0, "linux\nx86_64\n/home/lab\u0007\n", "", false, false, 3));
        var result = Probe(runner);
        HelperFixtures.Check(result.Code == HelperCodes.PlatformUnsupported);
        runner.Responses.Enqueue(new(0, "linux\r\nx86_64\n/home/lab\n", "", false, false, 3));
        var crlf = Probe(runner);
        HelperFixtures.Check(crlf.Code == HelperCodes.PlatformUnsupported);
    }

    static void UnknownAndWindows()
    {
        var runner = new FakeSshProcessRunner();
        runner.Responses.Enqueue(new(0, "windows\nx86_64\n/home/lab\n", "", false, false, 3));
        HelperFixtures.Check(Probe(runner).Code == HelperCodes.PlatformUnsupported);
        runner.Responses.Enqueue(new(0, "linux\narm64\n/home/lab\n", "", false, false, 3));
        HelperFixtures.Check(Probe(runner).Code == HelperCodes.PlatformUnsupported);
        runner.Responses.Enqueue(new(0, "unknown\nunknown\n/\n", "", false, false, 3));
        HelperFixtures.Check(Probe(runner).Code == HelperCodes.PlatformUnsupported);
        runner.Responses.Enqueue(new(0, "linux\nx86_64\nhome/relative\n", "", false, false, 3));
        HelperFixtures.Check(Probe(runner).Code == HelperCodes.PlatformUnsupported);
        runner.Responses.Enqueue(new(0, "linux\nx86_64\n/usr\n", "", false, false, 3));
        HelperFixtures.Check(Probe(runner).Code == HelperCodes.PlatformUnsupported);
        runner.Responses.Enqueue(new(0, "linux\nx86_64\n/opt/lab\n", "", false, false, 3));
        HelperFixtures.Check(Probe(runner).Code == HelperCodes.PlatformUnsupported);
    }

    static void TimeoutFails()
    {
        var runner = new FakeSshProcessRunner { Delay = TimeSpan.FromSeconds(30) };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var probe = new RemotePlatformProbe(
            SshFixtures.Locator(),
            runner,
            OperatingSystem.IsWindows() ? @"C:\HerdDesk\settings\ssh-known-hosts.json" : "/tmp/herddesk/known",
            new TrustedHelperManifestProvider(HelperFixtures.AppVersion).Targets);
        RemotePlatformProbeResult result;
        try
        {
            result = probe.ProbeAsync(HelperFixtures.Settings(), cts.Token).AsTask().GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            return;
        }

        HelperFixtures.Check(result.Code is HelperCodes.Cancelled or HelperCodes.PlatformUnsupported);
    }

    static RemotePlatformProbeResult Probe(FakeSshProcessRunner runner)
    {
        var probe = new RemotePlatformProbe(
            SshFixtures.Locator(),
            runner,
            OperatingSystem.IsWindows() ? @"C:\HerdDesk\settings\ssh-known-hosts.json" : "/tmp/herddesk/known",
            new TrustedHelperManifestProvider(HelperFixtures.AppVersion).Targets);
        return probe.ProbeAsync(HelperFixtures.Settings()).AsTask().GetAwaiter().GetResult();
    }
}
