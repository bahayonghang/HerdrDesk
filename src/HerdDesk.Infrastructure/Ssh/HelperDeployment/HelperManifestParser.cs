using System.Text;
using System.Text.Json;
using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.Ssh;

internal sealed record HelperReleaseIndex(
    string ApplicationVersion,
    string SourceCommit,
    string ManifestSha256,
    string BridgeSource,
    string Lockfile);

internal static class HelperManifestParser
{
    public const int MaxDocumentBytes = 256 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        MaxDepth = 64,
        AllowDuplicateProperties = false
    };

    private static readonly HashSet<string> IndexNames = new(StringComparer.Ordinal)
    {
        "application_version", "source_commit", "manifest_sha256", "bridge_source", "lockfile"
    };

    private static readonly HashSet<string> TargetRootNames = new(StringComparer.Ordinal)
    {
        "schema_version", "bridge_source", "lockfile", "targets"
    };

    private static readonly HashSet<string> TargetNames = new(StringComparer.Ordinal)
    {
        "triple", "os", "arch", "uname_s", "uname_m", "artifact", "remote_deploy", "atomic_publish"
    };

    private static readonly HashSet<string> ManifestRootNames = new(StringComparer.Ordinal)
    {
        "manifest_version", "bridge_version", "protocol_version", "source_commit",
        "application_version", "artifacts"
    };

    private static readonly HashSet<string> ArtifactNames = new(StringComparer.Ordinal)
    {
        "file", "length", "sha256", "os", "arch", "build_status"
    };

    public static readonly IReadOnlyList<string> RequiredTriples =
    [
        "x86_64-pc-windows-msvc",
        "x86_64-unknown-linux-gnu",
        "aarch64-unknown-linux-gnu",
        "x86_64-apple-darwin",
        "aarch64-apple-darwin"
    ];

    public static bool TryParseIndex(byte[] json, out HelperReleaseIndex index, out string code)
    {
        index = null!;
        code = HelperCodes.ManifestInvalid;
        if (!TryParseObject(json, IndexNames, out var root))
            return false;
        if (!TryString(root, "application_version", out var app) || !HelperRemoteScripts.IsAppVersion(app))
            return false;
        if (!TryString(root, "source_commit", out var commit) || !HelperRemoteScripts.IsCommit(commit))
            return false;
        if (!TryString(root, "manifest_sha256", out var digest) || !HelperRemoteScripts.IsSha256(digest))
            return false;
        if (!TryString(root, "bridge_source", out var source) || source != "bridge/herddesk-bridge")
            return false;
        if (!TryString(root, "lockfile", out var lockfile) || lockfile != "bridge/Cargo.lock")
            return false;
        index = new(app, commit, digest, source, lockfile);
        code = HelperCodes.Ok;
        return true;
    }

    public static bool TryParseTargets(byte[] json, out IReadOnlyList<HelperTarget> targets, out string code)
    {
        targets = [];
        code = HelperCodes.ManifestInvalid;
        if (!TryParseObject(json, TargetRootNames, out var root))
            return false;
        if (!root.TryGetProperty("schema_version", out var versionEl) ||
            !versionEl.TryGetInt32(out var version) || version != 1)
            return false;
        if (!TryString(root, "bridge_source", out var source) || source != "bridge/herddesk-bridge")
            return false;
        if (!TryString(root, "lockfile", out var lockfile) || lockfile != "bridge/Cargo.lock")
            return false;
        if (!root.TryGetProperty("targets", out var array) || array.ValueKind != JsonValueKind.Array)
            return false;
        var list = new List<HelperTarget>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in array.EnumerateArray())
        {
            if (!TryReadTarget(item, out var target))
                return false;
            if (!seen.Add(target.Triple))
                return false;
            list.Add(target);
        }

        if (list.Count != RequiredTriples.Count)
            return false;
        for (var i = 0; i < RequiredTriples.Count; i++)
        {
            if (list[i].Triple != RequiredTriples[i])
                return false;
        }

        targets = list;
        code = HelperCodes.Ok;
        return true;
    }

    public static bool TryParseManifest(
        byte[] json,
        IReadOnlyList<HelperTarget> targets,
        out HelperManifestSnapshot snapshot,
        out string code)
    {
        snapshot = null!;
        code = HelperCodes.ManifestInvalid;
        if (!TryParseObject(json, ManifestRootNames, out var root))
            return false;
        if (!root.TryGetProperty("manifest_version", out var versionEl) ||
            !versionEl.TryGetInt32(out var version) || version != 1)
            return false;
        if (!TryString(root, "bridge_version", out var bridge) || !HelperRemoteScripts.IsSafeVersion(bridge))
            return false;
        if (!TryString(root, "protocol_version", out var protocol) || protocol != "1")
            return false;
        if (!TryString(root, "source_commit", out var commit) || !HelperRemoteScripts.IsCommit(commit))
            return false;
        if (!TryString(root, "application_version", out var app) || !HelperRemoteScripts.IsAppVersion(app))
            return false;
        if (!root.TryGetProperty("artifacts", out var artifactsEl) ||
            artifactsEl.ValueKind != JsonValueKind.Object)
            return false;
        if (!TryNames(artifactsEl, [.. targets.Select(item => item.Triple)]))
            return false;

        var artifacts = new List<HelperArtifactRecord>();
        foreach (var target in targets)
        {
            if (!artifactsEl.TryGetProperty(target.Triple, out var item))
                return false;
            if (!TryReadArtifact(item, target, out var record))
                return false;
            artifacts.Add(record);
        }

        snapshot = new(version, bridge, protocol, commit, app, artifacts);
        code = HelperCodes.Ok;
        return true;
    }

    private static bool TryReadTarget(JsonElement element, out HelperTarget target)
    {
        target = null!;
        if (element.ValueKind != JsonValueKind.Object || !TryNames(element, TargetNames))
            return false;
        if (!TryString(element, "triple", out var triple))
            return false;
        if (!TryString(element, "os", out var os) || !TryString(element, "arch", out var arch))
            return false;
        if (!TryString(element, "uname_s", out var unameS) || !TryString(element, "uname_m", out var unameM))
            return false;
        if (!TryString(element, "artifact", out var artifact) || !HelperRemoteScripts.IsSafeFileName(artifact))
            return false;
        if (!element.TryGetProperty("remote_deploy", out var deployEl) ||
            (deployEl.ValueKind != JsonValueKind.True && deployEl.ValueKind != JsonValueKind.False))
            return false;
        if (!TryString(element, "atomic_publish", out var atomic) ||
            atomic is not "hardlink_noclobber" and not "unsupported")
            return false;
        if (triple == "x86_64-pc-windows-msvc")
        {
            if (deployEl.GetBoolean() || atomic != "unsupported")
                return false;
        }
        else if (!HelperRemoteScripts.IsDeployableTriple(triple) || !deployEl.GetBoolean() ||
                 atomic != "hardlink_noclobber")
        {
            return false;
        }

        target = new(triple, os, arch, unameS, unameM, artifact, deployEl.GetBoolean(), atomic);
        return true;
    }

    private static bool TryReadArtifact(JsonElement element, HelperTarget target, out HelperArtifactRecord record)
    {
        record = null!;
        if (element.ValueKind != JsonValueKind.Object || !TryNames(element, ArtifactNames))
            return false;
        if (!TryString(element, "file", out var file) || file != target.ArtifactFileName ||
            !HelperRemoteScripts.IsSafeFileName(file))
            return false;
        if (!element.TryGetProperty("length", out var lengthEl) || !lengthEl.TryGetInt64(out var length) ||
            length < 0 || length > HelperRemoteScripts.MaxPayloadBytes)
            return false;
        if (!TryString(element, "sha256", out var sha))
            return false;
        if (!TryString(element, "os", out var os) || os != target.Os)
            return false;
        if (!TryString(element, "arch", out var arch) || arch != target.Arch)
            return false;
        if (!TryString(element, "build_status", out var status) ||
            status is not "not_run" and not "built" and not "unavailable")
            return false;
        if (status == "built")
        {
            if (!HelperRemoteScripts.IsPositiveLength(length) || !HelperRemoteScripts.IsSha256(sha))
                return false;
        }
        else if (length != 0 || sha.Length != 0)
        {
            return false;
        }

        record = new(target.Triple, file, length, sha, os, arch, status);
        return true;
    }

    private static bool TryParseObject(byte[] json, HashSet<string> names, out JsonElement root)
    {
        root = default;
        if (json.Length is 0 or > MaxDocumentBytes)
            return false;
        try
        {
            _ = StrictUtf8.GetCharCount(json);
            using var document = JsonDocument.Parse(json, ParseOptions);
            root = document.RootElement.Clone();
            return root.ValueKind == JsonValueKind.Object && TryNames(root, names);
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException or InvalidOperationException
                                       or FormatException)
        {
            return false;
        }
    }

    private static bool TryString(JsonElement element, string name, out string value)
    {
        value = "";
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
            return false;
        try
        {
            var text = property.GetString();
            if (text is null)
                return false;
            value = text;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryNames(JsonElement element, HashSet<string> allowed)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name) || !seen.Add(property.Name))
                return false;
        }

        return seen.Count == allowed.Count;
    }
}
