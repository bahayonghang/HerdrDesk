using System.Text;
using System.Text.Json;
using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.Files;

internal static class FileBridgeFrames
{
    static readonly UTF8Encoding Utf8 = new(false, true);

    public static byte[] Frame(FileBridgeKind kind, uint seq, ReadOnlySpan<byte> payload)
    {
        var header = FileBridgeKindCodec.EncodeHeader(kind, seq, (uint)payload.Length);
        var frame = new byte[header.Length + payload.Length];
        header.CopyTo(frame, 0);
        payload.CopyTo(frame.AsSpan(header.Length));
        return frame;
    }

    public static byte[] JsonObject(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { SkipValidation = false }))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    public static byte[] ListRequest(string job, string path, int limit, string? cursor)
    {
        return JsonObject(writer =>
        {
            writer.WriteString("protocol", "1.0");
            writer.WriteString("job", job);
            writer.WriteString("op", "list");
            writer.WriteString("path", path);
            writer.WriteNumber("limit", limit);
            if (cursor is not null)
                writer.WriteString("cursor", cursor);
        });
    }

    public static byte[] StatRequest(string job, string op, string path, string? observation)
    {
        return JsonObject(writer =>
        {
            writer.WriteString("protocol", "1.0");
            writer.WriteString("job", job);
            writer.WriteString("op", op);
            writer.WriteString("path", path);
            if (observation is not null)
                writer.WriteString("observation", observation);
        });
    }

    public static byte[] WriteRequest(
        string job,
        string parent,
        string name,
        string mode,
        string parentObservation,
        string? targetObservation,
        string length,
        string sha256)
    {
        return JsonObject(writer =>
        {
            writer.WriteString("protocol", "1.0");
            writer.WriteString("job", job);
            writer.WriteString("op", "write");
            writer.WriteString("parent", parent);
            writer.WriteString("name", name);
            writer.WriteString("mode", mode);
            writer.WriteString("expected_parent_observation", parentObservation);
            if (targetObservation is not null)
                writer.WriteString("expected_target_observation", targetObservation);
            writer.WriteString("length", length);
            writer.WriteString("sha256", sha256);
        });
    }

    public static byte[] CancelRequest(string job) =>
        JsonObject(writer =>
        {
            writer.WriteString("job", job);
            writer.WriteString("reason", "user");
        });

    public static string Decimal(ulong value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static FileEntry ParseEntry(JsonElement root)
    {
        var name = WirePath.DecodeComponent(FileBridgeJson.ReqString(root, "name"));
        var display = FileBridgeJson.ReqString(root, "display_name");
        var type = FileBridgeJson.ReqString(root, "type");
        var kind = type switch
        {
            "file" => FileEntryKind.File,
            "directory" => FileEntryKind.Directory,
            "symlink" => FileEntryKind.Symlink,
            _ => FileEntryKind.Other
        };
        var size = FileBridgeText.RequireDecimalU64(FileBridgeJson.ReqString(root, "size"));
        var mtime = FileBridgeText.RequireDecimalU64(FileBridgeJson.ReqString(root, "mtime"));
        var precision = FileBridgeText.RequireDecimalU64(FileBridgeJson.ReqString(root, "mtime_precision"));
        var identity = FileBridgeText.BoundedBase64(
            FileBridgeJson.ReqString(root, "identity"),
            FileBridgeCodes.InvalidIdentity,
            FileBridgeLimits.MaxIdentity);
        var symlink = FileBridgeJson.ReqBool(root, "symlink");
        var observation = FileBridgeJson.ReqString(root, "observation");
        return new FileEntry(
            new FileComponent(name),
            display,
            kind,
            size,
            mtime,
            precision,
            new FileIdentity(identity),
            symlink,
            new FileObservation(observation));
    }

    public static string Utf8String(ReadOnlySpan<byte> payload) => Utf8.GetString(payload);
}
