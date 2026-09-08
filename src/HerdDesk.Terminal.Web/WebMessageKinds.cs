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

    public static string PolicyType(string kind, InputOrigin? origin = null) => kind switch
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
        Input when origin == InputOrigin.UserKey => "input.user_key",
        Input when origin == InputOrigin.CommittedText => "input.committed_text",
        Input when origin == InputOrigin.ExplicitPaste => "input.explicit_paste",
        Input when origin == InputOrigin.EmulatorReply => "input.emulator_reply",
        _ => kind,
    };
}
