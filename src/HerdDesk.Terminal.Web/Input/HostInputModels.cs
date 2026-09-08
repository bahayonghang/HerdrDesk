using HerdDesk.Contracts;

namespace HerdDesk.Terminal.Web;

public enum HostFocusState
{
    Unfocused,
    FocusRequested,
    Focused,
    Suspended
}

public static class HostInputCodes
{
    public const string Allowed = "allowed";
    public const string PreeditNotSent = "preedit_not_sent";
    public const string ImeOwnsShortcut = "ime_owns_shortcut";
    public const string RendererNotReady = "renderer_not_ready";
    public const string CompositionActive = "composition_active";
    public const string WrongPane = "wrong_pane";
    public const string StaleEpoch = "stale_epoch";
    public const string ControlNotVerified = "control_not_verified";
    public const string InputBytesLimit = "input_bytes_limit";
    public const string UnsupportedKeyProfile = "unsupported_key_profile";
    public const string ObserveNoResize = "observe_no_resize";
    public const string ObserveScrollDenied = "observe_scroll_denied";
    public const string LeaseNotGranted = "lease_not_granted";
    public const string CommitAlreadyAccepted = "commit_already_accepted";
    public const string InputPaused = "input_paused";
    public const string InputOriginDenied = "input_origin_denied";
}

public sealed record HostInputResult(
    bool Allowed,
    string Code,
    byte[] TransportBytes,
    InputOrigin? Origin = null,
    bool LocalCopy = false,
    bool LocalScroll = false,
    bool AcceleratorYielded = false,
    bool LocalSelection = false)
{
    public static HostInputResult Deny(string code, bool acceleratorYielded = false) =>
        new(false, code, [], AcceleratorYielded: acceleratorYielded);

    public static HostInputResult Accept(
        string code, byte[] bytes, InputOrigin origin) =>
        new(true, code, bytes, origin);

    public static HostInputResult Local(string code, bool copy = false, bool scroll = false, bool selection = false) =>
        new(true, code, [], LocalCopy: copy, LocalScroll: scroll, LocalSelection: selection);
}

public readonly record struct PhysicalKeyEvent(
    string Key,
    bool Ctrl = false,
    bool Shift = false,
    bool Alt = false,
    bool AltGr = false,
    bool CapsLock = false,
    bool LeftCtrl = false,
    bool RightCtrl = false,
    bool LeftAlt = false,
    bool RightAlt = false)
{
    public bool AnyCtrl => Ctrl || LeftCtrl || RightCtrl;
    public bool AnyAlt => Alt || LeftAlt || RightAlt;
}

public readonly record struct MouseHostEvent(
    string Kind,
    int Delta = 0,
    bool Shift = false,
    bool Ctrl = false);

public readonly record struct CandidateAnchor(
    int ViewportX,
    int ViewportY,
    int CursorCellX,
    int CursorCellY,
    int CellWidthPx,
    int CellHeightPx,
    double RasterScale);

public sealed record SelectionSnapshot(string VisibleText, bool HasSelection);

public sealed record AgentInputProfile(
    string AgentName,
    string Version,
    string Shell,
    string RendererVersion,
    bool LiveVerified = false)
{
    public bool IsUnknown =>
        string.IsNullOrEmpty(AgentName) ||
        string.Equals(AgentName, "unknown", StringComparison.OrdinalIgnoreCase);
}
