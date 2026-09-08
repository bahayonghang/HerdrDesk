using System.Text.Json;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Rpc;
using HerdDesk.Infrastructure.Ssh;
using HerdDesk.Infrastructure.SshTransports;

internal static class RemoteRpcStreamContractTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("remote rpc banner before first record is pollution", BannerBefore),
        ("remote rpc banner between records is pollution", BannerBetween),
        ("remote rpc stderr json never enters the parser", StderrJson),
        ("remote rpc oversize record fails closed", Oversize),
        ("remote rpc truncated eof fails closed", Truncated),
        ("remote rpc helper handshake is pollution", Handshake)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void BannerBefore()
    {
        var connection = Open("banner", out var spec);
        try
        {
            Check(spec.Arguments.Contains("-T"));
            Check(!SshProcessSpecFactory.HasInteractiveTtyFlag(spec.Arguments));
            using var result = connection.RequestAsync("ping", Empty()).AsTask().GetAwaiter().GetResult();
            Check(!result.Succeeded);
            Check(result.Failure is not null);
            Check(SshTransportMapper.FromRpc(result.Failure!.Code) == SshTransportCodes.StdoutProtocolPollution);
            Check(!result.Failure.Code.Contains("WELCOME", StringComparison.Ordinal));
        }
        finally
        {
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void BannerBetween()
    {
        var connection = Open("banner-between", out _);
        try
        {
            using var first = connection.RequestAsync("ping", Empty()).AsTask().GetAwaiter().GetResult();
            Check(first.Succeeded);
            using var second = connection.RequestAsync("ping", Empty()).AsTask().GetAwaiter().GetResult();
            Check(!second.Succeeded);
            Check(SshTransportMapper.FromRpc(second.Failure!.Code) == SshTransportCodes.StdoutProtocolPollution);
        }
        finally
        {
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void StderrJson()
    {
        var connection = Open("stderr-json", out _);
        try
        {
            using var result = connection.RequestAsync("ping", Empty()).AsTask().GetAwaiter().GetResult();
            Check(result.Succeeded);
            var json = result.Document!.RootElement.GetRawText();
            Check(!json.Contains(FakeSshChannelHost.StderrJsonCanary, StringComparison.Ordinal));
            Check(connection.Failure is null);
        }
        finally
        {
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void Oversize()
    {
        var connection = Open("oversize", out _);
        try
        {
            using var result = connection.RequestAsync("ping", Empty()).AsTask().GetAwaiter().GetResult();
            Check(!result.Succeeded);
            Check(SshTransportMapper.FromRpc(result.Failure!.Code) == SshTransportCodes.RecordTooLarge);
        }
        finally
        {
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void Truncated()
    {
        var connection = Open("truncated", out _);
        try
        {
            using var result = connection.RequestAsync("ping", Empty()).AsTask().GetAwaiter().GetResult();
            Check(!result.Succeeded);
            Check(SshTransportMapper.FromRpc(result.Failure!.Code) == SshTransportCodes.StreamTruncated);
        }
        finally
        {
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void Handshake()
    {
        var connection = Open("handshake", out _);
        try
        {
            using var result = connection.RequestAsync("ping", Empty()).AsTask().GetAwaiter().GetResult();
            Check(!result.Succeeded);
            Check(SshTransportMapper.FromRpc(result.Failure!.Code) == SshTransportCodes.StdoutProtocolPollution);
            Check(!result.Failure!.Code.Contains("herddesk-bridge", StringComparison.Ordinal));
        }
        finally
        {
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static IRpcRequestConnection Open(string mode, out SshProcessSpec spec)
    {
        var exe = Environment.ProcessPath!;
        var locator = new OpenSshLocator(exe, exe, ["--fake-ssh-channel", mode]);
        var factory = new RemoteRpcConnectionFactory(locator);
        var device = new DeviceId(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
        var session = new SessionKey(device, "remote-api", "dev");
        var hash = new string('a', 64);
        var launch = new RemoteSessionLaunch(
            session,
            new ConnectionEpoch(1),
            SshDeviceSettings.Create(
                "lab", SshAuthMode.OpenSshConfig, "/usr/bin/herdr", "git", 2222, null, "SSH_AUTH_SOCK", "jump",
                "/usr/lib/herdr/helper"),
            "/tmp/herdr.sock",
            Path.Combine(Path.GetTempPath(), "hd022-known-" + Guid.NewGuid().ToString("N")),
            new DeploymentReceipt(
                new HelperReceiptKey(device, session.EndpointKey, session.SessionName,
                    "x86_64-unknown-linux-gnu", hash),
                "0.1.0", hash, null, null, 1),
            1);
        Check(factory.TryOpenRequest(launch, out var connection, out var opened, out _));
        Check(connection is not null && opened is not null);
        spec = opened!;
        return connection!;
    }

    static JsonElement Empty()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }
}
