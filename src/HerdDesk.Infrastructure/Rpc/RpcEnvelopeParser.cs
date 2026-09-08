using System.Text;
using System.Text.Json;
using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.Rpc;

public sealed class ParsedRpcEnvelope : IDisposable
{
    internal ParsedRpcEnvelope(
        JsonDocument document,
        ulong? id,
        bool hasId,
        bool hasResult,
        bool hasError)
    {
        Document = document;
        Id = id;
        HasId = hasId;
        HasResult = hasResult;
        HasError = hasError;
    }

    public JsonDocument Document { get; }
    public ulong? Id { get; }
    public bool HasId { get; }
    public bool HasResult { get; }
    public bool HasError { get; }
    public bool IsResponse => HasId && HasResult ^ HasError;

    public void Dispose() => Document.Dispose();
}

public static class RpcEnvelopeParser
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        MaxDepth = 64,
        AllowDuplicateProperties = false
    };

    public static ParsedRpcEnvelope Parse(ReadOnlyMemory<byte> line)
    {
        try
        {
            if (line.Span.StartsWith(Utf8Bom))
                throw new RpcProtocolException(RpcCodes.EnvelopeInvalid);
            _ = StrictUtf8.GetCharCount(line.Span);
            var document = JsonDocument.Parse(line, ParseOptions);
            try
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new RpcProtocolException(RpcCodes.EnvelopeInvalid);
                CheckDuplicateKeys(root);
                var hasId = false;
                ulong? id = null;
                if (root.TryGetProperty("id", out var idElement) &&
                    idElement.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
                {
                    hasId = true;
                    id = ReadId(idElement);
                }
                var hasResult = root.TryGetProperty("result", out _);
                var hasError = root.TryGetProperty("error", out _);
                if (hasResult && hasError)
                    throw new RpcProtocolException(RpcCodes.EnvelopeInvalid);
                return new ParsedRpcEnvelope(document, id, hasId, hasResult, hasError);
            }
            catch
            {
                document.Dispose();
                throw;
            }
        }
        catch (RpcProtocolException)
        {
            throw;
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException or FormatException
            or InvalidOperationException)
        {
            throw new RpcProtocolException(RpcCodes.EnvelopeInvalid);
        }
    }

    private static ulong ReadId(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            if (!element.TryGetUInt64(out var value) || value == 0)
                throw new RpcProtocolException(RpcCodes.EnvelopeInvalid);
            return value;
        }

        if (element.ValueKind != JsonValueKind.String)
            throw new RpcProtocolException(RpcCodes.EnvelopeInvalid);
        var text = GetString(element);
        if (!ulong.TryParse(text, out var parsed) || parsed == 0)
            throw new RpcProtocolException(RpcCodes.EnvelopeInvalid);
        return parsed;
    }

    private static void CheckDuplicateKeys(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                string name;
                try
                {
                    name = property.Name;
                }
                catch (InvalidOperationException)
                {
                    throw new RpcProtocolException(RpcCodes.EnvelopeInvalid);
                }
                if (!names.Add(name))
                    throw new RpcProtocolException(RpcCodes.DuplicateJsonKey);
                CheckDuplicateKeys(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in value.EnumerateArray())
                CheckDuplicateKeys(child);
        }
    }

    private static string GetString(JsonElement element)
    {
        try
        {
            return element.GetString() ?? throw new RpcProtocolException(RpcCodes.EnvelopeInvalid);
        }
        catch (InvalidOperationException)
        {
            throw new RpcProtocolException(RpcCodes.EnvelopeInvalid);
        }
    }
}
