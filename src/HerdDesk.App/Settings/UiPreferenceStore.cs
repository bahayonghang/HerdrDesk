using System.Text;
using System.Text.Json;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Configuration;

namespace HerdDesk.App;

public sealed record UiPreferenceLoadResult(UiPreferences? Preferences, string? Code)
{
    public bool Succeeded => Preferences is not null && Code is null;
}

public sealed record UiPreferenceWriteResult(UiPreferences? Preferences, string? Code)
{
    public bool Succeeded => Preferences is not null && Code is null;
}

public sealed class UiPreferenceStoreHooks
{
    public Func<UiPreferences, byte[]>? Serialize { get; init; }
}

public sealed class UiPreferenceStore
{
    public const int MaxDocumentBytes = 64 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly HashSet<string> ForbiddenNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "passwd", "secret", "token", "private_key", "privatekey",
        "authorization", "credential", "terminal_text", "ansi", "input_text"
    };
    private static readonly HashSet<string> RootNames = new(StringComparer.Ordinal)
    {
        "schema_version", "revision", "font_family", "font_size", "zoom_percent",
        "theme", "notifications_enabled", "diagnostic_privacy_redact"
    };

    private readonly AppDataPaths _paths;
    private readonly UiPreferenceStoreHooks _hooks;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public UiPreferenceStore(AppDataPaths paths, UiPreferenceStoreHooks? hooks = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = paths;
        _hooks = hooks ?? new UiPreferenceStoreHooks();
        FilePath = Path.Combine(paths.SettingsDirectory, "ui-preferences.json");
        BackupPath = FilePath + ".bak";
    }

    public string FilePath { get; }
    public string BackupPath { get; }

    public async ValueTask<UiPreferenceLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return LoadUnlocked();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<UiPreferenceWriteResult> SaveAsync(
        UiPreferences preferences,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var loaded = LoadUnlocked();
            UiPreferences current;
            if (loaded.Code is ConfigurationCodes.Missing)
                current = UiPreferences.Default;
            else if (!loaded.Succeeded)
                return new(null, loaded.Code ?? ConfigurationCodes.Malformed);
            else
                current = loaded.Preferences!;
            if (current.Revision != expectedRevision)
                return new(null, ConfigurationCodes.WriteConflict);
            var error = Validate(preferences);
            if (error is not null)
                return new(null, error);
            var next = preferences with
            {
                SchemaVersion = UiPreferences.CurrentSchemaVersion,
                Revision = current.Revision + 1
            };
            return Commit(next, replaceExisting: File.Exists(FilePath), writeBackup: File.Exists(FilePath));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<UiPreferenceWriteResult> RestoreFromBackupAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(BackupPath))
                return new(null, ConfigurationCodes.BackupMissing);
            var bytes = File.ReadAllBytes(BackupPath);
            var parsed = Parse(bytes);
            if (!parsed.Succeeded)
                return new(null, parsed.Code ?? ConfigurationCodes.Malformed);
            return CommitBytes(bytes, parsed.Preferences!, replaceExisting: File.Exists(FilePath), writeBackup: false);
        }
        catch (IOException)
        {
            return new(null, ConfigurationCodes.BackupMissing);
        }
        finally
        {
            _gate.Release();
        }
    }

    public static string? Validate(UiPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (preferences.FontFamily.Length is < 1 or > 64)
            return ConfigurationCodes.InvalidProfile;
        if (HasUnsafe(preferences.FontFamily))
            return ConfigurationCodes.InvalidProfile;
        if (preferences.FontSize is < 8 or > 32)
            return ConfigurationCodes.InvalidProfile;
        if (preferences.ZoomPercent is < 50 or > 300)
            return ConfigurationCodes.InvalidProfile;
        return null;
    }

    private UiPreferenceLoadResult LoadUnlocked()
    {
        if (!File.Exists(FilePath))
            return new(null, ConfigurationCodes.Missing);
        try
        {
            return Parse(File.ReadAllBytes(FilePath));
        }
        catch (IOException)
        {
            return new(null, ConfigurationCodes.Malformed);
        }
    }

    private UiPreferenceWriteResult Commit(UiPreferences preferences, bool replaceExisting, bool writeBackup)
    {
        byte[] bytes;
        try
        {
            bytes = _hooks.Serialize is not null ? _hooks.Serialize(preferences) : Serialize(preferences);
        }
        catch (Exception)
        {
            return new(null, ConfigurationCodes.SerializeFailed);
        }

        return CommitBytes(bytes, preferences, replaceExisting, writeBackup);
    }

    private UiPreferenceWriteResult CommitBytes(
        byte[] bytes,
        UiPreferences preferences,
        bool replaceExisting,
        bool writeBackup)
    {
        Directory.CreateDirectory(_paths.SettingsDirectory);
        var temp = Path.Combine(_paths.SettingsDirectory, $".ui-preferences.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.None))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }

            if (!replaceExisting)
                File.Move(temp, FilePath);
            else if (writeBackup)
                File.Replace(temp, FilePath, BackupPath, ignoreMetadataErrors: true);
            else
                File.Move(temp, FilePath, overwrite: true);
            return new(preferences, null);
        }
        catch (Exception)
        {
            TryDelete(temp);
            return new(null, ConfigurationCodes.ReplaceFailed);
        }
    }

    internal static byte[] Serialize(UiPreferences preferences)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", preferences.SchemaVersion);
            writer.WriteNumber("revision", preferences.Revision);
            writer.WriteString("font_family", preferences.FontFamily);
            writer.WriteNumber("font_size", preferences.FontSize);
            writer.WriteNumber("zoom_percent", preferences.ZoomPercent);
            writer.WriteString("theme", ThemeName(preferences.Theme));
            writer.WriteBoolean("notifications_enabled", preferences.NotificationsEnabled);
            writer.WriteBoolean("diagnostic_privacy_redact", preferences.DiagnosticPrivacyRedact);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    internal static UiPreferenceLoadResult Parse(byte[] json)
    {
        if (json.Length is 0 or > MaxDocumentBytes)
            return new(null, ConfigurationCodes.Malformed);
        try
        {
            _ = StrictUtf8.GetCharCount(json);
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                MaxDepth = 64,
                AllowDuplicateProperties = false
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new(null, ConfigurationCodes.Malformed);
            if (FindForbidden(root) is not null)
                return new(null, ConfigurationCodes.ForbiddenField);
            if (!TryObjectNames(root, out var namesError))
                return new(null, namesError);
            if (!TryGetInt32(root, "schema_version", out var version))
                return new(null, ConfigurationCodes.Malformed);
            if (version < 1)
                return new(null, ConfigurationCodes.Malformed);
            if (version > UiPreferences.CurrentSchemaVersion)
                return new(null, ConfigurationCodes.VersionUnsupported);
            if (!TryGetInt64(root, "revision", out var revision) || revision < 0)
                return new(null, ConfigurationCodes.Malformed);
            if (!TryGetString(root, "font_family", out var family) ||
                !TryGetInt32(root, "font_size", out var size) ||
                !TryGetInt32(root, "zoom_percent", out var zoom) ||
                !TryGetString(root, "theme", out var themeName) ||
                !TryGetBool(root, "notifications_enabled", out var notifications) ||
                !TryGetBool(root, "diagnostic_privacy_redact", out var redact) ||
                !TryTheme(themeName, out var theme))
                return new(null, ConfigurationCodes.Malformed);
            var prefs = new UiPreferences(version, revision, family, size, zoom, theme, notifications, redact);
            var invalid = Validate(prefs);
            if (invalid is not null)
                return new(null, invalid);
            return new(prefs, null);
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException or InvalidOperationException
                                       or FormatException)
        {
            return new(null, ConfigurationCodes.Malformed);
        }
    }

    private static bool HasUnsafe(string value)
    {
        if (value.IndexOfAny(['\\', '/', '\0', '%']) >= 0)
            return true;
        foreach (var c in value)
        {
            if (char.IsControl(c) || char.IsSurrogate(c))
                return true;
        }

        return false;
    }

    private static string? FindForbidden(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var property in element.EnumerateObject())
        {
            if (ForbiddenNames.Contains(property.Name))
                return property.Name;
        }

        return null;
    }

    private static bool TryObjectNames(JsonElement element, out string code)
    {
        code = ConfigurationCodes.Malformed;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (ForbiddenNames.Contains(property.Name))
            {
                code = ConfigurationCodes.ForbiddenField;
                return false;
            }

            if (!RootNames.Contains(property.Name) || !seen.Add(property.Name))
                return false;
        }

        return true;
    }

    private static bool TryGetString(JsonElement obj, string name, out string value)
    {
        value = "";
        if (!obj.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
            return false;
        var text = element.GetString();
        if (text is null)
            return false;
        value = text;
        return true;
    }

    private static bool TryGetInt32(JsonElement obj, string name, out int value)
    {
        value = 0;
        return obj.TryGetProperty(name, out var element) && element.TryGetInt32(out value);
    }

    private static bool TryGetInt64(JsonElement obj, string name, out long value)
    {
        value = 0;
        return obj.TryGetProperty(name, out var element) && element.TryGetInt64(out value);
    }

    private static bool TryGetBool(JsonElement obj, string name, out bool value)
    {
        value = false;
        if (!obj.TryGetProperty(name, out var element) ||
            element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return false;
        value = element.GetBoolean();
        return true;
    }

    private static bool TryTheme(string name, out UiThemeKind theme)
    {
        switch (name)
        {
            case "system":
                theme = UiThemeKind.System;
                return true;
            case "light":
                theme = UiThemeKind.Light;
                return true;
            case "dark":
                theme = UiThemeKind.Dark;
                return true;
            case "high_contrast":
                theme = UiThemeKind.HighContrast;
                return true;
            default:
                theme = default;
                return false;
        }
    }

    private static string ThemeName(UiThemeKind theme) => theme switch
    {
        UiThemeKind.System => "system",
        UiThemeKind.Light => "light",
        UiThemeKind.Dark => "dark",
        UiThemeKind.HighContrast => "high_contrast",
        _ => throw new ArgumentOutOfRangeException(nameof(theme))
    };

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
