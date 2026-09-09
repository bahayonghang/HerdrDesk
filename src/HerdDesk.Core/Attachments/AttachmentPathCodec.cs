using System.Text;
using HerdDesk.Contracts;

namespace HerdDesk.Core;

public static class AttachmentPathCodec
{
    public static bool IsWindowsOs(string? targetOs) =>
        !string.IsNullOrEmpty(targetOs)
        && targetOs.StartsWith("windows", StringComparison.OrdinalIgnoreCase);

    public static byte[] EncodeLocator(FileLocator path, string targetOs)
    {
        ArgumentNullException.ThrowIfNull(path);
        var windows = IsWindowsOs(targetOs);
        var sep = windows ? (byte)'\\' : (byte)'/';
        if (path.Components.Count == 0)
            return windows ? "\\"u8.ToArray() : "/"u8.ToArray();

        var length = windows ? 0 : 1;
        for (var i = 0; i < path.Components.Count; i++)
        {
            var raw = path.Components[i].Raw;
            if (ContainsSubmitKey(raw))
                throw new ArgumentException(AttachmentCodes.InvalidPath, nameof(path));
            if (i > 0)
                length++;
            length += raw.Length;
        }

        var bytes = new byte[length];
        var offset = 0;
        if (!windows)
            bytes[offset++] = sep;
        for (var i = 0; i < path.Components.Count; i++)
        {
            if (i > 0)
                bytes[offset++] = sep;
            var raw = path.Components[i].Raw;
            raw.CopyTo(bytes.AsSpan(offset));
            offset += raw.Length;
        }

        return bytes;
    }

    public static byte[] EncodeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Encoding.UTF8.GetBytes(text);
    }

    public static bool ContainsSubmitKey(ReadOnlySpan<byte> bytes)
    {
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] is 0x0A or 0x0D)
                return true;
        }

        return false;
    }

    public static ReadOnlyMemory<byte> WithoutExtraSubmit(ReadOnlyMemory<byte> payload) => payload;
}
