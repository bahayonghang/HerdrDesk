using System.Text;
using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Terminal.Web;

internal static class TerminalHostSessionTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("bind then old epoch web input is rejected", StaleEpochRejected),
        ("observe input is not queued", ObserveNoInput),
        ("hidden pane does not enqueue frames", HiddenPane),
        ("codec roundtrip uses validator from start state", CodecRoundtrip)
    ];

    static PaneKey Pane() => new(AppTestHost.SessionOf(AppTestHost.DeviceA), "ws", "p1");

    static ConnectionEpoch Epoch() => new(1);

    static byte[] Utf8Json(string json) => Encoding.UTF8.GetBytes(json);

    static string B64(params byte[] bytes) => Convert.ToBase64String(bytes);

    static void StaleEpochRejected()
    {
        var session = new TerminalHostSession();
        session.Bind(Pane(), Epoch(), true);
        AppTestHost.Check(session.SurfaceState == RendererSurfaceState.Loading);
        var stale = session.AcceptFromWeb(Utf8Json(
            """{"version":1,"kind":"ready","epoch":2}"""));
        AppTestHost.Check(!stale.Allowed);
        AppTestHost.Check(stale.Code == "stale_epoch");
        AppTestHost.Check(session.SurfaceState == RendererSurfaceState.Faulted);
        session.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    static void ObserveNoInput()
    {
        var session = new TerminalHostSession();
        session.Bind(Pane(), Epoch(), true);
        var before = session.OutboundCount;
        var key = session.AcceptFromWeb(Utf8Json(
            "{\"version\":1,\"kind\":\"input\",\"epoch\":1,\"origin\":\"user_key\",\"bytes\":\"" +
            B64(0x61) + "\"}"));
        AppTestHost.Check(!key.Allowed);
        AppTestHost.Check(key.Code == "control_not_verified");
        var reply = session.AcceptFromWeb(Utf8Json(
            "{\"version\":1,\"kind\":\"input\",\"epoch\":1,\"origin\":\"emulator_reply\",\"bytes\":\"" +
            B64(0x61) + "\"}"));
        AppTestHost.Check(!reply.Allowed);
        AppTestHost.Check(reply.Code == "control_not_verified");
        AppTestHost.Check(session.OutboundCount == before);
        session.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    static void HiddenPane()
    {
        var session = new TerminalHostSession();
        session.Bind(Pane(), Epoch(), true);
        session.SetVisible(false);
        var result = session.ApplyFrame(new TerminalFrame(1, 80, 24, true, new byte[] { 0x61 }));
        AppTestHost.Check(!result.Accepted);
        AppTestHost.Check(result.Code == "pane_hidden");
        session.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    static void CodecRoundtrip()
    {
        var epoch = Epoch();
        var encoded = WebMessageCodec.Frame(epoch, 1, true, [0x61]);
        var parsed = WebMessageValidator.Evaluate(encoded, epoch);
        AppTestHost.Check(parsed.Accepted);
        AppTestHost.Check(parsed.Kind == WebMessageKinds.Frame);
        AppTestHost.Check(parsed.Bytes.Span[0] == 0x61);
        var init = WebMessageValidator.Evaluate(WebMessageCodec.Initialize(epoch, "dark", true), epoch);
        AppTestHost.Check(init.Accepted);
    }
}
