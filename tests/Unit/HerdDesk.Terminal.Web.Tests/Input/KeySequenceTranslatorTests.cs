using HerdDesk.Contracts;
using HerdDesk.Terminal.Web;

internal static class KeySequenceTranslatorTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("ctrl+c is sigint when controlling", CtrlCInterrupt),
        ("ctrl+shift+c copies local selection", CtrlShiftCCopy),
        ("observe ctrl+c and copy do not write", ObserveNoWrite),
        ("tab esc arrows pageup use terminal bytes", NavigationKeys),
        ("muse and shift+enter stay unknown", UnknownProfile)
    ];

    static void CtrlCInterrupt()
    {
        var controller = WebTestHost.Input();
        var result = controller.HandleKey(new PhysicalKeyEvent("c", Ctrl: true, LeftCtrl: true));
        WebTestHost.Check(result.Allowed);
        WebTestHost.Check(result.Origin == InputOrigin.UserKey);
        WebTestHost.Check(result.TransportBytes.SequenceEqual(new byte[] { 0x03 }));
        WebTestHost.Check(controller.Sent.Count == 1);
    }

    static void CtrlShiftCCopy()
    {
        var controller = WebTestHost.Input();
        controller.HandleSelection("visible");
        var result = controller.HandleKey(new PhysicalKeyEvent("c", Ctrl: true, Shift: true));
        WebTestHost.Check(result.Allowed);
        WebTestHost.Check(result.LocalCopy);
        WebTestHost.Check(result.TransportBytes.Length == 0);
        WebTestHost.Check(controller.TransportByteCount == 0);
    }

    static void ObserveNoWrite()
    {
        var controller = WebTestHost.Input(WebTestHost.Observe(), readOnly: true);
        var interrupt = controller.HandleKey(new PhysicalKeyEvent("c", Ctrl: true));
        WebTestHost.Check(!interrupt.Allowed);
        WebTestHost.Check(interrupt.Code == "control_not_verified");
        WebTestHost.Check(interrupt.TransportBytes.Length == 0);
        controller.HandleSelection("text");
        var copy = controller.HandleKey(new PhysicalKeyEvent("c", Ctrl: true, Shift: true));
        WebTestHost.Check(copy.LocalCopy);
        WebTestHost.Check(copy.TransportBytes.Length == 0);
        WebTestHost.Check(controller.Sent.Count == 0);
        WebTestHost.Check(controller.TransportByteCount == 0);
    }

    static void NavigationKeys()
    {
        var controller = WebTestHost.Input();
        WebTestHost.Check(controller.HandleKey(new PhysicalKeyEvent("Tab")).TransportBytes
            .SequenceEqual(new byte[] { 0x09 }));
        WebTestHost.Check(controller.HandleKey(new PhysicalKeyEvent("Escape")).TransportBytes
            .SequenceEqual(new byte[] { 0x1b }));
        WebTestHost.Check(controller.HandleKey(new PhysicalKeyEvent("ArrowUp")).TransportBytes
            .SequenceEqual(new byte[] { 0x1b, (byte)'[', (byte)'A' }));
        WebTestHost.Check(controller.HandleKey(new PhysicalKeyEvent("PageUp")).TransportBytes
            .SequenceEqual(new byte[] { 0x1b, (byte)'[', (byte)'5', (byte)'~' }));
        var altGr = controller.HandleKey(new PhysicalKeyEvent("@", AltGr: true));
        WebTestHost.Check(altGr.Allowed);
        WebTestHost.Check(altGr.TransportBytes.SequenceEqual([(byte)'@']));
    }

    static void UnknownProfile()
    {
        WebTestHost.Check(AgentInputProfiles.Resolve("muse").IsUnknown);
        WebTestHost.Check(AgentInputProfiles.Resolve("qwen").IsUnknown);
        WebTestHost.Check(!AgentInputProfiles.Resolve("claude-code").IsUnknown);
        WebTestHost.Check(!AgentInputProfiles.Resolve("codex").LiveVerified);
        WebTestHost.Check(!AgentInputProfiles.Resolve("opencode").LiveVerified);
        var unknown = WebTestHost.Input(profile: AgentInputProfiles.Unknown);
        var shiftEnter = unknown.HandleKey(new PhysicalKeyEvent("Enter", Shift: true));
        WebTestHost.Check(!shiftEnter.Allowed);
        WebTestHost.Check(shiftEnter.Code == "unsupported_key_profile");
        WebTestHost.Check(shiftEnter.TransportBytes.Length == 0);
        var claude = WebTestHost.Input(profile: AgentInputProfiles.ClaudeCode);
        var claudeShift = claude.HandleKey(new PhysicalKeyEvent("Enter", Shift: true));
        WebTestHost.Check(!claudeShift.Allowed);
        WebTestHost.Check(claudeShift.Code == "unsupported_key_profile");
    }
}
