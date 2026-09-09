using System.Text.Json;
using HerdDesk.Contracts;

namespace HerdDesk.Terminal.Web;

public static class WebMessageCodec
{
    public static byte[] Initialize(ConnectionEpoch epoch, string theme, bool readOnly)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(theme);
        return Write(epoch, WebMessageKinds.Initialize, writer =>
        {
            writer.WriteString("theme", theme);
            writer.WriteBoolean("readOnly", readOnly);
        });
    }

    public static byte[] Frame(ConnectionEpoch epoch, ulong sequence, bool full, ReadOnlySpan<byte> bytes)
    {
        if (sequence == 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        if (bytes.IsEmpty)
            throw new ArgumentException("empty_frame", nameof(bytes));
        var encoded = Convert.ToBase64String(bytes);
        return Write(epoch, WebMessageKinds.Frame, writer =>
        {
            writer.WriteNumber("seq", sequence);
            writer.WriteBoolean("full", full);
            writer.WriteString("bytes", encoded);
        });
    }

    public static byte[] Focus(ConnectionEpoch epoch, string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return Write(epoch, WebMessageKinds.Focus, writer => writer.WriteString("token", token));
    }

    public static byte[] Dispose(ConnectionEpoch epoch) =>
        Write(epoch, WebMessageKinds.Dispose, null);

    public static byte[] Display(ConnectionEpoch epoch, string fontFamily, int fontSize, int zoomPercent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontFamily);
        return Write(epoch, WebMessageKinds.Display, writer =>
        {
            writer.WriteString("fontFamily", fontFamily);
            writer.WriteNumber("fontSize", fontSize);
            writer.WriteNumber("zoomPercent", zoomPercent);
        });
    }

    private static byte[] Write(
        ConnectionEpoch epoch,
        string kind,
        Action<Utf8JsonWriter>? extra)
    {
        if (epoch.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(epoch));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", WebMessageLimits.SchemaVersion);
            writer.WriteString("kind", kind);
            writer.WriteNumber("epoch", epoch.Value);
            extra?.Invoke(writer);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }
}
