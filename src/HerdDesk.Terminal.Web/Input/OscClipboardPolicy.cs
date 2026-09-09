using HerdDesk.Contracts;

namespace HerdDesk.Terminal.Web;

public static class OscClipboardPolicy
{
    public static OscClipboardDecision Evaluate(
        ReadOnlySpan<byte> terminalOutput,
        IClipboardSnapshotReader? snapshotReader = null)
    {
        _ = snapshotReader;
        var kind = FindOsc52(terminalOutput);
        return kind switch
        {
            OscClipboardKind.Read => new OscClipboardDecision(
                OscClipboardKind.Read, false, ClipboardCodes.ClipboardReadDenied),
            OscClipboardKind.Write => new OscClipboardDecision(
                OscClipboardKind.Write, false, ClipboardCodes.ClipboardWriteDenied),
            _ => new OscClipboardDecision(OscClipboardKind.None, true, ClipboardCodes.Allowed)
        };
    }

    static OscClipboardKind FindOsc52(ReadOnlySpan<byte> bytes)
    {
        for (var i = 0; i < bytes.Length; i++)
        {
            int start;
            if (bytes[i] == 0x1B && i + 1 < bytes.Length && bytes[i + 1] == 0x5D)
                start = i + 2;
            else if (bytes[i] == 0x9D)
                start = i + 1;
            else
                continue;

            var j = start;
            while (j < bytes.Length && bytes[j] == 0x20)
                j++;
            if (j + 1 >= bytes.Length || bytes[j] != (byte)'5' || bytes[j + 1] != (byte)'2')
                continue;
            j += 2;
            while (j < bytes.Length && bytes[j] == 0x20)
                j++;
            if (j >= bytes.Length || bytes[j] != (byte)';')
                continue;

            j++;
            var pdStart = j;
            var semi = -1;
            while (j < bytes.Length && !IsOscTerminator(bytes, j, out _))
            {
                if (bytes[j] == (byte)';' && semi < 0)
                    semi = j;
                j++;
            }

            if (semi >= 0)
                pdStart = semi + 1;
            var pdEnd = j;
            while (pdStart < pdEnd && bytes[pdStart] == 0x20)
                pdStart++;
            while (pdEnd > pdStart && bytes[pdEnd - 1] == 0x20)
                pdEnd--;
            if (pdEnd == pdStart + 1 && bytes[pdStart] == (byte)'?')
                return OscClipboardKind.Read;
            return OscClipboardKind.Write;
        }

        return OscClipboardKind.None;
    }

    static bool IsOscTerminator(ReadOnlySpan<byte> bytes, int index, out int length)
    {
        if (bytes[index] is 0x07 or 0x9C)
        {
            length = 1;
            return true;
        }

        if (bytes[index] == 0x1B && index + 1 < bytes.Length && bytes[index + 1] == 0x5C)
        {
            length = 2;
            return true;
        }

        length = 0;
        return false;
    }
}
