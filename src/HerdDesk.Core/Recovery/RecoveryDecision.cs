namespace HerdDesk.Core;

public enum RecoveryAction
{
    Stop,
    AwaitUser,
    RetryNow,
    RetryAfter
}

public sealed record RecoveryDecision(
    RecoveryAction Action,
    TimeSpan Delay,
    RecoveryCause Cause,
    string Reason,
    bool StartDaemon)
{
    public static RecoveryDecision Stop(RecoveryCause cause, string reason) =>
        new(RecoveryAction.Stop, TimeSpan.Zero, cause, reason, false);

    public static RecoveryDecision AwaitUser(RecoveryCause cause, string reason) =>
        new(RecoveryAction.AwaitUser, TimeSpan.Zero, cause, reason, false);

    public static RecoveryDecision RetryNow(RecoveryCause cause) =>
        new(RecoveryAction.RetryNow, TimeSpan.Zero, cause, RecoveryCodes.RetryNow, false);

    public static RecoveryDecision RetryAfter(RecoveryCause cause, TimeSpan delay) =>
        new(RecoveryAction.RetryAfter, delay, cause, RecoveryCodes.RetryAfter, false);
}
