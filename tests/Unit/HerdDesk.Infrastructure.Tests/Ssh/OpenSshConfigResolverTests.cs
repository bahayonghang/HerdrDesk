using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Ssh;

internal static class OpenSshConfigResolverTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("ssh -G parser keeps unicode spaces identities agent and proxyjump", ParseMatrix),
        ("oversize or secret stdout is rejected", RejectsMalicious),
        ("resolver uses fake runner and does not copy stderr", ResolverFake)
    ];

    static void ParseMatrix()
    {
        var settings = SshFixtures.Settings(identity: "/tmp/id_ed25519");
        var stdout = SshFixtures.ConfigStdout(
            host: "café.example",
            identity: "/tmp/id with space",
            identity2: "/tmp/密钥",
            agent: "SSH_AUTH_SOCK",
            jump: "jump.example");
        SshFixtures.Check(OpenSshGParser.TryParse(stdout, settings, out var parsed, out var code));
        SshFixtures.Check(code == SshCodes.Ok);
        SshFixtures.Check(parsed.Hostname == "café.example");
        SshFixtures.Check(parsed.User == "git");
        SshFixtures.Check(parsed.Port == 2222);
        SshFixtures.Check(parsed.IdentityFiles.Count == 2);
        SshFixtures.Check(parsed.IdentityFiles[0].Path == "/tmp/id with space");
        SshFixtures.Check(parsed.IdentityFiles[1].Path == "/tmp/密钥");
        SshFixtures.Check(parsed.IdentityAgent == "SSH_AUTH_SOCK");
        SshFixtures.Check(parsed.ProxyJump == "jump.example");
        SshFixtures.Check(parsed.PortSource == SshValueSource.ExplicitField);
    }

    static void RejectsMalicious()
    {
        var settings = SshFixtures.Settings();
        var huge = new string('x', OpenSshGParser.MaxOutputBytes + 8);
        SshFixtures.Check(!OpenSshGParser.TryParse(huge, settings, out _, out _));
        SshFixtures.Check(!OpenSshGParser.TryParse("hostname -----BEGIN PRIVATE KEY-----\n", settings, out _, out _));
        SshFixtures.Check(!OpenSshGParser.LooksLikeOpenSshVersion("Password: hunter2"));
        SshFixtures.Check(!OpenSshGParser.TryParse("port not-a-number\nhostname example.test\n", settings, out _, out _));
    }

    static void ResolverFake()
    {
        var runner = new FakeSshProcessRunner();
        runner.Responses.Enqueue(SshFixtures.ConfigOk(SshFixtures.ConfigStdout(identity2: "/tmp/id with space")));
        var resolver = new OpenSshConfigResolver(SshFixtures.Locator(), runner);
        var result = resolver.PreviewAsync(SshFixtures.Settings()).AsTask().GetAwaiter().GetResult();
        SshFixtures.Check(result.Succeeded);
        SshFixtures.Check(result.Configuration!.IdentityFiles.Count == 2);
        SshFixtures.Check(runner.Started.Count == 1);
        SshFixtures.Check(runner.Started[0].Kind == SshProcessKind.ConfigPreview);
        SshFixtures.Check(!result.Configuration.Hostname.Contains("BEGIN", StringComparison.Ordinal));
    }
}
