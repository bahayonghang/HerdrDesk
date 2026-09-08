using System.Diagnostics;
using System.Text.Json;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Process;
using HerdDesk.Infrastructure.Ssh;
using HerdDesk.Infrastructure.SshTransports;
using OsProcess = System.Diagnostics.Process;

internal static class RemoteSessionTransportSetTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("request and event pair become ready after ping and subscribe", ReadyPair),
        ("same device two session keys stay isolated", TwoSessionKeys),
        ("device a failure does not occupy device b", CrossDeviceIsolation),
        ("partial event start rolls back the request child", PartialStartRollback),
        ("banner fails closed and reports await-user disposition", BannerFailsSet),
        ("epoch rollover drops old frames and input", EpochRollover),
        ("reconnect terminal is observe only", ReconnectObserve),
        ("transport has no retry timer", NoRetryTimer),
        ("l2 live ssh remains unverified", L2Unverified)
    ];

    static void ReadyPair()
    {
        var root = SshTransportFixtures.TempRoot();
        RemoteSessionTransportSet? set = null;
        try
        {
            set = SshTransportFixtures.OpenSet("rpc", root);
            SshTransportFixtures.Check(set.Ready);
            SshTransportFixtures.Check(set.Request is not null);
            SshTransportFixtures.Check(set.Events is not null);
            SshTransportFixtures.Check(set.Request!.ChildProcessId != set.Events!.ChildProcessId);
            SshTransportFixtures.Check(set.RequestSpec is not null);
            SshTransportFixtures.Check(set.RequestSpec!.Arguments.Contains("-T"));
            SshTransportFixtures.Check(set.EventSpec!.Arguments.Contains("-T"));
            SshTransportFixtures.Check(set.Outcome!.Stage == SshTransportStage.Ready);
            SshTransportFixtures.Check(set.HostProbe!.CliCompatible);
            SshTransportFixtures.Check(set.Ping!.Succeeded);
            var pane = SshTransportFixtures.Pane(set.Session);
            var request = Observe(pane, set.Epoch.Value);
            SshTransportFixtures.Check(set.TryOpenTerminal(request, out var transport, out var spec, out _));
            SshTransportFixtures.Check(transport is not null);
            SshTransportFixtures.Check(spec!.Arguments.Contains("-T"));
            SshTransportFixtures.Check(!SshProcessSpecFactory.HasInteractiveTtyFlag(spec.Arguments));
            using var ping = set.Request!.RequestAsync("ping", Empty()).AsTask().GetAwaiter().GetResult();
            SshTransportFixtures.Check(ping.Succeeded);
            var events = 0;
            var read = Task.Run(async () =>
            {
                await foreach (var item in set.Events!.ReadEventsAsync())
                {
                    _ = item;
                    events++;
                    if (events >= 1)
                        break;
                }
            });
            SshTransportFixtures.Check(read.Wait(TimeSpan.FromSeconds(5)));
            SshTransportFixtures.Check(events >= 1);
            var frames = new List<TerminalTransportEvent>();
            var drain = Task.Run(async () =>
            {
                await foreach (var item in transport!.ReadEventsAsync())
                {
                    frames.Add(item);
                    if (item is TerminalFrameArrived arrived)
                    {
                        arrived.Dispose();
                        break;
                    }
                }
            });
            SshTransportFixtures.Check(drain.Wait(TimeSpan.FromSeconds(5)));
            SshTransportFixtures.Check(frames.OfType<TerminalFrameArrived>().Any());
        }
        finally
        {
            set?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Directory.Delete(root, true);
        }
    }

    static void TwoSessionKeys()
    {
        var root = SshTransportFixtures.TempRoot();
        RemoteSessionTransportSet? keep = null;
        RemoteSessionTransportSet? fail = null;
        try
        {
            var sessionKeep = SshTransportFixtures.Session(name: "dev");
            var sessionFail = SshTransportFixtures.Session(name: "other");
            keep = SshTransportFixtures.OpenSet("rpc", root, sessionKeep);
            fail = SshTransportFixtures.OpenSet("banner", root, sessionFail);
            SshTransportFixtures.Check(keep.Ready);
            SshTransportFixtures.Check(!fail.Ready);
            SshTransportFixtures.Check(fail.Outcome!.Code == SshTransportCodes.StdoutProtocolPollution);
            SshTransportFixtures.Check(fail.Outcome.Disposition == SshRetryDisposition.AwaitUser);
            using var ping = keep.Request!.RequestAsync("ping", Empty()).AsTask().GetAwaiter().GetResult();
            SshTransportFixtures.Check(ping.Succeeded);
            SshTransportFixtures.Check(keep.Request.PendingCount == 0);
        }
        finally
        {
            fail?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            keep?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Directory.Delete(root, true);
        }
    }

    static void CrossDeviceIsolation()
    {
        var root = SshTransportFixtures.TempRoot();
        RemoteSessionTransportSet? deviceA = null;
        RemoteSessionTransportSet? deviceB = null;
        try
        {
            var sessionA = SshTransportFixtures.Session(SshTransportFixtures.DeviceA, name: "dev");
            var sessionB = SshTransportFixtures.Session(SshTransportFixtures.DeviceB, name: "dev");
            deviceA = SshTransportFixtures.OpenSet("banner", root, sessionA);
            deviceB = SshTransportFixtures.OpenSet("rpc", root, sessionB);
            SshTransportFixtures.Check(!deviceA.Ready);
            SshTransportFixtures.Check(deviceB.Ready);
            using var ping = deviceB.Request!.RequestAsync("ping", Empty()).AsTask().GetAwaiter().GetResult();
            SshTransportFixtures.Check(ping.Succeeded);
        }
        finally
        {
            deviceA?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            deviceB?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Directory.Delete(root, true);
        }
    }

    static void PartialStartRollback()
    {
        var root = SshTransportFixtures.TempRoot();
        var starter = new FailOnNthStarter(2);
        RemoteSessionTransportSet? set = null;
        try
        {
            set = SshTransportFixtures.OpenSet("rpc", root, starter: starter);
            SshTransportFixtures.Check(!set.Ready);
            SshTransportFixtures.Check(set.Outcome!.Code == SshTransportCodes.ChildExited);
            SshTransportFixtures.Check(starter.Count == 2);
            SshTransportFixtures.Check(starter.StartedIds.Count == 1);
            var id = starter.StartedIds[0];
            SshTransportFixtures.WaitUntil(() =>
            {
                try
                {
                    return OsProcess.GetProcessById(id).HasExited;
                }
                catch (ArgumentException)
                {
                    return true;
                }
            });
        }
        finally
        {
            set?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Directory.Delete(root, true);
        }
    }

    static void BannerFailsSet()
    {
        var root = SshTransportFixtures.TempRoot();
        RemoteSessionTransportSet? set = null;
        try
        {
            set = SshTransportFixtures.OpenSet("banner", root);
            SshTransportFixtures.Check(!set.Ready);
            SshTransportFixtures.Check(set.Outcome!.Code == SshTransportCodes.StdoutProtocolPollution);
            SshTransportFixtures.Check(set.Outcome.Disposition == SshRetryDisposition.AwaitUser);
            SshTransportFixtures.Check(!set.Outcome.Code.Contains("WELCOME", StringComparison.Ordinal));
        }
        finally
        {
            set?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Directory.Delete(root, true);
        }
    }

    static void EpochRollover()
    {
        var root = SshTransportFixtures.TempRoot();
        RemoteSessionTransportSet? first = null;
        RemoteSessionTransportSet? second = null;
        try
        {
            var session = SshTransportFixtures.Session();
            first = OpenTerminalSet(root, session, 1);
            second = OpenTerminalSet(root, session, 2);
            SshTransportFixtures.Check(first.Ready);
            SshTransportFixtures.Check(second.Ready);
            SshTransportFixtures.Check(first.Epoch.Value == 1);
            SshTransportFixtures.Check(second.Epoch.Value == 2);
            var pane = SshTransportFixtures.Pane(session);
            SshTransportFixtures.Check(first.TryOpenTerminal(
                Observe(pane, 1), out var oldTransport, out _, out _));
            var control = new TerminalOpenRequest(
                pane, new ConnectionEpoch(2), TerminalMode.Control, SshTransportFixtures.SelfExe(),
                "pane-target", 120, 40, "dev");
            SshTransportFixtures.Check(second.TryOpenTerminal(
                control, out var newTransport, out _, out _));
            SshTransportFixtures.Check(oldTransport is not null && newTransport is not null);
            var stale = newTransport!.SendInputAsync(
                new TerminalInputCommand(pane, new ConnectionEpoch(1), Bytes: [0x61]))
                .AsTask().GetAwaiter().GetResult();
            SshTransportFixtures.Check(stale.Code == TerminalTransportCodes.StaleEpoch);
            first.DisposeAsync().AsTask().GetAwaiter().GetResult();
            first = null;
            using var ping = second.Request!.RequestAsync("ping", Empty()).AsTask().GetAwaiter().GetResult();
            SshTransportFixtures.Check(ping.Succeeded);
        }
        finally
        {
            first?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            second?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Directory.Delete(root, true);
        }
    }

    static void ReconnectObserve()
    {
        var root = SshTransportFixtures.TempRoot();
        RemoteSessionTransportSet? set = null;
        try
        {
            var session = SshTransportFixtures.Session();
            set = OpenTerminalSet(root, session, 4);
            var pane = SshTransportFixtures.Pane(session);
            SshTransportFixtures.Check(set.TryOpenTerminal(Observe(pane, 4), out var transport, out var spec, out _));
            SshTransportFixtures.Check(transport!.Mode == TerminalMode.Observe);
            SshTransportFixtures.Check(spec!.Arguments[^1].Contains("observe", StringComparison.Ordinal));
            SshTransportFixtures.Check(!spec.Arguments[^1].Contains("takeover", StringComparison.Ordinal));
            var denied = transport.SendInputAsync(
                new TerminalInputCommand(pane, new ConnectionEpoch(4), Bytes: [0x61]))
                .AsTask().GetAwaiter().GetResult();
            SshTransportFixtures.Check(denied.Code == TerminalTransportCodes.ObserveInputDenied);
        }
        finally
        {
            set?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Directory.Delete(root, true);
        }
    }

    static void NoRetryTimer()
    {
        var root = SshTransportFixtures.TempRoot();
        RemoteSessionTransportSet? set = null;
        try
        {
            var started = Stopwatch.StartNew();
            set = SshTransportFixtures.OpenSet("banner", root);
            SshTransportFixtures.Check(started.Elapsed < TimeSpan.FromSeconds(3));
            SshTransportFixtures.Check(set.Outcome!.Disposition == SshRetryDisposition.AwaitUser);
            SshTransportFixtures.Check(typeof(RemoteSessionTransportSet).GetField(
                "_retry", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) is null);
        }
        finally
        {
            set?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Directory.Delete(root, true);
        }
    }

    static void L2Unverified()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HerdDesk.slnx")))
            dir = dir.Parent;
        SshTransportFixtures.Check(dir is not null);
        var json = File.ReadAllText(Path.Combine(dir!.FullName, "implementation", "hd-022-l2.json"));
        SshTransportFixtures.Check(json.Contains("UNVERIFIED", StringComparison.Ordinal));
        SshTransportFixtures.Check(json.Contains("\"ac24_passed\": false", StringComparison.Ordinal));
        SshTransportFixtures.Check(json.Contains("\"ac26_passed\": false", StringComparison.Ordinal));
        SshTransportFixtures.Check(json.Contains("\"phase_gate\": \"not_passed\"", StringComparison.Ordinal));
        SshTransportFixtures.Check(json.Contains("\"g0_passed\": false", StringComparison.Ordinal));
        SshTransportFixtures.Check(!Directory.Exists(Path.Combine(dir.FullName, "tests", "Integration.Ssh")));
    }

    static RemoteSessionTransportSet OpenTerminalSet(string root, SessionKey session, long epoch) =>
        SshTransportFixtures.OpenSet("rpc", root, session, epoch);

    static TerminalOpenRequest Observe(PaneKey pane, long epoch) =>
        new(pane, new ConnectionEpoch(epoch), TerminalMode.Observe, SshTransportFixtures.SelfExe(),
            "pane-target", 120, 40, "dev");

    static JsonElement Empty()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    sealed class FailOnNthStarter : ISshChildProcessStarter
    {
        private readonly int _failAt;
        public int Count;
        public List<int> StartedIds { get; } = [];

        public FailOnNthStarter(int failAt) => _failAt = failAt;

        public OwnedChildProcess Start(SshProcessSpec spec)
        {
            Count++;
            if (Count == _failAt)
                throw new IOException("start_failed");
            var child = OwnedSshChildProcessStarter.Instance.Start(spec);
            StartedIds.Add(child.Id);
            return child;
        }
    }

}
