using System.Text;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Configuration;
using HerdDesk.Infrastructure.Ssh;

internal static class HostKeyTrustStoreTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("unknown candidate is not written until confirm", UnknownNotWritten),
        ("changed key is hard blocked and old record stays", ChangedNotOverwritten),
        ("malformed trust file is not overwritten", MalformedNotOverwritten)
    ];

    static HostKeyCandidate Candidate(string host = "example.test", byte seed = 1) =>
        new(host, 2222, SshHopKind.Target, "ssh-ed25519",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                Convert.FromBase64String(SshFixtures.KeyBlob(seed)))).ToLowerInvariant(),
            "SHA256:fixture", DateTimeOffset.UtcNow);

    static void UnknownNotWritten()
    {
        var root = SshFixtures.TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            Directory.CreateDirectory(paths.SettingsDirectory);
            var store = new HostKeyTrustStore(paths);
            var observed = Candidate();
            var assess = store.AssessAsync(observed).AsTask().GetAwaiter().GetResult();
            SshFixtures.Check(assess.Status == HostKeyStatus.UnknownCandidate);
            SshFixtures.Check(assess.Candidate!.VerifiedOutOfBand is false);
            SshFixtures.Check(!File.Exists(paths.KnownHostsFile));
            var confirmed = store.ConfirmUnknownAsync(observed).AsTask().GetAwaiter().GetResult();
            SshFixtures.Check(confirmed.Succeeded);
            SshFixtures.Check(File.Exists(paths.KnownHostsFile));
            var trusted = store.AssessAsync(observed).AsTask().GetAwaiter().GetResult();
            SshFixtures.Check(trusted.Status == HostKeyStatus.Trusted);
            SshFixtures.Check(trusted.KnownHostRevision == confirmed.Revision);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void ChangedNotOverwritten()
    {
        var root = SshFixtures.TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            Directory.CreateDirectory(paths.SettingsDirectory);
            var store = new HostKeyTrustStore(paths);
            var first = Candidate(seed: 1);
            SshFixtures.Check(store.ConfirmUnknownAsync(first).AsTask().GetAwaiter().GetResult().Succeeded);
            var original = File.ReadAllBytes(paths.KnownHostsFile);
            var changed = Candidate(seed: 9);
            var assess = store.AssessAsync(changed).AsTask().GetAwaiter().GetResult();
            SshFixtures.Check(assess.Status == HostKeyStatus.Changed);
            var write = store.ConfirmUnknownAsync(changed).AsTask().GetAwaiter().GetResult();
            SshFixtures.Check(!write.Succeeded);
            SshFixtures.Check(write.Code == SshCodes.HostKeyChanged);
            SshFixtures.Check(File.ReadAllBytes(paths.KnownHostsFile).SequenceEqual(original));
            var still = store.AssessAsync(first).AsTask().GetAwaiter().GetResult();
            SshFixtures.Check(still.Status == HostKeyStatus.Trusted);
            var text = Encoding.UTF8.GetString(original);
            SshFixtures.Check(!text.Contains("password", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void MalformedNotOverwritten()
    {
        var root = SshFixtures.TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            Directory.CreateDirectory(paths.SettingsDirectory);
            var damaged = "{not-json"u8.ToArray();
            File.WriteAllBytes(paths.KnownHostsFile, damaged);
            var store = new HostKeyTrustStore(paths);
            var assess = store.AssessAsync(Candidate()).AsTask().GetAwaiter().GetResult();
            SshFixtures.Check(assess.Status == HostKeyStatus.Unavailable);
            var write = store.ConfirmUnknownAsync(Candidate()).AsTask().GetAwaiter().GetResult();
            SshFixtures.Check(!write.Succeeded);
            SshFixtures.Check(write.Code == SshCodes.PersistenceFailed);
            SshFixtures.Check(File.ReadAllBytes(paths.KnownHostsFile).SequenceEqual(damaged));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
