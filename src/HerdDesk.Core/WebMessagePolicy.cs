using HerdDesk.Contracts;

namespace HerdDesk.Core;

/// <summary>
/// Structured web-message allowlist. Unknown type, oversize, and wrong epoch
/// are rejected. InputContext is never taken from the renderer message.
/// </summary>
public static class WebMessagePolicy
{
    public const int SchemaVersion = 1;

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
        var limit = message.Type == "frame.apply"
            ? TerminalFrameParser.MaxFrameBytes
            : InputPolicy.MaxInputBytes;
        if (message.Type is "parse.consumed" or "terminal.resize")
        {
            if (message.PayloadBytes != 0)
                return new(false, "web_message_bytes_limit");
        }
        else if (message.PayloadBytes <= 0 || message.PayloadBytes > limit)
            return new(false, "web_message_bytes_limit");
        if (message.Type == "frame.apply" || message.Type == "parse.consumed")
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

    private static bool IsAllowlisted(string type, WebMessageDirection direction) =>
        (type, direction) switch
        {
            ("frame.apply", WebMessageDirection.HostToRenderer) => true,
            ("parse.consumed", WebMessageDirection.RendererToHost) => true,
            ("input.user_key", WebMessageDirection.RendererToHost) => true,
            ("input.committed_text", WebMessageDirection.RendererToHost) => true,
            ("input.explicit_paste", WebMessageDirection.RendererToHost) => true,
            ("input.emulator_reply", WebMessageDirection.RendererToHost) => true,
            ("ime.preedit", WebMessageDirection.RendererToHost) => true,
            ("terminal.resize", WebMessageDirection.RendererToHost) => true,
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
