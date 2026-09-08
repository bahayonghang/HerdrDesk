using HerdDesk.Contracts;

namespace HerdDesk.Core;

/// <summary>
/// IME host events. Preedit never becomes RendererInput. Commit is
/// CommittedText once. Composition shortcuts stay with the IME.
/// </summary>
public static class CompositionPolicy
{
    public static InputDecision Evaluate(
        ImeHostEvent imeEvent, InputContext context, RendererInput? proposed)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (imeEvent == ImeHostEvent.PreeditUpdate)
            return new(false, "preedit_not_sent");
        if (imeEvent == ImeHostEvent.KeyWhileComposing)
            return new(false, "ime_owns_shortcut");
        if (proposed is null)
            return new(false, "input_origin_denied");
        if (imeEvent == ImeHostEvent.Commit && proposed.Origin != InputOrigin.CommittedText)
            return new(false, "commit_origin_required");
        if (imeEvent == ImeHostEvent.KeyIdle && proposed.Origin != InputOrigin.UserKey)
            return new(false, "input_origin_denied");
        return InputPolicy.Evaluate(context, proposed);
    }
}
