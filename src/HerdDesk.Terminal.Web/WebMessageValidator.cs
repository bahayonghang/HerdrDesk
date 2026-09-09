using System.Text;
using System.Text.Json;
using HerdDesk.Contracts;

namespace HerdDesk.Terminal.Web;

public static class WebMessageValidator
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly HashSet<string> BaseFields = new(StringComparer.Ordinal)
    {
        "version", "kind", "epoch"
    };

    public static WebMessageValidation Evaluate(ReadOnlyMemory<byte> json, ConnectionEpoch boundEpoch)
    {
        if (boundEpoch.Value <= 0)
            return Reject("stale_epoch");
        if (json.Length is <= 0 or > WebMessageLimits.MaxJsonBytes)
            return Reject("web_message_bytes_limit");
        try
        {
            _ = StrictUtf8.GetCharCount(json.Span);
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                MaxDepth = 64,
                AllowDuplicateProperties = true
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return Reject("object_required");
            CheckDuplicateKeys(root);
            var kind = GetString(root, "kind");
            if (!root.TryGetProperty("version", out var versionElement) ||
                versionElement.ValueKind != JsonValueKind.Number ||
                !versionElement.TryGetInt32(out var version))
                return Reject("unsupported_web_message_version");
            if (version != WebMessageLimits.SchemaVersion)
                return Reject("unsupported_web_message_version");
            var epoch = GetEpoch(root);
            if (epoch != boundEpoch)
                return Reject("stale_epoch");
            return kind switch
            {
                WebMessageKinds.Initialize => Initialize(root, epoch),
                WebMessageKinds.Frame => Frame(root, epoch),
                WebMessageKinds.Focus => Control(root, epoch, WebMessageKinds.Focus,
                    WebMessageDirection.HostToRenderer, extra: "token"),
                WebMessageKinds.Dispose => Empty(root, epoch, WebMessageKinds.Dispose,
                    WebMessageDirection.HostToRenderer),
                WebMessageKinds.Display => Display(root, epoch),
                WebMessageKinds.Ready => Empty(root, epoch, WebMessageKinds.Ready,
                    WebMessageDirection.RendererToHost),
                WebMessageKinds.Parsed => Parsed(root, epoch),
                WebMessageKinds.Input => Input(root, epoch),
                WebMessageKinds.Resize => Resize(root, epoch),
                WebMessageKinds.LinkRequest => Link(root, epoch),
                WebMessageKinds.Fault => Fault(root, epoch),
                WebMessageKinds.Composition => Composition(root, epoch),
                WebMessageKinds.Key => Key(root, epoch),
                WebMessageKinds.PasteIntent => PasteIntent(root, epoch),
                WebMessageKinds.SelectionChanged => SelectionChanged(root, epoch),
                WebMessageKinds.MouseIntent => MouseIntent(root, epoch),
                _ => Reject("unknown_web_message_type"),
            };
        }
        catch (WebMessageException error)
        {
            return Reject(error.Code);
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException
            or EncoderFallbackException or FormatException)
        {
            return Reject("malformed_web_message");
        }
    }

    private static WebMessageValidation Initialize(JsonElement root, ConnectionEpoch epoch)
    {
        RequireFields(root, "theme", "readOnly");
        if (root.GetProperty("readOnly").ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new WebMessageException("malformed_web_message");
        _ = GetString(root, "theme");
        return Ok(WebMessageKinds.Initialize, epoch, WebMessageDirection.HostToRenderer, 0);
    }

    private static WebMessageValidation Frame(JsonElement root, ConnectionEpoch epoch)
    {
        RequireFields(root, "seq", "full", "bytes");
        var seq = GetUInt64(root, "seq");
        if (seq == 0)
            throw new WebMessageException("invalid_sequence");
        if (root.GetProperty("full").ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new WebMessageException("boolean_full_required");
        var full = root.GetProperty("full").GetBoolean();
        var bytes = DecodeBytes(root, WebMessageLimits.MaxFrameBytes);
        return Ok(WebMessageKinds.Frame, epoch, WebMessageDirection.HostToRenderer, bytes.Length,
            bytes: bytes, sequence: seq, full: full);
    }

    private static WebMessageValidation Parsed(JsonElement root, ConnectionEpoch epoch)
    {
        RequireFields(root, "seq", "bytesConsumed");
        var seq = GetUInt64(root, "seq");
        if (seq == 0)
            throw new WebMessageException("invalid_sequence");
        var consumed = GetInt32(root, "bytesConsumed");
        if (consumed < 0 || consumed > WebMessageLimits.MaxFrameBytes)
            throw new WebMessageException("web_message_bytes_limit");
        return Ok(WebMessageKinds.Parsed, epoch, WebMessageDirection.RendererToHost, 0, sequence: seq);
    }

    private static WebMessageValidation Input(JsonElement root, ConnectionEpoch epoch)
    {
        RequireFields(root, "origin", "bytes");
        var origin = ParseOrigin(GetString(root, "origin"));
        var bytes = DecodeBytes(root, WebMessageLimits.MaxInputBytes);
        return Ok(WebMessageKinds.Input, epoch, WebMessageDirection.RendererToHost, bytes.Length,
            origin: origin, bytes: bytes);
    }

    private static WebMessageValidation Resize(JsonElement root, ConnectionEpoch epoch)
    {
        RequireFields(root, "cols", "rows", "cellPx");
        var cols = GetDimension(root, "cols");
        var rows = GetDimension(root, "rows");
        if (!root.TryGetProperty("cellPx", out var cell) || cell.ValueKind != JsonValueKind.Array)
            throw new WebMessageException("malformed_web_message");
        if (cell.GetArrayLength() != 2)
            throw new WebMessageException("malformed_web_message");
        var width = GetArrayUInt32(cell, 0);
        var height = GetArrayUInt32(cell, 1);
        return new WebMessageValidation(
            true, "allowed", WebMessageKinds.Resize, WebMessageLimits.SchemaVersion, epoch,
            WebMessageDirection.RendererToHost, 0, Columns: cols, Rows: rows,
            CellWidthPx: width, CellHeightPx: height);
    }

    private static WebMessageValidation Link(JsonElement root, ConnectionEpoch epoch)
    {
        RequireFields(root, "uri", "userGesture");
        var uri = GetString(root, "uri");
        if (uri.Length is <= 0 or > WebMessageLimits.MaxLinkUriChars)
            throw new WebMessageException("web_message_bytes_limit");
        if (root.GetProperty("userGesture").ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new WebMessageException("malformed_web_message");
        var gesture = root.GetProperty("userGesture").GetBoolean();
        return new WebMessageValidation(
            true, "allowed", WebMessageKinds.LinkRequest, WebMessageLimits.SchemaVersion, epoch,
            WebMessageDirection.RendererToHost, uri.Length, Uri: uri, UserGesture: gesture);
    }

    private static WebMessageValidation Display(JsonElement root, ConnectionEpoch epoch)
    {
        RequireFields(root, "fontFamily", "fontSize", "zoomPercent");
        _ = GetString(root, "fontFamily");
        _ = GetInt32(root, "fontSize");
        _ = GetInt32(root, "zoomPercent");
        return Ok(WebMessageKinds.Display, epoch, WebMessageDirection.HostToRenderer, 0);
    }

    private static WebMessageValidation Fault(JsonElement root, ConnectionEpoch epoch)
    {
        RequireFields(root, "code");
        var code = GetString(root, "code");
        if (!IsStableCode(code))
            throw new WebMessageException("malformed_web_message");
        return Ok(WebMessageKinds.Fault, epoch, WebMessageDirection.RendererToHost, 0, faultCode: code);
    }

    private static WebMessageValidation Composition(JsonElement root, ConnectionEpoch epoch)
    {
        var phase = GetString(root, "phase");
        if (phase is not ("start" or "update" or "end" or "cancel"))
            throw new WebMessageException("malformed_web_message");
        if (phase == "end")
            RequireFields(root, "phase", "token", "text");
        else
            RequireFields(root, "phase", "token");
        var token = GetString(root, "token");
        if (!IsStableCode(token))
            throw new WebMessageException("malformed_web_message");
        string? text = null;
        var payload = 0;
        if (phase == "end")
        {
            text = GetString(root, "text");
            payload = StrictUtf8.GetByteCount(text);
            if (payload > WebMessageLimits.MaxInputBytes)
                throw new WebMessageException("web_message_bytes_limit");
        }

        return new WebMessageValidation(
            true, "allowed", WebMessageKinds.Composition, WebMessageLimits.SchemaVersion, epoch,
            WebMessageDirection.RendererToHost, payload, Phase: phase, Token: token, Text: text);
    }

    private static WebMessageValidation Key(JsonElement root, ConnectionEpoch epoch)
    {
        RequireFields(root, "key", "ctrl", "shift", "alt", "altGr", "capsLock");
        var key = GetString(root, "key");
        if (key.Length is <= 0 or > 32)
            throw new WebMessageException("malformed_web_message");
        return new WebMessageValidation(
            true, "allowed", WebMessageKinds.Key, WebMessageLimits.SchemaVersion, epoch,
            WebMessageDirection.RendererToHost, 0, KeyName: key, Ctrl: GetBoolean(root, "ctrl"),
            Shift: GetBoolean(root, "shift"), Alt: GetBoolean(root, "alt"),
            AltGr: GetBoolean(root, "altGr"), CapsLock: GetBoolean(root, "capsLock"));
    }

    private static WebMessageValidation PasteIntent(JsonElement root, ConnectionEpoch epoch)
    {
        RequireFields(root, "text");
        var text = GetString(root, "text");
        var payload = StrictUtf8.GetByteCount(text);
        if (payload > WebMessageLimits.MaxInputBytes)
            throw new WebMessageException("web_message_bytes_limit");
        return new WebMessageValidation(
            true, "allowed", WebMessageKinds.PasteIntent, WebMessageLimits.SchemaVersion, epoch,
            WebMessageDirection.RendererToHost, payload, Text: text);
    }

    private static WebMessageValidation SelectionChanged(JsonElement root, ConnectionEpoch epoch)
    {
        RequireFields(root, "visibleText", "shift");
        var text = GetString(root, "visibleText");
        if (text.Length > WebMessageLimits.MaxInputBytes)
            throw new WebMessageException("web_message_bytes_limit");
        return new WebMessageValidation(
            true, "allowed", WebMessageKinds.SelectionChanged, WebMessageLimits.SchemaVersion, epoch,
            WebMessageDirection.RendererToHost, 0, VisibleText: text, Shift: GetBoolean(root, "shift"));
    }

    private static WebMessageValidation MouseIntent(JsonElement root, ConnectionEpoch epoch)
    {
        RequireFields(root, "action", "delta", "shift");
        var action = GetString(root, "action");
        if (action is not ("scroll" or "drag"))
            throw new WebMessageException("malformed_web_message");
        return new WebMessageValidation(
            true, "allowed", WebMessageKinds.MouseIntent, WebMessageLimits.SchemaVersion, epoch,
            WebMessageDirection.RendererToHost, 0, MouseAction: action,
            Delta: GetInt32(root, "delta"), Shift: GetBoolean(root, "shift"));
    }

    private static bool GetBoolean(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) ||
            element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new WebMessageException("malformed_web_message");
        return element.GetBoolean();
    }

    private static WebMessageValidation Control(
        JsonElement root, ConnectionEpoch epoch, string kind, WebMessageDirection direction, string extra)
    {
        RequireFields(root, extra);
        _ = GetString(root, extra);
        return Ok(kind, epoch, direction, 0);
    }

    private static WebMessageValidation Empty(
        JsonElement root, ConnectionEpoch epoch, string kind, WebMessageDirection direction)
    {
        RequireFields(root);
        return Ok(kind, epoch, direction, 0);
    }

    private static WebMessageValidation Ok(
        string kind,
        ConnectionEpoch epoch,
        WebMessageDirection direction,
        int payloadBytes,
        InputOrigin? origin = null,
        ReadOnlyMemory<byte> bytes = default,
        ulong? sequence = null,
        bool? full = null,
        string? faultCode = null) =>
        new(true, "allowed", kind, WebMessageLimits.SchemaVersion, epoch, direction, payloadBytes,
            origin, bytes, sequence, full, FaultCode: faultCode);

    private static WebMessageValidation Reject(string code) =>
        new(false, code, "", 0, new ConnectionEpoch(0), WebMessageDirection.RendererToHost, 0);

    private static void RequireFields(JsonElement root, params string[] extra)
    {
        var allowed = new HashSet<string>(BaseFields, StringComparer.Ordinal);
        foreach (var name in extra)
            allowed.Add(name);
        foreach (var property in root.EnumerateObject())
        {
            if (!allowed.Contains(GetName(property)))
                throw new WebMessageException("unknown_web_message_field");
        }

        foreach (var name in extra)
        {
            if (!root.TryGetProperty(name, out _))
                throw new WebMessageException("malformed_web_message");
        }
    }

    private static byte[] DecodeBytes(JsonElement root, int maxBytes)
    {
        var encoded = GetString(root, "bytes");
        if (encoded.Length > 4 * ((maxBytes + 2) / 3))
            throw new WebMessageException("web_message_bytes_limit");
        var bytes = Convert.FromBase64String(encoded);
        if (bytes.Length <= 0 || bytes.Length > maxBytes)
            throw new WebMessageException("web_message_bytes_limit");
        if (Convert.ToBase64String(bytes) != encoded)
            throw new WebMessageException("noncanonical_base64");
        return bytes;
    }

    private static ConnectionEpoch GetEpoch(JsonElement root)
    {
        if (!root.TryGetProperty("epoch", out var element) ||
            element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt64(out var value) || value <= 0)
            throw new WebMessageException("stale_epoch");
        return new ConnectionEpoch(value);
    }

    private static ulong GetUInt64(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) ||
            element.ValueKind != JsonValueKind.Number ||
            !element.TryGetUInt64(out var value))
            throw new WebMessageException("malformed_web_message");
        return value;
    }

    private static int GetInt32(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) ||
            element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt32(out var value))
            throw new WebMessageException("malformed_web_message");
        return value;
    }

    private static uint GetArrayUInt32(JsonElement array, int index)
    {
        var element = array[index];
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetUInt32(out var value))
            throw new WebMessageException("malformed_web_message");
        return value;
    }

    private static ushort GetDimension(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) ||
            element.ValueKind != JsonValueKind.Number ||
            !element.TryGetUInt32(out var value) || value is 0 or > ushort.MaxValue)
            throw new WebMessageException("invalid_frame_dimensions");
        return (ushort)value;
    }

    private static string GetString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
            throw new WebMessageException("string_field_required");
        return GetString(element);
    }

    private static string GetString(JsonElement element)
    {
        try
        {
            return element.GetString()!;
        }
        catch (InvalidOperationException)
        {
            throw new WebMessageException("malformed_web_message");
        }
    }

    private static string GetName(JsonProperty property)
    {
        try
        {
            return property.Name;
        }
        catch (InvalidOperationException)
        {
            throw new WebMessageException("malformed_web_message");
        }
    }

    private static bool IsStableCode(string code)
    {
        if (code.Length is <= 0 or > 64)
            return false;
        if (code[0] is < 'a' or > 'z')
            return false;
        foreach (var ch in code)
        {
            if (ch is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '_')
                return false;
        }

        return true;
    }

    private static InputOrigin ParseOrigin(string value) => value switch
    {
        "user_key" => InputOrigin.UserKey,
        "committed_text" => InputOrigin.CommittedText,
        "explicit_paste" => InputOrigin.ExplicitPaste,
        "emulator_reply" => InputOrigin.EmulatorReply,
        _ => throw new WebMessageException("input_origin_denied"),
    };

    private static void CheckDuplicateKeys(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(GetName(property)))
                    throw new WebMessageException("duplicate_json_key");
                CheckDuplicateKeys(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in value.EnumerateArray())
                CheckDuplicateKeys(child);
        }
    }

    private sealed class WebMessageException(string code) : Exception(code)
    {
        public string Code { get; } = code;
    }
}
