using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.Terminal;

internal enum TerminalEndReason
{
    None = 0,
    StdoutEof,
    TruncatedEof,
    ClosedThenEof,
    ProtocolFailed,
    ProcessExited,
    ConsumerBackpressure,
    Cancelled,
    AppStopping,
    Released
}

internal static class TerminalExitClassifier
{
    public static string Code(TerminalEndReason reason, string? protocolCode = null) => reason switch
    {
        TerminalEndReason.StdoutEof => TerminalTransportCodes.StdoutEnded,
        TerminalEndReason.TruncatedEof => TerminalTransportCodes.TruncatedRecord,
        TerminalEndReason.ClosedThenEof => TerminalTransportCodes.Closed,
        TerminalEndReason.ProtocolFailed => protocolCode ?? TerminalTransportCodes.ProtocolFailed,
        TerminalEndReason.ProcessExited => TerminalTransportCodes.ProcessExited,
        TerminalEndReason.ConsumerBackpressure => TerminalTransportCodes.ConsumerBackpressure,
        TerminalEndReason.Cancelled => TerminalTransportCodes.Cancelled,
        TerminalEndReason.AppStopping => TerminalTransportCodes.AppStopping,
        TerminalEndReason.Released => TerminalTransportCodes.Closed,
        _ => TerminalTransportCodes.ConnectionLost
    };

}
