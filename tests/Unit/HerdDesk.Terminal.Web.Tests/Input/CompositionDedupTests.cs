using System.Text;
using HerdDesk.Contracts;
using HerdDesk.Terminal.Web;

internal static class CompositionDedupTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("preedit produces zero transport bytes", PreeditZeroBytes),
        ("compositionend and input commit once", CommitOnce),
        ("ctrl+k enter escape yield during composition", ShortcutsYield),
        ("commit encodes chinese english emoji combining once", MixedText),
        ("fabricated token does not commit or cancel composition", FabricatedToken),
        ("old token during new composition stays local", OldTokenDoesNotAbort)
    ];

    static void PreeditZeroBytes()
    {
        var controller = WebTestHost.Input();
        var started = controller.StartComposition();
        WebTestHost.Check(!started.Allowed);
        WebTestHost.Check(started.Code == "preedit_not_sent");
        WebTestHost.Check(controller.IsComposing);
        var update = controller.UpdatePreedit();
        WebTestHost.Check(!update.Allowed);
        WebTestHost.Check(update.Code == "preedit_not_sent");
        WebTestHost.Check(controller.HasPreedit);
        WebTestHost.Check(controller.TransportByteCount == 0);
        WebTestHost.Check(controller.Sent.Count == 0);
    }

    static void CommitOnce()
    {
        var controller = WebTestHost.Input();
        controller.StartComposition();
        controller.UpdatePreedit();
        var token = controller.ActiveCommitToken!;
        var first = controller.Commit(token, "你好");
        WebTestHost.Check(first.Allowed);
        WebTestHost.Check(first.Origin == InputOrigin.CommittedText);
        WebTestHost.Check(first.TransportBytes.SequenceEqual(Encoding.UTF8.GetBytes("你好")));
        var second = controller.Commit(token, "你好");
        WebTestHost.Check(!second.Allowed);
        WebTestHost.Check(second.Code == "commit_already_accepted");
        WebTestHost.Check(second.TransportBytes.Length == 0);
        WebTestHost.Check(controller.Sent.Count == 1);
        WebTestHost.Check(controller.TransportByteCount == Encoding.UTF8.GetByteCount("你好"));
        WebTestHost.Check(!controller.IsComposing);
        WebTestHost.Check(!controller.HasPreedit);
    }

    static void ShortcutsYield()
    {
        var controller = WebTestHost.Input();
        controller.StartComposition();
        foreach (var key in new PhysicalKeyEvent[]
                 {
                     new("k", Ctrl: true),
                     new("Enter"),
                     new("Escape")
                 })
        {
            var result = controller.HandleKey(key);
            WebTestHost.Check(!result.Allowed);
            WebTestHost.Check(result.Code == "ime_owns_shortcut");
            WebTestHost.Check(result.AcceleratorYielded);
            WebTestHost.Check(result.TransportBytes.Length == 0);
        }

        WebTestHost.Check(controller.TransportByteCount == 0);
        var token = controller.ActiveCommitToken!;
        WebTestHost.Check(controller.Commit(token, "词").Allowed);
        var enter = controller.HandleKey(new PhysicalKeyEvent("Enter"));
        WebTestHost.Check(enter.Allowed);
        WebTestHost.Check(enter.TransportBytes.SequenceEqual(new byte[] { 0x0d }));
    }

    static void MixedText()
    {
        var controller = WebTestHost.Input();
        controller.StartComposition();
        var token = controller.ActiveCommitToken!;
        var text = "中文hi😀e\u0301";
        var result = controller.Commit(token, text);
        WebTestHost.Check(result.Allowed);
        var decoded = Encoding.UTF8.GetString(result.TransportBytes);
        WebTestHost.Check(decoded == text);
        WebTestHost.Check(!decoded.Contains('\uFFFD'));
        WebTestHost.Check(controller.Sent.Count == 1);
    }

    static void FabricatedToken()
    {
        var controller = WebTestHost.Input();
        controller.StartComposition();
        var token = controller.ActiveCommitToken!;
        var fake = controller.Commit("not-the-token", "假");
        WebTestHost.Check(!fake.Allowed);
        WebTestHost.Check(fake.Code == "input_origin_denied");
        WebTestHost.Check(controller.IsComposing);
        WebTestHost.Check(controller.TransportByteCount == 0);
        WebTestHost.Check(controller.Sent.Count == 0);
        var real = controller.Commit(token, "词");
        WebTestHost.Check(real.Allowed);
        WebTestHost.Check(controller.Sent.Count == 1);
        WebTestHost.Check(controller.TransportByteCount == Encoding.UTF8.GetByteCount("词"));
    }

    static void OldTokenDoesNotAbort()
    {
        var controller = WebTestHost.Input();
        controller.StartComposition();
        var first = controller.ActiveCommitToken!;
        controller.StartComposition();
        var second = controller.ActiveCommitToken!;
        WebTestHost.Check(first != second);
        var old = controller.Commit(first, "旧");
        WebTestHost.Check(!old.Allowed);
        WebTestHost.Check(old.Code == "commit_already_accepted");
        WebTestHost.Check(controller.IsComposing);
        WebTestHost.Check(controller.TransportByteCount == 0);
        var ok = controller.Commit(second, "新");
        WebTestHost.Check(ok.Allowed);
        WebTestHost.Check(controller.Sent.Count == 1);
        WebTestHost.Check(!controller.IsComposing);
    }
}
