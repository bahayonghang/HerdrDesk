using HerdDesk.Contracts;

namespace HerdDesk.Terminal.Web;

public sealed record WebMessageValidation(
    bool Accepted,
    string Code,
    string Kind,
    int Version,
    ConnectionEpoch Epoch,
    WebMessageDirection Direction,
    int PayloadBytes,
    InputOrigin? Origin = null,
    ReadOnlyMemory<byte> Bytes = default,
    ulong? Sequence = null,
    bool? Full = null,
    string? Uri = null,
    bool UserGesture = false,
    ushort? Columns = null,
    ushort? Rows = null,
    uint? CellWidthPx = null,
    uint? CellHeightPx = null,
    string? FaultCode = null);
