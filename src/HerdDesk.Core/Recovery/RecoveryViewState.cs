using HerdDesk.Contracts;

namespace HerdDesk.Core;

public enum RecoveryViewKind
{
    Fresh,
    Stale,
    AwaitingUser,
    RebuildingProjection,
    ReobservingTerminal,
    Recovered,
    Offline
}

public sealed record RecoveryViewState(
    RecoveryViewKind Kind,
    bool Retrying,
    int Attempt,
    DateTimeOffset? NextUtc,
    string? Reason,
    bool CommandsEnabled,
    bool DataMayBeStale,
    string? LastKnownLabel)
{
    public static RecoveryViewState Offline { get; } =
        new(RecoveryViewKind.Offline, false, 0, null, RecoveryCodes.ManualDisconnect, false, true, null);
}

public static class RecoveryProjection
{
    public static RecoveryViewState Project(
        DeviceSessionState session,
        ControlLeaseState? lease,
        string? lastKnownLabel)
    {
        ArgumentNullException.ThrowIfNull(session);
        var recovery = session.Recovery;
        var commands = session.Phase == ConnectionPhase.Ready &&
                       session.Freshness == DeviceFreshness.Current;
        var staleContext = !commands;
        if (session.Phase == ConnectionPhase.Offline)
        {
            return new(
                RecoveryViewKind.Offline,
                false,
                recovery.Attempt,
                null,
                recovery.Cause ?? RecoveryCodes.ManualDisconnect,
                false,
                true,
                lastKnownLabel);
        }

        if (session.Phase == ConnectionPhase.Incompatible ||
            recovery.Decision == RecoveryCodes.AwaitUser)
        {
            return new(
                RecoveryViewKind.AwaitingUser,
                false,
                recovery.Attempt,
                null,
                recovery.Cause ?? session.LastErrorCode,
                false,
                true,
                lastKnownLabel);
        }

        if (session.Phase is ConnectionPhase.Connecting or ConnectionPhase.Synchronizing)
        {
            return new(
                RecoveryViewKind.RebuildingProjection,
                recovery.RetryTimerCount == 0,
                recovery.Attempt,
                recovery.NextRetryUtc,
                recovery.Cause,
                false,
                true,
                lastKnownLabel);
        }

        if (session.Phase == ConnectionPhase.Stale)
        {
            var retrying = recovery.Decision == RecoveryCodes.RetryAfter || recovery.RetryTimerCount > 0;
            return new(
                retrying ? RecoveryViewKind.Stale : RecoveryViewKind.AwaitingUser,
                retrying,
                recovery.Attempt,
                recovery.NextRetryUtc,
                recovery.Cause ?? session.LastErrorCode,
                false,
                true,
                lastKnownLabel);
        }

        if (session.Phase == ConnectionPhase.Ready)
        {
            if (lease is not null &&
                (lease.Access is TerminalAccess.Disconnected or TerminalAccess.Unknown ||
                 lease.ObserveBinding is null))
            {
                return new(
                    RecoveryViewKind.ReobservingTerminal,
                    false,
                    0,
                    null,
                    null,
                    commands,
                    staleContext,
                    lastKnownLabel);
            }

            if (lease is not null && lease.Access == TerminalAccess.Observing && !lease.ControlVerified)
            {
                return new(
                    RecoveryViewKind.Recovered,
                    false,
                    0,
                    null,
                    null,
                    commands,
                    false,
                    lastKnownLabel);
            }

            return new(
                RecoveryViewKind.Fresh,
                false,
                0,
                null,
                null,
                commands,
                false,
                lastKnownLabel);
        }

        return new(
            RecoveryViewKind.Stale,
            false,
            recovery.Attempt,
            recovery.NextRetryUtc,
            recovery.Cause ?? session.LastErrorCode,
            false,
            true,
            lastKnownLabel);
    }
}
