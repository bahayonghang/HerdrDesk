using System.Globalization;
using HerdDesk.Contracts;

namespace HerdDesk.App;

public static class ShellChrome
{
    public static string Lifecycle(ShellLifecycle value) => value switch
    {
        ShellLifecycle.Starting => ShellStrings.Starting,
        ShellLifecycle.Ready => ShellStrings.Ready,
        ShellLifecycle.NoDevices => ShellStrings.NoDevices,
        ShellLifecycle.DaemonUnavailable => ShellStrings.DaemonUnavailable,
        ShellLifecycle.Failed => ShellStrings.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static string Connection(ConnectionPhase value) => value switch
    {
        ConnectionPhase.Offline => ShellStrings.Offline,
        ConnectionPhase.Connecting => ShellStrings.Connecting,
        ConnectionPhase.Synchronizing => ShellStrings.Synchronizing,
        ConnectionPhase.Ready => ShellStrings.Ready,
        ConnectionPhase.Stale => ShellStrings.Stale,
        ConnectionPhase.Incompatible => ShellStrings.Incompatible,
        ConnectionPhase.WaitingForCapacity => ShellStrings.WaitingForCapacity,
        ConnectionPhase.PausedForCapacity => ShellStrings.PausedForCapacity,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static string Agent(AgentStatusKind? value) => value switch
    {
        AgentStatusKind.Idle => ShellStrings.NotificationIdle,
        AgentStatusKind.Working => ShellStrings.NotificationWorking,
        AgentStatusKind.Blocked => ShellStrings.NotificationBlocked,
        AgentStatusKind.Done => ShellStrings.NotificationDone,
        AgentStatusKind.Unknown or null => ShellStrings.Unknown,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static string Access(TerminalAccess value) => value switch
    {
        TerminalAccess.Observing => ShellStrings.Observing,
        TerminalAccess.Acquiring => ShellStrings.AcquiringControl,
        TerminalAccess.Controlling => ShellStrings.Controlling,
        TerminalAccess.Unknown => ShellStrings.Unknown,
        TerminalAccess.Disconnected => ShellStrings.Disconnected,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static string Unread(int count) =>
        ShellStrings.UnreadPrefix + count.ToString(CultureInfo.InvariantCulture);

    public static string Settings(SettingsLifecycle value) => value switch
    {
        SettingsLifecycle.Loading => ShellStrings.Loading,
        SettingsLifecycle.Ready => ShellStrings.Ready,
        SettingsLifecycle.Dirty => ShellStrings.Unsaved,
        SettingsLifecycle.Saving => ShellStrings.SavingSettings,
        SettingsLifecycle.Saved => ShellStrings.Saved,
        SettingsLifecycle.ValidationFailed => ShellStrings.ValidationFailed,
        SettingsLifecycle.SaveFailed => ShellStrings.SaveFailed,
        SettingsLifecycle.PermissionDenied => ShellStrings.PermissionDenied,
        SettingsLifecycle.Testing => ShellStrings.Testing,
        SettingsLifecycle.AwaitingHostKey => ShellStrings.HostKeyReviewRequired,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
    };

    public static string StatusLine(
        ConnectionPhase connection,
        AgentStatusKind? agent,
        int unread,
        TerminalAccess access) =>
        string.Join(" · ",
        [
            Connection(connection),
            Agent(agent),
            Unread(unread),
            Access(access)
        ]);
}
