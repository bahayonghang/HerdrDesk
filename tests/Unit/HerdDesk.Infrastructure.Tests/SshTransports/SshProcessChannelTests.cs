using System.Diagnostics;
using System.Text;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Process;
using HerdDesk.Infrastructure.Ssh;
using HerdDesk.Infrastructure.SshTransports;
using OsProcess = System.Diagnostics.Process;

internal static class SshProcessChannelTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("ssh channel argv uses -T and posix quoted remote command", ArgvTokens),
        ("ssh channel reassembles chunked ndjson", ChunkedIo),
        ("ssh channel stderr flood does not block stdout", StderrFlood),
        ("ssh channel concurrent writes stay serial", SingleWriter),
        ("ssh channel banner is protocol pollution", BannerPollution),
        ("ssh channel handshake is protocol pollution", HandshakePollution),
        ("ssh channel oversize line fails closed", Oversize),
        ("ssh channel truncated eof fails closed", Truncated),
        ("ssh channel cancel kills only the direct child within 3s", CancelDirectChild)
    ];

    static void ArgvTokens()
    {
        var root = SshTransportFixtures.TempRoot();
        try
        {
            var locator = SshTransportFixtures.ChannelLocator("rpc");
            var launch = SshTransportFixtures.Launch(root);
            SshTransportFixtures.Check(SshProcessSpecFactory.TryRemoteRpc(
                locator,
                launch.Settings,
                launch.KnownHostsFile,
                launch.Settings.RemoteHelperPath!,
                launch.SocketPath,
                out var spec,
                out _));
            SshTransportFixtures.Check(spec.Arguments.Contains("-T"));
            SshTransportFixtures.Check(!SshProcessSpecFactory.HasInteractiveTtyFlag(spec.Arguments));
            SshTransportFixtures.Check(spec.Arguments.Contains("BatchMode=yes"));
            SshTransportFixtures.Check(spec.Arguments.Contains("StrictHostKeyChecking=yes"));
            SshTransportFixtures.Check(spec.Arguments[^2] == "lab");
            SshTransportFixtures.Check(spec.Arguments[^1].StartsWith("'/usr/lib/herdr/helper'", StringComparison.Ordinal));
            SshTransportFixtures.Check(spec.Arguments[^1].Contains("rpc", StringComparison.Ordinal));
            SshTransportFixtures.Check(spec.Arguments[^1].Contains("'/tmp/herdr.sock'", StringComparison.Ordinal));
            SshTransportFixtures.Check(SshProcessSpecFactory.TryRemoteTerminal(
                locator,
                launch.Settings,
                launch.KnownHostsFile,
                launch.Settings.RemoteHerdrPath,
                new TerminalOpenRequest(
                    SshTransportFixtures.Pane(launch.Session),
                    launch.Epoch,
                    TerminalMode.Observe,
                    locator.SshExecutable,
                    "pane-target",
                    120,
                    40,
                    "dev"),
                out var terminal,
                out _));
            SshTransportFixtures.Check(terminal.Arguments.Contains("-T"));
            SshTransportFixtures.Check(!SshProcessSpecFactory.HasInteractiveTtyFlag(terminal.Arguments));
            SshTransportFixtures.Check(terminal.Arguments[^1].Contains("observe", StringComparison.Ordinal));
            SshTransportFixtures.Check(!terminal.Arguments[^1].Contains("takeover", StringComparison.Ordinal));
            SshTransportFixtures.Check(!terminal.Arguments.Contains("machine"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void ChunkedIo()
    {
        var channel = Open("chunked");
        try
        {
            channel.WriteAsync("{\"id\":1,\"method\":\"ping\",\"params\":{}}\n"u8.ToArray())
                .AsTask().GetAwaiter().GetResult();
            var line = channel.ReadStdoutLineAsync().AsTask().GetAwaiter().GetResult();
            SshTransportFixtures.Check(line is not null);
            SshTransportFixtures.Check(Encoding.UTF8.GetString(line!).Contains("\"id\":1", StringComparison.Ordinal));
            SshTransportFixtures.Check(channel.Outcome is null);
        }
        finally
        {
            channel.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void StderrFlood()
    {
        var channel = Open("stderr-flood");
        try
        {
            channel.WriteAsync("{\"id\":1,\"method\":\"ping\",\"params\":{}}\n"u8.ToArray())
                .AsTask().GetAwaiter().GetResult();
            var line = channel.ReadStdoutLineAsync().AsTask().GetAwaiter().GetResult();
            SshTransportFixtures.Check(line is not null);
            SshTransportFixtures.WaitUntil(() => channel.StderrBytes > 1000);
            SshTransportFixtures.Check(channel.Outcome is null);
            SshTransportFixtures.Check(Encoding.UTF8.GetString(line!).Contains("\"ok\":true", StringComparison.Ordinal));
        }
        finally
        {
            channel.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void SingleWriter()
    {
        var channel = Open("rpc");
        try
        {
            var tasks = Enumerable.Range(1, 8).Select(id =>
                channel.WriteAsync(Encoding.UTF8.GetBytes(
                    "{\"id\":" + id + ",\"method\":\"ping\",\"params\":{}}\n")).AsTask()).ToArray();
            Task.WaitAll(tasks);
            var ids = new HashSet<ulong>();
            for (var i = 0; i < 8; i++)
            {
                var line = channel.ReadStdoutLineAsync().AsTask().GetAwaiter().GetResult();
                SshTransportFixtures.Check(line is not null);
                using var document = System.Text.Json.JsonDocument.Parse(line!);
                ids.Add(document.RootElement.GetProperty("id").GetUInt64());
            }

            SshTransportFixtures.Check(ids.Count == 8);
        }
        finally
        {
            channel.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void BannerPollution() => Pollution("banner", FakeSshChannelHost.BannerText);

    static void HandshakePollution() => Pollution("handshake", FakeSshChannelHost.HandshakeText);

    static void Pollution(string mode, string canary)
    {
        var channel = Open(mode);
        try
        {
            channel.WriteAsync("{\"id\":1,\"method\":\"ping\",\"params\":{}}\n"u8.ToArray())
                .AsTask().GetAwaiter().GetResult();
            var line = channel.ReadStdoutLineAsync().AsTask().GetAwaiter().GetResult();
            SshTransportFixtures.Check(line is null);
            SshTransportFixtures.Check(channel.Outcome is not null);
            SshTransportFixtures.Check(channel.Outcome!.Code == SshTransportCodes.StdoutProtocolPollution);
            SshTransportFixtures.Check(channel.Outcome.Disposition == SshRetryDisposition.AwaitUser);
            SshTransportFixtures.Check(!channel.Outcome.Code.Contains(canary, StringComparison.Ordinal));
        }
        finally
        {
            channel.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void Oversize()
    {
        var channel = Open("oversize");
        try
        {
            var line = channel.ReadStdoutLineAsync().AsTask().GetAwaiter().GetResult();
            SshTransportFixtures.Check(line is null);
            SshTransportFixtures.Check(channel.Outcome!.Code == SshTransportCodes.RecordTooLarge);
        }
        finally
        {
            channel.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void Truncated()
    {
        var channel = Open("truncated");
        try
        {
            var line = channel.ReadStdoutLineAsync().AsTask().GetAwaiter().GetResult();
            SshTransportFixtures.Check(line is null);
            SshTransportFixtures.Check(channel.Outcome!.Code == SshTransportCodes.StreamTruncated);
        }
        finally
        {
            channel.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void CancelDirectChild()
    {
        var sentinel = OsProcess.Start(new ProcessStartInfo
        {
            FileName = SshTransportFixtures.SelfExe(),
            ArgumentList = { "--fake-ssh-channel", "hang", "-T", "lab", "true" },
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        });
        SshTransportFixtures.Check(sentinel is not null);
        var channel = Open("hang");
        try
        {
            var child = channel.ChildProcessId;
            var started = Stopwatch.StartNew();
            channel.DisposeAsync().AsTask().GetAwaiter().GetResult();
            SshTransportFixtures.Check(started.Elapsed < TimeSpan.FromSeconds(5));
            SshTransportFixtures.Check(!sentinel!.HasExited);
            try
            {
                var leftover = OsProcess.GetProcessById(child);
                SshTransportFixtures.Check(leftover.HasExited);
            }
            catch (ArgumentException)
            {
            }
        }
        finally
        {
            try
            {
                if (!sentinel!.HasExited)
                    sentinel.Kill(entireProcessTree: false);
            }
            catch (InvalidOperationException)
            {
            }

            sentinel?.Dispose();
        }
    }

    static SshProcessChannel Open(string mode)
    {
        var locator = SshTransportFixtures.ChannelLocator(mode);
        var launch = SshTransportFixtures.Launch(Path.GetTempPath());
        SshTransportFixtures.Check(SshProcessSpecFactory.TryRemoteRpc(
            locator,
            launch.Settings,
            launch.KnownHostsFile,
            launch.Settings.RemoteHelperPath!,
            launch.SocketPath,
            out var spec,
            out _));
        return SshProcessChannel.Start(
            spec, launch.Session, launch.Epoch, SshChannelKind.RequestRpc);
    }
}
