using System.Globalization;
using HerdDesk.Contracts;
using HerdDesk.Core;

namespace HerdDesk.App;

public enum DeviceConnectionStatusAction
{
    Retry,
    Cancel,
    EditCredentials,
    ReviewHostKey
}

public sealed record DeviceConnectionStatusIntent(
    SessionKey Session,
    DeviceConnectionStatusAction Action);

public sealed class DeviceConnectionStatusViewModel
{
    private readonly TimeProvider _time;

    public DeviceConnectionStatusViewModel(SessionKey session, TimeProvider? time = null)
    {
        if (session.Device.Value == Guid.Empty || string.IsNullOrWhiteSpace(session.EndpointKey))
            throw new ArgumentException(ProjectionCodes.InvalidIdentity, nameof(session));
        Session = session;
        _time = time ?? TimeProvider.System;
        StatusText = ShellStrings.Offline;
        AutomationName = "connection-offline";
        AutomationHint = ShellStrings.Offline;
    }

    public SessionKey Session { get; }
    public string StatusText { get; private set; }
    public string AutomationName { get; private set; }
    public string AutomationHint { get; private set; }
    public int SecondsRemaining { get; private set; }
    public bool RetryEnabled { get; private set; }
    public bool CancelEnabled { get; private set; }
    public bool EditCredentialsEnabled { get; private set; }
    public bool ReviewHostKeyEnabled { get; private set; }
    public bool InFlight { get; private set; }
    public string? InputReplayText { get; private set; }
    public DeviceConnectionStatusIntent? LastIntent { get; private set; }

    public void Apply(
        DeviceSessionState state,
        SessionRecoveryContext? context = null,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Session != Session)
            return;

        var recovery = state.Recovery;
        var clock = now ?? _time.GetUtcNow();
        InFlight = state.Phase is ConnectionPhase.Connecting or ConnectionPhase.Synchronizing;
        CancelEnabled = recovery.RetryTimerCount > 0 && !InFlight;
        SecondsRemaining = 0;
        if (recovery.NextRetryUtc is { } due && due > clock)
        {
            var seconds = Math.Ceiling((due - clock).TotalSeconds);
            SecondsRemaining = seconds > int.MaxValue ? int.MaxValue : (int)seconds;
        }

        var publicCode = recovery.PublicCode ?? recovery.Cause;
        EditCredentialsEnabled = publicCode is RecoveryCodes.AuthenticationActionRequired
            or RecoveryCodes.AuthenticationBlocked or RecoveryCodes.Authentication
            or RecoveryCodes.UnsupportedAuthentication;
        ReviewHostKeyEnabled = publicCode is RecoveryCodes.HostKeyReviewRequired
            or RecoveryCodes.HostKeyUnknown or RecoveryCodes.HostKeyChanged;
        RetryEnabled = !InFlight && state.Phase is ConnectionPhase.Stale or ConnectionPhase.Incompatible
            or ConnectionPhase.Offline;
        InputReplayText = recovery.InputNotReplayed ? ShellStrings.InputNotReplayed : null;
        StatusText = FormatStatus(publicCode, recovery);
        AutomationName = AutomationOf(publicCode, recovery);
        AutomationHint = StatusText;
        _ = context;
    }

    public DeviceConnectionStatusIntent Retry()
    {
        LastIntent = new(Session, DeviceConnectionStatusAction.Retry);
        return LastIntent;
    }

    public DeviceConnectionStatusIntent Cancel()
    {
        LastIntent = new(Session, DeviceConnectionStatusAction.Cancel);
        return LastIntent;
    }

    public DeviceConnectionStatusIntent EditCredentials()
    {
        LastIntent = new(Session, DeviceConnectionStatusAction.EditCredentials);
        return LastIntent;
    }

    public DeviceConnectionStatusIntent ReviewHostKey()
    {
        LastIntent = new(Session, DeviceConnectionStatusAction.ReviewHostKey);
        return LastIntent;
    }

    private string FormatStatus(string? publicCode, SessionRecoveryProgress recovery)
    {
        if (publicCode is RecoveryCodes.ReconnectWaiting || recovery.RetryTimerCount > 0)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                ShellStrings.ReconnectWaitingFormat,
                SecondsRemaining);
        }

        return publicCode switch
        {
            RecoveryCodes.AuthenticationActionRequired or RecoveryCodes.AuthenticationBlocked
                or RecoveryCodes.Authentication =>
                ShellStrings.AuthenticationBlocked,
            RecoveryCodes.UnsupportedAuthentication => ShellStrings.AuthenticationUnsupported,
            RecoveryCodes.HostKeyReviewRequired or RecoveryCodes.HostKeyUnknown
                or RecoveryCodes.HostKeyChanged =>
                ShellStrings.HostKeyReviewRequired,
            RecoveryCodes.ReconnectCancelled => ShellStrings.ReconnectCancelled,
            RecoveryCodes.ConnectionManualRetryRequired or RecoveryCodes.ProtocolError
                or RecoveryCodes.ProtocolPollution or RecoveryCodes.UnknownBlocked
                or RecoveryCodes.DaemonUnavailable or RecoveryCodes.Incompatible
                or RecoveryCodes.SchemaIncompatible =>
                ShellStrings.ConnectionManualRetryRequired,
            _ => recovery.Cause ?? ShellStrings.Offline
        };
    }

    private static string AutomationOf(string? publicCode, SessionRecoveryProgress recovery)
    {
        if (publicCode is RecoveryCodes.ReconnectWaiting || recovery.RetryTimerCount > 0)
            return "reconnect-waiting";
        if (publicCode is RecoveryCodes.AuthenticationActionRequired or RecoveryCodes.AuthenticationBlocked
            or RecoveryCodes.Authentication or RecoveryCodes.UnsupportedAuthentication)
            return "authentication-blocked";
        if (publicCode is RecoveryCodes.HostKeyReviewRequired or RecoveryCodes.HostKeyUnknown
            or RecoveryCodes.HostKeyChanged)
            return "host-key-blocked";
        if (publicCode is RecoveryCodes.ReconnectCancelled)
            return "reconnect-cancelled";
        return "manual-blocked";
    }
}
