using System.Collections.Frozen;
using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class DeviceConnectionStatusViewModelTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("waiting retry shows keyed countdown", WaitingCountdown),
        ("auth and host-key blocks enable the matching action", BlockedActions),
        ("retry intent stays on the same session key", NoFanOut),
        ("in-flight disables retry and cancel", InFlightDisables),
        ("accessibility names distinguish wait and block", AutomationNames)
    ];

    static SessionKey SessionA1() =>
        new(AppTestHost.DeviceA, "named-session", "a1");

    static SessionKey SessionA2() =>
        new(AppTestHost.DeviceA, "named-session", "a2");

    static DeviceSessionState State(
        SessionKey session,
        ConnectionPhase phase,
        SessionRecoveryProgress recovery,
        DeviceFreshness freshness = DeviceFreshness.Stale)
    {
        return new DeviceSessionState(
            session,
            new ConnectionEpoch(2),
            phase,
            freshness,
            new CapabilityProfile("0.9.0", "0.9.0", 22, 1, "sha", "sha", FrozenSet<string>.Empty),
            DeviceProjectionSnapshot.Empty,
            0,
            0,
            recovery.Cause,
            phase == ConnectionPhase.Ready,
            0,
            recovery.RetryTimerCount,
            0,
            0,
            recovery);
    }

    static void WaitingCountdown()
    {
        var now = DateTimeOffset.UnixEpoch.AddSeconds(10);
        var vm = new DeviceConnectionStatusViewModel(SessionA1(), TimeProvider.System);
        vm.Apply(
            State(
                SessionA1(),
                ConnectionPhase.Stale,
                new SessionRecoveryProgress(
                    1, now.AddSeconds(7), RecoveryCodes.TransientTransport, RecoveryCodes.RetryAfter,
                    1, 1, true, null, RecoveryCodes.ReconnectWaiting)),
            now: now);
        AppTestHost.Check(vm.SecondsRemaining == 7);
        AppTestHost.Check(vm.StatusText == "将在 7 秒重试");
        AppTestHost.Check(vm.CancelEnabled);
        AppTestHost.Check(vm.RetryEnabled);
        AppTestHost.Check(vm.InputReplayText == ShellStrings.InputNotReplayed);
        AppTestHost.Check(!vm.EditCredentialsEnabled);
        AppTestHost.Check(!vm.ReviewHostKeyEnabled);
    }

    static void BlockedActions()
    {
        var auth = new DeviceConnectionStatusViewModel(SessionA1());
        auth.Apply(State(
            SessionA1(),
            ConnectionPhase.Stale,
            new SessionRecoveryProgress(
                0, null, RecoveryCodes.AuthenticationBlocked, RecoveryCodes.AuthenticationBlocked,
                0, 1, true, 3, RecoveryCodes.AuthenticationActionRequired)));
        AppTestHost.Check(auth.StatusText == ShellStrings.AuthenticationBlocked);
        AppTestHost.Check(auth.EditCredentialsEnabled);
        AppTestHost.Check(!auth.ReviewHostKeyEnabled);
        AppTestHost.Check(!auth.CancelEnabled);
        var host = new DeviceConnectionStatusViewModel(SessionA1());
        host.Apply(State(
            SessionA1(),
            ConnectionPhase.Stale,
            new SessionRecoveryProgress(
                0, null, RecoveryCodes.HostKeyChanged, RecoveryCodes.HostKeyChanged,
                0, 1, true, 3, RecoveryCodes.HostKeyReviewRequired)));
        AppTestHost.Check(host.StatusText == ShellStrings.HostKeyReviewRequired);
        AppTestHost.Check(host.ReviewHostKeyEnabled);
        AppTestHost.Check(!host.EditCredentialsEnabled);
        var manual = new DeviceConnectionStatusViewModel(SessionA1());
        manual.Apply(State(
            SessionA1(),
            ConnectionPhase.Stale,
            new SessionRecoveryProgress(
                0, null, RecoveryCodes.UnknownBlocked, RecoveryCodes.UnknownBlocked,
                0, 1, false, null, RecoveryCodes.ConnectionManualRetryRequired)));
        AppTestHost.Check(manual.StatusText == ShellStrings.ConnectionManualRetryRequired);
    }

    static void NoFanOut()
    {
        var a1 = new DeviceConnectionStatusViewModel(SessionA1());
        var a2 = new DeviceConnectionStatusViewModel(SessionA2());
        a1.Apply(State(
            SessionA1(),
            ConnectionPhase.Stale,
            new SessionRecoveryProgress(
                1, DateTimeOffset.UnixEpoch.AddSeconds(4), RecoveryCodes.TransientTransport,
                RecoveryCodes.RetryAfter, 1, 1, true, null, RecoveryCodes.ReconnectWaiting)),
            now: DateTimeOffset.UnixEpoch);
        var retry = a1.Retry();
        AppTestHost.Check(retry.Session == SessionA1());
        AppTestHost.Check(retry.Session != SessionA2());
        AppTestHost.Check(a2.LastIntent is null);
        var cancel = a1.Cancel();
        AppTestHost.Check(cancel.Session == SessionA1());
        AppTestHost.Check(a1.EditCredentials().Session == SessionA1());
        AppTestHost.Check(a1.ReviewHostKey().Session == SessionA1());
    }

    static void InFlightDisables()
    {
        var vm = new DeviceConnectionStatusViewModel(SessionA1());
        vm.Apply(State(
            SessionA1(),
            ConnectionPhase.Connecting,
            new SessionRecoveryProgress(
                2, null, RecoveryCodes.TransientTransport, RecoveryCodes.RetryNow,
                0, 2, true, null, RecoveryCodes.ReconnectWaiting)));
        AppTestHost.Check(vm.InFlight);
        AppTestHost.Check(!vm.RetryEnabled);
        AppTestHost.Check(!vm.CancelEnabled);
    }

    static void AutomationNames()
    {
        var wait = new DeviceConnectionStatusViewModel(SessionA1());
        wait.Apply(State(
            SessionA1(),
            ConnectionPhase.Stale,
            new SessionRecoveryProgress(
                1, DateTimeOffset.UnixEpoch.AddSeconds(2), RecoveryCodes.TransientNetwork,
                RecoveryCodes.RetryAfter, 1, 1, false, null, RecoveryCodes.ReconnectWaiting)),
            now: DateTimeOffset.UnixEpoch);
        AppTestHost.Check(wait.AutomationName == "reconnect-waiting");
        var auth = new DeviceConnectionStatusViewModel(SessionA1());
        auth.Apply(State(
            SessionA1(),
            ConnectionPhase.Stale,
            new SessionRecoveryProgress(
                0, null, RecoveryCodes.AuthenticationBlocked, RecoveryCodes.AuthenticationBlocked,
                0, 1, true, 2, RecoveryCodes.AuthenticationActionRequired)));
        AppTestHost.Check(auth.AutomationName == "authentication-blocked");
        AppTestHost.Check(auth.AutomationName != wait.AutomationName);
        AppTestHost.Check(auth.StatusText != wait.StatusText);
        AppTestHost.Check(!auth.StatusText.Contains("password", StringComparison.OrdinalIgnoreCase));
    }
}
