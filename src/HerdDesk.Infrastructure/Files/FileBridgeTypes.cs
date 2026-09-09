namespace HerdDesk.Infrastructure.Files;

public enum FileBridgeKind : byte
{
    RequestJson = 0x01,
    Data = 0x02,
    EndData = 0x03,
    CancelJson = 0x04,
    AcceptedJson = 0x11,
    EntryJson = 0x12,
    ProgressJson = 0x13,
    CompleteJson = 0x14,
    ErrorJson = 0x7F
}

public enum FileBridgeDirection
{
    Client,
    Helper
}

public enum FileBridgeOp
{
    List,
    Stat,
    Read,
    Write,
    Rename
}

public enum FileBridgeMode
{
    Create,
    Replace
}

public enum FileBridgeOutcome
{
    Pending,
    Success,
    Failed,
    Cancelled,
    Unknown
}

public sealed class FileBridgeFrame
{
    public FileBridgeFrame(FileBridgeDirection direction, FileBridgeKind kind, uint sequence,
        byte[] payload)
    {
        Direction = direction;
        Kind = kind;
        Sequence = sequence;
        Payload = payload;
    }

    public FileBridgeDirection Direction { get; }
    public FileBridgeKind Kind { get; }
    public uint Sequence { get; }
    public byte[] Payload { get; }
}

public static class FileBridgeLimits
{
    public const int HeaderLength = 16;
    public const int MaxJson = 1024 * 1024;
    public const int MaxData = 1024 * 1024;
    public const long MaxControlTotal = 16L * 1024 * 1024;
    public const int MaxCursor = 4096;
    public const int MaxIdentity = 4096;
    public const byte Major = 1;
    public const byte Minor = 0;
    public static readonly byte[] Magic = "HDFB"u8.ToArray();
}
