namespace HerdDesk.Contracts;

public enum TerminalWriteDisposition
{
    NotSent,
    WrittenUnacknowledged,
    UnknownAfterDisconnect
}

public sealed record TerminalWriteReceipt(
    ulong CommandId,
    TerminalWriteDisposition Disposition,
    string Code);
