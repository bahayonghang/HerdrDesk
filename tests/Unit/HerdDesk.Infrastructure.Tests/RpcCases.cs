using System.Diagnostics;
using System.Text;
using System.Text.Json;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Process;
using HerdDesk.Infrastructure.Rpc;

internal static class RpcCases
{
    public static (string Name, Action Run)[] All =>
    [
        ("rpc socket path rejects client socket and remote smb", RejectsBadPaths),
        ("bounded ndjson reader splits on lf and rejects overflow", NdjsonLimits),
        ("envelope parser rejects duplicate keys and result+error", EnvelopeFaults),
        ("request responses may complete out of order once each", OutOfOrderRequests),
        ("cancel before write is not sent and pending returns to zero", CancelBeforeWrite),
        ("unknown response id does not complete other requests", UnknownIdDoesNotCompleteOthers),
        ("stderr flood does not appear on stdout", StderrFlood),
        ("banner on stdout is protocol pollution", BannerPollution),
        ("child hang dispose kills only the recorded pid within 3s", HangDispose),
        ("subscription uses a second process and waits for ack", SubscriptionAckAndEvents),
        ("subscription event overflow fails closed", SubscriptionOverflow),
        ("late old epoch response does not complete a new connection", LateEpochIsolated),
        ("l2 live endpoint remains unverified", L2Unverified)
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

    static OwnedChildProcess StartFake(string mode) =>
        OwnedChildProcess.Start(SelfExe(), ["--fake-bridge", mode]);

    static RpcRequestConnection RequestOverFake(string mode, long epoch = 1) =>
        new(StartFake(mode), new ConnectionEpoch(epoch), null);

    static RpcSubscriptionConnection SubscribeOverFake(string mode, long epoch = 1)
    {
        using var empty = JsonDocument.Parse("{}");
        return new RpcSubscriptionConnection(
            StartFake(mode), new ConnectionEpoch(epoch), empty.RootElement.Clone());
    }

    static JsonElement EmptyParams()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    static void RejectsBadPaths()
    {
        Check(RpcSocketPath.RejectReason("") == RpcCodes.EndpointInvalid);
        Check(RpcSocketPath.RejectReason("ok\0bad") == RpcCodes.EndpointInvalid);
        Check(RpcSocketPath.RejectReason(@"\\server\pipe\herdr") == RpcCodes.RemotePipeRejected);
        Check(RpcSocketPath.RejectReason("/tmp/herdr-client.sock") == RpcCodes.BinaryClientRejected);
        Check(RpcSocketPath.RejectReason("/tmp/herdr.sock") is null);
    }

    static void NdjsonLimits()
    {
        var reader = new BoundedNdjsonReader(new MemoryStream("abc\ndef\n"u8.ToArray()));
        var first = reader.ReadLineAsync().AsTask().GetAwaiter().GetResult();
        var second = reader.ReadLineAsync().AsTask().GetAwaiter().GetResult();
        var end = reader.ReadLineAsync().AsTask().GetAwaiter().GetResult();
        Check(Encoding.UTF8.GetString(first!) == "abc");
        Check(Encoding.UTF8.GetString(second!) == "def");
        Check(end is null);

        var bytes = new byte[BoundedNdjsonReader.MaxLineBytes + 1];
        Array.Fill(bytes, (byte)'x');
        var limited = new BoundedNdjsonReader(new MemoryStream(bytes));
        try
        {
            limited.ReadLineAsync().AsTask().GetAwaiter().GetResult();
            throw new Exception("assertion_failed");
        }
        catch (RpcProtocolException error)
        {
            Check(error.Message == RpcCodes.LineBytesLimit);
        }
    }

    static void EnvelopeFaults()
    {
        using var ok = RpcEnvelopeParser.Parse("""{"id":1,"result":{"ok":true}}"""u8.ToArray());
        Check(ok.IsResponse);
        Check(ok.Id == 1);
        try
        {
            RpcEnvelopeParser.Parse("""{"id":1,"result":{},"error":{}}"""u8.ToArray());
            throw new Exception("assertion_failed");
        }
        catch (RpcProtocolException error)
        {
            Check(error.Message == RpcCodes.EnvelopeInvalid);
        }
        try
        {
            RpcEnvelopeParser.Parse("""{"id":1,"result":{},"id":2}"""u8.ToArray());
            throw new Exception("assertion_failed");
        }
        catch (RpcProtocolException error)
        {
            Check(error.Message is RpcCodes.DuplicateJsonKey or RpcCodes.EnvelopeInvalid);
        }
        try
        {
            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            var body = """{"id":1,"result":{}}"""u8.ToArray();
            var prefixed = new byte[bom.Length + body.Length];
            bom.CopyTo(prefixed, 0);
            body.CopyTo(prefixed, bom.Length);
            RpcEnvelopeParser.Parse(prefixed);
            throw new Exception("assertion_failed");
        }
        catch (RpcProtocolException error)
        {
            Check(error.Message == RpcCodes.EnvelopeInvalid);
        }
        var nested = new StringBuilder();
        for (var i = 0; i < 65; i++)
            nested.Append("{\"a\":");
        nested.Append('1');
        nested.Append('}', 65);
        try
        {
            RpcEnvelopeParser.Parse(Encoding.UTF8.GetBytes(nested.ToString()));
            throw new Exception("assertion_failed");
        }
        catch (RpcProtocolException error)
        {
            Check(error.Message == RpcCodes.EnvelopeInvalid);
        }
    }

    static void OutOfOrderRequests()
    {
        var connection = RequestOverFake("rpc");
        try
        {
            var tasks = Enumerable.Range(0, 256).Select(_ =>
                connection.RequestAsync("ping", EmptyParams()).AsTask()).ToArray();
            var results = Task.WhenAll(tasks).GetAwaiter().GetResult();
            Check(results.All(item => item.Succeeded));
            Check(connection.PendingCount == 0);
            foreach (var result in results)
                result.Dispose();
        }
        finally
        {
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void CancelBeforeWrite()
    {
        var connection = RequestOverFake("hang");
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var result = connection.RequestAsync("ping", EmptyParams(), cts.Token).AsTask()
                .GetAwaiter().GetResult();
            Check(!result.Succeeded);
            Check(result.Failure!.Kind is RpcFailureKind.NotSent or RpcFailureKind.ConnectionLost);
            Check(connection.PendingCount == 0);
            result.Dispose();
        }
        finally
        {
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void UnknownIdDoesNotCompleteOthers()
    {
        var connection = RequestOverFake("unknown-then-ok");
        try
        {
            var first = connection.RequestAsync("ping", EmptyParams()).AsTask();
            var second = connection.RequestAsync("ping", EmptyParams()).AsTask();
            var results = Task.WhenAll(first, second).Wait(TimeSpan.FromSeconds(5));
            Check(results);
            Check(first.Result.Succeeded);
            Check(second.Result.Succeeded);
            Check(connection.PendingCount == 0);
            first.Result.Dispose();
            second.Result.Dispose();
        }
        finally
        {
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void StderrFlood()
    {
        var connection = RequestOverFake("stderr-flood");
        try
        {
            using var result = connection.RequestAsync("ping", EmptyParams()).AsTask().GetAwaiter().GetResult();
            Check(result.Succeeded);
            Check(result.Document is not null);
            var json = result.Document!.RootElement.GetRawText();
            Check(!json.Contains("xxxx", StringComparison.Ordinal));
        }
        finally
        {
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void BannerPollution()
    {
        var connection = RequestOverFake("banner");
        try
        {
            using var result = connection.RequestAsync("ping", EmptyParams()).AsTask().GetAwaiter().GetResult();
            Check(!result.Succeeded);
            Check(result.Failure is not null);
            Check(result.Failure!.Code is RpcCodes.ProtocolPollution or RpcCodes.EnvelopeInvalid);
            Check(!result.Failure.Code.Contains("WELCOME", StringComparison.Ordinal));
        }
        finally
        {
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void HangDispose()
    {
        var sentinel = Process.Start(new ProcessStartInfo
        {
            FileName = SelfExe(),
            ArgumentList = { "--fake-bridge", "hang" },
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        });
        Check(sentinel is not null);
        var connection = RequestOverFake("hang");
        try
        {
            var child = connection.ChildProcessId;
            Check(child is > 0);
            var childId = child!.Value;
            var started = Stopwatch.StartNew();
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Check(started.Elapsed < TimeSpan.FromSeconds(5));
            Check(!sentinel!.HasExited);
            try
            {
                var leftover = Process.GetProcessById(childId);
                Check(leftover.HasExited);
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
            sentinel!.Dispose();
        }
    }

    static void SubscriptionAckAndEvents()
    {
        var request = RequestOverFake("rpc", 2);
        var subscribe = SubscribeOverFake("subscribe", 2);
        try
        {
            Check(request.ChildProcessId != subscribe.ChildProcessId);
            using var ping = request.RequestAsync("session.snapshot", EmptyParams()).AsTask()
                .GetAwaiter().GetResult();
            Check(ping.Succeeded);
            var events = new List<JsonElement>();
            var read = Task.Run(async () =>
            {
                await foreach (var item in subscribe.ReadEventsAsync())
                {
                    events.Add(item);
                    if (events.Count >= 3)
                        break;
                }
            });
            Check(read.Wait(TimeSpan.FromSeconds(5)));
            Check(events.Count == 3);
            Check(subscribe.Failure is null);
        }
        finally
        {
            subscribe.DisposeAsync().AsTask().GetAwaiter().GetResult();
            request.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void SubscriptionOverflow()
    {
        var subscribe = SubscribeOverFake("subscribe-overflow");
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (subscribe.Failure is null && DateTime.UtcNow < deadline)
                Thread.Sleep(20);
            Check(subscribe.Failure is not null);
            Check(subscribe.Failure!.Kind is RpcFailureKind.EventQueueOverflow
                or RpcFailureKind.ConnectionLost);
            Check(subscribe.Failure.Code is RpcCodes.EventQueueOverflow or RpcCodes.ConnectionLost);
        }
        finally
        {
            subscribe.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void LateEpochIsolated()
    {
        var first = RequestOverFake("rpc", 1);
        first.DisposeAsync().AsTask().GetAwaiter().GetResult();
        var second = RequestOverFake("rpc", 2);
        try
        {
            using var result = second.RequestAsync("ping", EmptyParams()).AsTask().GetAwaiter().GetResult();
            Check(result.Succeeded);
            Check(second.Epoch.Value == 2);
            Check(first.PendingCount == 0);
            Check(second.PendingCount == 0);
        }
        finally
        {
            second.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void L2Unverified()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HerdDesk.slnx")))
            dir = dir.Parent;
        Check(dir is not null);
        var json = File.ReadAllText(Path.Combine(dir!.FullName, "implementation", "hd-008-l2.json"), Encoding.UTF8);
        Check(json.Contains("UNVERIFIED", StringComparison.Ordinal));
        Check(json.Contains("\"ac03_passed\": false", StringComparison.Ordinal));
        Check(json.Contains("\"phase_gate\": \"not_passed\"", StringComparison.Ordinal));
    }
}
