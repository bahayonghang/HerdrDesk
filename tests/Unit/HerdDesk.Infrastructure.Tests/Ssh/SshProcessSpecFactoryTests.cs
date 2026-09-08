using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Ssh;

internal static class SshProcessSpecFactoryTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("config and probe argv stay tokenized without shell or password", ArgvTokens),
        ("alias dash and relative helper are rejected before start", RejectsInjection)
    ];

    static void ArgvTokens()
    {
        var locator = SshFixtures.Locator();
        var identity = OperatingSystem.IsWindows() ? @"C:\keys\id_ed25519" : "/tmp/id_ed25519";
        var settings = SshFixtures.Settings(identity: identity, mode: SshAuthMode.IdentityFile);
        var known = OperatingSystem.IsWindows()
            ? @"C:\HerdDesk\settings\ssh-known-hosts.json"
            : "/tmp/herddesk/ssh-known-hosts.json";
        SshFixtures.Check(SshProcessSpecFactory.TryConfigPreview(locator, settings, out var preview, out _));
        SshFixtures.Check(preview.Arguments.Contains("-G"));
        SshFixtures.Check(!preview.Arguments.Contains("-tt"));
        SshFixtures.Check(!preview.Arguments.Any(item => item.Contains("password", StringComparison.OrdinalIgnoreCase)));
        SshFixtures.Check(preview.Arguments[^1] == "lab");
        SshFixtures.Check(SshProcessSpecFactory.TryAuthProbe(locator, settings, known, out var probe, out _));
        SshFixtures.Check(probe.Arguments.Contains("-T"));
        SshFixtures.Check(probe.Arguments.Contains("BatchMode=yes"));
        SshFixtures.Check(probe.Arguments.Contains("StrictHostKeyChecking=yes"));
        SshFixtures.Check(probe.Arguments.Contains("PasswordAuthentication=no"));
        SshFixtures.Check(probe.Arguments.Contains("KbdInteractiveAuthentication=no"));
        SshFixtures.Check(probe.Arguments.Contains("UserKnownHostsFile=" + known));
        SshFixtures.Check(probe.Arguments[^1] == SshProcessSpecFactory.RemoteProbeCommand);
        SshFixtures.Check(probe.Arguments[^2] == "lab");
        SshFixtures.Check(!probe.Arguments.Contains("-tt"));
        SshFixtures.Check(probe.Arguments.All(item => item != "cmd"));
    }

    static void RejectsInjection()
    {
        var locator = SshFixtures.Locator();
        var badAlias = SshFixtures.Settings(alias: "-oProxyCommand=evil");
        SshFixtures.Check(!SshProcessSpecFactory.TryConfigPreview(locator, badAlias, out _, out var code));
        SshFixtures.Check(code == SshCodes.ProfileInvalid);
        var relative = SshFixtures.Settings() with { RemoteHelperPath = "helper" };
        SshFixtures.Check(!SshProcessSpecFactory.TryConfigPreview(locator, relative, out _, out var helperCode));
        SshFixtures.Check(helperCode == SshCodes.ProfileInvalid);
        var unknown = SshFixtures.Settings() with { AuthMode = SshDeviceSettings.ParseAuthMode("password") };
        SshFixtures.Check(!SshProcessSpecFactory.TryConfigPreview(locator, unknown, out _, out var authCode));
        SshFixtures.Check(authCode == SshCodes.AuthUnsupported);
    }
}
