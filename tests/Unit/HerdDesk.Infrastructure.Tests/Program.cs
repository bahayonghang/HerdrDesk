using System.Text;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Configuration;
using HerdDesk.Infrastructure.Diagnostics;

if (args.Length > 0 && args[0] == "--fake-bridge")
    return FakeBridgeHost.Run(args.Skip(1).ToArray());
if (args.Length > 0 && args[0] == "--fake-terminal")
    return FakeTerminalHost.Run(args.Skip(1).ToArray());
if (args.Length > 0 && args[0] == "--fake-ssh")
    return FakeSshHost.Run(args.Skip(1).ToArray());


static void Check(bool condition)
{
    if (!condition)
        throw new Exception("assertion_failed");
}

static string TempRoot()
{
    var path = Path.Combine(Path.GetTempPath(), "herddesk-hd007-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

static DeviceProfile TwoSessions(string herdrPath, string label = "lab")
{
    var device = new DeviceId(Guid.Parse("11111111-2222-3333-4444-555555555555"));
    SessionProfile second;
    if (OperatingSystem.IsWindows())
        second = SessionProfile.Explicit(@"\\.\pipe\herdr-hd007", EndpointKind.NamedPipe);
    else
        second = SessionProfile.Explicit("/tmp/herdr-hd007.sock", EndpointKind.UnixSocket);
    return new DeviceProfile(
        device,
        label,
        ConnectionKinds.Local,
        herdrPath,
        [SessionProfile.Named("dev"), second]);
}

static byte[] Original(string path) => File.Exists(path) ? File.ReadAllBytes(path) : [];

static IEnumerable<string> AllFiles(string root) =>
    Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories) : [];

static string ReadAllLogs(string root)
{
    var builder = new StringBuilder();
    foreach (var file in AllFiles(root))
        builder.Append(File.ReadAllText(file, Encoding.UTF8));
    return builder.ToString();
}

static DiagnosticEvent OkEvent(string component = "configuration", string operation = "save") =>
    new(DateTimeOffset.UtcNow, component, operation, DiagnosticOutcome.Success, null, 1, 2, 3,
        "device-aaaaaaaaaaaa", "session-bbbbbbbbbbbb");

var cases = new (string Name, Action Run)[]
{
    // HD-007 configuration and diagnostics

    ("two sessions and endpoints round-trip on one device", () =>
    {
        var root = TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            var store = new AtomicConfigurationStore(paths);
            var herdr = Path.Combine(paths.Root, "herdr");
            var profile = TwoSessions(herdr);
            var saved = store.SaveDeviceAsync(profile, 0).AsTask().GetAwaiter().GetResult();
            Check(saved.Succeeded);
            Check(saved.Snapshot!.Revision == 1);
            Check(saved.Snapshot.Devices.Count == 1);
            Check(saved.Snapshot.Devices[0].Sessions.Count == 2);
            var loaded = store.LoadAsync().AsTask().GetAwaiter().GetResult();
            Check(loaded.Succeeded);
            var device = loaded.Snapshot!.Devices.Single();
            Check(device.Device == profile.Device);
            Check(device.Label == "lab");
            Check(device.Sessions[0].Kind == SessionProfileKind.NamedSession);
            Check(device.Sessions[0].SessionName == "dev");
            Check(device.Sessions[1].Kind == SessionProfileKind.ExplicitEndpoint);
            Check(device.Sessions[0].ToSessionKey(device.Device) !=
                  device.Sessions[1].ToSessionKey(device.Device));
            Check(!File.Exists(paths.ConfigurationBackupFile));
        }
        finally { Directory.Delete(root, true); }
    }),
    ("overwrite creates backup and keeps latest readable", () =>
    {
        var root = TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            var store = new AtomicConfigurationStore(paths);
            var herdr = Path.Combine(paths.Root, "herdr");
            Check(store.SaveDeviceAsync(TwoSessions(herdr, "one"), 0).AsTask().GetAwaiter().GetResult().Succeeded);
            var before = Original(paths.ConfigurationFile);
            Check(store.SaveDeviceAsync(TwoSessions(herdr, "two"), 1).AsTask().GetAwaiter().GetResult().Succeeded);
            Check(File.Exists(paths.ConfigurationBackupFile));
            Check(Original(paths.ConfigurationBackupFile).SequenceEqual(before));
            var loaded = store.LoadAsync().AsTask().GetAwaiter().GetResult();
            Check(loaded.Snapshot!.Devices.Single().Label == "two");
        }
        finally { Directory.Delete(root, true); }
    }),
    ("concurrent saves serialize and stale revisions conflict", () =>
    {
        var root = TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            var store = new AtomicConfigurationStore(paths);
            var herdr = Path.Combine(paths.Root, "herdr");
            var profile = TwoSessions(herdr);
            var tasks = Enumerable.Range(0, 8).Select(_ =>
                store.SaveDeviceAsync(profile, 0).AsTask()).ToArray();
            Task.WaitAll(tasks);
            var succeeded = tasks.Count(item => item.Result.Succeeded);
            var conflicts = tasks.Count(item => item.Result.Code == ConfigurationCodes.WriteConflict);
            Check(succeeded == 1);
            Check(conflicts == 7);
            var loaded = store.LoadAsync().AsTask().GetAwaiter().GetResult();
            Check(loaded.Succeeded);
            Check(loaded.Snapshot!.Revision == 1);
            var text = Encoding.UTF8.GetString(Original(paths.ConfigurationFile));
            Check(text.Contains("\"schema_version\": 1", StringComparison.Ordinal) ||
                  text.Contains("\"schema_version\":1", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, true); }
    }),
    ("serialize failure leaves original bytes and does not promote temp", () =>
    {
        var root = TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            var herdr = Path.Combine(paths.Root, "herdr");
            var store = new AtomicConfigurationStore(paths);
            Check(store.SaveDeviceAsync(TwoSessions(herdr), 0).AsTask().GetAwaiter().GetResult().Succeeded);
            var original = Original(paths.ConfigurationFile);
            var failing = new AtomicConfigurationStore(paths, new ConfigurationStoreHooks
            {
                Serialize = _ => throw new InvalidOperationException("serialize_boom")
            });
            var result = failing.SaveDeviceAsync(TwoSessions(herdr, "x"), 1).AsTask().GetAwaiter().GetResult();
            Check(!result.Succeeded);
            Check(result.Code == ConfigurationCodes.SerializeFailed);
            Check(Original(paths.ConfigurationFile).SequenceEqual(original));
            var temps = Directory.GetFiles(paths.SettingsDirectory, ".device-profiles.*.tmp");
            Check(temps.Length == 0);
        }
        finally { Directory.Delete(root, true); }
    }),
    ("replace failure leaves original bytes and does not promote temp", () =>
    {
        var root = TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            var herdr = Path.Combine(paths.Root, "herdr");
            var store = new AtomicConfigurationStore(paths);
            Check(store.SaveDeviceAsync(TwoSessions(herdr), 0).AsTask().GetAwaiter().GetResult().Succeeded);
            var original = Original(paths.ConfigurationFile);
            var failing = new AtomicConfigurationStore(paths, new ConfigurationStoreHooks
            {
                Commit = (_, _, _) => throw new IOException("replace_boom")
            });
            var result = failing.SaveDeviceAsync(TwoSessions(herdr, "x"), 1).AsTask().GetAwaiter().GetResult();
            Check(!result.Succeeded);
            Check(result.Code == ConfigurationCodes.ReplaceFailed);
            Check(Original(paths.ConfigurationFile).SequenceEqual(original));
            var temps = Directory.GetFiles(paths.SettingsDirectory, ".device-profiles.*.tmp");
            Check(temps.Length == 0);
        }
        finally { Directory.Delete(root, true); }
    }),
    ("flush failure after temp write leaves original bytes and does not promote temp", () =>
    {
        var root = TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            var herdr = Path.Combine(paths.Root, "herdr");
            var store = new AtomicConfigurationStore(paths);
            Check(store.SaveDeviceAsync(TwoSessions(herdr), 0).AsTask().GetAwaiter().GetResult().Succeeded);
            var original = Original(paths.ConfigurationFile);
            var failing = new AtomicConfigurationStore(paths, new ConfigurationStoreHooks
            {
                AfterTempFlushed = _ => throw new IOException("flush_boom")
            });
            var result = failing.SaveDeviceAsync(TwoSessions(herdr, "x"), 1).AsTask().GetAwaiter().GetResult();
            Check(!result.Succeeded);
            Check(result.Code == ConfigurationCodes.ReplaceFailed);
            Check(Original(paths.ConfigurationFile).SequenceEqual(original));
            var temps = Directory.GetFiles(paths.SettingsDirectory, ".device-profiles.*.tmp");
            Check(temps.Length == 0);
        }
        finally { Directory.Delete(root, true); }
    }),
    ("delete device removes only that profile and leaves the file readable", () =>
    {
        var root = TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            var store = new AtomicConfigurationStore(paths);
            var herdr = Path.Combine(paths.Root, "herdr");
            var first = TwoSessions(herdr, "keep-me");
            var second = new DeviceProfile(
                new DeviceId(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")),
                "drop-me",
                ConnectionKinds.Local,
                herdr,
                [SessionProfile.Named("other")]);
            Check(store.SaveDeviceAsync(first, 0).AsTask().GetAwaiter().GetResult().Succeeded);
            Check(store.SaveDeviceAsync(second, 1).AsTask().GetAwaiter().GetResult().Succeeded);
            var original = Original(paths.ConfigurationFile);
            var missing = store.DeleteDeviceAsync(
                new DeviceId(Guid.Parse("00000000-0000-0000-0000-000000000001")), 2)
                .AsTask().GetAwaiter().GetResult();
            Check(missing.Code == ConfigurationCodes.InvalidIdentity);
            Check(Original(paths.ConfigurationFile).SequenceEqual(original));
            var deleted = store.DeleteDeviceAsync(second.Device, 2).AsTask().GetAwaiter().GetResult();
            Check(deleted.Succeeded);
            Check(deleted.Snapshot!.Devices.Count == 1);
            Check(deleted.Snapshot.Devices[0].Device == first.Device);
            var loaded = store.LoadAsync().AsTask().GetAwaiter().GetResult();
            Check(loaded.Succeeded);
            Check(loaded.Snapshot!.Devices.Single().Label == "keep-me");
        }
        finally { Directory.Delete(root, true); }
    }),
    ("damaged file is not auto-overwritten", () =>
    {
        var root = TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            Directory.CreateDirectory(paths.SettingsDirectory);
            var damaged = Encoding.UTF8.GetBytes("{not-json");
            File.WriteAllBytes(paths.ConfigurationFile, damaged);
            var store = new AtomicConfigurationStore(paths);
            var loaded = store.LoadAsync().AsTask().GetAwaiter().GetResult();
            Check(loaded.Code == ConfigurationCodes.Malformed);
            var herdr = Path.Combine(paths.Root, "herdr");
            var saved = store.SaveDeviceAsync(TwoSessions(herdr), 0).AsTask().GetAwaiter().GetResult();
            Check(!saved.Succeeded);
            Check(Original(paths.ConfigurationFile).SequenceEqual(damaged));
        }
        finally { Directory.Delete(root, true); }
    }),
    ("unsupported version is not auto-overwritten", () =>
    {
        var root = TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            Directory.CreateDirectory(paths.SettingsDirectory);
            var high = """{"schema_version":99,"revision":1,"devices":[]}"""u8.ToArray();
            File.WriteAllBytes(paths.ConfigurationFile, high);
            var store = new AtomicConfigurationStore(paths);
            Check(store.LoadAsync().AsTask().GetAwaiter().GetResult().Code ==
                  ConfigurationCodes.VersionUnsupported);
            var herdr = Path.Combine(paths.Root, "herdr");
            Check(!store.SaveDeviceAsync(TwoSessions(herdr), 1).AsTask().GetAwaiter().GetResult().Succeeded);
            Check(Original(paths.ConfigurationFile).SequenceEqual(high));
        }
        finally { Directory.Delete(root, true); }
    }),
    ("restore uses only the original bak file", () =>
    {
        var root = TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            var store = new AtomicConfigurationStore(paths);
            var herdr = Path.Combine(paths.Root, "herdr");
            Check(store.SaveDeviceAsync(TwoSessions(herdr, "first"), 0).AsTask().GetAwaiter().GetResult().Succeeded);
            Check(store.SaveDeviceAsync(TwoSessions(herdr, "second"), 1).AsTask().GetAwaiter().GetResult().Succeeded);
            var bak = Original(paths.ConfigurationBackupFile);
            File.WriteAllBytes(paths.ConfigurationFile, "{broken"u8.ToArray());
            var other = Path.Combine(paths.SettingsDirectory, "device-profiles.json.old");
            File.WriteAllText(other, "not-a-backup", Encoding.UTF8);
            var restored = store.RestoreFromBackupAsync().AsTask().GetAwaiter().GetResult();
            Check(restored.Succeeded);
            Check(restored.Snapshot!.Devices.Single().Label == "first");
            var loaded = store.LoadAsync().AsTask().GetAwaiter().GetResult();
            Check(loaded.Snapshot!.Devices.Single().Label == "first");
            Check(Original(paths.ConfigurationBackupFile).SequenceEqual(bak));
        }
        finally { Directory.Delete(root, true); }
    }),
    ("restore without bak fails and does not invent a file", () =>
    {
        var root = TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            var store = new AtomicConfigurationStore(paths);
            var result = store.RestoreFromBackupAsync().AsTask().GetAwaiter().GetResult();
            Check(result.Code == ConfigurationCodes.BackupMissing);
            Check(!File.Exists(paths.ConfigurationFile));
        }
        finally { Directory.Delete(root, true); }
    }),
    ("forbidden secret fields are rejected and not written", () =>
    {
        var root = TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            Directory.CreateDirectory(paths.SettingsDirectory);
            var secret = """{"schema_version":1,"revision":0,"devices":[],"password":"supersecret-password"}"""u8.ToArray();
            File.WriteAllBytes(paths.ConfigurationFile, secret);
            var store = new AtomicConfigurationStore(paths);
            Check(store.LoadAsync().AsTask().GetAwaiter().GetResult().Code ==
                  ConfigurationCodes.ForbiddenField);
            Check(Original(paths.ConfigurationFile).SequenceEqual(secret));
        }
        finally { Directory.Delete(root, true); }
    }),
    ("channel full is bounded and increments dropped", () =>
    {
        var root = TempRoot();
        try
        {
            var log = Path.Combine(root, "diagnostics.jsonl");
            var sink = new JsonlDiagnosticSink(log, new DiagnosticSinkOptions
            {
                Capacity = 2,
                StartWriter = false
            });
            var accepted = 0;
            for (var i = 0; i < 16; i++)
            {
                if (sink.TryWrite(OkEvent(operation: "save")))
                    accepted++;
            }
            Check(accepted == 2);
            Check(sink.DroppedCount >= 14);
            sink.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        finally { Directory.Delete(root, true); }
    }),
    ("rotate stays inside byte budget", () =>
    {
        var root = TempRoot();
        try
        {
            var log = Path.Combine(root, "diagnostics.jsonl");
            var sink = new JsonlDiagnosticSink(log, new DiagnosticSinkOptions
            {
                Capacity = 64,
                MaxFileBytes = 400,
                MaxFiles = 3
            });
            for (var i = 0; i < 40; i++)
                Check(sink.TryWrite(OkEvent(operation: "save")));
            sink.DisposeAsync().AsTask().GetAwaiter().GetResult();
            var total = AllFiles(root).Sum(file => new FileInfo(file).Length);
            Check(total <= 400 * 3 + 400);
        }
        finally { Directory.Delete(root, true); }
    }),
    ("canary secrets never appear in diagnostic files", () =>
    {
        var root = TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            Directory.CreateDirectory(paths.SettingsDirectory);
            Directory.CreateDirectory(paths.LogDirectory);
            var projector = DiagnosticAliasProjector.LoadOrCreate(paths.DiagnosticSaltFile);
            var device = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            var alias = projector.Alias("device", device.ToString());
            var sink = new JsonlDiagnosticSink(paths.DiagnosticLogFile);
            var canaries = new[]
            {
                "ghp_canarytoken123",
                "supersecret-password",
                "-----BEGIN PRIVATE KEY-----",
                @"C:\Users\canary\secret",
                "\u001b[31msecret",
                "terminal.input canary text"
            };
            Check(sink.TryWrite(new DiagnosticEvent(
                DateTimeOffset.UtcNow, "diagnostics", "emit", DiagnosticOutcome.Success,
                null, 1, null, null, alias, null)));
            foreach (var canary in canaries)
            {
                Check(!sink.TryWrite(new DiagnosticEvent(
                    DateTimeOffset.UtcNow, canary, "emit", DiagnosticOutcome.Success,
                    null, 1, null, null, null, null)));
                Check(!sink.TryWrite(new DiagnosticEvent(
                    DateTimeOffset.UtcNow, "diagnostics", canary, DiagnosticOutcome.Success,
                    null, 1, null, null, null, null)));
            }
            sink.DisposeAsync().AsTask().GetAwaiter().GetResult();
            var logBytes = File.ReadAllBytes(paths.DiagnosticLogFile);
            Check(logBytes.Length > 0);
            Check(!(logBytes.Length >= 3 && logBytes[0] == 0xEF && logBytes[1] == 0xBB && logBytes[2] == 0xBF));
            Check(logBytes[0] == (byte)'{');
            var logs = ReadAllLogs(paths.LogDirectory);
            foreach (var canary in canaries)
                Check(!logs.Contains(canary, StringComparison.Ordinal));
            Check(!logs.Contains(device.ToString(), StringComparison.OrdinalIgnoreCase));
            Check(logs.Contains(alias, StringComparison.Ordinal));
            var saltBytes = File.ReadAllBytes(paths.DiagnosticSaltFile);
            Check(!logs.Contains(Convert.ToHexString(saltBytes), StringComparison.OrdinalIgnoreCase));
            Check(!logs.Contains(Convert.ToBase64String(saltBytes), StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, true); }
    }),
};

cases =
[
    .. cases,
    .. RpcCases.All,
    .. TerminalCliTransportCases.All,
    .. SshDeviceProfileStoreTests.All,
    .. OpenSshConfigResolverTests.All,
    .. SshProcessSpecFactoryTests.All,
    .. HostKeyTrustStoreTests.All,
    .. SshConnectionTestServiceTests.All,
    .. TrustedHelperManifestProviderTests.All,
    .. RemotePlatformProbeTests.All,
    .. HelperDeploymentReceiptStoreTests.All,
    .. RemoteHelperDeploymentServiceTests.All
];

var failed = 0;
foreach (var test in cases)
{
    try
    {
        test.Run();
        Console.WriteLine("PASS " + test.Name);
    }
    catch (Exception error)
    {
        failed++;
        Console.Error.WriteLine("FAIL " + test.Name + ": " + error.GetType().Name + " " + error.Message);
    }
}
Console.WriteLine($"{cases.Length - failed}/{cases.Length} infrastructure unit tests passed; no live Windows/daemon validation.");
return failed == 0 ? 0 : 1;
