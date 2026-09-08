using System.Text;
using System.Text.Json;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Configuration;

namespace HerdDesk.Infrastructure.Ssh;

internal sealed class RecoveryBlockStoreHooks
{
    public Func<RecoveryBlockDocument, byte[]>? Serialize { get; init; }
    public Action<string, string, string?>? Commit { get; init; }
}

internal sealed record StoredRecoveryBlock(
    string Device,
    string Kind,
    long BlockedProfileRevision,
    long? CredentialRevision,
    long? KnownHostRevision,
    string PublicCode);

internal sealed record RecoveryBlockDocument(
    int SchemaVersion,
    long Revision,
    IReadOnlyList<StoredRecoveryBlock> Blocks)
{
    public const int CurrentSchemaVersion = 1;

    public static RecoveryBlockDocument Empty { get; } =
        new(CurrentSchemaVersion, 0, Array.Empty<StoredRecoveryBlock>());
}

internal sealed class SshRecoveryBlockStore
{
    public const int MaxDocumentBytes = 256 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly HashSet<string> RootNames = new(StringComparer.Ordinal)
    {
        "schema_version", "revision", "blocks"
    };
    private static readonly HashSet<string> BlockNames = new(StringComparer.Ordinal)
    {
        "device", "kind", "blocked_profile_revision", "credential_revision",
        "known_host_revision", "public_code"
    };

    private readonly AppDataPaths _paths;
    private readonly RecoveryBlockStoreHooks _hooks;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SshRecoveryBlockStore(AppDataPaths paths, RecoveryBlockStoreHooks? hooks = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = paths;
        _hooks = hooks ?? new RecoveryBlockStoreHooks();
    }

    public async ValueTask<RecoveryBlockSnapshot?> ReadAsync(
        DeviceId device, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var fileLock = TryLock();
            if (fileLock is null)
                return null;
            var (kind, document) = LoadState();
            if (kind == LoadKind.Unreadable)
                return null;
            var stored = Find(document.Blocks, device);
            return stored is null ? null : ToSnapshot(stored);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<RecoveryBlockSnapshot>> ReadAllAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var fileLock = TryLock();
            if (fileLock is null)
                return [];
            var (kind, document) = LoadState();
            if (kind == LoadKind.Unreadable)
                return [];
            return document.Blocks.Select(ToSnapshot).OfType<RecoveryBlockSnapshot>().ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<RecoveryBlockWriteResult> SaveAsync(
        RecoveryBlockSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!RecoveryPolicy.IsPersistableBlock(snapshot.Kind))
            return new(false, RecoveryCodes.UnknownBlocked);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var fileLock = TryLock();
            if (fileLock is null)
                return new(false, RecoveryCodes.PersistenceFailed);
            var (kind, document) = LoadState();
            if (kind == LoadKind.Unreadable)
                return new(false, RecoveryCodes.PersistenceFailed);

            var stored = new StoredRecoveryBlock(
                snapshot.Device.Value.ToString("D"),
                RecoveryPolicy.Code(snapshot.Kind),
                snapshot.BlockedProfileRevision,
                snapshot.CredentialRevision,
                snapshot.KnownHostRevision,
                snapshot.PublicCode);
            if (AtomicConfigurationStore.LooksLikeSecret(stored.Device) ||
                AtomicConfigurationStore.LooksLikeSecret(stored.Kind) ||
                AtomicConfigurationStore.LooksLikeSecret(stored.PublicCode))
                return new(false, RecoveryCodes.PersistenceFailed);

            var blocks = document.Blocks.Where(item => !SameDevice(item, snapshot.Device)).ToList();
            blocks.Add(stored);
            var next = new RecoveryBlockDocument(
                RecoveryBlockDocument.CurrentSchemaVersion,
                document.Revision + 1,
                blocks);
            return CommitDocument(next).Code is null
                ? new(true, null)
                : new(false, RecoveryCodes.PersistenceFailed);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<RecoveryBlockWriteResult> ClearAsync(
        DeviceId device, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var fileLock = TryLock();
            if (fileLock is null)
                return new(false, RecoveryCodes.PersistenceFailed);
            var (kind, document) = LoadState();
            if (kind == LoadKind.Unreadable)
                return new(false, RecoveryCodes.PersistenceFailed);
            var blocks = document.Blocks.Where(item => !SameDevice(item, device)).ToList();
            if (blocks.Count == document.Blocks.Count)
                return new(true, null);
            var next = new RecoveryBlockDocument(
                RecoveryBlockDocument.CurrentSchemaVersion,
                document.Revision + 1,
                blocks);
            return CommitDocument(next).Code is null
                ? new(true, null)
                : new(false, RecoveryCodes.PersistenceFailed);
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
                _paths.RecoveryBlocksLockFile,
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

    private (LoadKind Kind, RecoveryBlockDocument Document) LoadState()
    {
        if (!File.Exists(_paths.RecoveryBlocksFile))
            return (LoadKind.Missing, RecoveryBlockDocument.Empty);
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(_paths.RecoveryBlocksFile);
        }
        catch (IOException)
        {
            return (LoadKind.Unreadable, RecoveryBlockDocument.Empty);
        }

        return TryParse(bytes, out var document)
            ? (LoadKind.Loaded, document)
            : (LoadKind.Unreadable, RecoveryBlockDocument.Empty);
    }

    private RecoveryBlockWriteResult CommitDocument(RecoveryBlockDocument document)
    {
        byte[] bytes;
        try
        {
            bytes = _hooks.Serialize is not null ? _hooks.Serialize(document) : Serialize(document);
        }
        catch (Exception)
        {
            return new(false, RecoveryCodes.PersistenceFailed);
        }

        Directory.CreateDirectory(_paths.SettingsDirectory);
        var temp = Path.Combine(
            _paths.SettingsDirectory,
            $".ssh-recovery-blocks.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.None))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }

            var replaceExisting = File.Exists(_paths.RecoveryBlocksFile);
            if (_hooks.Commit is not null)
            {
                _hooks.Commit(temp, _paths.RecoveryBlocksFile,
                    replaceExisting ? _paths.RecoveryBlocksBackupFile : null);
            }
            else if (!replaceExisting)
            {
                File.Move(temp, _paths.RecoveryBlocksFile);
            }
            else
            {
                File.Replace(temp, _paths.RecoveryBlocksFile, _paths.RecoveryBlocksBackupFile,
                    ignoreMetadataErrors: true);
            }

            return new(true, null);
        }
        catch (Exception)
        {
            TryDelete(temp);
            return new(false, RecoveryCodes.PersistenceFailed);
        }
    }

    internal static byte[] Serialize(RecoveryBlockDocument document)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", document.SchemaVersion);
            writer.WriteNumber("revision", document.Revision);
            writer.WritePropertyName("blocks");
            writer.WriteStartArray();
            foreach (var block in document.Blocks.OrderBy(item => item.Device, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("device", block.Device);
                writer.WriteString("kind", block.Kind);
                writer.WriteNumber("blocked_profile_revision", block.BlockedProfileRevision);
                if (block.CredentialRevision is { } credential)
                    writer.WriteNumber("credential_revision", credential);
                else
                    writer.WriteNull("credential_revision");
                if (block.KnownHostRevision is { } host)
                    writer.WriteNumber("known_host_revision", host);
                else
                    writer.WriteNull("known_host_revision");
                writer.WriteString("public_code", block.PublicCode);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    internal static bool TryParse(byte[] json, out RecoveryBlockDocument document)
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
                version != RecoveryBlockDocument.CurrentSchemaVersion)
                return false;
            if (!root.TryGetProperty("revision", out var revisionEl) ||
                !revisionEl.TryGetInt64(out var revision) || revision < 0)
                return false;
            if (!root.TryGetProperty("blocks", out var blocksEl) || blocksEl.ValueKind != JsonValueKind.Array)
                return false;
            var blocks = new List<StoredRecoveryBlock>();
            foreach (var item in blocksEl.EnumerateArray())
            {
                if (!TryReadBlock(item, out var record))
                    return false;
                blocks.Add(record);
            }

            document = new RecoveryBlockDocument(version, revision, blocks);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException or InvalidOperationException
                                       or FormatException)
        {
            return false;
        }
    }

    private static bool TryReadBlock(JsonElement element, out StoredRecoveryBlock record)
    {
        record = null!;
        if (element.ValueKind != JsonValueKind.Object || !TryNames(element, BlockNames))
            return false;
        if (!element.TryGetProperty("device", out var deviceEl) || deviceEl.ValueKind != JsonValueKind.String)
            return false;
        if (!element.TryGetProperty("kind", out var kindEl) || kindEl.ValueKind != JsonValueKind.String)
            return false;
        if (!element.TryGetProperty("blocked_profile_revision", out var profileEl) ||
            !profileEl.TryGetInt64(out var profile))
            return false;
        long? credential = null;
        if (element.TryGetProperty("credential_revision", out var credEl))
        {
            if (credEl.ValueKind == JsonValueKind.Null)
                credential = null;
            else if (credEl.TryGetInt64(out var cred))
                credential = cred;
            else
                return false;
        }

        long? knownHost = null;
        if (element.TryGetProperty("known_host_revision", out var hostEl))
        {
            if (hostEl.ValueKind == JsonValueKind.Null)
                knownHost = null;
            else if (hostEl.TryGetInt64(out var host))
                knownHost = host;
            else
                return false;
        }

        if (!element.TryGetProperty("public_code", out var publicEl) || publicEl.ValueKind != JsonValueKind.String)
            return false;
        var device = deviceEl.GetString();
        var kind = kindEl.GetString();
        var publicCode = publicEl.GetString();
        if (device is null || kind is null || publicCode is null)
            return false;
        if (AtomicConfigurationStore.LooksLikeSecret(device) ||
            AtomicConfigurationStore.LooksLikeSecret(kind) ||
            AtomicConfigurationStore.LooksLikeSecret(publicCode))
            return false;
        if (RecoveryPolicy.ParseCode(kind) is not { } cause ||
            !RecoveryPolicy.IsPersistableBlock(cause))
            return false;
        record = new StoredRecoveryBlock(device, kind, profile, credential, knownHost, publicCode);
        return true;
    }

    private static RecoveryBlockSnapshot? ToSnapshot(StoredRecoveryBlock stored)
    {
        if (!Guid.TryParse(stored.Device, out var guid))
            return null;
        var cause = RecoveryPolicy.ParseCode(stored.Kind);
        if (cause is null)
            return null;
        return new RecoveryBlockSnapshot(
            new DeviceId(guid),
            stored.BlockedProfileRevision,
            cause.Value,
            stored.CredentialRevision,
            stored.KnownHostRevision,
            stored.PublicCode);
    }

    private static StoredRecoveryBlock? Find(IReadOnlyList<StoredRecoveryBlock> blocks, DeviceId device)
    {
        var id = device.Value.ToString("D");
        return blocks.FirstOrDefault(item => string.Equals(item.Device, id, StringComparison.OrdinalIgnoreCase));
    }

    private static bool SameDevice(StoredRecoveryBlock item, DeviceId device) =>
        string.Equals(item.Device, device.Value.ToString("D"), StringComparison.OrdinalIgnoreCase);

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
