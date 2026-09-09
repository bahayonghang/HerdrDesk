using HerdDesk.Contracts;

namespace HerdDesk.Terminal.Web;

public static class WebMessageKinds
{
    public const string Initialize = "initialize";
    public const string Frame = "frame";
    public const string Focus = "focus";
    public const string Dispose = "dispose";
    public const string Display = "display";
    public const string Ready = "ready";
    public const string Parsed = "parsed";
    public const string Input = "input";
    public const string Resize = "resize";
    public const string LinkRequest = "linkRequest";
    public const string Fault = "fault";
    public const string Composition = "composition";
    public const string Key = "key";
    public const string PasteIntent = "pasteIntent";
    public const string SelectionChanged = "selectionChanged";
    public const string MouseIntent = "mouseIntent";

    public static bool IsHostInputKind(string kind) => kind is
        Input or Composition or Key or PasteIntent or SelectionChanged or MouseIntent;

    public static string PolicyType(string kind, InputOrigin? origin = null, string? phase = null) => kind switch
    {
        Initialize => "host.initialize",
        Frame => "frame.apply",
        Focus => "host.focus",
        Dispose => "host.dispose",
        Display => "host.display",
        Ready => "renderer.ready",
        Parsed => "parse.consumed",
        Resize => "terminal.resize",
        LinkRequest => "link.request",
        Fault => "renderer.fault",
        Composition when phase == "end" => "input.committed_text",
        Composition => "ime.preedit",
        Key => "input.user_key",
        PasteIntent => "input.explicit_paste",
        SelectionChanged => "selection.local",
        MouseIntent => "mouse.intent",
        Input when origin == InputOrigin.UserKey => "input.user_key",
        Input when origin == InputOrigin.CommittedText => "input.committed_text",
        Input when origin == InputOrigin.ExplicitPaste => "input.explicit_paste",
        Input when origin == InputOrigin.EmulatorReply => "input.emulator_reply",
        _ => kind,
    };
}
