using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Ssh;
using HerdDesk.Infrastructure.SshTransports;

internal static class RemoteCompatibilityProbeTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("host probe caches version schema helper by revision", CachesHostProbe),
        ("schema mismatch is incompatible and not daemon proof", SchemaMismatch),
        ("schema banner is pollution and is not skipped", SchemaBanner),
        ("unknown protocol is incompatible", UnknownProtocol),
        ("non-zero probe exit is unknown-blocked not retry", NonZeroExitUnknownBlocked),
        ("session ping is per session key", PingPerSession),
        ("missing helper path is trust required", MissingHelper)
    ];

    static void CachesHostProbe()
    {
        var root = SshTransportFixtures.TempRoot();
        try
        {
            var locator = SshTransportFixtures.ChannelLocator("rpc");
            var runner = SshTransportFixtures.HostOkRunner();
            var probe = new RemoteCompatibilityProbe(locator, runner);
            var launch = SshTransportFixtures.Launch(root);
            var first = probe.ProbeHostAsync(launch).AsTask().GetAwaiter().GetResult();
            var second = probe.ProbeHostAsync(launch).AsTask().GetAwaiter().GetResult();
            SshTransportFixtures.Check(first.Succeeded);
            SshTransportFixtures.Check(first.CliCompatible);
            SshTransportFixtures.Check(second.CliCompatible);
            SshTransportFixtures.Check(runner.Started.Count == 3);
            SshTransportFixtures.Check(runner.Started[0].Kind == SshProcessKind.RemoteHerdrVersion);
            SshTransportFixtures.Check(runner.Started[1].Kind == SshProcessKind.RemoteApiSchema);
            SshTransportFixtures.Check(runner.Started[2].Kind == SshProcessKind.RemoteHelperVersion);
            SshTransportFixtures.Check(first.SchemaProtocol == 22);
            SshTransportFixtures.Check(!string.IsNullOrEmpty(first.SchemaSha256));
            SshTransportFixtures.Check(!probe.TryGetPing(launch.Session, out _));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void SchemaMismatch()
    {
        var root = SshTransportFixtures.TempRoot();
        try
        {
            var locator = SshTransportFixtures.ChannelLocator("rpc");
            var runner = new FakeSshProcessRunner();
            runner.Responses.Enqueue(new(0, "herdr 0.9.0\n", "", false, false, 21));
            runner.Responses.Enqueue(new(0, "{\"protocol\":20,\"schema_version\":1}\n", "", false, false, 22));
            runner.Responses.Enqueue(new(0, "herddesk-bridge 0.1.0\n", "", false, false, 23));
            var probe = new RemoteCompatibilityProbe(locator, runner);
            var result = probe.ProbeHostAsync(SshTransportFixtures.Launch(root)).AsTask().GetAwaiter().GetResult();
            SshTransportFixtures.Check(result.Succeeded);
            SshTransportFixtures.Check(!result.CliCompatible);
            SshTransportFixtures.Check(result.Code == SshTransportCodes.SchemaIncompatible);
            SshTransportFixtures.Check(result.Disposition == SshRetryDisposition.AwaitUser);
            SshTransportFixtures.Check(result.SchemaProtocol == 20);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void SchemaBanner()
    {
        var root = SshTransportFixtures.TempRoot();
        try
        {
            var locator = SshTransportFixtures.ChannelLocator("rpc");
            var runner = new FakeSshProcessRunner();
            runner.Responses.Enqueue(new(0, "herdr 0.9.0\n", "", false, false, 21));
            runner.Responses.Enqueue(new(0, "Welcome to Ubuntu\n{\"protocol\":22,\"schema_version\":1}\n", "", false, false, 22));
            var probe = new RemoteCompatibilityProbe(locator, runner);
            var result = probe.ProbeHostAsync(SshTransportFixtures.Launch(root)).AsTask().GetAwaiter().GetResult();
            SshTransportFixtures.Check(!result.Succeeded);
            SshTransportFixtures.Check(!result.CliCompatible);
            SshTransportFixtures.Check(result.Code == SshTransportCodes.StdoutProtocolPollution);
            SshTransportFixtures.Check(runner.Started.Count == 2);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void UnknownProtocol()
    {
        var root = SshTransportFixtures.TempRoot();
        try
        {
            var locator = SshTransportFixtures.ChannelLocator("rpc");
            var runner = new FakeSshProcessRunner();
            runner.Responses.Enqueue(new(0, "herdr 0.9.0\n", "", false, false, 21));
            runner.Responses.Enqueue(new(0, "{\"title\":\"herdr-api\"}\n", "", false, false, 22));
            var probe = new RemoteCompatibilityProbe(locator, runner);
            var result = probe.ProbeHostAsync(SshTransportFixtures.Launch(root)).AsTask().GetAwaiter().GetResult();
            SshTransportFixtures.Check(!result.Succeeded);
            SshTransportFixtures.Check(result.Code == SshTransportCodes.StdoutProtocolPollution);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void NonZeroExitUnknownBlocked()
    {
        var root = SshTransportFixtures.TempRoot();
        try
        {
            var locator = SshTransportFixtures.ChannelLocator("rpc");
            var runner = new FakeSshProcessRunner();
            runner.Responses.Enqueue(new(255, "herdr 0.9.0\n", "", false, false, 21));
            var probe = new RemoteCompatibilityProbe(locator, runner);
            var result = probe.ProbeHostAsync(SshTransportFixtures.Launch(root)).AsTask().GetAwaiter().GetResult();
            SshTransportFixtures.Check(!result.Succeeded);
            SshTransportFixtures.Check(!result.CliCompatible);
            SshTransportFixtures.Check(result.Code == SshTransportCodes.UnclassifiedExit);
            SshTransportFixtures.Check(result.Disposition == SshRetryDisposition.UnknownBlocked);
            SshTransportFixtures.Check(runner.Started.Count == 1);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void PingPerSession()
    {
        var root = SshTransportFixtures.TempRoot();
        RemoteSessionTransportSet? first = null;
        RemoteSessionTransportSet? second = null;
        try
        {
            var sessionA = SshTransportFixtures.Session(name: "dev");
            var sessionB = SshTransportFixtures.Session(name: "other");
            first = SshTransportFixtures.OpenSet("rpc", root, sessionA);
            second = SshTransportFixtures.OpenSet("rpc", root, sessionB);
            SshTransportFixtures.Check(first.Ready);
            SshTransportFixtures.Check(second.Ready);
            SshTransportFixtures.Check(first.Ping!.Succeeded);
            SshTransportFixtures.Check(second.Ping!.Succeeded);
            SshTransportFixtures.Check(first.Request!.ChildProcessId != second.Request!.ChildProcessId);
        }
        finally
        {
            first?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            second?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Directory.Delete(root, true);
        }
    }

    static void MissingHelper()
    {
        var root = SshTransportFixtures.TempRoot();
        try
        {
            var locator = SshTransportFixtures.ChannelLocator("rpc");
            var probe = new RemoteCompatibilityProbe(locator, SshTransportFixtures.HostOkRunner());
            var settings = SshTransportFixtures.Settings() with { RemoteHelperPath = null };
            var launch = SshTransportFixtures.Launch(root) with { Settings = settings };
            var result = probe.ProbeHostAsync(launch).AsTask().GetAwaiter().GetResult();
            SshTransportFixtures.Check(!result.Succeeded);
            SshTransportFixtures.Check(result.Code == SshTransportCodes.TrustRequired);
            SshTransportFixtures.Check(result.Disposition == SshRetryDisposition.AwaitUser);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
