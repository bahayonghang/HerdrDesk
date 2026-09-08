using HerdDesk.Contracts;

namespace HerdDesk.Core;

/// <summary>
/// Structured web-message allowlist. Unknown type, oversize, and wrong epoch
/// are rejected. InputContext is never taken from the renderer message.
/// </summary>
public static class WebMessagePolicy
{
    public const int SchemaVersion = WebMessageLimits.SchemaVersion;

    public static InputDecision Evaluate(WebMessage message, InputContext context)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrEmpty(message.Type) || !IsAllowlisted(message.Type, message.Direction))
            return new(false, "unknown_web_message_type");
        if (message.Version != SchemaVersion)
            return new(false, "unsupported_web_message_version");
        if (context.Epoch.Value <= 0 || message.Epoch != context.Epoch)
            return new(false, "stale_epoch");
        if (message.Pane != context.ActivePane)
            return new(false, "wrong_pane");
        if (message.Type == "ime.preedit")
            return new(false, "preedit_not_sent");
        if (!PayloadAllowed(message.Type, message.PayloadBytes))
            return new(false, "web_message_bytes_limit");
        if (message.Type is "frame.apply" or "parse.consumed" or "host.initialize" or "host.focus"
            or "host.dispose" or "host.display" or "renderer.ready" or "renderer.fault"
            or "link.request")
            return new(true, "allowed");
        if (message.Type == "terminal.resize")
        {
            if (context.Access != TerminalAccess.Controlling || !context.ControlVerified)
                return new(false, "control_not_verified");
            return new(true, "allowed");
        }
        var origin = OriginFor(message.Type);
        if (message.Origin is { } claimed && claimed != origin)
            return new(false, "input_origin_denied");
        var payload = new byte[message.PayloadBytes];
        return InputPolicy.Evaluate(
            context, new RendererInput(context.ActivePane, context.Epoch, origin, payload));
    }

    private static bool PayloadAllowed(string type, int payloadBytes) => type switch
    {
        "parse.consumed" or "terminal.resize" or "host.dispose" or "renderer.ready" =>
            payloadBytes == 0,
        "frame.apply" => payloadBytes is > 0 and <= WebMessageLimits.MaxFrameBytes,
        "link.request" => payloadBytes is > 0 and <= WebMessageLimits.MaxLinkUriChars,
        "host.initialize" or "host.focus" or "host.display" or "renderer.fault" =>
            payloadBytes is >= 0 and <= WebMessageLimits.MaxInputBytes,
        _ => payloadBytes is > 0 and <= WebMessageLimits.MaxInputBytes,
    };

    private static bool IsAllowlisted(string type, WebMessageDirection direction) =>
        (type, direction) switch
        {
            ("frame.apply", WebMessageDirection.HostToRenderer) => true,
            ("host.initialize", WebMessageDirection.HostToRenderer) => true,
            ("host.focus", WebMessageDirection.HostToRenderer) => true,
            ("host.dispose", WebMessageDirection.HostToRenderer) => true,
            ("host.display", WebMessageDirection.HostToRenderer) => true,
            ("parse.consumed", WebMessageDirection.RendererToHost) => true,
            ("input.user_key", WebMessageDirection.RendererToHost) => true,
            ("input.committed_text", WebMessageDirection.RendererToHost) => true,
            ("input.explicit_paste", WebMessageDirection.RendererToHost) => true,
            ("input.emulator_reply", WebMessageDirection.RendererToHost) => true,
            ("ime.preedit", WebMessageDirection.RendererToHost) => true,
            ("terminal.resize", WebMessageDirection.RendererToHost) => true,
            ("link.request", WebMessageDirection.RendererToHost) => true,
            ("renderer.ready", WebMessageDirection.RendererToHost) => true,
            ("renderer.fault", WebMessageDirection.RendererToHost) => true,
            _ => false,
        };

    private static InputOrigin OriginFor(string type) => type switch
    {
        "input.user_key" => InputOrigin.UserKey,
        "input.committed_text" => InputOrigin.CommittedText,
        "input.explicit_paste" => InputOrigin.ExplicitPaste,
        "input.emulator_reply" => InputOrigin.EmulatorReply,
        _ => (InputOrigin)999,
    };
}
