namespace HerdDesk.Infrastructure.Files;

public static class WirePath
{
    public static IReadOnlyList<byte[]> Decode(string path)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '/' || path.Contains('\0'))
            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidWirePath);
        if (path == "/")
            return [];
        var rest = path[1..];
        var components = new List<byte[]>();
        var i = 0;
        while (i < rest.Length)
        {
            var found = -1;
            byte[]? decoded = null;
            for (var j = i + 1; j <= rest.Length; j++)
            {
                if (j != rest.Length && rest[j] != '/')
                    continue;
                var slice = rest[i..j];
                try
                {
                    decoded = FileBridgeText.CanonicalBase64(slice, FileBridgeCodes.InvalidWirePath);
                    found = j;
                    break;
                }
                catch (FileBridgeProtocolException)
                {
                }
            }
            if (found < 0 || decoded is null)
                throw new FileBridgeProtocolException(FileBridgeCodes.InvalidWirePath);
            CheckComponent(decoded);
            components.Add(decoded);
            if (found == rest.Length)
                break;
            i = found + 1;
            if (i == rest.Length)
                throw new FileBridgeProtocolException(FileBridgeCodes.InvalidWirePath);
        }
        return components;
    }

    public static byte[] DecodeComponent(string value)
    {
        var raw = FileBridgeText.CanonicalBase64(value, FileBridgeCodes.InvalidComponent);
        CheckComponent(raw);
        return raw;
    }

    public static void CheckComponent(ReadOnlySpan<byte> raw)
    {
        if (raw.Length == 0 || raw.SequenceEqual("."u8) || raw.SequenceEqual(".."u8) ||
            raw.Contains((byte)0) || raw.Contains((byte)'/'))
            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidComponent);
    }
}
