using System.Text;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Terminal.Web;

internal static class WebTerminalRendererTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("utf8 chunks assemble without replacement or duplication", Utf8Chunks),
        ("control bytes are not rewritten as text", ControlBytes),
        ("observe input does not enqueue", ObserveNoInput),
        ("observe display and web resize send no upstream resize", ObserveNoResize),
        ("control resize increments only after verified lease", ControlResize),
        ("wrong epoch web input faults the surface", EpochReject),
        ("web fault uses a stable host code", WebFaultCode),
        ("forged parsed does not release in-flight bytes", ForgedParsed),
        ("cancel invalidates the in-flight apply", CancelApply)
    ];

    static void Utf8Chunks()
    {
        var probe = new RecordingParseProbe();
        var renderer = WebTestHost.Renderer(parse: probe);
        WebTestHost.Check(renderer.ApplyAsync(WebTestHost.Frame(1, true, Convert.FromHexString("e4")))
            .AsTask().GetAwaiter().GetResult().Accepted);
        var assembler = new Utf8ChunkAssembler();
        assembler.Append(probe.Chunks[0]);
        WebTestHost.Check(assembler.HeldIncomplete);
        WebTestHost.Check(assembler.Text == "");
        WebTestHost.Check(renderer.ApplyAsync(WebTestHost.Frame(2, false, Convert.FromHexString("bda0e5a5bd")))
            .AsTask().GetAwaiter().GetResult().Accepted);
        assembler.Append(probe.Chunks[1]);
        WebTestHost.Check(assembler.Text == "你好");
        WebTestHost.Check(!assembler.Text.Contains('\uFFFD'));
        WebTestHost.Check(assembler.Text != "你好你好");
        WebTestHost.Check(!assembler.HeldIncomplete);
        renderer.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    static void ControlBytes()
    {
        var probe = new RecordingParseProbe();
        var renderer = WebTestHost.Renderer(parse: probe);
        WebTestHost.Check(renderer.ApplyAsync(WebTestHost.Frame(1, true, [0x03, 0x1b]))
            .AsTask().GetAwaiter().GetResult().Accepted);
        WebTestHost.Check(probe.WrittenBytes.SequenceEqual(new byte[] { 0x03, 0x1b }));
        WebTestHost.Check(!Encoding.UTF8.GetString(probe.WrittenBytes).Contains("^C"));
        renderer.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    static void ObserveNoInput()
    {
        var renderer = WebTestHost.Renderer();
        var key = renderer.AcceptWebMessage(WebTestHost.Utf8Json(
            "{\"version\":1,\"kind\":\"input\",\"epoch\":1,\"origin\":\"user_key\",\"bytes\":\"" +
            WebTestHost.B64(0x61) + "\"}"));
        WebTestHost.Check(!key.Allowed);
        WebTestHost.Check(key.Code == "control_not_verified");
        var reply = renderer.AcceptWebMessage(WebTestHost.Utf8Json(
            "{\"version\":1,\"kind\":\"input\",\"epoch\":1,\"origin\":\"emulator_reply\",\"bytes\":\"" +
            WebTestHost.B64(0x61) + "\"}"));
        WebTestHost.Check(!reply.Allowed);
        WebTestHost.Check(reply.Code == "control_not_verified");
        renderer.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    static void ObserveNoResize()
    {
        var renderer = WebTestHost.Renderer();
        for (var i = 0; i < 100; i++)
            renderer.ApplyLocalDisplay("Cascadia Mono", 12, 100 + i % 3);
        WebTestHost.Check(renderer.LocalDisplayApplyCount == 100);
        WebTestHost.Check(renderer.UpstreamResizeCount == 0);
        WebTestHost.Check(!renderer.TryRequestUpstreamResize(80, 24, WebTestHost.Observe()));
        WebTestHost.Check(renderer.UpstreamResizeCount == 0);
        var resize = renderer.AcceptWebMessage(WebTestHost.Utf8Json(
            """{"version":1,"kind":"resize","epoch":1,"cols":80,"rows":24,"cellPx":[8,16]}"""));
        WebTestHost.Check(!resize.Allowed);
        WebTestHost.Check(resize.Code == "observe_no_resize");
        WebTestHost.Check(renderer.UpstreamResizeCount == 0);
        renderer.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    static void ControlResize()
    {
        var renderer = WebTestHost.Renderer(context: WebTestHost.Control());
        renderer.SetReadOnlyAsync(false).AsTask().GetAwaiter().GetResult();
        WebTestHost.Check(renderer.TryRequestUpstreamResize(120, 40, WebTestHost.Control()));
        WebTestHost.Check(renderer.UpstreamResizeCount == 1);
        var denied = renderer.TryRequestUpstreamResize(80, 24, WebTestHost.Observe());
        WebTestHost.Check(!denied);
        WebTestHost.Check(renderer.UpstreamResizeCount == 1);
        renderer.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    static void EpochReject()
    {
        var renderer = WebTestHost.Renderer(context: WebTestHost.Control());
        renderer.SetReadOnlyAsync(false).AsTask().GetAwaiter().GetResult();
        var stale = renderer.AcceptWebMessage(WebTestHost.Utf8Json(
            "{\"version\":1,\"kind\":\"input\",\"epoch\":2,\"origin\":\"user_key\",\"bytes\":\"" +
            WebTestHost.B64(0x61) + "\"}"));
        WebTestHost.Check(!stale.Allowed);
        WebTestHost.Check(stale.Code == "stale_epoch");
        WebTestHost.Check(renderer.SurfaceState == RendererSurfaceState.Faulted);
        renderer.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    static void WebFaultCode()
    {
        var renderer = WebTestHost.Renderer();
        var fault = renderer.AcceptWebMessage(WebTestHost.Utf8Json(
            """{"version":1,"kind":"fault","epoch":1,"code":"renderer_crash"}"""));
        WebTestHost.Check(fault.Allowed);
        WebTestHost.Check(renderer.SurfaceState == RendererSurfaceState.Faulted);
        WebTestHost.Check(renderer.LastCode == "renderer_fault");
        renderer.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    static void ForgedParsed()
    {
        var probe = new ManualParseProbe();
        var flow = WebTestHost.Flow();
        var renderer = WebTestHost.Renderer(flow, parse: probe);
        var apply = renderer.ApplyAsync(WebTestHost.Frame(1, true, [0x61])).AsTask();
        WebTestHost.WaitUntil(() => probe.Pending == 1);
        var forged = renderer.AcceptWebMessage(WebTestHost.Utf8Json(
            """{"version":1,"kind":"parsed","epoch":1,"seq":1,"bytesConsumed":1}"""));
        WebTestHost.Check(forged.Allowed);
        WebTestHost.Check(renderer.InFlightBytes == 1);
        probe.CompleteNext();
        var result = apply.GetAwaiter().GetResult();
        WebTestHost.Check(result.Accepted);
        WebTestHost.Check(renderer.InFlightBytes == 0);
        renderer.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    static void CancelApply()
    {
        var probe = new ManualParseProbe();
        var renderer = WebTestHost.Renderer(parse: probe);
        using var cts = new CancellationTokenSource();
        var apply = renderer.ApplyAsync(WebTestHost.Frame(1, true, [0x61]), cts.Token).AsTask();
        WebTestHost.WaitUntil(() => probe.Pending == 1);
        cts.Cancel();
        try
        {
            apply.GetAwaiter().GetResult();
            throw new Exception("expected_cancel");
        }
        catch (OperationCanceledException)
        {
        }

        WebTestHost.Check(renderer.SurfaceState == RendererSurfaceState.Resetting);
        WebTestHost.Check(renderer.InFlightBytes == 0);
        if (probe.Pending > 0)
            probe.CompleteNext();
        renderer.RetryObserve();
        var retry = renderer.ApplyAsync(WebTestHost.Frame(1, true, [0x61])).AsTask();
        WebTestHost.WaitUntil(() => probe.Pending == 1);
        probe.CompleteNext();
        WebTestHost.Check(retry.GetAwaiter().GetResult().Accepted);
        renderer.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
