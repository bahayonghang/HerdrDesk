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
        if (IsRemoteTransient(failure.Cause))
            return DecideRemote(failure, Math.Max(0, attempt - 1), ZeroRetryRandom.Instance);
        if (attempt > RecoveryBackoff.AutomaticRetryLimit)
            return RecoveryDecision.AwaitUser(failure.Cause, RecoveryCodes.RetryExhausted);
        return RecoveryDecision.RetryAfter(failure.Cause, RecoveryBackoff.Delay(attempt, entropy));
    }

    public static RecoveryDecision DecideRemote(RecoveryFailure failure, int n, IRetryRandom random)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(random);
        if (failure.RetryClass == RecoveryRetryClass.Stop)
            return RecoveryDecision.Stop(failure.Cause, Code(failure.Cause));
        if (failure.RetryClass == RecoveryRetryClass.AwaitUser || !IsRemoteTransient(failure.Cause))
            return RecoveryDecision.AwaitUser(failure.Cause, Code(failure.Cause));
        var delay = RecoveryBackoff.EqualJitter(n, random.NextUnitInterval());
        return RecoveryDecision.RetryAfter(failure.Cause, delay);
    }

    public static bool IsRemoteTransient(RecoveryCause cause) =>
        cause is RecoveryCause.TransientNetwork or RecoveryCause.TransientTransport;

    public static bool IsPersistableBlock(RecoveryCause cause) =>
        cause is RecoveryCause.Authentication or RecoveryCause.AuthenticationBlocked
            or RecoveryCause.UnsupportedAuthentication or RecoveryCause.HostKeyUnknown
            or RecoveryCause.HostKeyChanged;

    public static bool CanExplicitRetry(RecoveryBlockSnapshot? block, SessionRecoveryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (block is null)
            return true;
        return block.Kind switch
        {
            RecoveryCause.Authentication or RecoveryCause.AuthenticationBlocked
                or RecoveryCause.UnsupportedAuthentication =>
                context.AuthenticationSupported &&
                (context.ProfileRevision != block.BlockedProfileRevision ||
                 (block.CredentialRevision is { } credential &&
                  context.CredentialRevision != credential)),
            RecoveryCause.HostKeyUnknown or RecoveryCause.HostKeyChanged =>
                context.HostTrusted &&
                block.KnownHostRevision is { } known &&
                context.KnownHostRevision != known,
            _ => false
        };
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
        bool userInitiated,
        RecoveryRetryClass? retryClass = null) =>
        new(
            scope,
            cause,
            session,
            pane,
            epoch,
            userInitiated,
            retryClass ?? RetryClass(cause),
            Code(cause));

    public static RecoveryRetryClass RetryClass(RecoveryCause cause) =>
        cause switch
        {
            RecoveryCause.ManualDisconnect or RecoveryCause.AppStopping or RecoveryCause.Cancellation
                or RecoveryCause.Cancelled =>
                RecoveryRetryClass.Stop,
            RecoveryCause.SchemaIncompatible or RecoveryCause.ProtocolError or RecoveryCause.Authentication
                or RecoveryCause.HostKeyChanged or RecoveryCause.AuthenticationBlocked
                or RecoveryCause.UnsupportedAuthentication or RecoveryCause.HostKeyUnknown
                or RecoveryCause.DaemonUnavailable or RecoveryCause.ProtocolPollution
                or RecoveryCause.Incompatible or RecoveryCause.UnknownBlocked =>
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
            RecoveryCause.TransientNetwork => RecoveryCodes.TransientNetwork,
            RecoveryCause.TransientTransport => RecoveryCodes.TransientTransport,
            RecoveryCause.AuthenticationBlocked => RecoveryCodes.AuthenticationBlocked,
            RecoveryCause.UnsupportedAuthentication => RecoveryCodes.UnsupportedAuthentication,
            RecoveryCause.HostKeyUnknown => RecoveryCodes.HostKeyUnknown,
            RecoveryCause.DaemonUnavailable => RecoveryCodes.DaemonUnavailable,
            RecoveryCause.ProtocolPollution => RecoveryCodes.ProtocolPollution,
            RecoveryCause.Incompatible => RecoveryCodes.Incompatible,
            RecoveryCause.Cancelled => RecoveryCodes.Cancelled,
            RecoveryCause.UnknownBlocked => RecoveryCodes.UnknownBlocked,
            _ => RecoveryCodes.UnknownBlocked
        };

    public static string PublicCode(RecoveryCause cause) =>
        cause switch
        {
            RecoveryCause.TransientNetwork or RecoveryCause.TransientTransport
                or RecoveryCause.RequestEof or RecoveryCause.SubscriptionEof
                or RecoveryCause.RpcBridgeExit or RecoveryCause.TerminalStdoutEof
                or RecoveryCause.TerminalClientExit or RecoveryCause.RendererFailure =>
                RecoveryCodes.ReconnectWaiting,
            RecoveryCause.Authentication or RecoveryCause.AuthenticationBlocked =>
                RecoveryCodes.AuthenticationActionRequired,
            RecoveryCause.UnsupportedAuthentication => RecoveryCodes.UnsupportedAuthentication,
            RecoveryCause.HostKeyUnknown or RecoveryCause.HostKeyChanged =>
                RecoveryCodes.HostKeyReviewRequired,
            RecoveryCause.Cancellation or RecoveryCause.Cancelled or RecoveryCause.ManualDisconnect
                or RecoveryCause.AppStopping =>
                RecoveryCodes.ReconnectCancelled,
            _ => RecoveryCodes.ConnectionManualRetryRequired
        };

    public static RecoveryCause? ParseCode(string? code) =>
        code switch
        {
            RecoveryCodes.ManualDisconnect => RecoveryCause.ManualDisconnect,
            RecoveryCodes.AppStopping => RecoveryCause.AppStopping,
            RecoveryCodes.RequestEof => RecoveryCause.RequestEof,
            RecoveryCodes.SubscriptionEof => RecoveryCause.SubscriptionEof,
            RecoveryCodes.RpcBridgeExit => RecoveryCause.RpcBridgeExit,
            RecoveryCodes.DaemonUnreachable => RecoveryCause.DaemonUnreachable,
            RecoveryCodes.SchemaIncompatible => RecoveryCause.SchemaIncompatible,
            RecoveryCodes.ProtocolError => RecoveryCause.ProtocolError,
            RecoveryCodes.TerminalClosed => RecoveryCause.TerminalClosed,
            RecoveryCodes.TerminalStdoutEof => RecoveryCause.TerminalStdoutEof,
            RecoveryCodes.TerminalClientExit => RecoveryCause.TerminalClientExit,
            RecoveryCodes.RendererFailure => RecoveryCause.RendererFailure,
            RecoveryCodes.Cancellation => RecoveryCause.Cancellation,
            RecoveryCodes.Authentication => RecoveryCause.Authentication,
            RecoveryCodes.HostKeyChanged => RecoveryCause.HostKeyChanged,
            RecoveryCodes.TransientNetwork => RecoveryCause.TransientNetwork,
            RecoveryCodes.TransientTransport => RecoveryCause.TransientTransport,
            RecoveryCodes.AuthenticationBlocked => RecoveryCause.AuthenticationBlocked,
            RecoveryCodes.UnsupportedAuthentication => RecoveryCause.UnsupportedAuthentication,
            RecoveryCodes.HostKeyUnknown => RecoveryCause.HostKeyUnknown,
            RecoveryCodes.DaemonUnavailable => RecoveryCause.DaemonUnavailable,
            RecoveryCodes.ProtocolPollution => RecoveryCause.ProtocolPollution,
            RecoveryCodes.Incompatible => RecoveryCause.Incompatible,
            RecoveryCodes.Cancelled => RecoveryCause.Cancelled,
            RecoveryCodes.UnknownBlocked => RecoveryCause.UnknownBlocked,
            _ => null
        };
}
