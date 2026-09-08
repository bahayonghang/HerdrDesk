using System.Text;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Configuration;
using HerdDesk.Infrastructure.Ssh;

internal static class SshRecoveryBlockStoreTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("auth block is per device and ignores session order", DeviceLevelAuthBlock),
        ("atomic failure leaves previous bytes", AtomicFailure),
        ("file lock failure does not write", FileLockFailure),
        ("restart reloads the persisted block", RestartReload),
        ("secrets and timer fields are not persisted", NoSecretsOrTimers)
    ];

    static DeviceId DeviceA() => new(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
    static DeviceId DeviceB() => new(Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff"));

    static RecoveryBlockSnapshot Auth(DeviceId device, long profile = 3, long credential = 5) =>
        new(device, profile, RecoveryCause.AuthenticationBlocked, credential, null,
            RecoveryCodes.AuthenticationActionRequired);

    static RecoveryBlockSnapshot Host(DeviceId device, long profile = 3, long known = 8) =>
        new(device, profile, RecoveryCause.HostKeyChanged, null, known,
            RecoveryCodes.HostKeyReviewRequired);

    static void DeviceLevelAuthBlock()
    {
        var root = SshFixtures.TempRoot();
        try
        {
            var store = new SshRecoveryBlockStore(AppDataPaths.FromRoot(root));
            SshFixtures.Check(store.SaveAsync(Auth(DeviceA())).AsTask().GetAwaiter().GetResult().Succeeded);
            SshFixtures.Check(store.SaveAsync(Host(DeviceB())).AsTask().GetAwaiter().GetResult().Succeeded);
            var a = store.ReadAsync(DeviceA()).AsTask().GetAwaiter().GetResult();
            var b = store.ReadAsync(DeviceB()).AsTask().GetAwaiter().GetResult();
            SshFixtures.Check(a is not null);
            SshFixtures.Check(a!.Kind == RecoveryCause.AuthenticationBlocked);
            SshFixtures.Check(a.BlockedProfileRevision == 3);
            SshFixtures.Check(a.CredentialRevision == 5);
            SshFixtures.Check(b is not null);
            SshFixtures.Check(b!.Kind == RecoveryCause.HostKeyChanged);
            SshFixtures.Check(store.SaveAsync(Auth(DeviceA(), 4, 9)).AsTask().GetAwaiter().GetResult().Succeeded);
            var again = store.ReadAsync(DeviceA()).AsTask().GetAwaiter().GetResult();
            SshFixtures.Check(again!.BlockedProfileRevision == 4);
            SshFixtures.Check(again.CredentialRevision == 9);
            SshFixtures.Check(store.ReadAsync(DeviceB()).AsTask().GetAwaiter().GetResult()!.Kind ==
                              RecoveryCause.HostKeyChanged);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void AtomicFailure()
    {
        var root = SshFixtures.TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            Directory.CreateDirectory(paths.SettingsDirectory);
            var store = new SshRecoveryBlockStore(paths);
            SshFixtures.Check(store.SaveAsync(Auth(DeviceA())).AsTask().GetAwaiter().GetResult().Succeeded);
            var original = File.ReadAllBytes(paths.RecoveryBlocksFile);
            var failing = new SshRecoveryBlockStore(paths, new RecoveryBlockStoreHooks
            {
                Commit = (_, _, _) => throw new IOException("replace_boom")
            });
            var result = failing.SaveAsync(Auth(DeviceA(), 9, 12)).AsTask().GetAwaiter().GetResult();
            SshFixtures.Check(!result.Succeeded);
            SshFixtures.Check(result.Code == RecoveryCodes.PersistenceFailed);
            SshFixtures.Check(File.ReadAllBytes(paths.RecoveryBlocksFile).SequenceEqual(original));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void FileLockFailure()
    {
        var root = SshFixtures.TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            Directory.CreateDirectory(paths.SettingsDirectory);
            var store = new SshRecoveryBlockStore(paths);
            SshFixtures.Check(store.SaveAsync(Auth(DeviceA())).AsTask().GetAwaiter().GetResult().Succeeded);
            using var held = new FileStream(
                paths.RecoveryBlocksLockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var blocked = new SshRecoveryBlockStore(paths);
            var result = blocked.SaveAsync(Auth(DeviceA(), 8, 8)).AsTask().GetAwaiter().GetResult();
            SshFixtures.Check(!result.Succeeded);
            SshFixtures.Check(result.Code == RecoveryCodes.PersistenceFailed);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void RestartReload()
    {
        var root = SshFixtures.TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            var store = new SshRecoveryBlockStore(paths);
            SshFixtures.Check(store.SaveAsync(Host(DeviceA())).AsTask().GetAwaiter().GetResult().Succeeded);
            var reloaded = new SshRecoveryBlockStore(paths);
            var block = reloaded.ReadAsync(DeviceA()).AsTask().GetAwaiter().GetResult();
            SshFixtures.Check(block is not null);
            SshFixtures.Check(block!.Kind == RecoveryCause.HostKeyChanged);
            SshFixtures.Check(block.KnownHostRevision == 8);
            SshFixtures.Check(block.PublicCode == RecoveryCodes.HostKeyReviewRequired);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void NoSecretsOrTimers()
    {
        var root = SshFixtures.TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            var store = new SshRecoveryBlockStore(paths);
            SshFixtures.Check(store.SaveAsync(Auth(DeviceA())).AsTask().GetAwaiter().GetResult().Succeeded);
            var text = File.ReadAllText(paths.RecoveryBlocksFile, Encoding.UTF8);
            SshFixtures.Check(!text.Contains("password", StringComparison.OrdinalIgnoreCase));
            SshFixtures.Check(!text.Contains("stderr", StringComparison.OrdinalIgnoreCase));
            SshFixtures.Check(!text.Contains("due_at", StringComparison.Ordinal));
            SshFixtures.Check(!text.Contains("attempt", StringComparison.Ordinal));
            SshFixtures.Check(!text.Contains("timer", StringComparison.Ordinal));
            SshFixtures.Check(!text.Contains("input", StringComparison.Ordinal));
            SshFixtures.Check(!text.Contains("-----BEGIN", StringComparison.Ordinal));
            var secret = Encoding.UTF8.GetBytes(
                """{"schema_version":1,"revision":1,"blocks":[{"device":"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee","kind":"authentication_blocked","blocked_profile_revision":1,"credential_revision":1,"known_host_revision":null,"public_code":"password=supersecret-password"}]}""");
            File.WriteAllBytes(paths.RecoveryBlocksFile, secret);
            var loaded = new SshRecoveryBlockStore(paths).ReadAsync(DeviceA()).AsTask().GetAwaiter().GetResult();
            SshFixtures.Check(loaded is null);
            SshFixtures.Check(File.ReadAllBytes(paths.RecoveryBlocksFile).SequenceEqual(secret));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
