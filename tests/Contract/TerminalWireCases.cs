using System.Reflection;
using System.Text;
using System.Text.Json;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Host;
using HerdDesk.Infrastructure.Terminal;

internal static class TerminalWireCases
{
    public static (string Name, Action Run)[] All =>
    [
        ("terminal factory argv is herdr terminal session without takeover", ArgvShape),
        ("stdin commands are typed ndjson without rpc envelopes", StdinIsNotRpc),
        ("public terminal ports do not expose process", NoProcessLeak),
        ("production composition still leaves terminal transport unavailable", ProductionUnavailable),
        ("hd-013 l2 live herdr remains unverified", L2Unverified)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static string SelfExe()
    {
        var path = Environment.ProcessPath;
        Check(!string.IsNullOrWhiteSpace(path));
        return path!;
    }

    static PaneKey TestPane() => new(
        new SessionKey(new DeviceId(Guid.Parse("11111111-2222-3333-4444-555555555555")), "local-api", "dev"),
        "ws1",
        "pane1");

    static void ArgvShape()
    {
        var root = Path.Combine(Path.GetTempPath(), "herddesk-hd013-wire-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var stdinFile = Path.Combine(root, "stdin.ndjson");
        var argvFile = Path.Combine(root, "argv.txt");
        var exe = SelfExe();
        var factory = new TerminalCliProcessFactory(
            exe,
            null,
            null,
            ["--fake-terminal", "frames", "--stdin-file", stdinFile, "--argv-file", argvFile]);
        var transport = factory.OpenAsync(new TerminalOpenRequest(
            TestPane(), new ConnectionEpoch(2), TerminalMode.Observe, exe, "pane-target", 80, 24, "dev"))
            .AsTask().GetAwaiter().GetResult();
        Check(transport is not null);
        try
        {
            var seen = new List<TerminalTransportEvent>();
            var drain = Task.Run(async () =>
            {
                await foreach (var item in transport!.ReadEventsAsync())
                {
                    seen.Add(item);
                    if (item is TerminalFrameArrived frame)
                        frame.Dispose();
                    if (item is TerminalTransportEnded)
                        break;
                }
            });
            var clock = DateTime.UtcNow;
            while (seen.OfType<TerminalFrameArrived>().Count() < 1 && DateTime.UtcNow - clock < TimeSpan.FromSeconds(5))
                Thread.Sleep(20);
            transport!.DisposeAsync().AsTask().GetAwaiter().GetResult();
            drain.Wait(TimeSpan.FromSeconds(5));
            var argv = File.ReadAllLines(argvFile);
            Check(argv.SequenceEqual(["--session", "dev", "terminal", "session", "observe", "pane-target",
                "--cols", "80", "--rows", "24"]));
            Check(!argv.Contains("takeover"));
        }
        finally
        {
            transport!.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Directory.Delete(root, true);
        }
    }

    static void StdinIsNotRpc()
    {
        var root = Path.Combine(Path.GetTempPath(), "herddesk-hd013-stdin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var stdinFile = Path.Combine(root, "stdin.ndjson");
        var argvFile = Path.Combine(root, "argv.txt");
        var exe = SelfExe();
        var factory = new TerminalCliProcessFactory(
            exe,
            null,
            null,
            ["--fake-terminal", "capture", "--stdin-file", stdinFile, "--argv-file", argvFile]);
        var pane = TestPane();
        var epoch = new ConnectionEpoch(1);
        var transport = factory.OpenAsync(new TerminalOpenRequest(
            pane, epoch, TerminalMode.Control, exe, "pane-target", 120, 40, "dev"))
            .AsTask().GetAwaiter().GetResult();
        Check(transport is not null);
        try
        {
            var seen = new List<TerminalTransportEvent>();
            var drain = Task.Run(async () =>
            {
                await foreach (var item in transport!.ReadEventsAsync())
                {
                    seen.Add(item);
                    if (item is TerminalFrameArrived frame)
                        frame.Dispose();
                    if (item is TerminalTransportEnded)
                        break;
                }
            });
            var clock = DateTime.UtcNow;
            while (!seen.OfType<TerminalFrameArrived>().Any() && DateTime.UtcNow - clock < TimeSpan.FromSeconds(5))
                Thread.Sleep(20);
            var sent = transport!.SendInputAsync(new TerminalInputCommand(pane, epoch, Text: "ab"))
                .AsTask().GetAwaiter().GetResult();
            Check(sent.Disposition == TerminalWriteDisposition.WrittenUnacknowledged);
            var scrolled = transport.ScrollAsync(
                new TerminalScrollCommand(pane, epoch, "up", 3, "wheel", 5, 6, 0))
                .AsTask().GetAwaiter().GetResult();
            Check(scrolled.Disposition == TerminalWriteDisposition.WrittenUnacknowledged);
            transport.ReleaseAsync().AsTask().GetAwaiter().GetResult();
            transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
            drain.Wait(TimeSpan.FromSeconds(8));
            foreach (var line in File.ReadAllLines(stdinFile))
            {
                using var document = JsonDocument.Parse(line);
                Check(!document.RootElement.TryGetProperty("method", out _));
                Check(!document.RootElement.TryGetProperty("id", out _));
                var type = document.RootElement.GetProperty("type").GetString();
                Check(type is "terminal.input" or "terminal.resize" or "terminal.scroll" or "terminal.release");
            }
        }
        finally
        {
            transport!.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Directory.Delete(root, true);
        }
    }

    static void NoProcessLeak()
    {
        foreach (var method in typeof(ITerminalTransport).GetMethods())
        {
            Check(method.ReturnType.Name != "Process");
            foreach (var parameter in method.GetParameters())
                Check(parameter.ParameterType.Name != "Process");
        }

        Check(typeof(ITerminalTransport).GetProperty("StandardInput") is null);
        Check(typeof(ITerminalTransportFactory).GetMethod(nameof(ITerminalTransportFactory.OpenAsync)) is not null);
        var refs = typeof(TerminalCliProcessFactory).Assembly.GetReferencedAssemblies().Select(item => item.Name!);
        Check(refs.Contains("HerdDesk.Contracts"));
        Check(refs.Contains("HerdDesk.Core"));
    }

    static void ProductionUnavailable()
    {
        var adapter = new UnavailableAdapter("terminal-transport", "terminal_transport_unavailable");
        Check(!adapter.Available);
        Check(!adapter.IsFakeSuccess);
        var exe = SelfExe();
        Check(adapter.OpenAsync(new TerminalOpenRequest(
            TestPane(), new ConnectionEpoch(1), TerminalMode.Observe, exe, "pane-target", 120, 40))
            .AsTask().GetAwaiter().GetResult() is null);
    }

    static void L2Unverified()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HerdDesk.slnx")))
            dir = dir.Parent;
        Check(dir is not null);
        var json = File.ReadAllText(Path.Combine(dir!.FullName, "implementation", "hd-013-l2.json"), Encoding.UTF8);
        Check(json.Contains("UNVERIFIED", StringComparison.Ordinal));
        Check(json.Contains("\"ac05_passed\": false", StringComparison.Ordinal));
        Check(json.Contains("\"ac06_passed\": false", StringComparison.Ordinal));
        Check(typeof(ITerminalTransport).Assembly.GetName().Name == "HerdDesk.Contracts");
    }
}
