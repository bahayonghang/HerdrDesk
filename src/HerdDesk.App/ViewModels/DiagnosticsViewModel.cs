using System.Text;
using System.Text.Json;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Diagnostics;

namespace HerdDesk.App;

public sealed class DiagnosticsViewModel
{
    private readonly ProjectionCatalog _catalog;
    private readonly DiagnosticAliasProjector? _aliases;
    private readonly IReadOnlyList<UnavailableCapability> _unavailable;

    public DiagnosticsViewModel(
        ProjectionCatalog catalog,
        IReadOnlyList<UnavailableCapability> unavailable,
        DiagnosticAliasProjector? aliases = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(unavailable);
        _catalog = catalog;
        _unavailable = unavailable;
        _aliases = aliases;
        AboutStatement = ProductInfo.IndependentClientStatement;
        Version = ProductInfo.Version;
    }

    public string Version { get; }
    public string AboutStatement { get; }
    public DiagnosticPreview? Preview { get; private set; }
    public bool ExportConfirmed { get; private set; }
    public string? LastExportPath { get; private set; }
    public bool Opened { get; private set; }
    public int ImplicitWrites { get; private set; }

    public void Open()
    {
        Opened = true;
        Preview = BuildPreview();
        ExportConfirmed = false;
        LastExportPath = null;
    }

    public DiagnosticPreview BuildPreview()
    {
        var redact = true;
        var fields = new List<DiagnosticPreviewField>
        {
            Field("product", ProductInfo.Name, false),
            Field("version", Version, false),
            Field("independent_client", AboutStatement, false),
            Field("connection_phase", _catalog.Snapshot.Phase.ToString(), false),
            Field("epoch", _catalog.Snapshot.Epoch.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), false),
            Field("freshness", _catalog.Freshness.ToString(), false),
            Field("queue_bytes", _catalog.QueueBytes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unavailable",
                _catalog.QueueBytes is null),
            Field("error_category", _catalog.LastErrorCode ?? "none", false)
        };

        var device = _catalog.Snapshot.Devices.FirstOrDefault();
        if (device is not null)
        {
            var raw = device.Device.Value.ToString("D");
            fields.Add(Field("device", Alias("device", raw), true));
        }

        var session = device?.Sessions.FirstOrDefault();
        if (session is not null)
        {
            var raw = session.Session.EndpointKey + ":" + (session.Session.SessionName ?? "");
            fields.Add(Field("session", Alias("session", raw), true));
        }

        if (_unavailable.Count == 0)
            fields.Add(Field("unavailable", "none", false));
        else
        {
            foreach (var item in _unavailable)
                fields.Add(Field("unavailable_" + item.Name, item.Code, false));
        }

        fields.Add(Field("herdr_path", "[redacted]", true));
        Preview = new DiagnosticPreview(fields, redact, false);
        return Preview;
    }

    public bool ConfirmExport()
    {
        if (Preview is null)
            BuildPreview();
        ExportConfirmed = true;
        Preview = Preview! with { Confirmed = true };
        return true;
    }

    public bool TryExport(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!ExportConfirmed || Preview is null)
            return false;
        var json = Serialize(Preview);
        if (LooksSensitive(json))
            return false;
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, json, new UTF8Encoding(false));
        LastExportPath = path;
        return true;
    }

    private static string Serialize(DiagnosticPreview preview)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("redacted", preview.IsRedactedDefault);
            writer.WriteBoolean("confirmed", preview.Confirmed);
            writer.WritePropertyName("fields");
            writer.WriteStartArray();
            foreach (var field in preview.Fields)
            {
                writer.WriteStartObject();
                writer.WriteString("name", field.Name);
                writer.WriteString("value", field.Value);
                writer.WriteBoolean("redacted", field.Redacted);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private string Alias(string kind, string raw)
    {
        if (_aliases is null)
            return kind + "-redacted";
        return _aliases.Alias(kind, raw);
    }

    private static DiagnosticPreviewField Field(string name, string value, bool redacted) =>
        new(name, value, redacted);

    private static bool LooksSensitive(string value)
    {
        var lower = value.ToLowerInvariant();
        return lower.Contains("password", StringComparison.Ordinal) ||
               lower.Contains("private_key", StringComparison.Ordinal) ||
               lower.Contains("begin ", StringComparison.Ordinal) ||
               lower.Contains("terminal.input", StringComparison.Ordinal) ||
               value.Contains('\u001b', StringComparison.Ordinal);
    }
}
