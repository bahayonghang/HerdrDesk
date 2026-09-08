using HerdDesk.Contracts;
using HerdDesk.Core;

namespace HerdDesk.Infrastructure.Ssh;

internal static class SshFailureClassifier
{
    public static RecoveryFailure Classify(
        SessionKey session,
        ConnectionEpoch epoch,
        SshTransportOutcome? transport = null,
        SshConnectionTestResult? test = null,
        HostKeyAssessment? hostKey = null,
        string? stderr = null)
    {
        _ = stderr;
        var mapped = Map(transport, test, hostKey);
        var scope = transport?.Channel == SshChannelKind.Terminal
            ? RecoveryScope.Terminal
            : RecoveryScope.Rpc;
        return RecoveryPolicy.Create(
            scope, mapped.Cause, session, null, epoch, false, mapped.RetryClass);
    }

    private static (RecoveryCause Cause, RecoveryRetryClass? RetryClass) Map(
        SshTransportOutcome? transport,
        SshConnectionTestResult? test,
        HostKeyAssessment? hostKey)
    {
        if (hostKey is not null)
        {
            if (hostKey.Status == HostKeyStatus.UnknownCandidate)
                return (RecoveryCause.HostKeyUnknown, RecoveryRetryClass.AwaitUser);
            if (hostKey.Status == HostKeyStatus.Changed)
                return (RecoveryCause.HostKeyChanged, RecoveryRetryClass.AwaitUser);
            if (hostKey.Status == HostKeyStatus.Unavailable)
                return (RecoveryCause.UnknownBlocked, RecoveryRetryClass.AwaitUser);
        }

        var testCode = test?.Code;
        var cause = FromCode(testCode);
        if (cause is not null)
            return (cause.Value, null);

        var transportCode = transport?.Code;
        cause = FromCode(transportCode);
        if (cause is not null)
        {
            var retry = cause.Value == RecoveryCause.TerminalClosed
                ? RecoveryRetryClass.AwaitUser
                : (RecoveryRetryClass?)null;
            return (cause.Value, retry);
        }

        if (transport is not null)
        {
            return transport.Disposition switch
            {
                SshRetryDisposition.RetryAfter when IsTransientTransport(transport.Code) =>
                    (RecoveryCause.TransientTransport, RecoveryRetryClass.Transient),
                SshRetryDisposition.RetryNow when IsTransientTransport(transport.Code) =>
                    (RecoveryCause.TransientNetwork, RecoveryRetryClass.Transient),
                SshRetryDisposition.HostKeyBlocked =>
                    (RecoveryCause.HostKeyChanged, RecoveryRetryClass.AwaitUser),
                SshRetryDisposition.AuthenticationBlocked =>
                    (RecoveryCause.AuthenticationBlocked, RecoveryRetryClass.AwaitUser),
                SshRetryDisposition.Stop =>
                    (RecoveryCause.Cancelled, RecoveryRetryClass.Stop),
                SshRetryDisposition.AwaitUser =>
                    (RecoveryCause.UnknownBlocked, RecoveryRetryClass.AwaitUser),
                _ => (RecoveryCause.UnknownBlocked, RecoveryRetryClass.AwaitUser)
            };
        }

        return (RecoveryCause.UnknownBlocked, RecoveryRetryClass.AwaitUser);
    }

    private static RecoveryCause? FromCode(string? code) =>
        code switch
        {
            SshCodes.HostKeyUnknown or SshTransportCodes.TrustRequired => RecoveryCause.HostKeyUnknown,
            SshCodes.HostKeyChanged => RecoveryCause.HostKeyChanged,
            SshCodes.AuthFailed => RecoveryCause.AuthenticationBlocked,
            SshCodes.AuthUnsupported => RecoveryCause.UnsupportedAuthentication,
            SshTransportCodes.StdoutProtocolPollution => RecoveryCause.ProtocolPollution,
            SshTransportCodes.SchemaIncompatible or SshTransportCodes.BridgeIncompatible =>
                RecoveryCause.Incompatible,
            SshTransportCodes.TransportCancelled => RecoveryCause.Cancelled,
            SshTransportCodes.TerminalClosed => RecoveryCause.TerminalClosed,
            SshTransportCodes.StreamTruncated or SshTransportCodes.ChildExited =>
                RecoveryCause.TransientTransport,
            SshTransportCodes.DaemonUnavailable => RecoveryCause.DaemonUnavailable,
            SshTransportCodes.UnclassifiedExit => RecoveryCause.UnknownBlocked,
            SshCodes.TestCancelled => RecoveryCause.Cancelled,
            SshCodes.ConfigInvalid or SshCodes.ProfileInvalid or SshCodes.ExecutableUnavailable
                or SshCodes.TestFailed or SshCodes.TestTimeout =>
                RecoveryCause.UnknownBlocked,
            _ => null
        };

    private static bool IsTransientTransport(string code) =>
        code is SshTransportCodes.StreamTruncated or SshTransportCodes.ChildExited;
}
