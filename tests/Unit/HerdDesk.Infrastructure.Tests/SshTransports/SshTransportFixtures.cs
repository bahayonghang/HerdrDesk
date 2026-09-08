using System.Diagnostics;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Process;
using HerdDesk.Infrastructure.Ssh;
using HerdDesk.Infrastructure.SshTransports;

internal static class SshTransportFixtures
{
    public static readonly DeviceId DeviceA = new(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
    public static readonly DeviceId DeviceB = new(Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff"));
    public static readonly string HelperHash = new string('a', 64);

    public static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    public static string SelfExe()
    {
        var path = Environment.ProcessPath;
        Check(!string.IsNullOrWhiteSpace(path));
        return path!;
    }

    public static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "herddesk-hd022-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static OpenSshLocator ChannelLocator(string mode) =>
        new(SelfExe(), SelfExe(), ["--fake-ssh-channel", mode]);

    public static SshDeviceSettings Settings(string alias = "lab") =>
        SshDeviceSettings.Create(
            alias,
            SshAuthMode.OpenSshConfig,
            "/usr/bin/herdr",
            "git",
            2222,
            null,
            "SSH_AUTH_SOCK",
            "jump",
            "/usr/lib/herdr/helper");

    public static SessionKey Session(DeviceId? device = null, string endpoint = "remote-api", string? name = "dev") =>
        new(device ?? DeviceA, endpoint, name);

    public static DeploymentReceipt Receipt(SessionKey session) =>
        new(
            new HelperReceiptKey(
                session.Device,
                session.EndpointKey,
                session.SessionName,
                HelperFixtures.LinuxX64,
                HelperHash),
            "0.1.0",
            HelperHash,
            null,
            null,
            1);

    public static RemoteSessionLaunch Launch(
        string root,
        SessionKey? session = null,
        long epoch = 1,
        SshDeviceSettings? settings = null)
    {
        var key = session ?? Session();
        return new(
            key,
            new ConnectionEpoch(epoch),
            settings ?? Settings(),
            "/tmp/herdr.sock",
            Path.Combine(root, "known_hosts"),
            Receipt(key),
            1);
    }

    public static FakeSshProcessRunner HostOkRunner()
    {
        var runner = new FakeSshProcessRunner();
        EnqueueHostOk(runner);
        return runner;
    }

    public static void EnqueueHostOk(FakeSshProcessRunner runner)
    {
        runner.Responses.Enqueue(new(0, "herdr 0.9.0\n", "", false, false, 21));
        runner.Responses.Enqueue(new(0, "{\"protocol\":22,\"schema_version\":1}\n", "", false, false, 22));
        runner.Responses.Enqueue(new(0, "herddesk-bridge 0.1.0\n", "", false, false, 23));
    }

    public static RemoteCompatibilityProbe Probe(OpenSshLocator locator, FakeSshProcessRunner? runner = null) =>
        new(locator, runner ?? HostOkRunner());

    public static RemoteSessionTransportSet OpenSet(
        string mode,
        string root,
        SessionKey? session = null,
        long epoch = 1,
        ISshChildProcessStarter? starter = null,
        FakeSshProcessRunner? runner = null)
    {
        var locator = ChannelLocator(mode);
        var launch = Launch(root, session, epoch);
        return RemoteSessionTransportSet.OpenAsync(
                launch, locator, Probe(locator, runner), starter: starter ?? new TerminalAwareStarter())
            .AsTask().GetAwaiter().GetResult();
    }

    internal sealed class TerminalAwareStarter : ISshChildProcessStarter
    {
        public OwnedChildProcess Start(SshProcessSpec spec)
        {
            var arguments = spec.Arguments.ToList();
            if (spec.Kind == SshProcessKind.RemoteTerminal &&
                arguments.Count >= 2 &&
                arguments[0] == "--fake-ssh-channel")
                arguments[1] = "terminal";
            return OwnedChildProcess.Start(spec.Executable, arguments);
        }
    }

    public static void WaitUntil(Func<bool> predicate, int milliseconds = 5000)
    {
        var clock = Stopwatch.StartNew();
        while (!predicate())
        {
            if (clock.ElapsedMilliseconds > milliseconds)
                throw new Exception("wait_timeout");
            Thread.Sleep(10);
        }
    }

    public static PaneKey Pane(SessionKey session) => new(session, "ws1", "pane1");
}
