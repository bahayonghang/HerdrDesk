using System.Text;

namespace HerdDesk.Core;

/// <summary>
/// Incremental UTF-8 decoder for L1 text checks. Incomplete sequences stay
/// in the decoder. Do not call Encoding.UTF8.GetString per chunk.
/// </summary>
public sealed class Utf8ChunkAssembler
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly Decoder decoder = StrictUtf8.GetDecoder();
    private readonly StringBuilder text = new();
    private readonly byte[] lookback = new byte[4];
    private int lookbackLength;

    public string Text => text.ToString();
    public bool HeldIncomplete => IncompleteUtf8Tail(lookback.AsSpan(0, lookbackLength)) > 0;

    public void Append(ReadOnlySpan<byte> chunk)
    {
        if (chunk.IsEmpty)
            return;
        var remaining = chunk;
        var maxChars = StrictUtf8.GetMaxCharCount(chunk.Length + 3);
        Span<char> buffer = maxChars <= 128 ? stackalloc char[maxChars] : new char[maxChars];
        while (!remaining.IsEmpty)
        {
            decoder.Convert(remaining, buffer, flush: false, out var bytesUsed, out var charsUsed, out _);
            if (charsUsed > 0)
                text.Append(buffer[..charsUsed]);
            if (bytesUsed <= 0)
                break;
            remaining = remaining[bytesUsed..];
        }
        NoteBytes(chunk);
    }

    private void NoteBytes(ReadOnlySpan<byte> chunk)
    {
        if (chunk.Length >= lookback.Length)
        {
            chunk[^lookback.Length..].CopyTo(lookback);
            lookbackLength = lookback.Length;
            return;
        }
        var keep = Math.Min(lookbackLength, lookback.Length - chunk.Length);
        if (keep > 0 && keep < lookbackLength)
            lookback.AsSpan(lookbackLength - keep, keep).CopyTo(lookback);
        chunk.CopyTo(lookback.AsSpan(keep));
        lookbackLength = keep + chunk.Length;
    }

    private static int IncompleteUtf8Tail(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty)
            return 0;
        var max = Math.Min(4, buffer.Length);
        for (var i = 1; i <= max; i++)
        {
            var lead = buffer[^i];
            if ((lead & 0xC0) == 0x80)
                continue;
            var need = lead < 0x80 ? 1
                : (lead & 0xE0) == 0xC0 ? 2
                : (lead & 0xF0) == 0xE0 ? 3
                : (lead & 0xF8) == 0xF0 ? 4
                : 0;
            if (need == 0)
                return 0;
            return i < need ? i : 0;
        }
        return 0;
    }
}
