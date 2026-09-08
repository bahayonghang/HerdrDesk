using System.Text;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Configuration;

internal static class SshDeviceProfileStoreTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("ssh variant and two sessions round-trip on one device id", RoundTrip),
        ("label edit keeps device id and ssh profile revision", LabelKeepsRevision),
        ("secret identity content is rejected and not written", SecretRejected),
        ("unknown auth mode persists and stays unmapped", UnknownAuthPersists)
    ];

    static void RoundTrip()
    {
        var root = SshFixtures.TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            var store = new AtomicConfigurationStore(paths);
            var herdr = Path.Combine(paths.Root, "herdr");
            var device = new DeviceId(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
            var saved = store.SaveDeviceAsync(SshFixtures.SshDevice(herdr, device: device), 0)
                .AsTask().GetAwaiter().GetResult();
            SshFixtures.Check(saved.Succeeded);
            var loaded = store.LoadAsync().AsTask().GetAwaiter().GetResult();
            SshFixtures.Check(loaded.Succeeded);
            var profile = loaded.Snapshot!.Devices.Single();
            SshFixtures.Check(profile.Device == device);
            SshFixtures.Check(profile.IsSshConnection);
            SshFixtures.Check(profile.Ssh is not null);
            SshFixtures.Check(profile.Ssh!.HostAlias == "lab");
            SshFixtures.Check(profile.Ssh.Port == 2222);
            SshFixtures.Check(profile.Ssh.AuthMode.Known == SshAuthMode.OpenSshConfig);
            SshFixtures.Check(profile.Sessions.Count == 2);
            var first = profile.Sessions[0].ToSessionKey(device);
            var second = profile.Sessions[1].ToSessionKey(device);
            SshFixtures.Check(first != second);
            SshFixtures.Check(profile.Ssh.ProfileRevision == 1);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void LabelKeepsRevision()
    {
        var root = SshFixtures.TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            var store = new AtomicConfigurationStore(paths);
            var herdr = Path.Combine(paths.Root, "herdr");
            var first = store.SaveDeviceAsync(SshFixtures.SshDevice(herdr, "one"), 0)
                .AsTask().GetAwaiter().GetResult();
            SshFixtures.Check(first.Succeeded);
            var revision = first.Snapshot!.Devices[0].Ssh!.ProfileRevision;
            var renamed = first.Snapshot.Devices[0] with { Label = "two" };
            var second = store.SaveDeviceAsync(renamed, 1).AsTask().GetAwaiter().GetResult();
            SshFixtures.Check(second.Succeeded);
            SshFixtures.Check(second.Snapshot!.Devices[0].Device == renamed.Device);
            SshFixtures.Check(second.Snapshot.Devices[0].Label == "two");
            SshFixtures.Check(second.Snapshot.Devices[0].Ssh!.ProfileRevision == revision);
            var changedSsh = renamed with
            {
                Label = "two",
                Ssh = renamed.Ssh! with { Port = 2200 }
            };
            var third = store.SaveDeviceAsync(changedSsh, 2).AsTask().GetAwaiter().GetResult();
            SshFixtures.Check(third.Succeeded);
            SshFixtures.Check(third.Snapshot!.Devices[0].Ssh!.ProfileRevision == revision + 1);
            SshFixtures.Check(third.Snapshot.Devices[0].Device == renamed.Device);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void SecretRejected()
    {
        var root = SshFixtures.TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            Directory.CreateDirectory(paths.SettingsDirectory);
            var pem = """{"schema_version":1,"revision":0,"devices":[],"private_key":"-----BEGIN PRIVATE KEY-----"}"""u8.ToArray();
            File.WriteAllBytes(paths.ConfigurationFile, pem);
            var store = new AtomicConfigurationStore(paths);
            var loaded = store.LoadAsync().AsTask().GetAwaiter().GetResult();
            SshFixtures.Check(loaded.Code == ConfigurationCodes.ForbiddenField);
            var original = File.ReadAllBytes(paths.ConfigurationFile);
            var herdr = Path.Combine(paths.Root, "herdr");
            var saved = store.SaveDeviceAsync(SshFixtures.SshDevice(herdr), 0).AsTask().GetAwaiter().GetResult();
            SshFixtures.Check(!saved.Succeeded);
            SshFixtures.Check(File.ReadAllBytes(paths.ConfigurationFile).SequenceEqual(original));
            SshFixtures.Check(Encoding.UTF8.GetString(original).Contains("BEGIN PRIVATE KEY", StringComparison.Ordinal));
            var emptyRoot = SshFixtures.TempRoot();
            try
            {
                var empty = AppDataPaths.FromRoot(emptyRoot);
                Directory.CreateDirectory(empty.SettingsDirectory);
                var clean = new AtomicConfigurationStore(empty);
                var secret = SshFixtures.SshDevice(Path.Combine(empty.Root, "herdr")) with
                {
                    Label = "lab -----BEGIN PRIVATE KEY-----"
                };
                var rejected = clean.SaveDeviceAsync(secret, 0).AsTask().GetAwaiter().GetResult();
                SshFixtures.Check(rejected.Code == ConfigurationCodes.ForbiddenField);
                SshFixtures.Check(!File.Exists(empty.ConfigurationFile));
            }
            finally
            {
                Directory.Delete(emptyRoot, true);
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void UnknownAuthPersists()
    {
        var root = SshFixtures.TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            var store = new AtomicConfigurationStore(paths);
            var herdr = Path.Combine(paths.Root, "herdr");
            var ssh = SshFixtures.Settings() with { AuthMode = SshDeviceSettings.ParseAuthMode("pkcs11") };
            var saved = store.SaveDeviceAsync(SshFixtures.SshDevice(herdr, ssh: ssh), 0)
                .AsTask().GetAwaiter().GetResult();
            SshFixtures.Check(saved.Succeeded);
            var loaded = store.LoadAsync().AsTask().GetAwaiter().GetResult();
            SshFixtures.Check(loaded.Snapshot!.Devices[0].Ssh!.AuthMode.Raw == "pkcs11");
            SshFixtures.Check(loaded.Snapshot.Devices[0].Ssh!.AuthMode.Known is null);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
