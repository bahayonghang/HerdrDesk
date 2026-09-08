using System.Text;
using HerdDesk.Contracts;
using HerdDesk.Terminal.Web;

internal static class SelectionMousePolicyTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("copy strips ansi and stays local", CopyVisibleText),
        ("observe scroll does not change viewport", ObserveScroll),
        ("observe font changes do not resize", ObserveNoResize),
        ("paste is explicit and does not append enter", PasteNoEnter),
        ("request control does not grant a lease", NoLeaseGrant)
    ];

    static void CopyVisibleText()
    {
        var controller = WebTestHost.Input(WebTestHost.Observe(), readOnly: true);
        var raw = "\u001b[31mhello\u001b[0m";
        var select = controller.HandleSelection(raw);
        WebTestHost.Check(select.LocalSelection);
        WebTestHost.Check(controller.Selection.VisibleText == "hello");
        var copy = controller.CopySelection();
        WebTestHost.Check(copy.LocalCopy);
        WebTestHost.Check(copy.TransportBytes.Length == 0);
        var shifted = controller.HandleSelection("x", shift: true);
        WebTestHost.Check(controller.ApplicationMouseMode);
        WebTestHost.Check(shifted.LocalSelection is false);
        WebTestHost.Check(controller.TransportByteCount == 0);
    }

    static void ObserveScroll()
    {
        var controller = WebTestHost.Input(WebTestHost.Observe(), readOnly: true);
        var scroll = controller.HandleScroll(3, mouseReporting: true);
        WebTestHost.Check(!scroll.Allowed);
        WebTestHost.Check(scroll.Code == "observe_scroll_denied");
        WebTestHost.Check(controller.LocalScrollCount == 1);
        WebTestHost.Check(controller.UpstreamScrollCount == 0);
        WebTestHost.Check(controller.ReadOnlyScrollNotice == "只读，滚动由当前控制者决定");
        var controlling = WebTestHost.Input();
        var allowed = controlling.HandleScroll(1, mouseReporting: true);
        WebTestHost.Check(allowed.Allowed);
        WebTestHost.Check(controlling.UpstreamScrollCount == 1);
        WebTestHost.Check(controlling.Sent.Count == 0);
    }

    static void ObserveNoResize()
    {
        var controller = WebTestHost.Input(WebTestHost.Observe(), readOnly: true);
        for (var i = 0; i < 100; i++)
            controller.ApplyLocalDisplay("Cascadia Mono", 12, 100 + i % 3);
        WebTestHost.Check(controller.LocalDisplayCount == 100);
        WebTestHost.Check(!controller.TryRequestResize(80, 24));
        WebTestHost.Check(controller.UpstreamResizeCount == 0);
        var controlling = WebTestHost.Input();
        WebTestHost.Check(controlling.TryRequestResize(120, 40));
        WebTestHost.Check(controlling.UpstreamResizeCount == 1);
        WebTestHost.Check(controlling.TryRequestResize(120, 40));
        WebTestHost.Check(controlling.UpstreamResizeCount == 1);
        controlling.SetInputContext(WebTestHost.Observe());
        controlling.SetReadOnly(true);
        WebTestHost.Check(!controlling.TryRequestResize(80, 24));
        WebTestHost.Check(controlling.UpstreamResizeCount == 1);
        var composing = WebTestHost.Input(readOnly: true);
        composing.StartComposition();
        var token = composing.ActiveCommitToken!;
        var commit = composing.Commit(token, "词");
        WebTestHost.Check(!commit.Allowed);
        WebTestHost.Check(commit.Code == "control_not_verified");
        WebTestHost.Check(composing.TransportByteCount == 0);
    }

    static void PasteNoEnter()
    {
        var controller = WebTestHost.Input();
        var text = "hello\nworld";
        var paste = controller.HandlePaste(text);
        WebTestHost.Check(paste.Allowed);
        WebTestHost.Check(paste.Origin == InputOrigin.ExplicitPaste);
        WebTestHost.Check(paste.TransportBytes.SequenceEqual(Encoding.UTF8.GetBytes(text)));
        WebTestHost.Check(!paste.TransportBytes.SequenceEqual(Encoding.UTF8.GetBytes(text + "\r")));
        WebTestHost.Check(controller.Sent.Count == 1);
        var observe = WebTestHost.Input(WebTestHost.Observe(), readOnly: true);
        var denied = observe.HandlePaste(text);
        WebTestHost.Check(!denied.Allowed);
        WebTestHost.Check(observe.TransportByteCount == 0);
    }

    static void NoLeaseGrant()
    {
        var controller = WebTestHost.Input(WebTestHost.Observe(), readOnly: true);
        var request = controller.RequestControl();
        WebTestHost.Check(!request.Allowed);
        WebTestHost.Check(request.Code == "lease_not_granted");
        WebTestHost.Check(!controller.ControlVerified);
        WebTestHost.Check(controller.Context.Access == TerminalAccess.Observing);
        WebTestHost.Check(controller.ControlRequestCount == 1);
        var release = controller.ReleaseControl();
        WebTestHost.Check(!release.Allowed);
        WebTestHost.Check(controller.Context.Access == TerminalAccess.Observing);
        WebTestHost.Check(controller.ControlVerified is false);
    }
}
