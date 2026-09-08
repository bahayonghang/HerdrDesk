using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Ssh;

internal static class BridgeReleaseManifestTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("release targets list five closed triples from hd-008 source", FiveTargets),
        ("embedded production manifest is not_run and bound to app version", EmbeddedNotRun),
        ("schema rejects traversal duplicate protocol and hash errors", SchemaRejects)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void FiveTargets()
    {
        var root = FindRepoRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "bridge", "release", "targets.json")));
        Check(document.RootElement.GetProperty("bridge_source").GetString() == "bridge/herddesk-bridge");
        Check(document.RootElement.GetProperty("lockfile").GetString() == "bridge/Cargo.lock");
        Check(File.Exists(Path.Combine(root, "bridge", "herddesk-bridge", "Cargo.toml")));
        Check(File.Exists(Path.Combine(root, "bridge", "Cargo.lock")));
        var targets = document.RootElement.GetProperty("targets");
        Check(targets.GetArrayLength() == 5);
        string[] expected =
        [
            "x86_64-pc-windows-msvc",
            "x86_64-unknown-linux-gnu",
            "aarch64-unknown-linux-gnu",
            "x86_64-apple-darwin",
            "aarch64-apple-darwin"
        ];
        var i = 0;
        foreach (var item in targets.EnumerateArray())
        {
            Check(item.GetProperty("triple").GetString() == expected[i]);
            i++;
        }

        Check(HelperManifestParser.TryParseTargets(
            File.ReadAllBytes(Path.Combine(root, "bridge", "release", "targets.json")),
            out var parsed,
            out var code));
        Check(code == HelperCodes.Ok);
        Check(parsed.Count == 5);
        Check(!parsed[0].RemoteDeploy);
        Check(parsed[1].AtomicPublish == "hardlink_noclobber");
    }

    static void EmbeddedNotRun()
    {
        var provider = new TrustedHelperManifestProvider(ProductInfo.Version);
        Check(provider.LoadCode is null);
        Check(provider.Snapshot is not null);
        Check(provider.Snapshot!.Artifacts.Count == 5);
        Check(provider.Snapshot.Artifacts.All(item => item.BuildStatus == "not_run"));
        Check(provider.Snapshot.SourceCommit == "not_run");
        Check(provider.Snapshot.ApplicationVersion == ProductInfo.Version);
        var indexPath = Path.Combine(FindRepoRoot(), "bridge", "release", "index.json");
        using var index = JsonDocument.Parse(File.ReadAllText(indexPath));
        var expected = index.RootElement.GetProperty("manifest_sha256").GetString();
        var actual = Convert.ToHexString(SHA256.HashData(
            File.ReadAllBytes(Path.Combine(FindRepoRoot(), "bridge", "release", "manifest.json"))))
            .ToLowerInvariant();
        Check(actual == expected);
        Check(!provider.TryGetArtifact("x86_64-unknown-linux-gnu", out _, out _, out var code));
        Check(code == HelperCodes.PlatformUnsupported);
    }

    static void SchemaRejects()
    {
        var targets = File.ReadAllBytes(Path.Combine(FindRepoRoot(), "bridge", "release", "targets.json"));
        var traversal = """
            {"manifest_version":1,"bridge_version":"0.1.0","protocol_version":"1","source_commit":"not_run","application_version":"0.0.0-g0","artifacts":{"x86_64-pc-windows-msvc":{"file":"../evil","length":0,"sha256":"","os":"windows","arch":"x86_64","build_status":"not_run"}}}
            """u8.ToArray();
        Check(!HelperManifestParser.TryParseManifest(traversal,
            new TrustedHelperManifestProvider(ProductInfo.Version).Targets, out _, out var traversalCode));
        Check(traversalCode == HelperCodes.ManifestInvalid);

        var protocol = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(FindRepoRoot(), "bridge", "release", "manifest.json")))
                .Replace("\"protocol_version\": \"1\"", "\"protocol_version\": \"99\"", StringComparison.Ordinal));
        Check(!HelperManifestParser.TryParseManifest(protocol,
            new TrustedHelperManifestProvider(ProductInfo.Version).Targets, out _, out var protocolCode));
        Check(protocolCode == HelperCodes.ManifestInvalid);

        var built = HelperManifestParser.TryParseTargets(targets, out var list, out _);
        Check(built);
        var hash = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(FindRepoRoot(), "bridge", "release", "manifest.json")))
                .Replace("\"sha256\": \"\"", "\"sha256\": \"zzzz\"", StringComparison.Ordinal));
        Check(!HelperManifestParser.TryParseManifest(hash, list, out _, out var hashCode));
        Check(hashCode == HelperCodes.ManifestInvalid);
        Check(!Directory.Exists(Path.Combine(FindRepoRoot(), "tests", "Integration.Ssh")));
    }

    static string FindRepoRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "HerdDesk.slnx")))
                return directory;
            directory = Directory.GetParent(directory)?.FullName ?? "";
        }

        throw new Exception("repo_root_missing");
    }
}
