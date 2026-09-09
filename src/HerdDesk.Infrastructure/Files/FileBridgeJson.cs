using System.Globalization;
using System.Text;
using System.Text.Json;

namespace HerdDesk.Infrastructure.Files;

internal static class FileBridgeJson
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];
    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        MaxDepth = 32,
        AllowDuplicateProperties = false
    };
    private static readonly JsonReaderOptions ReaderOptions = new()
    {
        MaxDepth = 32,
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false
    };

    public static JsonDocument ParseObject(ReadOnlySpan<byte> utf8)
    {
        if (utf8.StartsWith(Bom))
            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidJson);
        try
        {
            _ = StrictUtf8.GetCharCount(utf8);
        }
        catch (DecoderFallbackException)
        {
            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidJson);
        }
        Scan(utf8);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8.ToArray(), ParseOptions);
        }
        catch (JsonException)
        {
            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidJson);
        }
        try
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new FileBridgeProtocolException(FileBridgeCodes.InvalidFieldType);
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    static void Scan(ReadOnlySpan<byte> utf8)
    {
        var reader = new Utf8JsonReader(utf8, ReaderOptions);
        var depth = 0;
        var stack = new Stack<HashSet<string>>();
        try
        {
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                        depth++;
                        if (depth > 32)
                            throw new FileBridgeProtocolException(FileBridgeCodes.JsonDepthLimit);
                        if (reader.TokenType == JsonTokenType.StartObject)
                            stack.Push(new HashSet<string>(StringComparer.Ordinal));
                        break;
                    case JsonTokenType.EndObject:
                        if (stack.Count == 0)
                            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidJson);
                        stack.Pop();
                        depth--;
                        break;
                    case JsonTokenType.EndArray:
                        depth--;
                        break;
                    case JsonTokenType.PropertyName:
                        if (stack.Count == 0)
                            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidJson);
                        string name;
                        try
                        {
                            name = reader.GetString() ?? throw new FileBridgeProtocolException(FileBridgeCodes.InvalidJson);
                        }
                        catch (InvalidOperationException)
                        {
                            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidJson);
                        }
                        if (!stack.Peek().Add(name))
                            throw new FileBridgeProtocolException(FileBridgeCodes.DuplicateJsonKey);
                        break;
                    case JsonTokenType.Number:
                        var raw = Encoding.UTF8.GetString(reader.ValueSpan);
                        if (raw.Contains('.') || raw.Contains('e') || raw.Contains('E'))
                            throw new FileBridgeProtocolException(FileBridgeCodes.JsonFloatRejected);
                        if (raw == "-0" || (raw.Length > 1 && raw[0] == '0') ||
                            (raw.Length > 2 && raw[0] == '-' && raw[1] == '0'))
                            throw new FileBridgeProtocolException(FileBridgeCodes.JsonNumberInvalid);
                        if (!long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
                            throw new FileBridgeProtocolException(FileBridgeCodes.JsonNumberInvalid);
                        break;
                }
            }
        }
        catch (FileBridgeProtocolException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidJson);
        }
    }

    public static bool Has(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) &&
        value.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null;

    public static void RejectUnknown(JsonElement obj, params string[] allowed)
    {
        var set = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (var property in obj.EnumerateObject())
        {
            var name = FileBridgeText.GetName(property);
            if (!set.Contains(name))
                throw new FileBridgeProtocolException(FileBridgeCodes.UnknownField);
        }
    }

    public static string ReqString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value) ||
            value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new FileBridgeProtocolException(FileBridgeCodes.MissingField);
        return FileBridgeText.GetUtf16(value);
    }

    public static string? OptString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value) ||
            value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return null;
        return FileBridgeText.GetUtf16(value);
    }

    public static long ReqInt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value) ||
            value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new FileBridgeProtocolException(FileBridgeCodes.MissingField);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number))
            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidFieldType);
        return number;
    }

    public static bool ReqBool(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value) ||
            value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new FileBridgeProtocolException(FileBridgeCodes.MissingField);
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidFieldType);
        return value.GetBoolean();
    }
}
