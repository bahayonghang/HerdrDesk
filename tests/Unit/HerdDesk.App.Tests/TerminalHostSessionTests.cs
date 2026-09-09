using System.Text;
using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Configuration;
using HerdDesk.Terminal.Web;

internal static class TerminalHostSessionTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("bind then old epoch web input is rejected", StaleEpochRejected),
        ("observe input is not queued", ObserveNoInput),
        ("hidden pane does not enqueue frames", HiddenPane),
        ("codec roundtrip uses validator from start state", CodecRoundtrip),
        ("session composition never sends preedit and commits once", SessionCommitOnce),
        ("session composition yields ctrl+k to IME not search", SessionCompositionYieldsSearch),
        ("session observe denies key paste mouse and emulator reply", SessionObserveNoWrite),
        ("session selection copy is visible text and ready is not a lease", SessionCopyAndReady)
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

    static void SessionCommitOnce()
    {
        var session = ControllingSession();
        var start = session.AcceptFromWeb(WebMessageCodec.Composition(Epoch(), "start", "c1"));
        AppTestHost.Check(!start.Allowed);
        AppTestHost.Check(start.Code == "preedit_not_sent");
        AppTestHost.Check(session.IsComposing);
        AppTestHost.Check(session.TransportByteCount == 0);
        var update = session.AcceptFromWeb(WebMessageCodec.Composition(Epoch(), "update", "c1"));
        AppTestHost.Check(!update.Allowed);
        AppTestHost.Check(update.Code == "preedit_not_sent");
        AppTestHost.Check(session.TransportByteCount == 0);
        var stray = session.AcceptFromWeb(Utf8Json(
            "{\"version\":1,\"kind\":\"input\",\"epoch\":1,\"origin\":\"user_key\",\"bytes\":\"" +
            B64(0x61) + "\"}"));
        AppTestHost.Check(!stray.Allowed);
        AppTestHost.Check(stray.Code == "preedit_not_sent");
        AppTestHost.Check(session.TransportByteCount == 0);
        var first = session.AcceptFromWeb(WebMessageCodec.Composition(Epoch(), "end", "c1", "你好"));
        AppTestHost.Check(first.Allowed);
        AppTestHost.Check(session.TransportByteCount == Encoding.UTF8.GetByteCount("你好"));
        AppTestHost.Check(!session.IsComposing);
        var second = session.AcceptFromWeb(WebMessageCodec.Composition(Epoch(), "end", "c1", "你好"));
        AppTestHost.Check(!second.Allowed);
        AppTestHost.Check(second.Code == "commit_already_accepted");
        AppTestHost.Check(session.TransportByteCount == Encoding.UTF8.GetByteCount("你好"));
        var later = session.AcceptFromWeb(Utf8Json(
            "{\"version\":1,\"kind\":\"input\",\"epoch\":1,\"origin\":\"user_key\",\"bytes\":\"" +
            Convert.ToBase64String(Encoding.UTF8.GetBytes("你好")) + "\"}"));
        AppTestHost.Check(later.Allowed);
        AppTestHost.Check(session.TransportByteCount == 2 * Encoding.UTF8.GetByteCount("你好"));
        AppTestHost.Check(session.ControlVerified);
        session.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    static void SessionCompositionYieldsSearch()
    {
        var session = ControllingSession();
        session.AcceptFromWeb(WebMessageCodec.Composition(Epoch(), "start", "c2"));
        AppTestHost.Check(session.IsComposing);
        var key = session.AcceptFromWeb(WebMessageCodec.Key(Epoch(), "k", ctrl: true));
        AppTestHost.Check(!key.Allowed);
        AppTestHost.Check(key.Code == "ime_owns_shortcut");
        AppTestHost.Check(session.TransportByteCount == 0);
        var enter = session.AcceptFromWeb(WebMessageCodec.Key(Epoch(), "Enter"));
        AppTestHost.Check(!enter.Allowed);
        AppTestHost.Check(enter.Code == "ime_owns_shortcut");
        var escape = session.AcceptFromWeb(WebMessageCodec.Key(Epoch(), "Escape"));
        AppTestHost.Check(!escape.Allowed);
        AppTestHost.Check(escape.Code == "ime_owns_shortcut");
        var shell = AppTestHost.Shell(
            new MemoryDeviceProfileStore
            {
                Snapshot = new ConfigurationSnapshot(
                    1, 1,
                    [
                        new DeviceProfile(
                            AppTestHost.DeviceA, "lab", ConnectionKinds.Local, "/tmp/herdr",
                            [SessionProfile.Named("dev")])
                    ])
            },
            input: new TerminalInputViewModel(session.Input!));
        shell.StartAsync().AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(!shell.HandleAccelerator(ShellAccelerator.OpenSearch));
        AppTestHost.Check(!shell.Search.IsOpen);
        session.AcceptFromWeb(WebMessageCodec.Composition(Epoch(), "end", "c2", "词"));
        AppTestHost.Check(!session.IsComposing);
        AppTestHost.Check(shell.HandleAccelerator(ShellAccelerator.OpenSearch));
        AppTestHost.Check(shell.Search.IsOpen);
        session.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    static void SessionObserveNoWrite()
    {
        var session = new TerminalHostSession();
        session.Bind(Pane(), Epoch(), true);
        AppTestHost.Check(!session.ControlVerified);
        var ready = session.AcceptFromWeb(Utf8Json("""{"version":1,"kind":"ready","epoch":1}"""));
        AppTestHost.Check(ready.Allowed);
        AppTestHost.Check(!session.ControlVerified);
        AppTestHost.Check(session.Input is { ControlVerified: false });
        var key = session.AcceptFromWeb(WebMessageCodec.Key(Epoch(), "a"));
        AppTestHost.Check(!key.Allowed);
        AppTestHost.Check(key.Code == "control_not_verified");
        var paste = session.AcceptFromWeb(WebMessageCodec.PasteIntent(Epoch(), "ab"));
        AppTestHost.Check(!paste.Allowed);
        AppTestHost.Check(paste.Code == "control_not_verified");
        var mouse = session.AcceptFromWeb(WebMessageCodec.MouseIntent(Epoch(), "scroll", 3));
        AppTestHost.Check(!mouse.Allowed);
        AppTestHost.Check(mouse.Code == "observe_scroll_denied");
        var reply = session.AcceptFromWeb(Utf8Json(
            "{\"version\":1,\"kind\":\"input\",\"epoch\":1,\"origin\":\"emulator_reply\",\"bytes\":\"" +
            B64(0x61) + "\"}"));
        AppTestHost.Check(!reply.Allowed);
        AppTestHost.Check(reply.Code == "control_not_verified");
        var user = session.AcceptFromWeb(Utf8Json(
            "{\"version\":1,\"kind\":\"input\",\"epoch\":1,\"origin\":\"user_key\",\"bytes\":\"" +
            B64(0x61) + "\"}"));
        AppTestHost.Check(!user.Allowed);
        AppTestHost.Check(user.Code == "control_not_verified");
        AppTestHost.Check(session.TransportByteCount == 0);
        AppTestHost.Check(session.OutboundCount >= 1);
        session.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    static void SessionCopyAndReady()
    {
        var session = new TerminalHostSession();
        session.Bind(Pane(), Epoch(), true);
        session.AcceptFromWeb(Utf8Json("""{"version":1,"kind":"ready","epoch":1}"""));
        AppTestHost.Check(!session.ControlVerified);
        var select = session.AcceptFromWeb(
            WebMessageCodec.SelectionChanged(Epoch(), "\u001b[31mhello\u001b[0m"));
        AppTestHost.Check(select.Allowed);
        AppTestHost.Check(session.Input!.Selection.VisibleText == "hello");
        var copy = session.Input.CopySelection();
        AppTestHost.Check(copy.LocalCopy);
        AppTestHost.Check(copy.TransportBytes.Length == 0);
        AppTestHost.Check(session.TransportByteCount == 0);
        var osc = OscClipboardPolicy.Evaluate("\u001b]52;c;?\u0007"u8);
        AppTestHost.Check(!osc.Allowed);
        AppTestHost.Check(osc.Code == ClipboardCodes.ClipboardReadDenied);
        session.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    static TerminalHostSession ControllingSession()
    {
        var session = new TerminalHostSession();
        session.Bind(Pane(), Epoch(), false);
        session.AcceptFromWeb(Utf8Json("""{"version":1,"kind":"ready","epoch":1}"""));
        AppTestHost.Check(session.Input is not null);
        return session;
    }
}
