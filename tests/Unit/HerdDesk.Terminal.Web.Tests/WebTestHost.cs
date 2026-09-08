using System.Diagnostics;
using System.Text;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Terminal.Web;

internal static class WebTestHost
{
    public static PaneKey Pane() => new(
        new SessionKey(new DeviceId(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")), "endpoint", "dev"),
        "ws", "p1");

    public static ConnectionEpoch Epoch(long value = 1) => new(value);

    public static InputContext Observe() =>
        new(Pane(), Epoch(), TerminalAccess.Observing, false);

    public static InputContext Control() =>
        new(Pane(), Epoch(), TerminalAccess.Controlling, true);

    public static TerminalFrame Frame(ulong seq, bool full, byte[] bytes) =>
        new(seq, 80, 24, full, bytes);

    public static byte[] Utf8Json(string json) => Encoding.UTF8.GetBytes(json);

    public static string B64(params byte[] bytes) => Convert.ToBase64String(bytes);

    public static RenderFlowController Flow(int maxBytes = 64, int maxFrames = 4) =>
        new(Epoch(), maxBytes, maxFrames);

    public static WebTerminalRenderer Renderer(
        IRenderFlowController? flow = null,
        InputContext? context = null,
        IWebParseProbe? parse = null)
    {
        var renderer = new WebTerminalRenderer(
            flow ?? Flow(),
            context ?? Observe(),
            WebMessagePolicy.Evaluate,
            parse);
        renderer.BindAsync(Pane(), Epoch()).AsTask().GetAwaiter().GetResult();
        return renderer;
    }

    public static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    public static void WaitUntil(Func<bool> predicate, int milliseconds = 2000)
    {
        var clock = Stopwatch.StartNew();
        while (!predicate())
        {
            if (clock.ElapsedMilliseconds > milliseconds)
                throw new Exception("assertion_failed");
            Thread.Sleep(10);
        }
    }
}
