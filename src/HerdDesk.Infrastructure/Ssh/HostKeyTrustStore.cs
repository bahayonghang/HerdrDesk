using System.Text;
using System.Text.Json;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Configuration;

namespace HerdDesk.Infrastructure.Ssh;

internal sealed class HostKeyStoreHooks
{
    public Func<HostKeyDocument, byte[]>? Serialize { get; init; }
    public Action<string, string, string?>? Commit { get; init; }
}

internal sealed record KnownHostRecord(
    string Host,
    int Port,
    SshHopKind Hop,
    string KeyType,
    string KeyBlobSha256,
    string FingerprintSha256,
    bool VerifiedOutOfBand,
    DateTimeOffset ConfirmedUtc);

internal sealed record HostKeyDocument(int SchemaVersion, long Revision, IReadOnlyList<KnownHostRecord> Hosts)
{
    public const int CurrentSchemaVersion = 1;

    public static HostKeyDocument Empty { get; } =
        new(CurrentSchemaVersion, 0, Array.Empty<KnownHostRecord>());
}

internal sealed class HostKeyTrustStore : ISshHostKeyStore
{
    public const int MaxDocumentBytes = 256 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly HashSet<string> RootNames = new(StringComparer.Ordinal)
    {
        "schema_version", "revision", "hosts"
    };
    private static readonly HashSet<string> HostNames = new(StringComparer.Ordinal)
    {
        "host", "port", "hop_kind", "key_type", "key_blob_sha256", "fingerprint_sha256",
        "verified_out_of_band", "confirmed_utc"
    };

    private readonly AppDataPaths _paths;
    private readonly HostKeyStoreHooks _hooks;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public HostKeyTrustStore(AppDataPaths paths, HostKeyStoreHooks? hooks = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = paths;
        _hooks = hooks ?? new HostKeyStoreHooks();
    }

    public async ValueTask<HostKeyAssessment> AssessAsync(
        HostKeyCandidate observed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observed);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var (kind, document) = LoadState();
            if (kind == TrustLoadKind.Unreadable)
            {
                return new(
                    HostKeyStatus.Unavailable,
                    observed with { Host = NormalizeHost(observed.Host), VerifiedOutOfBand = false },
                    0);
            }

            var host = NormalizeHost(observed.Host);
            var match = document.Hosts.FirstOrDefault(item =>
                item.Host == host && item.Port == observed.Port && item.Hop == observed.Hop);
            if (match is null)
            {
                return new(
                    HostKeyStatus.UnknownCandidate,
                    observed with { Host = host, VerifiedOutOfBand = false },
                    document.Revision);
            }

            if (string.Equals(match.KeyBlobSha256, observed.KeyBlobSha256, StringComparison.Ordinal) &&
                string.Equals(match.KeyType, observed.KeyType, StringComparison.Ordinal))
            {
                return new(HostKeyStatus.Trusted, observed with { Host = host, VerifiedOutOfBand = true },
                    document.Revision);
            }

            return new(
                HostKeyStatus.Changed,
                observed with { Host = host, VerifiedOutOfBand = false },
                document.Revision);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<HostKeyTrustWriteResult> ConfirmUnknownAsync(
        HostKeyCandidate candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.VerifiedOutOfBand)
            return new(null, SshCodes.ProfileInvalid);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var (kind, document) = LoadState();
            if (kind == TrustLoadKind.Unreadable)
                return new(null, SshCodes.PersistenceFailed);

            var host = NormalizeHost(candidate.Host);
            var existing = document.Hosts.FirstOrDefault(item =>
                item.Host == host && item.Port == candidate.Port && item.Hop == candidate.Hop);
            if (existing is not null)
            {
                if (!string.Equals(existing.KeyBlobSha256, candidate.KeyBlobSha256, StringComparison.Ordinal) ||
                    !string.Equals(existing.KeyType, candidate.KeyType, StringComparison.Ordinal))
                    return new(null, SshCodes.HostKeyChanged);
                return new(document.Revision, null);
            }

            var record = new KnownHostRecord(
                host,
                candidate.Port,
                candidate.Hop,
                candidate.KeyType,
                candidate.KeyBlobSha256,
                candidate.FingerprintSha256,
                true,
                DateTimeOffset.UtcNow);
            var next = new HostKeyDocument(
                HostKeyDocument.CurrentSchemaVersion,
                document.Revision + 1,
                [.. document.Hosts, record]);
            return CommitDocument(next);
        }
        finally
        {
            _gate.Release();
        }
    }

    private enum TrustLoadKind
    {
        Missing,
        Loaded,
        Unreadable
    }

    private (TrustLoadKind Kind, HostKeyDocument Document) LoadState()
    {
        if (!File.Exists(_paths.KnownHostsFile))
            return (TrustLoadKind.Missing, HostKeyDocument.Empty);

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(_paths.KnownHostsFile);
        }
        catch (IOException)
        {
            return (TrustLoadKind.Unreadable, HostKeyDocument.Empty);
        }

        return TryParse(bytes, out var document)
            ? (TrustLoadKind.Loaded, document)
            : (TrustLoadKind.Unreadable, HostKeyDocument.Empty);
    }

    private HostKeyTrustWriteResult CommitDocument(HostKeyDocument document)
    {
        byte[] bytes;
        try
        {
            bytes = _hooks.Serialize is not null ? _hooks.Serialize(document) : Serialize(document);
        }
        catch (Exception)
        {
            return new(null, SshCodes.PersistenceFailed);
        }

        Directory.CreateDirectory(_paths.SettingsDirectory);
        var temp = Path.Combine(
            _paths.SettingsDirectory,
            $".ssh-known-hosts.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.None))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }

            var replaceExisting = File.Exists(_paths.KnownHostsFile);
            if (_hooks.Commit is not null)
            {
                _hooks.Commit(temp, _paths.KnownHostsFile,
                    replaceExisting ? _paths.KnownHostsBackupFile : null);
            }
            else if (!replaceExisting)
            {
                File.Move(temp, _paths.KnownHostsFile);
            }
            else
            {
                File.Replace(temp, _paths.KnownHostsFile, _paths.KnownHostsBackupFile,
                    ignoreMetadataErrors: true);
            }

            return new(document.Revision, null);
        }
        catch (Exception)
        {
            TryDelete(temp);
            return new(null, SshCodes.PersistenceFailed);
        }
    }

    internal static byte[] Serialize(HostKeyDocument document)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", document.SchemaVersion);
            writer.WriteNumber("revision", document.Revision);
            writer.WritePropertyName("hosts");
            writer.WriteStartArray();
            foreach (var host in document.Hosts)
            {
                writer.WriteStartObject();
                writer.WriteString("host", host.Host);
                writer.WriteNumber("port", host.Port);
                writer.WriteString("hop_kind", host.Hop == SshHopKind.ProxyJump ? "proxy_jump" : "target");
                writer.WriteString("key_type", host.KeyType);
                writer.WriteString("key_blob_sha256", host.KeyBlobSha256);
                writer.WriteString("fingerprint_sha256", host.FingerprintSha256);
                writer.WriteBoolean("verified_out_of_band", host.VerifiedOutOfBand);
                writer.WriteString("confirmed_utc", host.ConfirmedUtc.ToString("O"));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    internal static bool TryParse(byte[] json, out HostKeyDocument document)
    {
        document = null!;
        if (json.Length is 0 or > MaxDocumentBytes)
            return false;
        try
        {
            _ = StrictUtf8.GetCharCount(json);
            using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;
            if (!TryNames(root, RootNames))
                return false;
            if (!root.TryGetProperty("schema_version", out var versionEl) ||
                !versionEl.TryGetInt32(out var version) ||
                version != HostKeyDocument.CurrentSchemaVersion)
                return false;
            if (!root.TryGetProperty("revision", out var revisionEl) ||
                !revisionEl.TryGetInt64(out var revision) || revision < 0)
                return false;
            if (!root.TryGetProperty("hosts", out var hostsEl) || hostsEl.ValueKind != JsonValueKind.Array)
                return false;
            var hosts = new List<KnownHostRecord>();
            foreach (var item in hostsEl.EnumerateArray())
            {
                if (!TryReadHost(item, out var record))
                    return false;
                hosts.Add(record);
            }

            document = new HostKeyDocument(version, revision, hosts);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException or InvalidOperationException
                                       or FormatException)
        {
            return false;
        }
    }

    private static bool TryReadHost(JsonElement element, out KnownHostRecord record)
    {
        record = null!;
        if (element.ValueKind != JsonValueKind.Object || !TryNames(element, HostNames))
            return false;
        if (!element.TryGetProperty("host", out var hostEl) || hostEl.ValueKind != JsonValueKind.String)
            return false;
        if (!element.TryGetProperty("port", out var portEl) || !portEl.TryGetInt32(out var port))
            return false;
        if (!element.TryGetProperty("hop_kind", out var hopEl) || hopEl.ValueKind != JsonValueKind.String)
            return false;
        if (!element.TryGetProperty("key_type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
            return false;
        if (!element.TryGetProperty("key_blob_sha256", out var blobEl) || blobEl.ValueKind != JsonValueKind.String)
            return false;
        if (!element.TryGetProperty("fingerprint_sha256", out var fpEl) || fpEl.ValueKind != JsonValueKind.String)
            return false;
        if (!element.TryGetProperty("verified_out_of_band", out var verifiedEl) ||
            (verifiedEl.ValueKind != JsonValueKind.True && verifiedEl.ValueKind != JsonValueKind.False))
            return false;
        if (!element.TryGetProperty("confirmed_utc", out var utcEl) || utcEl.ValueKind != JsonValueKind.String)
            return false;
        var hopName = hopEl.GetString();
        var hop = hopName switch
        {
            "target" => SshHopKind.Target,
            "proxy_jump" => SshHopKind.ProxyJump,
            _ => (SshHopKind?)null
        };
        if (hop is null)
            return false;
        if (!DateTimeOffset.TryParse(utcEl.GetString(), out var utc))
            return false;
        var host = hostEl.GetString();
        var keyType = typeEl.GetString();
        var blob = blobEl.GetString();
        var fingerprint = fpEl.GetString();
        if (host is null || keyType is null || blob is null || fingerprint is null)
            return false;
        if (AtomicConfigurationStore.LooksLikeSecret(host) ||
            AtomicConfigurationStore.LooksLikeSecret(keyType) ||
            AtomicConfigurationStore.LooksLikeSecret(blob) ||
            AtomicConfigurationStore.LooksLikeSecret(fingerprint))
            return false;
        record = new KnownHostRecord(
            NormalizeHost(host), port, hop.Value, keyType, blob, fingerprint, verifiedEl.GetBoolean(), utc);
        return true;
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

    internal static string NormalizeHost(string host) => host.Trim().ToLowerInvariant();

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
