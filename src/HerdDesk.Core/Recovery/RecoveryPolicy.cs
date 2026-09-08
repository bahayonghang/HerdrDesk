using HerdDesk.Contracts;

namespace HerdDesk.Core;

public static class RecoveryPolicy
{
    public static RecoveryDecision Decide(RecoveryFailure failure, int attempt, IRecoveryEntropy entropy)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(entropy);
        if (failure.RetryClass == RecoveryRetryClass.Stop)
            return RecoveryDecision.Stop(failure.Cause, Code(failure.Cause));
        if (failure.RetryClass == RecoveryRetryClass.AwaitUser)
            return RecoveryDecision.AwaitUser(failure.Cause, Code(failure.Cause));
        if (attempt > RecoveryBackoff.AutomaticRetryLimit)
            return RecoveryDecision.AwaitUser(failure.Cause, RecoveryCodes.RetryExhausted);
        return RecoveryDecision.RetryAfter(failure.Cause, RecoveryBackoff.Delay(attempt, entropy));
    }

    public static RecoveryDecision Manual(bool appStopping, RecoveryCause cause)
    {
        if (appStopping)
            return RecoveryDecision.Stop(RecoveryCause.AppStopping, RecoveryCodes.AppStopping);
        return RecoveryDecision.RetryNow(cause);
    }

    public static RecoveryFailure ClassifyRpc(
        SessionKey session,
        ConnectionEpoch epoch,
        string? code,
        RpcFailureKind kind,
        bool fromRequest,
        bool fromSubscription,
        bool userInitiated,
        bool appStopping)
    {
        if (appStopping)
            return Create(RecoveryScope.Application, RecoveryCause.AppStopping, session, null, epoch, true);
        if (userInitiated)
            return Create(RecoveryScope.Application, RecoveryCause.ManualDisconnect, session, null, epoch, true);
        if (code == ProjectionCodes.SchemaIncompatible)
            return Create(RecoveryScope.Rpc, RecoveryCause.SchemaIncompatible, session, null, epoch, false);
        if (code is RpcCodes.ProtocolPollution or RpcCodes.EnvelopeInvalid || kind == RpcFailureKind.Protocol)
            return Create(RecoveryScope.Rpc, RecoveryCause.ProtocolError, session, null, epoch, false);
        if (kind == RpcFailureKind.CancelledAfterWrite || code == RpcCodes.CancelledAfterWrite)
            return Create(RecoveryScope.Rpc, RecoveryCause.Cancellation, session, null, epoch, false);
        if (code == RpcCodes.ChildExited)
            return Create(RecoveryScope.Rpc, RecoveryCause.RpcBridgeExit, session, null, epoch, false);
        if (code == RpcCodes.SubscriptionLost || (fromSubscription && kind == RpcFailureKind.ConnectionLost))
            return Create(RecoveryScope.Rpc, RecoveryCause.SubscriptionEof, session, null, epoch, false);
        if (code == RpcCodes.RequestLost || (fromRequest && kind == RpcFailureKind.ConnectionLost))
            return Create(RecoveryScope.Rpc, RecoveryCause.RequestEof, session, null, epoch, false);
        if (code is RpcCodes.Unavailable or RpcCodes.ConnectFailed || kind == RpcFailureKind.Unavailable)
            return Create(RecoveryScope.Rpc, RecoveryCause.DaemonUnreachable, session, null, epoch, false);
        return Create(RecoveryScope.Rpc, RecoveryCause.DaemonUnreachable, session, null, epoch, false);
    }

    public static RecoveryFailure ClassifyTerminal(
        SessionKey session,
        PaneKey? pane,
        ConnectionEpoch epoch,
        RecoveryCause cause)
    {
        var scope = cause == RecoveryCause.RendererFailure ? RecoveryScope.Renderer : RecoveryScope.Terminal;
        return Create(scope, cause, session, pane, epoch, false);
    }

    public static RecoveryFailure Create(
        RecoveryScope scope,
        RecoveryCause cause,
        SessionKey session,
        PaneKey? pane,
        ConnectionEpoch epoch,
        bool userInitiated) =>
        new(
            scope,
            cause,
            session,
            pane,
            epoch,
            userInitiated,
            RetryClass(cause),
            Code(cause));

    public static RecoveryRetryClass RetryClass(RecoveryCause cause) =>
        cause switch
        {
            RecoveryCause.ManualDisconnect or RecoveryCause.AppStopping or RecoveryCause.Cancellation =>
                RecoveryRetryClass.Stop,
            RecoveryCause.SchemaIncompatible or RecoveryCause.ProtocolError or RecoveryCause.Authentication
                or RecoveryCause.HostKeyChanged =>
                RecoveryRetryClass.AwaitUser,
            _ => RecoveryRetryClass.Transient
        };

    public static string Code(RecoveryCause cause) =>
        cause switch
        {
            RecoveryCause.ManualDisconnect => RecoveryCodes.ManualDisconnect,
            RecoveryCause.AppStopping => RecoveryCodes.AppStopping,
            RecoveryCause.RequestEof => RecoveryCodes.RequestEof,
            RecoveryCause.SubscriptionEof => RecoveryCodes.SubscriptionEof,
            RecoveryCause.RpcBridgeExit => RecoveryCodes.RpcBridgeExit,
            RecoveryCause.DaemonUnreachable => RecoveryCodes.DaemonUnreachable,
            RecoveryCause.SchemaIncompatible => RecoveryCodes.SchemaIncompatible,
            RecoveryCause.ProtocolError => RecoveryCodes.ProtocolError,
            RecoveryCause.TerminalClosed => RecoveryCodes.TerminalClosed,
            RecoveryCause.TerminalStdoutEof => RecoveryCodes.TerminalStdoutEof,
            RecoveryCause.TerminalClientExit => RecoveryCodes.TerminalClientExit,
            RecoveryCause.RendererFailure => RecoveryCodes.RendererFailure,
            RecoveryCause.Cancellation => RecoveryCodes.Cancellation,
            RecoveryCause.Authentication => RecoveryCodes.Authentication,
            RecoveryCause.HostKeyChanged => RecoveryCodes.HostKeyChanged,
            _ => RecoveryCodes.DaemonUnreachable
        };
}
