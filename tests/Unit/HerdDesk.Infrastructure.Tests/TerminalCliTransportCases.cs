using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Diagnostics;
using HerdDesk.Infrastructure.Process;
using HerdDesk.Infrastructure.Terminal;
using OsProcess = System.Diagnostics.Process;

internal static class TerminalCliTransportCases
{
    public static (string Name, Action Run)[] All =>
    [
        ("chunked frames reassemble baseline then delta", ChunkedFrames),
        ("crlf frames parse as one record each", CrlfFrames),
        ("oversize line fails closed", OversizeLine),
        ("sequence gap fails closed", SequenceGap),
        ("malformed line fails closed", MalformedLine),
        ("truncated eof fails closed", TruncatedEof),
        ("terminal.closed then eof are distinct", ClosedThenEof),
        ("missing graphics is not a parse failure", GraphicsAbsent),
        ("slow consumer backpressure ends the transport", Backpressure),
        ("stderr flood drains without raw text in diagnostics", StderrFlood),
        ("one hundred concurrent sends stay serial ndjson", ConcurrentSends),
        ("disconnect race does not replay", DisconnectRace),
        ("cancel before write is not sent", CancelBeforeWrite),
        ("resize and release match the wire contract", ResizeReleaseWire),
        ("release is serialized once and then rejects input", ReleaseOnce),
        ("dispose does not write server stop", NoServerStop),
        ("dispose kills only the recorded direct child", DirectChildOnlyKill),
        ("observe never sets control verified", ObserveNotVerified),
        ("control first frame stays unconfirmed", ControlFirstFrameUnknown),
        ("loaded hd-004 fingerprint can verify with attempt", LoadedFingerprintVerified),
        ("observe send input is denied", ObserveInputDenied),
        ("invalid target and takeover are rejected before start", RejectsBadOpen),
        ("confirmed control takeover adds the takeover flag", ConfirmedTakeoverArgv),
        ("serializer emits canonical stdin commands", SerializerWire),
        ("l2 live herdr remains unverified", L2Unverified)
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

    static TerminalOpenRequest Request(
        string exe,
        TerminalMode mode,
        string target = "pane-target",
        string? attempt = null) =>
        new(TestPane(), new ConnectionEpoch(1), mode, exe, target, 120, 40, "dev", attempt);

    static Harness Open(
        string mode,
        TerminalMode terminalMode = TerminalMode.Observe,
        TerminalTransportOptions? options = null,
        string? attempt = null,
        IDiagnosticSink? diagnostics = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "herddesk-hd013-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var stdinFile = Path.Combine(root, "stdin.ndjson");
        var argvFile = Path.Combine(root, "argv.txt");
        var exe = SelfExe();
        var factory = new TerminalCliProcessFactory(
            exe,
            diagnostics,
            options,
            ["--fake-terminal", mode, "--stdin-file", stdinFile, "--argv-file", argvFile]);
        var transport = factory.OpenAsync(Request(exe, terminalMode, attempt: attempt))
            .AsTask().GetAwaiter().GetResult();
        Check(transport is not null);
        return new Harness(transport!, stdinFile, argvFile, root);
    }

    static List<TerminalTransportEvent> Drain(ITerminalTransport transport, TimeSpan timeout)
    {
        var list = new List<TerminalTransportEvent>();
        var task = PumpEvents(transport, list, timeout);
        if (!task.Wait(timeout + TimeSpan.FromSeconds(2)))
            throw new Exception("drain_timeout");
        task.GetAwaiter().GetResult();
        lock (list)
            return [.. list];
    }

    static Task PumpEvents(
        ITerminalTransport transport,
        List<TerminalTransportEvent> list,
        TimeSpan timeout)
    {
        return Task.Run(async () =>
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await foreach (var item in transport.ReadEventsAsync(cts.Token).ConfigureAwait(false))
                {
                    lock (list)
                        list.Add(item);
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
    }

    static int Count<T>(List<TerminalTransportEvent> list) where T : TerminalTransportEvent
    {
        lock (list)
            return list.OfType<T>().Count();
    }

    static bool Any<T>(List<TerminalTransportEvent> list, Func<T, bool>? predicate = null)
        where T : TerminalTransportEvent
    {
        lock (list)
            return predicate is null ? list.OfType<T>().Any() : list.OfType<T>().Any(predicate);
    }

    static T[] Snapshot<T>(List<TerminalTransportEvent> list) where T : TerminalTransportEvent
    {
        lock (list)
            return list.OfType<T>().ToArray();
    }

    static void WaitUntil(Func<bool> predicate, int milliseconds = 5000)
    {
        var clock = Stopwatch.StartNew();
        while (!predicate())
        {
            if (clock.ElapsedMilliseconds > milliseconds)
                throw new Exception("wait_timeout");
            Thread.Sleep(20);
        }
    }

    static void ChunkedFrames() => ExpectFrames("chunked", 2);

    static void CrlfFrames() => ExpectFrames("crlf", 2);

    static void ExpectFrames(string mode, int count)
    {
        var harness = Open(mode);
        try
        {
            var seen = new List<TerminalTransportEvent>();
            var drain = PumpEvents(harness.Transport, seen, TimeSpan.FromSeconds(8));
            WaitUntil(() => Count<TerminalFrameArrived>(seen) >= count);
            harness.Transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Check(drain.Wait(TimeSpan.FromSeconds(8)));
            var frames = Snapshot<TerminalFrameArrived>(seen);
            Check(frames.Length >= count);
            Check(frames[0].Frame.Full);
            Check(frames[0].Frame.Sequence == 1);
            Check(frames[1].Frame.Sequence == 2);
            Check(!frames[1].Frame.Full);
            Check(frames[0].Epoch.Value == 1);
            Check(!Any<TerminalProtocolFailed>(seen));
        }
        finally
        {
            harness.Dispose();
        }
    }

    static void OversizeLine() => ExpectProtocol("oversize", TerminalTransportCodes.LineBytesLimit);

    static void SequenceGap() => ExpectProtocol("gap", "sequence_gap_or_replay");

    static void MalformedLine() => ExpectProtocol("malformed", TerminalTransportCodes.Malformed);

    static void TruncatedEof()
    {
        var harness = Open("truncated");
        try
        {
            var events = Drain(harness.Transport, TimeSpan.FromSeconds(8));
            harness.Transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Check(events.OfType<TerminalStdoutEnded>().Any(item => item.Truncated) ||
                  events.OfType<TerminalTransportEnded>().Any(item =>
                      item.Code == TerminalTransportCodes.TruncatedRecord));
        }
        finally
        {
            harness.Dispose();
        }
    }

    static void ExpectProtocol(string mode, string code)
    {
        var harness = Open(mode);
        try
        {
            var events = Drain(harness.Transport, TimeSpan.FromSeconds(15));
            harness.Transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Check(events.OfType<TerminalProtocolFailed>().Any(item => item.Code == code) ||
                  events.OfType<TerminalTransportEnded>().Any(item => item.Code == code));
        }
        finally
        {
            harness.Dispose();
        }
    }

    static void ClosedThenEof()
    {
        var harness = Open("closed-eof");
        try
        {
            var events = Drain(harness.Transport, TimeSpan.FromSeconds(8));
            harness.Transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Check(events.OfType<TerminalClosedObserved>().Any());
            Check(events.OfType<TerminalStdoutEnded>().Any(item => !item.Truncated));
            var ended = events.OfType<TerminalTransportEnded>().Single();
            Check(ended.SawClosedEnvelope);
            Check(ended.Code is TerminalTransportCodes.Closed or TerminalTransportCodes.StdoutEnded
                or TerminalTransportCodes.ProcessExited);
        }
        finally
        {
            harness.Dispose();
        }
    }

    static void GraphicsAbsent()
    {
        var harness = Open("graphics-absent");
        try
        {
            var seen = new List<TerminalTransportEvent>();
            var drain = PumpEvents(harness.Transport, seen, TimeSpan.FromSeconds(8));
            WaitUntil(() => Count<TerminalFrameArrived>(seen) >= 2);
            harness.Transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Check(drain.Wait(TimeSpan.FromSeconds(8)));
            Check(!Any<TerminalProtocolFailed>(seen));
            Check(Count<TerminalFrameArrived>(seen) >= 2);
        }
        finally
        {
            harness.Dispose();
        }
    }

    static void Backpressure()
    {
        var harness = Open("flood-frames", options: new TerminalTransportOptions
        {
            EventItemLimit = 2,
            EventByteLimit = 24 * 1024 * 1024
        });
        try
        {
            Thread.Sleep(400);
            var events = Drain(harness.Transport, TimeSpan.FromSeconds(8));
            harness.Transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Check(events.OfType<TerminalConsumerBackpressure>().Any() ||
                  events.OfType<TerminalTransportEnded>().Any(item =>
                      item.Code == TerminalTransportCodes.ConsumerBackpressure));
            Check(events.OfType<TerminalFrameArrived>().Count() <= 2);
        }
        finally
        {
            harness.Dispose();
        }
    }

    static void StderrFlood()
    {
        var sink = new CollectingSink();
        var harness = Open("stderr-flood", diagnostics: sink);
        try
        {
            var seen = new List<TerminalTransportEvent>();
            var drain = PumpEvents(harness.Transport, seen, TimeSpan.FromSeconds(8));
            WaitUntil(() => Any<TerminalFrameArrived>(seen));
            harness.Transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Check(drain.Wait(TimeSpan.FromSeconds(8)));
            Check(Any<TerminalFrameArrived>(seen));
            foreach (var evt in sink.Events)
            {
                Check(evt.Component != "xxxx");
                Check(evt.Operation != "xxxx");
                Check(evt.ErrorCode is null || !evt.ErrorCode.Contains("xxxx", StringComparison.Ordinal));
                Check(evt.ErrorCode is null ||
                      !evt.ErrorCode.Contains("terminal.input", StringComparison.Ordinal));
            }
        }
        finally
        {
            harness.Dispose();
        }
    }

    static void ConcurrentSends()
    {
        var harness = Open("capture", TerminalMode.Control);
        try
        {
            var seen = new List<TerminalTransportEvent>();
            var drain = PumpEvents(harness.Transport, seen, TimeSpan.FromSeconds(15));
            WaitUntil(() => Any<TerminalFrameArrived>(seen));
            var pane = TestPane();
            var epoch = new ConnectionEpoch(1);
            var tasks = Enumerable.Range(0, 100).Select(index =>
                harness.Transport.SendInputAsync(
                    new TerminalInputCommand(pane, epoch, Text: "c" + index.ToString("D3"))).AsTask()).ToArray();
            Task.WaitAll(tasks);
            var ids = new HashSet<ulong>();
            foreach (var task in tasks)
            {
                Check(task.Result.Disposition == TerminalWriteDisposition.WrittenUnacknowledged);
                Check(ids.Add(task.Result.CommandId));
            }

            var release = harness.Transport.ReleaseAsync().AsTask().GetAwaiter().GetResult();
            Check(release.Disposition is TerminalWriteDisposition.WrittenUnacknowledged
                or TerminalWriteDisposition.UnknownAfterDisconnect);
            harness.Transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Check(drain.Wait(TimeSpan.FromSeconds(15)));
            var lines = File.Exists(harness.StdinFile) ? File.ReadAllLines(harness.StdinFile) : [];
            Check(lines.Length >= 100);
            var inputs = 0;
            var releases = 0;
            foreach (var line in lines)
            {
                using var document = JsonDocument.Parse(line);
                Check(document.RootElement.ValueKind == JsonValueKind.Object);
                Check(!document.RootElement.TryGetProperty("method", out _));
                var type = document.RootElement.GetProperty("type").GetString();
                if (type == "terminal.input")
                    inputs++;
                if (type == "terminal.release")
                    releases++;
            }

            Check(inputs == 100);
            Check(releases == 1);
        }
        finally
        {
            harness.Dispose();
        }
    }

    static void DisconnectRace()
    {
        var harness = Open("capture", TerminalMode.Control);
        try
        {
            var seen = new List<TerminalTransportEvent>();
            var drain = PumpEvents(harness.Transport, seen, TimeSpan.FromSeconds(10));
            WaitUntil(() => Any<TerminalFrameArrived>(seen));
            var pane = TestPane();
            var epoch = new ConnectionEpoch(1);
            var sends = Enumerable.Range(0, 20).Select(index =>
                harness.Transport.SendInputAsync(
                    new TerminalInputCommand(pane, epoch, Text: "r" + index)).AsTask()).ToArray();
            var stop = Task.Run(() => harness.Transport.DisposeAsync().AsTask());
            Task.WaitAll([.. sends, stop]);
            var ids = new HashSet<ulong>();
            foreach (var send in sends)
            {
                Check(send.Result.Disposition is TerminalWriteDisposition.NotSent
                    or TerminalWriteDisposition.WrittenUnacknowledged
                    or TerminalWriteDisposition.UnknownAfterDisconnect);
                Check(ids.Add(send.Result.CommandId));
            }

            drain.Wait(TimeSpan.FromSeconds(10));
            if (File.Exists(harness.StdinFile))
            {
                var lines = File.ReadAllLines(harness.StdinFile);
                Check(lines.Length <= 20);
                foreach (var line in lines)
                    using (var document = JsonDocument.Parse(line))
                        Check(document.RootElement.GetProperty("type").GetString() != "terminal.input" ||
                              document.RootElement.TryGetProperty("text", out _));
            }
        }
        finally
        {
            harness.Dispose();
        }
    }

    static void CancelBeforeWrite()
    {
        var harness = Open("hang", TerminalMode.Control);
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var receipt = harness.Transport.SendInputAsync(
                new TerminalInputCommand(TestPane(), new ConnectionEpoch(1), Text: "x"),
                cts.Token).AsTask().GetAwaiter().GetResult();
            Check(receipt.Disposition == TerminalWriteDisposition.NotSent);
            Check(receipt.Code is TerminalTransportCodes.NotSent or TerminalTransportCodes.Closing);
        }
        finally
        {
            harness.Dispose();
        }
    }

    static void ResizeReleaseWire()
    {
        var harness = Open("capture", TerminalMode.Control);
        try
        {
            var seen = new List<TerminalTransportEvent>();
            var drain = PumpEvents(harness.Transport, seen, TimeSpan.FromSeconds(8));
            WaitUntil(() => Any<TerminalFrameArrived>(seen));
            var pane = TestPane();
            var epoch = new ConnectionEpoch(1);
            var resized = harness.Transport.ResizeAsync(
                new TerminalResizeCommand(pane, epoch, 80, 24, 9, 18)).AsTask().GetAwaiter().GetResult();
            Check(resized.Disposition == TerminalWriteDisposition.WrittenUnacknowledged);
            var released = harness.Transport.ReleaseAsync().AsTask().GetAwaiter().GetResult();
            Check(released.Disposition == TerminalWriteDisposition.WrittenUnacknowledged);
            harness.Transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Check(drain.Wait(TimeSpan.FromSeconds(8)));
            var lines = File.ReadAllLines(harness.StdinFile);
            using (var resize = JsonDocument.Parse(lines.First(item => item.Contains("terminal.resize", StringComparison.Ordinal))))
            {
                Check(resize.RootElement.GetProperty("cols").GetUInt16() == 80);
                Check(resize.RootElement.GetProperty("rows").GetUInt16() == 24);
                Check(resize.RootElement.GetProperty("cell_width_px").GetUInt32() == 9);
                Check(resize.RootElement.GetProperty("cell_height_px").GetUInt32() == 18);
            }

            Check(lines.Count(item => item.Contains("terminal.release", StringComparison.Ordinal)) == 1);
            Check(!lines.Any(item => item.Contains("server.stop", StringComparison.Ordinal)));
            Check(!lines.Any(item => item.Contains("pane.close", StringComparison.Ordinal)));
        }
        finally
        {
            harness.Dispose();
        }
    }

    static void ReleaseOnce()
    {
        var harness = Open("capture", TerminalMode.Control);
        try
        {
            var seen = new List<TerminalTransportEvent>();
            var drain = PumpEvents(harness.Transport, seen, TimeSpan.FromSeconds(8));
            WaitUntil(() => Any<TerminalFrameArrived>(seen));
            var first = harness.Transport.ReleaseAsync().AsTask().GetAwaiter().GetResult();
            var second = harness.Transport.ReleaseAsync().AsTask().GetAwaiter().GetResult();
            Check(first.Disposition is TerminalWriteDisposition.WrittenUnacknowledged
                or TerminalWriteDisposition.UnknownAfterDisconnect);
            Check(second.Code is TerminalTransportCodes.ReleaseAlreadyQueued
                or TerminalTransportCodes.Closing
                or TerminalTransportCodes.WrittenUnacknowledged);
            Check(second.CommandId == first.CommandId ||
                  second.Code is TerminalTransportCodes.ReleaseAlreadyQueued or TerminalTransportCodes.Closing);
            var input = harness.Transport.SendInputAsync(
                new TerminalInputCommand(TestPane(), new ConnectionEpoch(1), Text: "z"))
                .AsTask().GetAwaiter().GetResult();
            Check(input.Disposition == TerminalWriteDisposition.NotSent);
            harness.Transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
            drain.Wait(TimeSpan.FromSeconds(8));
            var releases = File.Exists(harness.StdinFile)
                ? File.ReadAllLines(harness.StdinFile).Count(item =>
                    item.Contains("terminal.release", StringComparison.Ordinal))
                : 0;
            Check(releases <= 1);
        }
        finally
        {
            harness.Dispose();
        }
    }

    static void NoServerStop()
    {
        var harness = Open("capture", TerminalMode.Control);
        try
        {
            var seen = new List<TerminalTransportEvent>();
            var drain = PumpEvents(harness.Transport, seen, TimeSpan.FromSeconds(8));
            WaitUntil(() => Any<TerminalFrameArrived>(seen));
            harness.Transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
            drain.Wait(TimeSpan.FromSeconds(8));
            var argv = File.ReadAllLines(harness.ArgvFile);
            Check(argv.Contains("terminal"));
            Check(argv.Contains("session"));
            Check(argv.Contains("control"));
            Check(!argv.Contains("--takeover"));
            Check(!argv.Contains("stop"));
            Check(!argv.Contains("server"));
            if (File.Exists(harness.StdinFile))
            {
                var text = File.ReadAllText(harness.StdinFile);
                Check(!text.Contains("server.stop", StringComparison.Ordinal));
            }
        }
        finally
        {
            harness.Dispose();
        }
    }

    static void DirectChildOnlyKill()
    {
        var daemon = StartHang();
        var agent = StartHang();
        var before = OwnedChildProcessKillLedger.KilledProcessIds.ToArray();
        var harness = Open("hang");
        try
        {
            var child = ((TerminalCliTransport)harness.Transport).ChildProcessId;
            Check(child != daemon.Id && child != agent.Id);
            var started = Stopwatch.StartNew();
            harness.Transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Check(started.Elapsed < TimeSpan.FromSeconds(5));
            Check(!daemon.HasExited);
            Check(!agent.HasExited);
            var killed = OwnedChildProcessKillLedger.KilledProcessIds.Except(before).ToArray();
            Check(!killed.Contains(daemon.Id));
            Check(!killed.Contains(agent.Id));
        }
        finally
        {
            harness.Dispose();
            StopHang(daemon);
            StopHang(agent);
        }
    }

    static void ObserveNotVerified()
    {
        var harness = Open("frames");
        try
        {
            var seen = new List<TerminalTransportEvent>();
            var drain = PumpEvents(harness.Transport, seen, TimeSpan.FromSeconds(8));
            WaitUntil(() => Any<TerminalOwnershipObserved>(seen) || Any<TerminalFrameArrived>(seen));
            harness.Transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
            drain.Wait(TimeSpan.FromSeconds(8));
            foreach (var observed in Snapshot<TerminalOwnershipObserved>(seen))
                Check(!observed.Result.ControlVerified);
        }
        finally
        {
            harness.Dispose();
        }
    }

    static void ControlFirstFrameUnknown()
    {
        var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(SelfExe()))).ToLowerInvariant();
        var harness = Open("frames", TerminalMode.Control, new TerminalTransportOptions
        {
            OwnershipFingerprints =
            [
                new TerminalOwnershipFingerprint(sha, TerminalMode.Control, "attempt-1", "", true)
            ]
        }, "attempt-1");
        try
        {
            var seen = new List<TerminalTransportEvent>();
            var drain = PumpEvents(harness.Transport, seen, TimeSpan.FromSeconds(8));
            WaitUntil(() => Any<TerminalOwnershipObserved>(seen));
            harness.Transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
            drain.Wait(TimeSpan.FromSeconds(8));
            var observed = Snapshot<TerminalOwnershipObserved>(seen)[0];
            Check(!observed.Result.ControlVerified);
            Check(observed.Result.Access is TerminalAccess.Acquiring or TerminalAccess.Unknown
                or TerminalAccess.Observing);
        }
        finally
        {
            harness.Dispose();
        }
    }

    static void LoadedFingerprintVerified()
    {
        var exe = SelfExe();
        var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(exe))).ToLowerInvariant();
        var options = new TerminalTransportOptions
        {
            OwnershipFingerprints =
            [
                new TerminalOwnershipFingerprint(
                    sha, TerminalMode.Control, "attempt-1", FakeTerminalHost.AdapterProvedMarker, true)
            ]
        };
        var harness = Open("proved", TerminalMode.Control, options, "attempt-1");
        try
        {
            var seen = new List<TerminalTransportEvent>();
            var drain = PumpEvents(harness.Transport, seen, TimeSpan.FromSeconds(8));
            WaitUntil(() => Any<TerminalOwnershipObserved>(seen, item => item.Result.ControlVerified), 8000);
            harness.Transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
            drain.Wait(TimeSpan.FromSeconds(8));
            var verified = Snapshot<TerminalOwnershipObserved>(seen).First(item => item.Result.ControlVerified);
            Check(verified.ControlAttemptId == "attempt-1");
            Check(verified.Epoch.Value == 1);
            Check(verified.Result.Access == TerminalAccess.Controlling);
        }
        finally
        {
            harness.Dispose();
        }
    }

    static void ObserveInputDenied()
    {
        var harness = Open("frames");
        try
        {
            var seen = new List<TerminalTransportEvent>();
            var drain = PumpEvents(harness.Transport, seen, TimeSpan.FromSeconds(8));
            WaitUntil(() => Any<TerminalFrameArrived>(seen));
            var receipt = harness.Transport.SendInputAsync(
                new TerminalInputCommand(TestPane(), new ConnectionEpoch(1), Text: "no"))
                .AsTask().GetAwaiter().GetResult();
            Check(receipt.Disposition == TerminalWriteDisposition.NotSent);
            Check(receipt.Code == TerminalTransportCodes.ObserveInputDenied);
            harness.Transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
            drain.Wait(TimeSpan.FromSeconds(8));
            if (File.Exists(harness.StdinFile))
                Check(!File.ReadAllText(harness.StdinFile).Contains("terminal.input", StringComparison.Ordinal));
        }
        finally
        {
            harness.Dispose();
        }
    }

    static void RejectsBadOpen()
    {
        var exe = SelfExe();
        var factory = new TerminalCliProcessFactory(exe, null, null, ["--fake-terminal", "frames"]);
        try
        {
            factory.OpenAsync(Request(exe, TerminalMode.Observe, target: "-evil"))
                .AsTask().GetAwaiter().GetResult();
            throw new Exception("assertion_failed");
        }
        catch (TerminalProtocolException error)
        {
            Check(error.Message == TerminalTransportCodes.InvalidTarget);
        }

        try
        {
            factory.OpenAsync(Request(exe, TerminalMode.Observe) with
            {
                Takeover = new TerminalTakeoverAuthorization(true, "attempt-1")
            }).AsTask().GetAwaiter().GetResult();
            throw new Exception("assertion_failed");
        }
        catch (TerminalProtocolException error)
        {
            Check(error.Message == TerminalTransportCodes.TakeoverUnverified);
        }

        try
        {
            factory.OpenAsync(Request(exe, TerminalMode.Observe) with { Epoch = new ConnectionEpoch(0) })
                .AsTask().GetAwaiter().GetResult();
            throw new Exception("assertion_failed");
        }
        catch (TerminalProtocolException error)
        {
            Check(error.Message == TerminalTransportCodes.InvalidEpoch);
        }

        try
        {
            factory.OpenAsync(Request(exe, TerminalMode.Control, attempt: "attempt-1") with
            {
                Takeover = new TerminalTakeoverAuthorization(false, "attempt-1")
            }).AsTask().GetAwaiter().GetResult();
            throw new Exception("assertion_failed");
        }
        catch (TerminalProtocolException error)
        {
            Check(error.Message == TerminalTransportCodes.TakeoverNotConfirmed);
        }

        try
        {
            factory.OpenAsync(Request(exe, TerminalMode.Control, attempt: "attempt-1") with
            {
                Takeover = new TerminalTakeoverAuthorization(true, "other-attempt")
            }).AsTask().GetAwaiter().GetResult();
            throw new Exception("assertion_failed");
        }
        catch (TerminalProtocolException error)
        {
            Check(error.Message == TerminalTransportCodes.TakeoverUnverified);
        }
    }

    static void ConfirmedTakeoverArgv()
    {
        var root = Path.Combine(Path.GetTempPath(), "herddesk-hd016-takeover-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var stdinFile = Path.Combine(root, "stdin.ndjson");
        var argvFile = Path.Combine(root, "argv.txt");
        var exe = SelfExe();
        var factory = new TerminalCliProcessFactory(
            exe, null, null, ["--fake-terminal", "frames", "--stdin-file", stdinFile, "--argv-file", argvFile]);
        var transport = factory.OpenAsync(Request(exe, TerminalMode.Control, attempt: "attempt-1") with
        {
            Takeover = new TerminalTakeoverAuthorization(true, "attempt-1")
        }).AsTask().GetAwaiter().GetResult();
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
            Check(argv.Contains("--takeover"));
            Check(argv.Contains("control"));
            Check(!argv.Contains("observe"));
        }
        finally
        {
            transport!.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Directory.Delete(root, true);
        }
    }

    static void SerializerWire()
    {
        var pane = TestPane();
        var epoch = new ConnectionEpoch(1);
        Check(TerminalCommandSerializer.TrySerializeInput(
            new TerminalInputCommand(pane, epoch, Text: "你好"), out var text) is null);
        using (var document = JsonDocument.Parse(text))
        {
            Check(document.RootElement.GetProperty("type").GetString() == "terminal.input");
            Check(document.RootElement.GetProperty("text").GetString() == "你好");
            Check(!document.RootElement.TryGetProperty("bytes", out _));
        }

        Check(TerminalCommandSerializer.TrySerializeInput(
            new TerminalInputCommand(pane, epoch, Bytes: [0x03]), out var bytes) is null);
        using (var document = JsonDocument.Parse(bytes))
        {
            Check(document.RootElement.GetProperty("bytes").GetString() == "Aw==");
            Check(!document.RootElement.TryGetProperty("text", out _));
        }

        Check(TerminalCommandSerializer.TrySerializeInput(
            new TerminalInputCommand(pane, epoch), out _) == TerminalTransportCodes.ExactlyOnePayload);
        Check(TerminalCommandSerializer.TrySerializeResize(
            new TerminalResizeCommand(pane, epoch, 0, 40, 9, 18), out _) ==
              TerminalTransportCodes.InvalidResize);
        var release = TerminalCommandSerializer.SerializeRelease();
        using (var document = JsonDocument.Parse(release))
            Check(document.RootElement.GetProperty("type").GetString() == "terminal.release");
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
        Check(json.Contains("\"phase_gate\": \"not_passed\"", StringComparison.Ordinal));
    }

    static OsProcess StartHang()
    {
        var process = OsProcess.Start(new ProcessStartInfo
        {
            FileName = SelfExe(),
            ArgumentList = { "--fake-terminal", "hang" },
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        });
        Check(process is not null);
        return process!;
    }

    static void StopHang(OsProcess process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: false);
        }
        catch (InvalidOperationException)
        {
        }

        process.Dispose();
    }

    sealed class Harness : IDisposable
    {
        public Harness(ITerminalTransport transport, string stdinFile, string argvFile, string root)
        {
            Transport = transport;
            StdinFile = stdinFile;
            ArgvFile = argvFile;
            Root = root;
        }

        public ITerminalTransport Transport { get; }
        public string StdinFile { get; }
        public string ArgvFile { get; }
        public string Root { get; }

        public void Dispose()
        {
            try
            {
                Transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception)
            {
            }

            try
            {
                Directory.Delete(Root, true);
            }
            catch (IOException)
            {
            }
        }
    }

    sealed class CollectingSink : IDiagnosticSink
    {
        public List<DiagnosticEvent> Events { get; } = [];
        public long DroppedCount { get; private set; }

        public bool TryWrite(DiagnosticEvent evt)
        {
            if (!DiagnosticEventValidator.IsValid(evt))
            {
                DroppedCount++;
                return false;
            }

            Events.Add(evt);
            return true;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
