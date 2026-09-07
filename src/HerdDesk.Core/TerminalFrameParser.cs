using System.Text;
using System.Text.Json;
using HerdDesk.Contracts;

namespace HerdDesk.Core;

public sealed class TerminalProtocolException : Exception
{
    public TerminalProtocolException(string code) : base(code) { }
}

/// <summary>
/// A fail-closed parser for one upstream terminal-session connection/epoch.
/// Parse takes one complete JSON record without its LF. Framing and lifecycle
/// belong to the future transport. No terminal bytes are decoded as UTF-8.
/// </summary>
public sealed class TerminalFrameParser
{
    public const int MaxLineBytes = 16 * 1024 * 1024;
    public const int MaxFrameBytes = 8 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private ulong? lastSequence;
    private bool closed;
    private bool failed;

    public TerminalEnvelope Parse(ReadOnlyMemory<byte> json)
    {
        if (failed || closed)
            throw new TerminalProtocolException("terminal_stream_not_active");
        try
        {
            if (json.Length > MaxLineBytes)
                throw new TerminalProtocolException("line_bytes_limit");
            _ = StrictUtf8.GetCharCount(json.Span);
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new TerminalProtocolException("object_required");
            CheckDuplicateKeys(root);
            var type = GetString(root, "type");
            if (type == "terminal.closed")
            {
                var present = false;
                if (root.TryGetProperty("reason", out var reason))
                {
                    if (reason.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                        throw new TerminalProtocolException("invalid_closed_reason");
                    present = reason.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(reason.GetString());
                }
                closed = true;
                return new TerminalClosed(present);
            }
            if (type != "terminal.frame")
                throw new TerminalProtocolException("unknown_terminal_type");
            if (GetString(root, "encoding") != "ansi")
                throw new TerminalProtocolException("unsupported_encoding");
            if (!root.TryGetProperty("seq", out var sequenceElement) ||
                sequenceElement.ValueKind != JsonValueKind.Number ||
                !sequenceElement.TryGetUInt64(out var sequence) || sequence == 0)
                throw new TerminalProtocolException("invalid_sequence");
            if (lastSequence is { } previous &&
                (previous == ulong.MaxValue || sequence != previous + 1))
                throw new TerminalProtocolException("sequence_gap_or_replay");
            var columns = GetDimension(root, "width");
            var rows = GetDimension(root, "height");
            if (!root.TryGetProperty("full", out var fullElement) ||
                fullElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new TerminalProtocolException("boolean_full_required");
            var full = fullElement.GetBoolean();
            if (lastSequence is null && !full)
                throw new TerminalProtocolException("initial_full_frame_required");
            var encoded = GetString(root, "bytes");
            if (encoded.Length > 4 * ((MaxFrameBytes + 2) / 3))
                throw new TerminalProtocolException("decoded_bytes_limit");
            var bytes = Convert.FromBase64String(encoded);
            if (bytes.Length > MaxFrameBytes)
                throw new TerminalProtocolException("decoded_bytes_limit");
            if (Convert.ToBase64String(bytes) != encoded)
                throw new TerminalProtocolException("noncanonical_base64");
            // Change state only after every field is validated.
            lastSequence = sequence;
            return new TerminalFrame(sequence, columns, rows, full, bytes);
        }
        catch (TerminalProtocolException)
        {
            failed = true;
            throw;
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException or FormatException)
        {
            failed = true;
            // Do not expose JsonException details or attacker-controlled bytes.
            throw new TerminalProtocolException("malformed_terminal_record");
        }
    }

    private static string GetString(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var element) || element.ValueKind != JsonValueKind.String)
            throw new TerminalProtocolException("string_field_required");
        return element.GetString()!;
    }

    private static ushort GetDimension(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var element) || element.ValueKind != JsonValueKind.Number ||
            !element.TryGetUInt32(out var value) || value is 0 or > ushort.MaxValue)
            throw new TerminalProtocolException("invalid_frame_dimensions");
        return (ushort)value;
    }

    private static void CheckDuplicateKeys(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new TerminalProtocolException("duplicate_json_key");
                CheckDuplicateKeys(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in value.EnumerateArray()) CheckDuplicateKeys(child);
        }
    }
}
