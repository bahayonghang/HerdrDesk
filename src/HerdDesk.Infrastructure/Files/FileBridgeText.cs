using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;

namespace HerdDesk.Infrastructure.Files;

internal static class FileBridgeText
{
    public static void RequireUuid(string value)
    {
        if (value.Length != 36)
            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidJob);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (i is 8 or 13 or 18 or 23)
            {
                if (ch != '-')
                    throw new FileBridgeProtocolException(FileBridgeCodes.InvalidJob);
                continue;
            }
            if (ch is >= 'A' and <= 'F' || !Uri.IsHexDigit(ch))
                throw new FileBridgeProtocolException(FileBridgeCodes.InvalidJob);
        }
    }

    public static void RequireHex64(string value, string code)
    {
        if (value.Length != 64)
            throw new FileBridgeProtocolException(code);
        foreach (var ch in value)
        {
            if (ch is (>= '0' and <= '9') or (>= 'a' and <= 'f'))
                continue;
            throw new FileBridgeProtocolException(code);
        }
    }

    public static ulong RequireDecimalU64(string value)
    {
        if (value.Length == 0)
            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidLength);
        if (value == "0")
            return 0;
        if (value[0] == '0')
            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidLength);
        foreach (var ch in value)
        {
            if (ch is < '0' or > '9')
                throw new FileBridgeProtocolException(FileBridgeCodes.InvalidLength);
        }
        if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidLength);
        return parsed;
    }

    public static byte[] CanonicalBase64(string value, string invalid)
    {
        if (value.Length == 0 || value.Length % 4 != 0)
            throw new FileBridgeProtocolException(invalid);
        try
        {
            var raw = Convert.FromBase64String(value);
            if (Convert.ToBase64String(raw) != value)
                throw new FileBridgeProtocolException(invalid);
            return raw;
        }
        catch (FormatException)
        {
            throw new FileBridgeProtocolException(invalid);
        }
    }

    public static byte[] BoundedBase64(string value, string invalid, int max)
    {
        var raw = CanonicalBase64(value, invalid);
        if (raw.Length == 0 || raw.Length > max)
            throw new FileBridgeProtocolException(invalid);
        return raw;
    }

    public static bool ErrorCodeSyntax(string value)
    {
        if (value.Length is 0 or > 64)
            return false;
        if (value[0] is < 'a' or > 'z')
            return false;
        foreach (var ch in value)
        {
            if (ch is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_')
                continue;
            return false;
        }
        return true;
    }

    public static string GetUtf16(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String)
            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidFieldType);
        try
        {
            return element.GetString() ?? throw new FileBridgeProtocolException(FileBridgeCodes.InvalidFieldType);
        }
        catch (InvalidOperationException)
        {
            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidJson);
        }
    }

    public static string GetName(JsonProperty property)
    {
        try
        {
            return property.Name;
        }
        catch (InvalidOperationException)
        {
            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidJson);
        }
    }
}

internal static class FileBridgeKindCodec
{
    public static FileBridgeKind Parse(byte value) =>
        value switch
        {
            0x01 => FileBridgeKind.RequestJson,
            0x02 => FileBridgeKind.Data,
            0x03 => FileBridgeKind.EndData,
            0x04 => FileBridgeKind.CancelJson,
            0x11 => FileBridgeKind.AcceptedJson,
            0x12 => FileBridgeKind.EntryJson,
            0x13 => FileBridgeKind.ProgressJson,
            0x14 => FileBridgeKind.CompleteJson,
            0x7F => FileBridgeKind.ErrorJson,
            _ => throw new FileBridgeProtocolException(FileBridgeCodes.UnknownKind)
        };

    public static bool IsJson(FileBridgeKind kind) =>
        kind is not FileBridgeKind.Data and not FileBridgeKind.EndData;

    public static int MaxPayload(FileBridgeKind kind) =>
        kind switch
        {
            FileBridgeKind.EndData => 0,
            FileBridgeKind.Data => FileBridgeLimits.MaxData,
            _ => FileBridgeLimits.MaxJson
        };

    public static FileBridgeDirection DirectionOf(FileBridgeKind kind, FileBridgeOp? op)
    {
        switch (kind)
        {
            case FileBridgeKind.RequestJson:
            case FileBridgeKind.EndData:
            case FileBridgeKind.CancelJson:
                return FileBridgeDirection.Client;
            case FileBridgeKind.AcceptedJson:
            case FileBridgeKind.EntryJson:
            case FileBridgeKind.ProgressJson:
            case FileBridgeKind.CompleteJson:
            case FileBridgeKind.ErrorJson:
                return FileBridgeDirection.Helper;
            case FileBridgeKind.Data:
                if (op == FileBridgeOp.Write)
                    return FileBridgeDirection.Client;
                if (op == FileBridgeOp.Read)
                    return FileBridgeDirection.Helper;
                throw new FileBridgeProtocolException(FileBridgeCodes.UnexpectedKind);
            default:
                throw new FileBridgeProtocolException(FileBridgeCodes.UnknownKind);
        }
    }

    public static FileBridgeHeader ParseHeader(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != FileBridgeLimits.HeaderLength)
            throw new FileBridgeProtocolException(FileBridgeCodes.TruncatedHeader);
        if (!bytes[..4].SequenceEqual(FileBridgeLimits.Magic))
            throw new FileBridgeProtocolException(FileBridgeCodes.ProtocolPollution);
        if (bytes[4] != FileBridgeLimits.Major || bytes[5] != FileBridgeLimits.Minor)
            throw new FileBridgeProtocolException(FileBridgeCodes.UnsupportedVersion);
        if (bytes[7] != 0)
            throw new FileBridgeProtocolException(FileBridgeCodes.UnknownFlags);
        var kind = Parse(bytes[6]);
        var payloadLen = BinaryPrimitives.ReadUInt32BigEndian(bytes[8..12]);
        var seq = BinaryPrimitives.ReadUInt32BigEndian(bytes[12..16]);
        if (kind == FileBridgeKind.EndData)
        {
            if (payloadLen != 0)
                throw new FileBridgeProtocolException(FileBridgeCodes.InvalidPayloadLength);
        }
        else if (payloadLen > (uint)MaxPayload(kind))
            throw new FileBridgeProtocolException(FileBridgeCodes.PayloadTooLarge);
        return new FileBridgeHeader(kind, payloadLen, seq);
    }

    public static byte[] EncodeHeader(FileBridgeKind kind, uint seq, uint payloadLen)
    {
        if (kind == FileBridgeKind.EndData)
        {
            if (payloadLen != 0)
                throw new FileBridgeProtocolException(FileBridgeCodes.InvalidPayloadLength);
        }
        else if (payloadLen > (uint)MaxPayload(kind))
            throw new FileBridgeProtocolException(FileBridgeCodes.PayloadTooLarge);
        var header = new byte[FileBridgeLimits.HeaderLength];
        FileBridgeLimits.Magic.CopyTo(header, 0);
        header[4] = FileBridgeLimits.Major;
        header[5] = FileBridgeLimits.Minor;
        header[6] = (byte)kind;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8, 4), payloadLen);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12, 4), seq);
        return header;
    }
}

internal readonly struct FileBridgeHeader
{
    public FileBridgeHeader(FileBridgeKind kind, uint payloadLength, uint sequence)
    {
        Kind = kind;
        PayloadLength = payloadLength;
        Sequence = sequence;
    }

    public FileBridgeKind Kind { get; }
    public uint PayloadLength { get; }
    public uint Sequence { get; }
}
