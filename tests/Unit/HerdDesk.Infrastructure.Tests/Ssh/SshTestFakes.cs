using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Configuration;
using HerdDesk.Infrastructure.Ssh;

internal sealed class FakeSshProcessRunner : ISshProcessRunner
{
    public List<SshProcessSpec> Started { get; } = [];
    public Queue<SshProcessRunResult> Responses { get; } = new();
    public TimeSpan Delay { get; set; }

    public async ValueTask<SshProcessRunResult> RunAsync(
        SshProcessSpec spec, CancellationToken cancellationToken = default)
    {
        Started.Add(spec);
        if (Delay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new(255, "", "", false, true, 4242);
            }
        }

        if (Responses.Count == 0)
            return new(255, "", "", false, false, 1);
        return Responses.Dequeue();
    }
}

internal static class SshFixtures
{
    public static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    public static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "herddesk-hd020-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static string SelfExe()
    {
        var path = Environment.ProcessPath;
        Check(!string.IsNullOrWhiteSpace(path));
        return path!;
    }

    public static OpenSshLocator Locator() => new(SelfExe(), SelfExe(), ["--fake-ssh"]);

    public static SshDeviceSettings Settings(
        string alias = "lab",
        SshAuthMode mode = SshAuthMode.OpenSshConfig,
        int? port = 2222,
        string? identity = null,
        string? agent = "SSH_AUTH_SOCK",
        string? jump = "jump",
        string? user = "git") =>
        SshDeviceSettings.Create(
            alias,
            mode,
            "/usr/bin/herdr",
            user,
            port,
            identity,
            agent,
            jump,
            "/usr/lib/herdr/helper");

    public static DeviceProfile SshDevice(
        string herdrPath,
        string label = "ssh-lab",
        DeviceId? device = null,
        SshDeviceSettings? ssh = null,
        IReadOnlyList<SessionProfile>? sessions = null)
    {
        SessionProfile second;
        if (OperatingSystem.IsWindows())
            second = SessionProfile.Explicit(@"\\.\pipe\herdr-hd020", EndpointKind.NamedPipe);
        else
            second = SessionProfile.Explicit("/tmp/herdr-hd020.sock", EndpointKind.UnixSocket);
        return new DeviceProfile(
            device ?? new DeviceId(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")),
            label,
            ConnectionKinds.Ssh,
            herdrPath,
            sessions ?? [SessionProfile.Named("dev"), second],
            ssh ?? Settings());
    }

    public static string KeyBlob(byte seed)
    {
        var bytes = new byte[32];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)(seed + i);
        return Convert.ToBase64String(bytes);
    }

    public static string ConfigStdout(
        string host = "example.test",
        string user = "git",
        string port = "2222",
        string? identity = "/tmp/id_ed25519",
        string? identity2 = null,
        string? agent = "SSH_AUTH_SOCK",
        string? jump = "jump.example")
    {
        var lines = new List<string>
        {
            "user " + user,
            "hostname " + host,
            "port " + port
        };
        if (identity is not null)
            lines.Add("identityfile " + identity);
        if (identity2 is not null)
            lines.Add("identityfile " + identity2);
        lines.Add("identityagent " + (agent ?? "none"));
        lines.Add("proxyjump " + (jump ?? "none"));
        return string.Join("\n", lines) + "\n";
    }

    public static string KeyScanStdout(string host, byte seed = 1) =>
        host + " ssh-ed25519 " + KeyBlob(seed) + "\n";

    public static SshProcessRunResult VersionOk() =>
        new(0, "", "OpenSSH_9.5p1 Ubuntu\n", false, false, 11);

    public static SshProcessRunResult ConfigOk(string? stdout = null) =>
        new(0, stdout ?? ConfigStdout(), "", false, false, 12);

    public static SshProcessRunResult ScanOk(string host = "example.test", byte seed = 1) =>
        new(0, KeyScanStdout(host, seed), "", false, false, 13);

    public static SshProcessRunResult ProbeOk() =>
        new(0, "", "", false, false, 14);

    public static SshProcessRunResult ConfigOkNoJump() =>
        new(0, ConfigStdout(jump: null), "", false, false, 12);

    public static void EnqueueUntilHostKey(
        FakeSshProcessRunner runner,
        string host = "example.test",
        byte seed = 1,
        bool jump = false)
    {
        runner.Responses.Enqueue(VersionOk());
        runner.Responses.Enqueue(jump ? ConfigOk() : ConfigOkNoJump());
        if (jump)
            runner.Responses.Enqueue(ScanOk("jump.example", seed));
        runner.Responses.Enqueue(ScanOk(host, seed));
    }

    public static void EnqueueAuthenticate(
        FakeSshProcessRunner runner,
        string host = "example.test",
        byte seed = 1,
        bool jump = false)
    {
        EnqueueUntilHostKey(runner, host, seed, jump);
        runner.Responses.Enqueue(ProbeOk());
    }

    public static SshConnectionTestService Service(
        string root,
        FakeSshProcessRunner runner,
        ISshHostKeyStore? trust = null)
    {
        var paths = AppDataPaths.FromRoot(root);
        Directory.CreateDirectory(paths.SettingsDirectory);
        return new SshConnectionTestService(
            Locator(),
            runner,
            trust ?? new HostKeyTrustStore(paths),
            paths.KnownHostsFile);
    }
}
