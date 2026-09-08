using System.Text;
using System.Text.Json;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Configuration;

namespace HerdDesk.Infrastructure.Ssh;

internal sealed class HelperReceiptStoreHooks
{
    public Func<HelperReceiptDocument, byte[]>? Serialize { get; init; }
    public Action<string, string, string?>? Commit { get; init; }
}

internal sealed record StoredHelperReceipt(
    string Device,
    string EndpointKey,
    string? SessionName,
    string TargetTriple,
    string RemoteHomeSha256,
    string Version,
    string Sha256,
    string? PreviousVersion,
    string? PreviousSha256,
    DateTimeOffset WrittenUtc);

internal sealed record StoredHelperLease(
    string Device,
    string EndpointKey,
    string? SessionName,
    string TargetTriple,
    string RemoteHomeSha256,
    long Epoch,
    string Version,
    string Sha256,
    DateTimeOffset AcquiredUtc);

internal sealed record HelperReceiptDocument(
    int SchemaVersion,
    long Revision,
    IReadOnlyList<StoredHelperReceipt> Receipts,
    IReadOnlyList<StoredHelperLease> Leases)
{
    public const int CurrentSchemaVersion = 1;

    public static HelperReceiptDocument Empty { get; } =
        new(CurrentSchemaVersion, 0, Array.Empty<StoredHelperReceipt>(), Array.Empty<StoredHelperLease>());
}

internal sealed class HelperDeploymentReceiptStore
{
    public const int MaxDocumentBytes = 256 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly HashSet<string> RootNames = new(StringComparer.Ordinal)
    {
        "schema_version", "revision", "receipts", "leases"
    };
    private static readonly HashSet<string> ReceiptNames = new(StringComparer.Ordinal)
    {
        "device", "endpoint_key", "session_name", "target_triple", "remote_home_sha256",
        "version", "sha256", "previous_version", "previous_sha256", "written_utc"
    };
    private static readonly HashSet<string> LeaseNames = new(StringComparer.Ordinal)
    {
        "device", "endpoint_key", "session_name", "target_triple", "remote_home_sha256",
        "epoch", "version", "sha256", "acquired_utc"
    };

    private readonly AppDataPaths _paths;
    private readonly HelperReceiptStoreHooks _hooks;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public HelperDeploymentReceiptStore(AppDataPaths paths, HelperReceiptStoreHooks? hooks = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = paths;
        _hooks = hooks ?? new HelperReceiptStoreHooks();
    }

    public async ValueTask<DeploymentReceipt?> ReadCurrentAsync(
        HelperReceiptKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var fileLock = TryLock();
            if (fileLock is null)
                return null;
            var (kind, document) = LoadState();
            if (kind == LoadKind.Unreadable)
                return null;
            var stored = Find(document.Receipts, key);
            return stored is null ? null : ToReceipt(stored, document.Revision);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<(DeploymentReceipt? Receipt, string? Code)> ActivateAsync(
        HelperReceiptKey key,
        string version,
        string sha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!HelperRemoteScripts.IsSafeVersion(version) || !HelperRemoteScripts.IsSha256(sha256))
            return (null, HelperCodes.ManifestInvalid);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var fileLock = TryLock();
            if (fileLock is null)
                return (null, HelperCodes.ActivationFailed);
            var (kind, document) = LoadState();
            if (kind == LoadKind.Unreadable)
                return (null, HelperCodes.ActivationFailed);
            if (HasForeignHashLease(document.Leases, key, sha256))
                return (null, HelperCodes.VersionCollision);

            var existing = Find(document.Receipts, key);
            if (existing is not null && existing.Sha256 == sha256 && existing.Version == version)
                return (ToReceipt(existing, document.Revision), null);

            var nextReceipt = new StoredHelperReceipt(
                key.Device.Value.ToString("D"),
                key.EndpointKey,
                key.SessionName,
                key.TargetTriple,
                key.RemoteHomeSha256,
                version,
                sha256,
                existing?.Version,
                existing?.Sha256,
                DateTimeOffset.UtcNow);
            var receipts = document.Receipts.Where(item => !SameOwner(item, key)).ToList();
            receipts.Add(nextReceipt);
            var next = new HelperReceiptDocument(
                HelperReceiptDocument.CurrentSchemaVersion,
                document.Revision + 1,
                receipts,
                document.Leases);
            var written = CommitDocument(next);
            return written.Code is null
                ? (ToReceipt(nextReceipt, next.Revision), null)
                : (null, written.Code);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<(DeploymentReceipt? Receipt, string? Code)> RollbackAsync(
        HelperReceiptKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var fileLock = TryLock();
            if (fileLock is null)
                return (null, HelperCodes.ActivationFailed);
            var (kind, document) = LoadState();
            if (kind == LoadKind.Unreadable)
                return (null, HelperCodes.ActivationFailed);
            var existing = Find(document.Receipts, key);
            if (existing is null || existing.PreviousVersion is null || existing.PreviousSha256 is null)
                return (null, HelperCodes.ActivationFailed);
            if (HasForeignHashLease(document.Leases, key, existing.PreviousSha256))
                return (null, HelperCodes.VersionCollision);

            var rolled = new StoredHelperReceipt(
                existing.Device,
                existing.EndpointKey,
                existing.SessionName,
                existing.TargetTriple,
                existing.RemoteHomeSha256,
                existing.PreviousVersion,
                existing.PreviousSha256,
                existing.Version,
                existing.Sha256,
                DateTimeOffset.UtcNow);
            var receipts = document.Receipts.Where(item => !SameOwner(item, key)).ToList();
            receipts.Add(rolled);
            var next = new HelperReceiptDocument(
                HelperReceiptDocument.CurrentSchemaVersion,
                document.Revision + 1,
                receipts,
                document.Leases);
            var written = CommitDocument(next);
            return written.Code is null
                ? (ToReceipt(rolled, next.Revision), null)
                : (null, written.Code);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<string?> AcquireLeaseAsync(
        HelperReceiptKey key,
        ConnectionEpoch epoch,
        string version,
        string sha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (epoch.Value <= 0 || !HelperRemoteScripts.IsSafeVersion(version) || !HelperRemoteScripts.IsSha256(sha256))
            return HelperCodes.ActivationFailed;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var fileLock = TryLock();
            if (fileLock is null)
                return HelperCodes.ActivationFailed;
            var (kind, document) = LoadState();
            if (kind == LoadKind.Unreadable)
                return HelperCodes.ActivationFailed;
            if (HasForeignHashLease(document.Leases, key, sha256))
                return HelperCodes.VersionCollision;

            var leases = document.Leases.Where(item =>
                !(SameOwner(item, key) && item.Epoch == epoch.Value)).ToList();
            leases.Add(new StoredHelperLease(
                key.Device.Value.ToString("D"),
                key.EndpointKey,
                key.SessionName,
                key.TargetTriple,
                key.RemoteHomeSha256,
                epoch.Value,
                version,
                sha256,
                DateTimeOffset.UtcNow));
            var next = new HelperReceiptDocument(
                HelperReceiptDocument.CurrentSchemaVersion,
                document.Revision + 1,
                document.Receipts,
                leases);
            return CommitDocument(next).Code;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask ReleaseLeaseAsync(
        HelperReceiptKey key, ConnectionEpoch epoch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var fileLock = TryLock();
            if (fileLock is null)
                return;
            var (kind, document) = LoadState();
            if (kind == LoadKind.Unreadable)
                return;
            var leases = document.Leases.Where(item =>
                !(SameOwner(item, key) && item.Epoch == epoch.Value)).ToList();
            if (leases.Count == document.Leases.Count)
                return;
            var next = new HelperReceiptDocument(
                HelperReceiptDocument.CurrentSchemaVersion,
                document.Revision + 1,
                document.Receipts,
                leases);
            _ = CommitDocument(next);
        }
        finally
        {
            _gate.Release();
        }
    }

    private FileStream? TryLock()
    {
        Directory.CreateDirectory(_paths.SettingsDirectory);
        try
        {
            return new FileStream(
                _paths.HelperReceiptsLockFile,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private enum LoadKind
    {
        Missing,
        Loaded,
        Unreadable
    }

    private (LoadKind Kind, HelperReceiptDocument Document) LoadState()
    {
        if (!File.Exists(_paths.HelperReceiptsFile))
            return (LoadKind.Missing, HelperReceiptDocument.Empty);
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(_paths.HelperReceiptsFile);
        }
        catch (IOException)
        {
            return (LoadKind.Unreadable, HelperReceiptDocument.Empty);
        }

        return TryParse(bytes, out var document)
            ? (LoadKind.Loaded, document)
            : (LoadKind.Unreadable, HelperReceiptDocument.Empty);
    }

    private (long? Revision, string? Code) CommitDocument(HelperReceiptDocument document)
    {
        byte[] bytes;
        try
        {
            bytes = _hooks.Serialize is not null ? _hooks.Serialize(document) : Serialize(document);
        }
        catch (Exception)
        {
            return (null, HelperCodes.ActivationFailed);
        }

        Directory.CreateDirectory(_paths.SettingsDirectory);
        var temp = Path.Combine(
            _paths.SettingsDirectory,
            $".helper-receipts.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.None))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }

            var replaceExisting = File.Exists(_paths.HelperReceiptsFile);
            if (_hooks.Commit is not null)
            {
                _hooks.Commit(temp, _paths.HelperReceiptsFile,
                    replaceExisting ? _paths.HelperReceiptsBackupFile : null);
            }
            else if (!replaceExisting)
            {
                File.Move(temp, _paths.HelperReceiptsFile);
            }
            else
            {
                File.Replace(temp, _paths.HelperReceiptsFile, _paths.HelperReceiptsBackupFile,
                    ignoreMetadataErrors: true);
            }

            return (document.Revision, null);
        }
        catch (Exception)
        {
            TryDelete(temp);
            return (null, HelperCodes.ActivationFailed);
        }
    }

    internal static byte[] Serialize(HelperReceiptDocument document)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", document.SchemaVersion);
            writer.WriteNumber("revision", document.Revision);
            writer.WritePropertyName("receipts");
            writer.WriteStartArray();
            foreach (var receipt in document.Receipts)
            {
                writer.WriteStartObject();
                writer.WriteString("device", receipt.Device);
                writer.WriteString("endpoint_key", receipt.EndpointKey);
                if (receipt.SessionName is null)
                    writer.WriteNull("session_name");
                else
                    writer.WriteString("session_name", receipt.SessionName);
                writer.WriteString("target_triple", receipt.TargetTriple);
                writer.WriteString("remote_home_sha256", receipt.RemoteHomeSha256);
                writer.WriteString("version", receipt.Version);
                writer.WriteString("sha256", receipt.Sha256);
                if (receipt.PreviousVersion is null)
                    writer.WriteNull("previous_version");
                else
                    writer.WriteString("previous_version", receipt.PreviousVersion);
                if (receipt.PreviousSha256 is null)
                    writer.WriteNull("previous_sha256");
                else
                    writer.WriteString("previous_sha256", receipt.PreviousSha256);
                writer.WriteString("written_utc", receipt.WrittenUtc.ToString("O"));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("leases");
            writer.WriteStartArray();
            foreach (var lease in document.Leases)
            {
                writer.WriteStartObject();
                writer.WriteString("device", lease.Device);
                writer.WriteString("endpoint_key", lease.EndpointKey);
                if (lease.SessionName is null)
                    writer.WriteNull("session_name");
                else
                    writer.WriteString("session_name", lease.SessionName);
                writer.WriteString("target_triple", lease.TargetTriple);
                writer.WriteString("remote_home_sha256", lease.RemoteHomeSha256);
                writer.WriteNumber("epoch", lease.Epoch);
                writer.WriteString("version", lease.Version);
                writer.WriteString("sha256", lease.Sha256);
                writer.WriteString("acquired_utc", lease.AcquiredUtc.ToString("O"));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    internal static bool TryParse(byte[] json, out HelperReceiptDocument document)
    {
        document = null!;
        if (json.Length is 0 or > MaxDocumentBytes)
            return false;
        try
        {
            _ = StrictUtf8.GetCharCount(json);
            using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                MaxDepth = 64,
                AllowDuplicateProperties = false
            });
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !TryNames(root, RootNames))
                return false;
            if (!root.TryGetProperty("schema_version", out var versionEl) ||
                !versionEl.TryGetInt32(out var version) ||
                version != HelperReceiptDocument.CurrentSchemaVersion)
                return false;
            if (!root.TryGetProperty("revision", out var revisionEl) ||
                !revisionEl.TryGetInt64(out var revision) || revision < 0)
                return false;
            if (!root.TryGetProperty("receipts", out var receiptsEl) ||
                receiptsEl.ValueKind != JsonValueKind.Array)
                return false;
            if (!root.TryGetProperty("leases", out var leasesEl) || leasesEl.ValueKind != JsonValueKind.Array)
                return false;
            var receipts = new List<StoredHelperReceipt>();
            foreach (var item in receiptsEl.EnumerateArray())
            {
                if (!TryReadReceipt(item, out var receipt))
                    return false;
                receipts.Add(receipt);
            }

            var leases = new List<StoredHelperLease>();
            foreach (var item in leasesEl.EnumerateArray())
            {
                if (!TryReadLease(item, out var lease))
                    return false;
                leases.Add(lease);
            }

            document = new HelperReceiptDocument(version, revision, receipts, leases);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException or InvalidOperationException
                                       or FormatException)
        {
            return false;
        }
    }

    private static bool TryReadReceipt(JsonElement element, out StoredHelperReceipt record)
    {
        record = null!;
        if (element.ValueKind != JsonValueKind.Object || !TryNames(element, ReceiptNames))
            return false;
        if (!TryString(element, "device", out var device) ||
            !TryString(element, "endpoint_key", out var endpoint) ||
            !TryOptionalString(element, "session_name", out var session) ||
            !TryString(element, "target_triple", out var triple) ||
            !TryString(element, "remote_home_sha256", out var home) ||
            !TryString(element, "version", out var version) ||
            !TryString(element, "sha256", out var sha) ||
            !TryOptionalString(element, "previous_version", out var previousVersion) ||
            !TryOptionalString(element, "previous_sha256", out var previousSha) ||
            !TryString(element, "written_utc", out var utcText) ||
            !DateTimeOffset.TryParse(utcText, out var utc))
            return false;
        if (!Guid.TryParse(device, out _) ||
            !HelperRemoteScripts.IsSha256(home) ||
            !HelperRemoteScripts.IsSafeVersion(version) ||
            !HelperRemoteScripts.IsSha256(sha))
            return false;
        record = new(device, endpoint, session, triple, home, version, sha, previousVersion, previousSha, utc);
        return true;
    }

    private static bool TryReadLease(JsonElement element, out StoredHelperLease record)
    {
        record = null!;
        if (element.ValueKind != JsonValueKind.Object || !TryNames(element, LeaseNames))
            return false;
        if (!TryString(element, "device", out var device) ||
            !TryString(element, "endpoint_key", out var endpoint) ||
            !TryOptionalString(element, "session_name", out var session) ||
            !TryString(element, "target_triple", out var triple) ||
            !TryString(element, "remote_home_sha256", out var home) ||
            !element.TryGetProperty("epoch", out var epochEl) || !epochEl.TryGetInt64(out var epoch) ||
            epoch <= 0 ||
            !TryString(element, "version", out var version) ||
            !TryString(element, "sha256", out var sha) ||
            !TryString(element, "acquired_utc", out var utcText) ||
            !DateTimeOffset.TryParse(utcText, out var utc))
            return false;
        record = new(device, endpoint, session, triple, home, epoch, version, sha, utc);
        return true;
    }

    private static bool TryString(JsonElement element, string name, out string value)
    {
        value = "";
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
            return false;
        var text = property.GetString();
        if (text is null)
            return false;
        value = text;
        return true;
    }

    private static bool TryOptionalString(JsonElement element, string name, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(name, out var property))
            return false;
        if (property.ValueKind == JsonValueKind.Null)
            return true;
        if (property.ValueKind != JsonValueKind.String)
            return false;
        value = property.GetString();
        return value is not null;
    }

    private static bool TryNames(JsonElement element, HashSet<string> allowed)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name) || !seen.Add(property.Name))
                return false;
        }

        return true;
    }

    private static StoredHelperReceipt? Find(IReadOnlyList<StoredHelperReceipt> receipts, HelperReceiptKey key) =>
        receipts.FirstOrDefault(item => SameOwner(item, key));

    private static bool SameOwner(StoredHelperReceipt item, HelperReceiptKey key) =>
        item.Device == key.Device.Value.ToString("D") &&
        item.EndpointKey == key.EndpointKey &&
        item.SessionName == key.SessionName &&
        item.TargetTriple == key.TargetTriple &&
        item.RemoteHomeSha256 == key.RemoteHomeSha256;

    private static bool SameOwner(StoredHelperLease item, HelperReceiptKey key) =>
        item.Device == key.Device.Value.ToString("D") &&
        item.EndpointKey == key.EndpointKey &&
        item.SessionName == key.SessionName &&
        item.TargetTriple == key.TargetTriple &&
        item.RemoteHomeSha256 == key.RemoteHomeSha256;

    private static bool HasForeignHashLease(
        IReadOnlyList<StoredHelperLease> leases, HelperReceiptKey key, string sha256) =>
        leases.Any(item => SameOwner(item, key) && item.Sha256 != sha256);

    private static DeploymentReceipt ToReceipt(StoredHelperReceipt stored, long revision) =>
        new(
            new HelperReceiptKey(
                new DeviceId(Guid.Parse(stored.Device)),
                stored.EndpointKey,
                stored.SessionName,
                stored.TargetTriple,
                stored.RemoteHomeSha256),
            stored.Version,
            stored.Sha256,
            stored.PreviousVersion,
            stored.PreviousSha256,
            revision);

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
