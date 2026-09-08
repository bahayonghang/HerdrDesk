using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Terminal.Web;

internal static class FocusRaceTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("focus does not set control verified", FocusNotLease),
        ("renderer not ready stays requested", NotReady),
        ("hundred pane switches reject stale input", HundredSwitches),
        ("reset clears preedit and does not replay", ResetNoReplay)
    ];

    static void FocusNotLease()
    {
        var controller = WebTestHost.Input(WebTestHost.Observe(), readOnly: true, ready: true);
        WebTestHost.Check(!controller.ControlVerified);
        var focus = controller.RequestFocus();
        WebTestHost.Check(focus.Allowed);
        WebTestHost.Check(controller.HasFocus);
        WebTestHost.Check(!controller.ControlVerified);
        WebTestHost.Check(controller.Context.Access == TerminalAccess.Observing);
        controller.Blur();
        WebTestHost.Check(!controller.HasFocus);
        WebTestHost.Check(!controller.ControlVerified);
        WebTestHost.Check(controller.Context.Access == TerminalAccess.Observing);
        WebTestHost.Check(controller.ReleaseRequestCount == 0);
    }

    static void NotReady()
    {
        var controller = WebTestHost.Input(ready: false);
        var requested = controller.RequestFocus();
        WebTestHost.Check(!requested.Allowed);
        WebTestHost.Check(requested.Code == "renderer_not_ready");
        WebTestHost.Check(!controller.HasFocus);
        WebTestHost.Check(controller.FocusState == HostFocusState.FocusRequested);
        controller.SetRendererReady(true);
        WebTestHost.Check(controller.HasFocus);
        WebTestHost.Check(controller.FocusState == HostFocusState.Focused);
        WebTestHost.Check(controller.ControlVerified);
    }

    static void HundredSwitches()
    {
        var first = WebTestHost.PaneId("p000");
        var controller = new TerminalInputController(
            new InputContext(first, WebTestHost.Epoch(), TerminalAccess.Controlling, true),
            CompositionPolicy.Evaluate, InputPolicy.Evaluate);
        controller.SetReadOnly(false);
        controller.SetRendererReady(false);
        controller.Bind(first, WebTestHost.Epoch());
        controller.RequestFocus();
        for (var i = 0; i < 100; i++)
        {
            var pane = WebTestHost.PaneId("p" + i.ToString("D3"));
            var epoch = WebTestHost.Epoch(i == 99 ? 2 : 1);
            controller.SetInputContext(new InputContext(pane, epoch, TerminalAccess.Controlling, true));
            controller.SwitchPane(pane, epoch);
            controller.SetRendererReady(false);
            controller.RequestFocus();
        }

        controller.SetRendererReady(true);
        WebTestHost.Check(controller.HasFocus);
        WebTestHost.Check(controller.Pane == WebTestHost.PaneId("p099"));
        WebTestHost.Check(controller.Epoch == WebTestHost.Epoch(2));
        var key = controller.HandleKey(new PhysicalKeyEvent("a"));
        WebTestHost.Check(key.Allowed);
        WebTestHost.Check(controller.Sent.Count == 1);
        WebTestHost.Check(controller.Sent[0].Pane == WebTestHost.PaneId("p099"));
        WebTestHost.Check(controller.Sent[0].Epoch == WebTestHost.Epoch(2));
        controller.SetInputContext(new InputContext(first, WebTestHost.Epoch(), TerminalAccess.Controlling, true));
        var stale = controller.HandleKey(new PhysicalKeyEvent("b"));
        WebTestHost.Check(!stale.Allowed);
        WebTestHost.Check(stale.Code is "wrong_pane" or "stale_epoch");
        WebTestHost.Check(controller.Sent.Count == 1);
    }

    static void ResetNoReplay()
    {
        var controller = WebTestHost.Input();
        controller.StartComposition();
        controller.UpdatePreedit();
        var token = controller.ActiveCommitToken!;
        controller.Reset();
        WebTestHost.Check(!controller.IsComposing);
        WebTestHost.Check(!controller.HasPreedit);
        var replay = controller.Commit(token, "半成品");
        WebTestHost.Check(!replay.Allowed);
        WebTestHost.Check(controller.TransportByteCount == 0);
        controller.Suspend("stale_epoch");
        var paused = controller.HandleKey(new PhysicalKeyEvent("a"));
        WebTestHost.Check(!paused.Allowed);
        WebTestHost.Check(paused.Code == "input_paused");
        WebTestHost.Check(controller.TransportByteCount == 0);
    }
}
