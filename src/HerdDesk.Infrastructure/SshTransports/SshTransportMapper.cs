using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.SshTransports;

internal static class SshTransportMapper
{
    public static SshRetryDisposition Disposition(string code) => code switch
    {
        SshTransportCodes.StdoutProtocolPollution or SshTransportCodes.RecordTooLarge
            or SshTransportCodes.SchemaIncompatible or SshTransportCodes.BridgeIncompatible
            or SshTransportCodes.TrustRequired or SshTransportCodes.TerminalClosed =>
            SshRetryDisposition.AwaitUser,
        SshCodes.HostKeyChanged or SshCodes.HostKeyUnknown => SshRetryDisposition.HostKeyBlocked,
        SshCodes.AuthFailed or SshCodes.AuthUnsupported => SshRetryDisposition.AuthenticationBlocked,
        SshTransportCodes.TransportCancelled => SshRetryDisposition.Stop,
        SshTransportCodes.StreamTruncated or SshTransportCodes.ChildExited
            or SshTransportCodes.DaemonUnavailable =>
            SshRetryDisposition.RetryAfter,
        _ => SshRetryDisposition.UnknownBlocked
    };

    public static string FromRpc(string? code)
    {
        return code switch
        {
            RpcCodes.ProtocolPollution or RpcCodes.EnvelopeInvalid or RpcCodes.DuplicateJsonKey =>
                SshTransportCodes.StdoutProtocolPollution,
            RpcCodes.LineBytesLimit => SshTransportCodes.RecordTooLarge,
            RpcCodes.TruncatedRecord => SshTransportCodes.StreamTruncated,
            RpcCodes.ChildExited => SshTransportCodes.ChildExited,
            RpcCodes.Unavailable or RpcCodes.ConnectFailed or RpcCodes.ConnectionLost
                or RpcCodes.RequestLost or RpcCodes.SubscriptionLost =>
                SshTransportCodes.DaemonUnavailable,
            RpcCodes.CancelledAfterWrite => SshTransportCodes.TransportCancelled,
            ProjectionCodes.SchemaIncompatible => SshTransportCodes.SchemaIncompatible,
            _ => string.IsNullOrEmpty(code) ? SshTransportCodes.DaemonUnavailable : code
        };
    }

    public static string FromTerminal(string? code)
    {
        return code switch
        {
            TerminalTransportCodes.Malformed or TerminalTransportCodes.ProtocolFailed =>
                SshTransportCodes.StdoutProtocolPollution,
            TerminalTransportCodes.LineBytesLimit => SshTransportCodes.RecordTooLarge,
            TerminalTransportCodes.TruncatedRecord => SshTransportCodes.StreamTruncated,
            TerminalTransportCodes.Closed => SshTransportCodes.TerminalClosed,
            TerminalTransportCodes.Cancelled or TerminalTransportCodes.AppStopping =>
                SshTransportCodes.TransportCancelled,
            TerminalTransportCodes.ProcessExited or TerminalTransportCodes.StdoutEnded
                or TerminalTransportCodes.ConnectionLost =>
                SshTransportCodes.ChildExited,
            _ => string.IsNullOrEmpty(code) ? SshTransportCodes.ChildExited : code
        };
    }

    public static SshTransportOutcome Outcome(
        SshChannelKind channel,
        SshTransportStage stage,
        ConnectionEpoch epoch,
        string code,
        long durationMs,
        long stdoutBytes,
        long stderrBytes,
        int? exitCode,
        int? childProcessId) =>
        new(
            channel,
            stage,
            epoch,
            code,
            Disposition(code),
            durationMs,
            stdoutBytes,
            stderrBytes,
            exitCode,
            childProcessId);
}
