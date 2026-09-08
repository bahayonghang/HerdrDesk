namespace HerdDesk.Contracts;

public sealed record TerminalInputCommand(
    PaneKey Pane,
    ConnectionEpoch Epoch,
    string? Text = null,
    byte[]? Bytes = null);

public sealed record TerminalResizeCommand(
    PaneKey Pane,
    ConnectionEpoch Epoch,
    ushort Columns,
    ushort Rows,
    uint CellWidthPx,
    uint CellHeightPx);

public sealed record TerminalScrollCommand(
    PaneKey Pane,
    ConnectionEpoch Epoch,
    string Direction,
    ushort Lines,
    string Source,
    ushort? Column = null,
    ushort? Row = null,
    byte Modifiers = 0);
