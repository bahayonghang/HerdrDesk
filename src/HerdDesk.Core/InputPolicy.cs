using HerdDesk.Contracts;

namespace HerdDesk.Core;

/// <summary>
/// G0 policy specimen. It does not acquire control and cannot infer a grant
/// from a terminal frame. Reconnect creates a new epoch and loses write access.
/// </summary>
public static class InputPolicy
{
    public const int MaxInputBytes = 64 * 1024;

    public static InputDecision Evaluate(InputContext context, RendererInput input)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);
        if (!IsValid(context.ActivePane) || !IsValid(input.Pane))
            return new(false, "invalid_identity");
        if (context.ActivePane != input.Pane)
            return new(false, "wrong_pane");
        if (context.Epoch.Value <= 0 || context.Epoch != input.Epoch)
            return new(false, "stale_epoch");
        if (context.Access != TerminalAccess.Controlling || !context.ControlVerified)
            return new(false, "control_not_verified");
        // Emulator responses require a separately audited route. They are not
        // user keystrokes and must never silently inherit write permission.
        if (input.Origin is not (InputOrigin.UserKey or InputOrigin.CommittedText or InputOrigin.ExplicitPaste))
            return new(false, "input_origin_denied");
        if (input.Bytes.Length is <= 0 or > MaxInputBytes)
            return new(false, "input_bytes_limit");
        return new(true, "allowed");
    }

    private static bool IsValid(PaneKey pane) =>
        pane.Session.Device.Value != Guid.Empty &&
        !string.IsNullOrWhiteSpace(pane.Session.EndpointKey) &&
        !string.IsNullOrWhiteSpace(pane.WorkspaceId) &&
        !string.IsNullOrWhiteSpace(pane.PaneId);
}
