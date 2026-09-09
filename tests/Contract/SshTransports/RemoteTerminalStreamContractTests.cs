using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Ssh;
using HerdDesk.Infrastructure.SshTransports;

internal static class RemoteTerminalStreamContractTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("remote terminal argv is ssh -T without tty allocation", ArgvIsT),
        ("remote terminal first full then delta", FullThenDelta),
        ("remote terminal banner is pollution", BannerPollution),
        ("remote terminal stderr json never enters the parser", StderrJson),
        ("remote terminal oversize record fails closed", Oversize),
        ("remote terminal truncated eof fails closed", Truncated),
        ("remote terminal closed eof and exit stay distinct", ClosedEofExit),
        ("remote terminal old epoch input is dropped", OldEpochInputDropped)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void ArgvIsT()
    {
        var transport = Open("terminal", TerminalMode.Observe, out var spec, out _);
        try
        {
            Check(spec.Arguments.Contains("-T"));
            Check(!SshProcessSpecFactory.HasInteractiveTtyFlag(spec.Arguments));
            Check(spec.Arguments.Contains("BatchMode=yes"));
            Check(spec.Arguments[^1].Contains("observe", StringComparison.Ordinal));
            Check(!spec.Arguments[^1].Contains("takeover", StringComparison.Ordinal));
            Check(!spec.Arguments.Contains("machine"));
        }
        finally
        {
            transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void FullThenDelta()
    {
        var transport = Open("terminal", TerminalMode.Observe, out _, out _);
        try
        {
            var frames = new List<TerminalFrameArrived>();
            var task = Task.Run(async () =>
            {
                await foreach (var item in transport.ReadEventsAsync())
                {
                    if (item is TerminalFrameArrived frame)
                    {
                        frames.Add(frame);
                        if (frames.Count >= 2)
                            break;
                    }
                }
            });
            Check(task.Wait(TimeSpan.FromSeconds(5)));
            Check(frames.Count >= 2);
            Check(frames[0].Frame.Full);
            Check(!frames[1].Frame.Full);
            Check(frames[0].Epoch.Value == 1);
            foreach (var frame in frames)
                frame.Dispose();
        }
        finally
        {
            transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void BannerPollution()
    {
        var transport = Open("terminal-banner", TerminalMode.Observe, out _, out _);
        try
        {
            var failed = DrainFailed(transport, TimeSpan.FromSeconds(5));
            Check(failed.Count >= 1);
            Check(SshTransportMapper.FromTerminal(failed[0].Code) == SshTransportCodes.StdoutProtocolPollution);
            Check(!failed[0].Code.Contains("WELCOME", StringComparison.Ordinal));
        }
        finally
        {
            transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void StderrJson()
    {
        var transport = Open("terminal-stderr-json", TerminalMode.Observe, out _, out _);
        try
        {
            var frames = new List<TerminalFrameArrived>();
            var failed = new List<TerminalProtocolFailed>();
            var task = Task.Run(async () =>
            {
                await foreach (var item in transport.ReadEventsAsync())
                {
                    if (item is TerminalProtocolFailed protocol)
                        failed.Add(protocol);
                    if (item is TerminalFrameArrived frame)
                    {
                        frames.Add(frame);
                        if (frames.Count >= 2)
                            break;
                    }
                }
            });
            Check(task.Wait(TimeSpan.FromSeconds(5)));
            Check(frames.Count >= 2);
            Check(failed.Count == 0);
            foreach (var frame in frames)
                frame.Dispose();
        }
        finally
        {
            transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void Oversize()
    {
        var transport = Open("terminal-oversize", TerminalMode.Observe, out _, out _);
        try
        {
            var failed = DrainFailed(transport, TimeSpan.FromSeconds(30));
            Check(failed.Count >= 1);
            Check(SshTransportMapper.FromTerminal(failed[0].Code) == SshTransportCodes.RecordTooLarge);
        }
        finally
        {
            transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void Truncated()
    {
        var transport = Open("terminal-truncated", TerminalMode.Observe, out _, out _);
        try
        {
            var ended = new List<TerminalStdoutEnded>();
            var failed = new List<TerminalProtocolFailed>();
            var task = Task.Run(async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    await foreach (var item in transport.ReadEventsAsync(cts.Token))
                    {
                        if (item is TerminalStdoutEnded stdout)
                            ended.Add(stdout);
                        if (item is TerminalProtocolFailed protocol)
                            failed.Add(protocol);
                        if (item is TerminalFrameArrived frame)
                            frame.Dispose();
                        if (item is TerminalTransportEnded)
                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                }
            });
            Check(task.Wait(TimeSpan.FromSeconds(7)));
            Check(ended.Any(item => item.Truncated) ||
                  failed.Any(item => SshTransportMapper.FromTerminal(item.Code) ==
                                     SshTransportCodes.StreamTruncated));
        }
        finally
        {
            transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void ClosedEofExit()
    {
        var closed = Open("terminal-closed", TerminalMode.Observe, out _, out _);
        try
        {
            var kinds = DrainKinds(closed, TimeSpan.FromSeconds(5));
            Check(kinds.Contains(nameof(TerminalClosedObserved)));
            Check(kinds.Contains(nameof(TerminalStdoutEnded)) || kinds.Contains(nameof(TerminalTransportEnded)));
        }
        finally
        {
            closed.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        var exited = Open("terminal-exit-1", TerminalMode.Observe, out _, out _);
        try
        {
            var kinds = DrainKinds(exited, TimeSpan.FromSeconds(5));
            Check(kinds.Contains(nameof(TerminalProcessExited)) || kinds.Contains(nameof(TerminalTransportEnded)));
        }
        finally
        {
            exited.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void OldEpochInputDropped()
    {
        var transport = Open("terminal", TerminalMode.Control, out _, out var pane);
        try
        {
            var stale = transport.SendInputAsync(
                new TerminalInputCommand(pane, new ConnectionEpoch(9), Bytes: [0x61]))
                .AsTask().GetAwaiter().GetResult();
            Check(stale.Code == TerminalTransportCodes.StaleEpoch);
            Check(stale.Disposition == TerminalWriteDisposition.NotSent);
        }
        finally
        {
            transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static List<TerminalProtocolFailed> DrainFailed(ITerminalTransport transport, TimeSpan timeout)
    {
        var failed = new List<TerminalProtocolFailed>();
        var task = Task.Run(async () =>
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await foreach (var item in transport.ReadEventsAsync(cts.Token))
                {
                    if (item is TerminalProtocolFailed protocol)
                        failed.Add(protocol);
                    if (item is TerminalFrameArrived frame)
                        frame.Dispose();
                    if (item is TerminalTransportEnded)
                        break;
                }
            }
            catch (OperationCanceledException)
            {
            }
        });
        Check(task.Wait(timeout + TimeSpan.FromSeconds(2)));
        return failed;
    }

    static List<string> DrainKinds(ITerminalTransport transport, TimeSpan timeout)
    {
        var kinds = new List<string>();
        var task = Task.Run(async () =>
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await foreach (var item in transport.ReadEventsAsync(cts.Token))
                {
                    kinds.Add(item.GetType().Name);
                    if (item is TerminalFrameArrived frame)
                        frame.Dispose();
                    if (item is TerminalTransportEnded)
                        break;
                }
            }
            catch (OperationCanceledException)
            {
            }
        });
        Check(task.Wait(timeout + TimeSpan.FromSeconds(2)));
        return kinds;
    }

    static ITerminalTransport Open(
        string mode, TerminalMode terminalMode, out SshProcessSpec spec, out PaneKey pane)
    {
        var exe = Environment.ProcessPath!;
        var locator = new OpenSshLocator(exe, exe, ["--fake-ssh-channel", mode]);
        var factory = new RemoteTerminalTransportFactory(locator);
        var device = new DeviceId(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
        var session = new SessionKey(device, "remote-api", "dev");
        pane = new PaneKey(session, "ws1", "pane1");
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
        var request = new TerminalOpenRequest(
            pane, launch.Epoch, terminalMode, exe, "pane-target", 120, 40, "dev");
        Check(factory.TryOpen(launch, request, out var transport, out var opened, out _));
        Check(transport is not null && opened is not null);
        spec = opened!;
        return transport!;
    }
}
