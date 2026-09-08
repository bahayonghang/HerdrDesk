using System.Security.Cryptography;
using System.Text;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Ssh;

internal static class TrustedHelperManifestProviderTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("embedded index digest binds application version", EmbeddedBinds),
        ("arbitrary path constructor is absent", NoPathProvider),
        ("tampered payload is rejected at load", TamperedPayload),
        ("tampered manifest digest is rejected", TamperedManifest),
        ("path traversal artifact name is rejected", PathTraversal),
        ("duplicate target is rejected", DuplicateTarget),
        ("protocol mismatch is rejected", ProtocolMismatch)
    ];

    static void EmbeddedBinds()
    {
        var provider = new TrustedHelperManifestProvider(HelperFixtures.AppVersion);
        HelperFixtures.Check(provider.LoadCode is null);
        HelperFixtures.Check(provider.Snapshot is not null);
        HelperFixtures.Check(provider.Snapshot!.ApplicationVersion == HelperFixtures.AppVersion);
        HelperFixtures.Check(provider.Targets.Count == 5);
        HelperFixtures.Check(provider.Snapshot.Artifacts.Count == 5);
        HelperFixtures.Check(provider.Snapshot.Artifacts.All(item => item.BuildStatus == "not_run"));
        HelperFixtures.Check(!provider.TryGetArtifact(HelperFixtures.LinuxX64, out _, out _, out var code));
        HelperFixtures.Check(code == HelperCodes.PlatformUnsupported);
        var wrong = new TrustedHelperManifestProvider("9.9.9-wrong");
        HelperFixtures.Check(wrong.LoadCode == HelperCodes.ManifestUntrusted);
        HelperFixtures.Check(wrong.Snapshot is null);
    }

    static void NoPathProvider()
    {
        foreach (var ctor in typeof(TrustedHelperManifestProvider).GetConstructors())
        {
            foreach (var parameter in ctor.GetParameters())
            {
                HelperFixtures.Check(parameter.Name is not "path" and not "manifestPath" and not "directory");
                HelperFixtures.Check(parameter.ParameterType != typeof(FileInfo));
            }
        }

        foreach (var method in typeof(TrustedHelperManifestProvider).GetMethods())
        {
            HelperFixtures.Check(method.Name is not "FromPath" and not "LoadFromPath" and not "FromDirectory");
            foreach (var parameter in method.GetParameters())
                HelperFixtures.Check(parameter.Name is not "path" and not "manifestPath");
        }
    }

    static void TamperedPayload()
    {
        var payload = HelperFixtures.Payload.ToArray();
        var manifest = HelperFixtures.ManifestWithBuilt(payload, HelperFixtures.LinuxX64);
        var digest = Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant();
        payload[0] ^= 0xff;
        var provider = TrustedHelperManifestProvider.FromBundle(
            HelperFixtures.AppVersion,
            HelperFixtures.IndexJson(HelperFixtures.AppVersion, digest),
            HelperFixtures.TargetsJson(),
            manifest,
            new Dictionary<string, byte[]> { [HelperFixtures.LinuxX64] = payload });
        HelperFixtures.Check(provider.LoadCode == HelperCodes.ManifestUntrusted);
        HelperFixtures.Check(!provider.TryGetArtifact(HelperFixtures.LinuxX64, out _, out _, out var code));
        HelperFixtures.Check(code == HelperCodes.ManifestUntrusted);
    }

    static void TamperedManifest()
    {
        var manifest = HelperFixtures.ManifestWithBuilt(HelperFixtures.Payload, HelperFixtures.LinuxX64);
        var digest = Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant();
        var flipped = digest.ToCharArray();
        flipped[0] = flipped[0] == 'a' ? 'b' : 'a';
        var provider = TrustedHelperManifestProvider.FromBundle(
            HelperFixtures.AppVersion,
            HelperFixtures.IndexJson(HelperFixtures.AppVersion, new string(flipped)),
            HelperFixtures.TargetsJson(),
            manifest,
            new Dictionary<string, byte[]> { [HelperFixtures.LinuxX64] = HelperFixtures.Payload });
        HelperFixtures.Check(provider.LoadCode == HelperCodes.ManifestUntrusted);
    }

    static void PathTraversal()
    {
        var json = Encoding.UTF8.GetString(HelperFixtures.ManifestWithBuilt(HelperFixtures.Payload));
        json = json.Replace(
            "herddesk-bridge-x86_64-unknown-linux-gnu",
            "../evil-bridge",
            StringComparison.Ordinal);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        var provider = TrustedHelperManifestProvider.FromBundle(
            HelperFixtures.AppVersion,
            HelperFixtures.IndexJson(HelperFixtures.AppVersion, digest),
            HelperFixtures.TargetsJson(),
            Encoding.UTF8.GetBytes(json),
            new Dictionary<string, byte[]> { [HelperFixtures.LinuxX64] = HelperFixtures.Payload });
        HelperFixtures.Check(provider.LoadCode == HelperCodes.ManifestInvalid);
    }

    static void DuplicateTarget()
    {
        var json = """
            {"schema_version":1,"bridge_source":"bridge/herddesk-bridge","lockfile":"bridge/Cargo.lock","targets":[
            {"triple":"x86_64-pc-windows-msvc","os":"windows","arch":"x86_64","uname_s":"windows","uname_m":"x86_64","artifact":"herddesk-bridge-x86_64-pc-windows-msvc.exe","remote_deploy":false,"atomic_publish":"unsupported"},
            {"triple":"x86_64-pc-windows-msvc","os":"windows","arch":"x86_64","uname_s":"windows","uname_m":"x86_64","artifact":"herddesk-bridge-x86_64-pc-windows-msvc.exe","remote_deploy":false,"atomic_publish":"unsupported"},
            {"triple":"x86_64-unknown-linux-gnu","os":"linux","arch":"x86_64","uname_s":"linux","uname_m":"x86_64","artifact":"herddesk-bridge-x86_64-unknown-linux-gnu","remote_deploy":true,"atomic_publish":"hardlink_noclobber"},
            {"triple":"aarch64-unknown-linux-gnu","os":"linux","arch":"aarch64","uname_s":"linux","uname_m":"aarch64","artifact":"herddesk-bridge-aarch64-unknown-linux-gnu","remote_deploy":true,"atomic_publish":"hardlink_noclobber"},
            {"triple":"x86_64-apple-darwin","os":"darwin","arch":"x86_64","uname_s":"darwin","uname_m":"x86_64","artifact":"herddesk-bridge-x86_64-apple-darwin","remote_deploy":true,"atomic_publish":"hardlink_noclobber"}
            ]}
            """;
        HelperFixtures.Check(!HelperManifestParser.TryParseTargets(Encoding.UTF8.GetBytes(json), out _, out var code));
        HelperFixtures.Check(code == HelperCodes.ManifestInvalid);
    }

    static void ProtocolMismatch()
    {
        var json = Encoding.UTF8.GetString(HelperFixtures.ManifestWithBuilt(HelperFixtures.Payload));
        json = json.Replace("\"protocol_version\": \"1\"", "\"protocol_version\": \"2\"", StringComparison.Ordinal);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        var provider = TrustedHelperManifestProvider.FromBundle(
            HelperFixtures.AppVersion,
            HelperFixtures.IndexJson(HelperFixtures.AppVersion, digest),
            HelperFixtures.TargetsJson(),
            Encoding.UTF8.GetBytes(json),
            new Dictionary<string, byte[]> { [HelperFixtures.LinuxX64] = HelperFixtures.Payload });
        HelperFixtures.Check(provider.LoadCode == HelperCodes.ManifestInvalid);
    }
}
