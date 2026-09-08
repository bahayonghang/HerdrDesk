using System.Text.Json;
using HerdDesk.Contracts;

namespace HerdDesk.App;

public sealed record ActivationIntent(
    ActivationKind Kind,
    DeviceId? Device = null,
    SessionKey? Session = null,
    string? WorkspaceId = null,
    PaneKey? Pane = null,
    ConnectionEpoch? Epoch = null)
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "kind", "device_id", "endpoint_key", "session_name", "workspace_id", "pane_id", "epoch"
    };

    private static readonly HashSet<string> Forbidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "command", "input", "takeover", "password", "argv", "bytes", "ansi", "payload"
    };

    public static bool TryParse(byte[] json, out ActivationIntent? intent, out string? code)
    {
        intent = null;
        code = ConfigurationCodes.Malformed;
        if (json.Length is 0 or > 4096)
            return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                MaxDepth = 8,
                AllowDuplicateProperties = false
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (Forbidden.Contains(property.Name))
                {
                    code = ConfigurationCodes.ForbiddenField;
                    return false;
                }

                if (!Allowed.Contains(property.Name) || !seen.Add(property.Name))
                    return false;
            }

            if (!root.TryGetProperty("kind", out var kindElement) || kindElement.ValueKind != JsonValueKind.String)
                return false;
            if (!TryKind(kindElement.GetString(), out var kind))
                return false;
            DeviceId? device = null;
            if (root.TryGetProperty("device_id", out var deviceElement))
            {
                if (deviceElement.ValueKind != JsonValueKind.String ||
                    !Guid.TryParse(deviceElement.GetString(), out var guid))
                    return false;
                device = new DeviceId(guid);
            }

            string? endpoint = null;
            string? sessionName = null;
            if (root.TryGetProperty("endpoint_key", out var endpointElement))
            {
                if (endpointElement.ValueKind != JsonValueKind.String)
                    return false;
                endpoint = endpointElement.GetString();
            }

            if (root.TryGetProperty("session_name", out var sessionNameElement))
            {
                if (sessionNameElement.ValueKind != JsonValueKind.String)
                    return false;
                sessionName = sessionNameElement.GetString();
            }

            SessionKey? session = null;
            if (device is { } deviceId && endpoint is not null)
                session = new SessionKey(deviceId, endpoint, sessionName);

            string? workspaceId = null;
            if (root.TryGetProperty("workspace_id", out var workspaceElement))
            {
                if (workspaceElement.ValueKind != JsonValueKind.String)
                    return false;
                workspaceId = workspaceElement.GetString();
            }

            string? paneId = null;
            if (root.TryGetProperty("pane_id", out var paneElement))
            {
                if (paneElement.ValueKind != JsonValueKind.String)
                    return false;
                paneId = paneElement.GetString();
            }

            PaneKey? pane = null;
            if (session is { } sessionKey && workspaceId is not null && paneId is not null)
                pane = new PaneKey(sessionKey, workspaceId, paneId);

            ConnectionEpoch? epoch = null;
            if (root.TryGetProperty("epoch", out var epochElement))
            {
                if (!epochElement.TryGetInt64(out var value) || value <= 0)
                    return false;
                epoch = new ConnectionEpoch(value);
            }

            intent = new ActivationIntent(kind, device, session, workspaceId, pane, epoch);
            code = null;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    public byte[] ToUtf8()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", KindName(Kind));
            if (Device is { } device)
                writer.WriteString("device_id", device.Value);
            if (Session is { } session)
            {
                writer.WriteString("endpoint_key", session.EndpointKey);
                if (session.SessionName is not null)
                    writer.WriteString("session_name", session.SessionName);
            }

            if (WorkspaceId is not null)
                writer.WriteString("workspace_id", WorkspaceId);
            if (Pane is { } pane)
                writer.WriteString("pane_id", pane.PaneId);
            if (Epoch is { } epoch)
                writer.WriteNumber("epoch", epoch.Value);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    private static bool TryKind(string? name, out ActivationKind kind)
    {
        switch (name)
        {
            case "normal":
                kind = ActivationKind.Normal;
                return true;
            case "settings":
                kind = ActivationKind.Settings;
                return true;
            case "diagnostics":
                kind = ActivationKind.Diagnostics;
                return true;
            case "notification_target":
                kind = ActivationKind.NotificationTarget;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static string KindName(ActivationKind kind) => kind switch
    {
        ActivationKind.Normal => "normal",
        ActivationKind.Settings => "settings",
        ActivationKind.Diagnostics => "diagnostics",
        ActivationKind.NotificationTarget => "notification_target",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
