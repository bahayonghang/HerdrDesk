using HerdDesk.Contracts;

namespace HerdDesk.App;

public enum ShellLifecycle
{
    Starting,
    Ready,
    NoDevices,
    DaemonUnavailable,
    Failed
}

public enum NavigationKind
{
    Device,
    Session,
    Workspace,
    Pane
}

public enum SelectionKind
{
    None,
    Device,
    Session,
    Workspace,
    Pane
}

public enum ShellRoute
{
    Welcome,
    Settings,
    Diagnostics,
    About,
    Pane
}

public enum RouteAvailabilityKind
{
    Enabled,
    Loading,
    Disabled,
    Failed
}

public enum DetailsPaneKind
{
    Collapsed,
    Info,
    Files,
    Diagnostics
}

public enum SettingsLifecycle
{
    Loading,
    Ready,
    Dirty,
    Saving,
    Saved,
    ValidationFailed,
    SaveFailed,
    PermissionDenied
}

public enum LayoutBreakpoint
{
    Narrow,
    Medium,
    Wide
}

public enum ActivationKind
{
    Normal,
    Settings,
    Diagnostics,
    NotificationTarget
}

public enum SearchResultKind
{
    Device,
    Session,
    Workspace,
    Pane,
    Recent
}

public enum RecoveryActionKind
{
    None,
    AddDevice,
    OpenDiagnostics,
    RetryProjection,
    OpenSettings,
    RemoveRecent
}

public enum TreeMove
{
    Up,
    Down,
    Parent,
    Child,
    Expand,
    Collapse,
    Activate
}

public enum ShellAccelerator
{
    OpenSearch,
    CloseSearch
}

public enum FocusRegion
{
    Title,
    DeviceRail,
    Tree,
    Content,
    Details,
    Search
}

public enum UiThemeKind
{
    System,
    Light,
    Dark,
    HighContrast
}

public static class ShellCodes
{
    public const string NoDevices = "no_devices";
    public const string DaemonUnavailable = "daemon_unavailable";
    public const string Stale = "stale";
    public const string Offline = "offline";
    public const string Incompatible = "incompatible";
    public const string Expired = "expired";
    public const string SshProviderPending = "ssh_provider_pending";
    public const string FilesProviderPending = "files_provider_pending";
    public const string CapabilityUnverified = "capability_unverified";
    public const string ObserveNoResize = "observe_no_resize";
    public const string PermissionDenied = "permission_denied";
    public const string TargetExpired = "target_expired";
    public const string Starting = "starting";
    public const string Failed = "failed";
    public const string Loading = "loading";
    public const string Empty = "empty";
    public const string Error = "error";
}

public readonly record struct FocusToken(string ControlId);

public sealed record RouteAvailability(
    RouteAvailabilityKind Kind,
    string? ReasonCode = null,
    string? ReasonText = null);

public sealed record StatusPresentation(
    string Code,
    string Text,
    string IconKey,
    RecoveryActionKind Action,
    string ActionLabel,
    bool ActionEnabled,
    string? ActionDisabledReason = null);

public sealed record NavigationItem(
    NavigationKind Kind,
    DeviceId Device,
    SessionKey? Session,
    string? WorkspaceId,
    PaneKey? Pane,
    ConnectionEpoch Epoch,
    string Label,
    string KindLabel,
    ConnectionPhase Connection,
    WireEnum<AgentStatusKind> AgentStatus,
    int UnreadCount,
    TerminalAccess Access,
    bool ControlVerified,
    bool IsLoading,
    bool IsEmpty,
    bool IsError,
    bool IsOffline,
    bool IsStale,
    bool IsIncompatible,
    bool IsDisabled,
    bool IsSelected,
    bool IsHovered,
    bool IsFocusVisible,
    bool IsExpanded,
    bool IsExpired,
    StatusPresentation Status,
    IReadOnlyList<string> CapabilityReasons,
    string? ParentIdentityKey,
    IReadOnlyList<NavigationItem> Children)
{
    public string IdentityKey => NavigationIdentity.Format(Kind, Device, Session, WorkspaceId, Pane);
}

public sealed record SelectionState(
    SelectionKind Kind,
    DeviceId? Device,
    SessionKey? Session,
    string? WorkspaceId,
    PaneKey? Pane,
    ConnectionEpoch Epoch,
    bool IsExpired)
{
    public static SelectionState None { get; } =
        new(SelectionKind.None, null, null, null, null, new ConnectionEpoch(0), false);
}

public sealed record SearchHit(
    SearchResultKind Kind,
    string Label,
    string KindLabel,
    DeviceId Device,
    SessionKey? Session,
    string? WorkspaceId,
    PaneKey? Pane,
    ConnectionEpoch Epoch,
    DateTimeOffset? RecentUtc,
    bool IsExpired,
    bool IsRecent);

public sealed record SearchState(
    string Query,
    IReadOnlyList<SearchHit> Results,
    int SelectedIndex,
    bool IsOpen,
    bool IsComposing,
    double LatencyMs,
    bool HasExpiredResults);

public sealed record LocalSessionDraft(
    SessionProfileKind Kind,
    string? SessionName,
    EndpointKind? Endpoint,
    string? CanonicalLocation);

public sealed record LocalDeviceDraft(
    DeviceId Device,
    string Label,
    string VerifiedHerdrPath,
    IReadOnlyList<LocalSessionDraft> Sessions);

public sealed record TerminalDisplayPreferences(string FontFamily, int FontSize, int ZoomPercent)
{
    public static TerminalDisplayPreferences Default { get; } = new("Cascadia Mono", 12, 100);
}

public sealed record UiPreferences(
    int SchemaVersion,
    long Revision,
    string FontFamily,
    int FontSize,
    int ZoomPercent,
    UiThemeKind Theme,
    bool NotificationsEnabled,
    bool DiagnosticPrivacyRedact)
{
    public const int CurrentSchemaVersion = 1;

    public static UiPreferences Default { get; } =
        new(CurrentSchemaVersion, 0, "Cascadia Mono", 12, 100, UiThemeKind.System, true, true);

    public TerminalDisplayPreferences Display => new(FontFamily, FontSize, ZoomPercent);
}

public sealed record DiagnosticPreviewField(string Name, string Value, bool Redacted);

public sealed record DiagnosticPreview(
    IReadOnlyList<DiagnosticPreviewField> Fields,
    bool IsRedactedDefault,
    bool Confirmed);

public sealed record RecentEntry(
    DeviceId Device,
    SessionKey? Session,
    string? WorkspaceId,
    PaneKey? Pane,
    ConnectionEpoch Epoch,
    DateTimeOffset Utc,
    string Label);

public interface IConfigurationOwnership
{
    bool CanWrite { get; }
}

public static class ConfigurationOwnership
{
    public static IConfigurationOwnership Owner { get; } = new FixedOwnership(true);
    public static IConfigurationOwnership Secondary { get; } = new FixedOwnership(false);

    private sealed class FixedOwnership(bool canWrite) : IConfigurationOwnership
    {
        public bool CanWrite { get; } = canWrite;
    }
}

public interface ITerminalDisplaySurface
{
    void ApplyLocal(TerminalDisplayPreferences preferences);
    bool TryUpstreamResize(int columns, int rows);
    int LocalApplyCount { get; }
    int UpstreamResizeCount { get; }
}

public sealed class NullDisplaySurface : ITerminalDisplaySurface
{
    public int LocalApplyCount { get; private set; }
    public int UpstreamResizeCount { get; private set; }

    public void ApplyLocal(TerminalDisplayPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        LocalApplyCount++;
    }

    public bool TryUpstreamResize(int columns, int rows)
    {
        _ = (columns, rows);
        UpstreamResizeCount++;
        return true;
    }
}

public static class NavigationIdentity
{
    public static string Format(
        NavigationKind kind,
        DeviceId device,
        SessionKey? session,
        string? workspaceId,
        PaneKey? pane)
    {
        var sessionPart = session is { } key
            ? key.EndpointKey + "\u001f" + (key.SessionName ?? "")
            : "";
        var panePart = pane is { } paneKey
            ? paneKey.WorkspaceId + "\u001f" + paneKey.PaneId
            : workspaceId ?? "";
        return string.Join('\u001e',
        [
            kind.ToString(),
            device.Value.ToString("D"),
            sessionPart,
            panePart
        ]);
    }
}
