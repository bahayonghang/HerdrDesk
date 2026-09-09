using System.Globalization;
using System.Text.Json;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Configuration;

namespace HerdDesk.Infrastructure.Clipboard;

public sealed class AttachmentCacheOptions
{
    public long MaxBytes { get; init; } = 32L * 1024 * 1024;
    public TimeSpan DefaultTtl { get; init; } = TimeSpan.FromHours(1);
    public int MaxObjects { get; init; } = 256;
}

public sealed class CacheObjectLease : IDisposable
{
    readonly Action<Guid> _release;

    internal CacheObjectLease(Guid objectId, Action<Guid> release)
    {
        ObjectId = objectId;
        _release = release;
    }

    public Guid ObjectId { get; }
    public bool Released { get; private set; }

    public void Dispose()
    {
        if (Released)
            return;
        Released = true;
        _release(ObjectId);
    }
}

public sealed class AttachmentCache
{
    const string RegistryName = "registry.json";

    readonly string _root;
    readonly TimeProvider _time;
    readonly AttachmentCacheOptions _options;
    readonly IDiagnosticSink? _diagnostics;
    readonly SemaphoreSlim _gate = new(1, 1);
    readonly Dictionary<Guid, CacheRecord> _records = [];

    public AttachmentCache(
        AppDataPaths paths,
        TimeProvider? time = null,
        AttachmentCacheOptions? options = null,
        IDiagnosticSink? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _root = Path.Combine(paths.CacheDirectory, "attachments");
        _time = time ?? TimeProvider.System;
        _options = options ?? new AttachmentCacheOptions();
        _diagnostics = diagnostics;
        Directory.CreateDirectory(_root);
        LoadRegistry();
    }

    public string Root => _root;
    public CacheUsage Usage => SnapshotUsage();

    public CacheStoreResult Store(ReadOnlyMemory<byte> content, TimeSpan? ttl = null)
    {
        _gate.Wait();
        try
        {
            var now = _time.GetUtcNow();
            EvictExpiredUnlocked(now, out _);
            if (content.Length == 0)
                return new(false, ClipboardCodes.Empty, null, UsedBytesUnlocked());
            if (content.Length > _options.MaxBytes)
                return new(false, ClipboardCodes.Oversize, null, UsedBytesUnlocked());
            if (_records.Count >= _options.MaxObjects
                || UsedBytesUnlocked() + content.Length > _options.MaxBytes)
            {
                WriteDiagnostic(now, "store", DiagnosticOutcome.Failure, ClipboardCodes.CacheFull);
                return new(false, ClipboardCodes.CacheFull, null, UsedBytesUnlocked());
            }

            var id = Guid.NewGuid();
            var lifetime = ttl ?? _options.DefaultTtl;
            if (lifetime < TimeSpan.Zero)
                lifetime = _options.DefaultTtl;
            var nonce = Guid.NewGuid().ToString("N");
            var tempName = "." + id.ToString("N") + "." + nonce + ".tmp";
            var temp = Path.Combine(_root, tempName);
            var final = ObjectPath(id);
            try
            {
                using (var stream = new FileStream(
                           temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.None))
                {
                    stream.Write(content.Span);
                    stream.Flush(true);
                }

                File.Move(temp, final);
            }
            catch (IOException ex) when (IsDiskFull(ex))
            {
                TryDeleteExact(temp);
                return new(false, ClipboardCodes.DiskFull, null, UsedBytesUnlocked());
            }
            catch (UnauthorizedAccessException)
            {
                TryDeleteExact(temp);
                return new(false, ClipboardCodes.PermissionDenied, null, UsedBytesUnlocked());
            }
            catch (Exception)
            {
                TryDeleteExact(temp);
                WriteDiagnostic(now, "store", DiagnosticOutcome.Failure, ClipboardCodes.Unknown);
                return new(false, ClipboardCodes.Unknown, null, UsedBytesUnlocked());
            }

            _records[id] = new CacheRecord(
                id,
                content.Length,
                now,
                now + lifetime,
                0,
                "ready",
                null);
            if (!TryPersistRegistry())
            {
                TryDeleteExact(final);
                _records.Remove(id);
                WriteDiagnostic(now, "store", DiagnosticOutcome.Failure, ClipboardCodes.CleanupFailed);
                return new(false, ClipboardCodes.CleanupFailed, null, UsedBytesUnlocked());
            }

            WriteDiagnostic(now, "store", DiagnosticOutcome.Success, ClipboardCodes.Allowed);
            return new(true, ClipboardCodes.Allowed, id, UsedBytesUnlocked());
        }
        finally
        {
            _gate.Release();
        }
    }

    public CacheObjectLease Acquire(Guid objectId)
    {
        _gate.Wait();
        try
        {
            if (!_records.TryGetValue(objectId, out var record))
                throw new ArgumentException(ClipboardCodes.Empty, nameof(objectId));
            _records[objectId] = record with { LeaseCount = record.LeaseCount + 1 };
            _ = TryPersistRegistry();
            return new CacheObjectLease(objectId, Release);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Release(Guid objectId)
    {
        _gate.Wait();
        try
        {
            if (!_records.TryGetValue(objectId, out var record))
                return;
            var next = Math.Max(0, record.LeaseCount - 1);
            _records[objectId] = record with { LeaseCount = next };
            _ = TryPersistRegistry();
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool IsHeld(Guid objectId)
    {
        _gate.Wait();
        try
        {
            return _records.TryGetValue(objectId, out var record) && record.LeaseCount > 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    public CacheCleanupResult CleanupExpired()
    {
        _gate.Wait();
        try
        {
            var now = _time.GetUtcNow();
            var removed = EvictExpiredUnlocked(now, out var deleteFailed);
            var persisted = TryPersistRegistry();
            var failed = deleteFailed || !persisted;
            var code = failed ? ClipboardCodes.CleanupFailed : ClipboardCodes.Allowed;
            WriteDiagnostic(now, "evict", failed ? DiagnosticOutcome.Failure : DiagnosticOutcome.Success, code);
            return new(!failed, code, removed, UsedBytesUnlocked());
        }
        finally
        {
            _gate.Release();
        }
    }

    public CacheCleanupResult CleanupExact(Guid objectId)
    {
        _gate.Wait();
        try
        {
            var now = _time.GetUtcNow();
            if (!_records.TryGetValue(objectId, out var record))
                return new(true, ClipboardCodes.Empty, 0, UsedBytesUnlocked());
            if (record.LeaseCount > 0)
                return new(false, ClipboardCodes.Allowed, 0, UsedBytesUnlocked());
            if (!TryDeleteOwned(record))
            {
                WriteDiagnostic(now, "cleanup", DiagnosticOutcome.Failure, ClipboardCodes.CleanupFailed);
                return new(false, ClipboardCodes.CleanupFailed, 0, UsedBytesUnlocked());
            }

            _records.Remove(objectId);
            var persisted = TryPersistRegistry();
            var code = persisted ? ClipboardCodes.Allowed : ClipboardCodes.CleanupFailed;
            WriteDiagnostic(now, "cleanup", persisted ? DiagnosticOutcome.Success : DiagnosticOutcome.Failure, code);
            return new(persisted, code, persisted ? 1 : 0, UsedBytesUnlocked());
        }
        finally
        {
            _gate.Release();
        }
    }

    int EvictExpiredUnlocked(DateTimeOffset now, out bool deleteFailed)
    {
        var removed = 0;
        deleteFailed = false;
        foreach (var id in _records.Keys.ToArray())
        {
            var record = _records[id];
            if (record.LeaseCount > 0 || record.ExpiresAt > now)
                continue;
            if (!TryDeleteOwned(record))
            {
                deleteFailed = true;
                continue;
            }

            _records.Remove(id);
            removed++;
        }

        return removed;
    }

    bool TryDeleteOwned(CacheRecord record)
    {
        var ok = TryDeleteExact(ObjectPath(record.Id));
        if (!string.IsNullOrEmpty(record.TempName))
            ok = TryDeleteExact(Path.Combine(_root, record.TempName)) && ok;
        return ok;
    }

    static bool TryDeleteExact(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    string ObjectPath(Guid id) => Path.Combine(_root, id.ToString("N"));

    long UsedBytesUnlocked()
    {
        long total = 0;
        foreach (var record in _records.Values)
            total += record.Size;
        return total;
    }

    CacheUsage SnapshotUsage()
    {
        _gate.Wait();
        try
        {
            DateTimeOffset? earliest = null;
            foreach (var record in _records.Values)
            {
                if (earliest is null || record.ExpiresAt < earliest)
                    earliest = record.ExpiresAt;
            }

            return new CacheUsage(UsedBytesUnlocked(), _options.MaxBytes, _records.Count, earliest);
        }
        finally
        {
            _gate.Release();
        }
    }

    void LoadRegistry()
    {
        var path = Path.Combine(_root, RegistryName);
        if (!File.Exists(path))
            return;
        try
        {
            var bytes = File.ReadAllBytes(path);
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return;
            if (!document.RootElement.TryGetProperty("objects", out var objects)
                || objects.ValueKind != JsonValueKind.Array)
                return;
            foreach (var item in objects.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;
                if (!item.TryGetProperty("id", out var idNode)
                    || !Guid.TryParse(idNode.GetString(), out var id)
                    || id == Guid.Empty)
                    continue;
                var size = item.TryGetProperty("size", out var sizeNode) && sizeNode.TryGetInt64(out var rawSize)
                    ? Math.Max(0, rawSize)
                    : 0;
                var created = ReadTime(item, "created_utc");
                var expires = ReadTime(item, "expires_utc");
                var lease = item.TryGetProperty("lease", out var leaseNode) && leaseNode.TryGetInt32(out var rawLease)
                    ? Math.Max(0, rawLease)
                    : 0;
                var temp = item.TryGetProperty("temp", out var tempNode) ? tempNode.GetString() : null;
                if (!string.IsNullOrEmpty(temp)
                    && (temp.Contains('/', StringComparison.Ordinal)
                        || temp.Contains('\\', StringComparison.Ordinal)
                        || temp.Contains("..", StringComparison.Ordinal)))
                    temp = null;
                _records[id] = new CacheRecord(id, size, created, expires, lease, "ready", temp);
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }
    }

    static DateTimeOffset ReadTime(JsonElement item, string name)
    {
        if (item.TryGetProperty(name, out var node)
            && DateTimeOffset.TryParse(node.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var value))
            return value.ToUniversalTime();
        return DateTimeOffset.UnixEpoch;
    }

    bool TryPersistRegistry()
    {
        Directory.CreateDirectory(_root);
        var final = Path.Combine(_root, RegistryName);
        var temp = Path.Combine(_root, ".registry." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(
                       temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.None))
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
            {
                writer.WriteStartObject();
                writer.WriteNumber("schema_version", 1);
                writer.WriteStartArray("objects");
                foreach (var record in _records.Values)
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", record.Id.ToString("D"));
                    writer.WriteNumber("size", record.Size);
                    writer.WriteString("created_utc", record.CreatedAt.ToString("O"));
                    writer.WriteString("expires_utc", record.ExpiresAt.ToString("O"));
                    writer.WriteNumber("lease", record.LeaseCount);
                    writer.WriteString("state", record.State);
                    if (!string.IsNullOrEmpty(record.TempName))
                        writer.WriteString("temp", record.TempName);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.Flush();
                stream.Flush(true);
            }

            if (File.Exists(final))
                File.Move(temp, final, overwrite: true);
            else
                File.Move(temp, final);
            return true;
        }
        catch (Exception)
        {
            TryDeleteExact(temp);
            return false;
        }
    }

    static bool IsDiskFull(IOException ex)
    {
        var hresult = ex.HResult;
        return hresult == unchecked((int)0x80070070) || hresult == 28 || hresult == 112;
    }

    void WriteDiagnostic(DateTimeOffset now, string operation, DiagnosticOutcome outcome, string code)
    {
        _diagnostics?.TryWrite(new DiagnosticEvent(
            now,
            "cache",
            operation,
            outcome,
            code,
            1,
            null,
            UsedBytesUnlocked(),
            null,
            null));
    }

    sealed record CacheRecord(
        Guid Id,
        long Size,
        DateTimeOffset CreatedAt,
        DateTimeOffset ExpiresAt,
        int LeaseCount,
        string State,
        string? TempName);
}
