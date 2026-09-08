using System.Reflection;
using System.Security.Cryptography;
using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.Ssh;

public sealed class TrustedHelperManifestProvider : ITrustedHelperManifest
{
    public const string TargetsResource = "HerdDesk.Infrastructure.HelperRelease.targets.json";
    public const string ManifestResource = "HerdDesk.Infrastructure.HelperRelease.manifest.json";
    public const string IndexResource = "HerdDesk.Infrastructure.HelperRelease.index.json";

    private readonly Dictionary<string, (HelperArtifactRecord Artifact, byte[] Payload)> _payloads;

    public TrustedHelperManifestProvider(string applicationVersion)
        : this(applicationVersion, LoadEmbeddedBundle())
    {
    }

    internal TrustedHelperManifestProvider(string applicationVersion, HelperReleaseBundle bundle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationVersion);
        ArgumentNullException.ThrowIfNull(bundle);
        ApplicationVersion = applicationVersion;
        var code = bundle.LoadCode ?? Validate(applicationVersion, bundle);
        if (code is not null)
        {
            Targets = bundle.Targets.Count > 0 ? bundle.Targets : [];
            Snapshot = null;
            _payloads = [];
            LoadCode = code;
            return;
        }

        Targets = bundle.Targets;
        Snapshot = bundle.Snapshot;
        _payloads = bundle.Payloads;
        LoadCode = null;
    }

    public string ApplicationVersion { get; }
    public string? LoadCode { get; }
    public HelperManifestSnapshot? Snapshot { get; }
    public IReadOnlyList<HelperTarget> Targets { get; }

    internal static TrustedHelperManifestProvider FromBundle(
        string applicationVersion,
        byte[] indexJson,
        byte[] targetsJson,
        byte[] manifestJson,
        IReadOnlyDictionary<string, byte[]> payloads)
    {
        ArgumentNullException.ThrowIfNull(indexJson);
        ArgumentNullException.ThrowIfNull(targetsJson);
        ArgumentNullException.ThrowIfNull(manifestJson);
        ArgumentNullException.ThrowIfNull(payloads);
        if (!HelperManifestParser.TryParseIndex(indexJson, out var index, out var indexCode))
            return Untrusted(applicationVersion, indexCode);
        if (!HelperManifestParser.TryParseTargets(targetsJson, out var targets, out var targetCode))
            return Untrusted(applicationVersion, targetCode);
        if (!HelperManifestParser.TryParseManifest(manifestJson, targets, out var snapshot, out var manifestCode))
            return Untrusted(applicationVersion, manifestCode);
        var digest = Convert.ToHexString(SHA256.HashData(manifestJson)).ToLowerInvariant();
        var map = new Dictionary<string, (HelperArtifactRecord Artifact, byte[] Payload)>(StringComparer.Ordinal);
        foreach (var artifact in snapshot.Artifacts)
        {
            payloads.TryGetValue(artifact.Triple, out var payload);
            map[artifact.Triple] = (artifact, payload ?? []);
        }

        return new(
            applicationVersion,
            new HelperReleaseBundle(index, targets, snapshot, digest, map, null));
    }

    public bool TryGetArtifact(string triple, out HelperArtifactRecord artifact, out byte[] payload, out string code)
    {
        artifact = null!;
        payload = [];
        if (LoadCode is not null)
        {
            code = LoadCode;
            return false;
        }

        if (string.IsNullOrEmpty(triple) ||
            triple.Contains("..", StringComparison.Ordinal) ||
            triple.Contains('/', StringComparison.Ordinal) ||
            triple.Contains('\\', StringComparison.Ordinal))
        {
            code = HelperCodes.ManifestInvalid;
            return false;
        }

        if (!_payloads.TryGetValue(triple, out var item))
        {
            code = HelperCodes.PlatformUnsupported;
            return false;
        }

        artifact = item.Artifact;
        if (artifact.BuildStatus != "built")
        {
            payload = [];
            code = HelperCodes.PlatformUnsupported;
            return false;
        }

        payload = item.Payload;
        if (payload.Length != artifact.Length ||
            HelperRemoteScripts.PayloadSha256(payload) != artifact.Sha256)
        {
            payload = [];
            code = HelperCodes.ManifestUntrusted;
            return false;
        }

        code = HelperCodes.Ok;
        return true;
    }

    private static TrustedHelperManifestProvider Untrusted(string applicationVersion, string code) =>
        new(
            applicationVersion,
            new HelperReleaseBundle(
                new HelperReleaseIndex(applicationVersion, "not_run", new string('0', 64),
                    "bridge/herddesk-bridge", "bridge/Cargo.lock"),
                [],
                new HelperManifestSnapshot(1, "0", "1", "not_run", applicationVersion, []),
                new string('1', 64),
                [],
                code));

    private static string? Validate(string applicationVersion, HelperReleaseBundle bundle)
    {
        if (bundle.Index.ApplicationVersion != applicationVersion ||
            bundle.Snapshot.ApplicationVersion != applicationVersion)
            return HelperCodes.ManifestUntrusted;
        if (bundle.Index.ManifestSha256 != bundle.ManifestSha256)
            return HelperCodes.ManifestUntrusted;
        if (bundle.Index.SourceCommit != bundle.Snapshot.SourceCommit)
            return HelperCodes.ManifestUntrusted;
        foreach (var artifact in bundle.Snapshot.Artifacts)
        {
            if (artifact.BuildStatus != "built")
                continue;
            if (!bundle.Payloads.TryGetValue(artifact.Triple, out var item))
                return HelperCodes.ManifestUntrusted;
            if (item.Payload.Length != artifact.Length ||
                HelperRemoteScripts.PayloadSha256(item.Payload) != artifact.Sha256)
                return HelperCodes.ManifestUntrusted;
        }

        return null;
    }

    private static HelperReleaseBundle LoadEmbeddedBundle()
    {
        var assembly = typeof(TrustedHelperManifestProvider).Assembly;
        var indexJson = ReadResource(assembly, IndexResource);
        var targetsJson = ReadResource(assembly, TargetsResource);
        var manifestJson = ReadResource(assembly, ManifestResource);
        if (indexJson.Length == 0 || targetsJson.Length == 0 || manifestJson.Length == 0)
        {
            return new(
                new HelperReleaseIndex("missing", "not_run", new string('0', 64),
                    "bridge/herddesk-bridge", "bridge/Cargo.lock"),
                [],
                new HelperManifestSnapshot(1, "0", "1", "not_run", "missing", []),
                new string('1', 64),
                [],
                HelperCodes.ManifestUntrusted);
        }

        if (!HelperManifestParser.TryParseIndex(indexJson, out var index, out var indexCode))
            return Empty(indexCode);
        if (!HelperManifestParser.TryParseTargets(targetsJson, out var targets, out var targetCode))
            return Empty(targetCode);
        if (!HelperManifestParser.TryParseManifest(manifestJson, targets, out var snapshot, out var manifestCode))
            return Empty(manifestCode);

        var digest = Convert.ToHexString(SHA256.HashData(manifestJson)).ToLowerInvariant();
        return new(index, targets, snapshot, digest, [], null);
    }

    private static HelperReleaseBundle Empty(string code) =>
        new(
            new HelperReleaseIndex("invalid", "not_run", new string('0', 64),
                "bridge/herddesk-bridge", "bridge/Cargo.lock"),
            [],
            new HelperManifestSnapshot(1, "0", "1", "not_run", "invalid", []),
            new string('1', 64),
            [],
            code);

    private static byte[] ReadResource(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name);
        if (stream is null)
            return [];
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}

internal sealed record HelperReleaseBundle(
    HelperReleaseIndex Index,
    IReadOnlyList<HelperTarget> Targets,
    HelperManifestSnapshot Snapshot,
    string ManifestSha256,
    Dictionary<string, (HelperArtifactRecord Artifact, byte[] Payload)> Payloads,
    string? LoadCode);
