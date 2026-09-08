using System.Text;
using System.Text.Json;
using HerdDesk.Contracts;
using HerdDesk.Core;

namespace HerdDesk.Infrastructure.Terminal;

internal static class TerminalCommandSerializer
{
    public static string? TrySerializeInput(TerminalInputCommand input, out byte[] payload)
    {
        payload = [];
        var hasText = input.Text is not null;
        var hasBytes = input.Bytes is not null;
        if (hasText == hasBytes)
            return TerminalTransportCodes.ExactlyOnePayload;
        byte[] data;
        if (hasText)
        {
            try
            {
                data = new UTF8Encoding(false, true).GetBytes(input.Text!);
            }
            catch (EncoderFallbackException)
            {
                return TerminalTransportCodes.Malformed;
            }
        }
        else
        {
            data = input.Bytes!;
        }

        if (data.Length == 0)
            return TerminalTransportCodes.EmptyInput;
        if (data.Length > InputPolicy.MaxInputBytes)
            return TerminalTransportCodes.InputBytesLimit;

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "terminal.input");
            if (hasText)
                writer.WriteString("text", input.Text);
            else
                writer.WriteString("bytes", Convert.ToBase64String(data));
            writer.WriteEndObject();
        }

        buffer.WriteByte((byte)'\n');
        payload = buffer.ToArray();
        return null;
    }

    public static string? TrySerializeResize(TerminalResizeCommand size, out byte[] payload)
    {
        payload = [];
        if (size.Columns == 0 || size.Rows == 0)
            return TerminalTransportCodes.InvalidResize;

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "terminal.resize");
            writer.WriteNumber("cols", size.Columns);
            writer.WriteNumber("rows", size.Rows);
            writer.WriteNumber("cell_width_px", size.CellWidthPx);
            writer.WriteNumber("cell_height_px", size.CellHeightPx);
            writer.WriteEndObject();
        }

        buffer.WriteByte((byte)'\n');
        payload = buffer.ToArray();
        return null;
    }

    public static string? TrySerializeScroll(TerminalScrollCommand request, out byte[] payload)
    {
        payload = [];
        if (request.Direction is not ("up" or "down"))
            return TerminalTransportCodes.InvalidScrollDirection;
        if (request.Lines == 0)
            return TerminalTransportCodes.InvalidScrollLines;
        if (request.Source is not ("wheel" or "page_key"))
            return TerminalTransportCodes.InvalidScrollSource;

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "terminal.scroll");
            writer.WriteString("direction", request.Direction);
            writer.WriteNumber("lines", request.Lines);
            writer.WriteString("source", request.Source);
            if (request.Column is { } column)
                writer.WriteNumber("column", column);
            if (request.Row is { } row)
                writer.WriteNumber("row", row);
            writer.WriteNumber("modifiers", request.Modifiers);
            writer.WriteEndObject();
        }

        buffer.WriteByte((byte)'\n');
        payload = buffer.ToArray();
        return null;
    }

    public static byte[] SerializeRelease()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "terminal.release");
            writer.WriteEndObject();
        }

        buffer.WriteByte((byte)'\n');
        return buffer.ToArray();
    }
}
