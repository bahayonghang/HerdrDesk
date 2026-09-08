using HerdDesk.Contracts;

namespace HerdDesk.App;

public sealed class TerminalDisplayCoordinator
{
    private readonly ITerminalDisplaySurface _surface;

    public TerminalDisplayCoordinator(ITerminalDisplaySurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _surface = surface;
        Committed = TerminalDisplayPreferences.Default;
        Preview = Committed;
    }

    public TerminalDisplayPreferences Committed { get; private set; }
    public TerminalDisplayPreferences Preview { get; private set; }
    public int LocalApplyCount => _surface.LocalApplyCount;
    public int UpstreamResizeCount => _surface.UpstreamResizeCount;

    public string? ApplyPreview(TerminalDisplayPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var wrapped = new UiPreferences(
            UiPreferences.CurrentSchemaVersion, 0, preferences.FontFamily, preferences.FontSize,
            preferences.ZoomPercent, UiThemeKind.System, true, true);
        var error = UiPreferenceStore.Validate(wrapped);
        if (error is not null)
            return error;
        Preview = preferences;
        _surface.ApplyLocal(preferences);
        return null;
    }

    public void RestoreCommitted()
    {
        Preview = Committed;
        _surface.ApplyLocal(Committed);
    }

    public void AcceptCommitted(TerminalDisplayPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        Committed = preferences;
        Preview = preferences;
        _surface.ApplyLocal(preferences);
    }

    public bool TryRequestResize(int columns, int rows, InputContext context, CapabilityProfile capabilities)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (context.Access != TerminalAccess.Controlling || !context.ControlVerified)
            return false;
        if (!capabilities.HasOperation("pane.resize"))
            return false;
        if (columns <= 0 || rows <= 0)
            return false;
        return _surface.TryUpstreamResize(columns, rows);
    }
}
