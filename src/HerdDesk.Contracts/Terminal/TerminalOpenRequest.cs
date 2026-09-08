namespace HerdDesk.Contracts;

public enum TerminalMode
{
    Observe,
    Control
}

public sealed record TerminalTakeoverAuthorization(bool Confirmed, string ControlAttemptId);

public sealed record TerminalOwnershipFingerprint(
    string ExecutableSha256,
    TerminalMode Mode,
    string ControlAttemptId,
    string Marker,
    bool AdapterProvedWriteOwnership);

public sealed record TerminalOpenRequest(
    PaneKey Pane,
    ConnectionEpoch Epoch,
    TerminalMode Mode,
    string ExecutablePath,
    string Target,
    ushort Columns,
    ushort Rows,
    string? SessionName = null,
    string? ControlAttemptId = null,
    TerminalTakeoverAuthorization? Takeover = null);
