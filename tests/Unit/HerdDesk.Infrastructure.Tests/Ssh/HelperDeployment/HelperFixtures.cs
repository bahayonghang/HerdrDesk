using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Configuration;
using HerdDesk.Infrastructure.Ssh;

internal static class HelperFixtures
{
    public static readonly byte[] Payload = "herddesk-bridge-fixture-bytes"u8.ToArray();
    public static readonly byte[] OtherPayload = "herddesk-bridge-other-payload"u8.ToArray();
    public const string AppVersion = "0.0.0-g0";
    public const string LinuxX64 = "x86_64-unknown-linux-gnu";
    public static readonly DeviceId Device = new(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
    public static readonly SessionKey Session = new(Device, "named-session", "dev");
    public static readonly SessionKey OtherSession = new(Device, "named-session", "other");

    public static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    public static string TempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "herddesk-hd021-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static byte[] Resource(string name)
    {
        using var stream = typeof(TrustedHelperManifestProvider).Assembly.GetManifestResourceStream(name);
        Check(stream is not null);
        using var buffer = new MemoryStream();
        stream!.CopyTo(buffer);
        return buffer.ToArray();
    }

    public static byte[] TargetsJson() => Resource(TrustedHelperManifestProvider.TargetsResource);

    public static string Sha(byte[] payload) => HelperRemoteScripts.PayloadSha256(payload);

    public static TrustedHelperManifestProvider BuiltProvider(
        byte[]? payload = null,
        string triple = LinuxX64,
        string applicationVersion = AppVersion)
    {
        payload ??= Payload;
        var manifest = ManifestWithBuilt(payload, triple);
        var digest = Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant();
        var index = IndexJson(applicationVersion, digest);
        return TrustedHelperManifestProvider.FromBundle(
            applicationVersion,
            index,
            TargetsJson(),
            manifest,
            new Dictionary<string, byte[]>(StringComparer.Ordinal) { [triple] = payload });
    }

    public static byte[] IndexJson(string applicationVersion, string manifestSha256)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("application_version", applicationVersion);
            writer.WriteString("source_commit", "not_run");
            writer.WriteString("manifest_sha256", manifestSha256);
            writer.WriteString("bridge_source", "bridge/herddesk-bridge");
            writer.WriteString("lockfile", "bridge/Cargo.lock");
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    public static byte[] ManifestWithBuilt(byte[] payload, string triple = LinuxX64)
    {
        var sha = Sha(payload);
        var rows = new (string Triple, string File, string Os, string Arch, bool Built)[]
        {
            ("x86_64-pc-windows-msvc", "herddesk-bridge-x86_64-pc-windows-msvc.exe", "windows", "x86_64", false),
            ("x86_64-unknown-linux-gnu", "herddesk-bridge-x86_64-unknown-linux-gnu", "linux", "x86_64", false),
            ("aarch64-unknown-linux-gnu", "herddesk-bridge-aarch64-unknown-linux-gnu", "linux", "aarch64", false),
            ("x86_64-apple-darwin", "herddesk-bridge-x86_64-apple-darwin", "darwin", "x86_64", false),
            ("aarch64-apple-darwin", "herddesk-bridge-aarch64-apple-darwin", "darwin", "arm64", false)
        };
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("manifest_version", 1);
            writer.WriteString("bridge_version", "0.1.0");
            writer.WriteString("protocol_version", "1");
            writer.WriteString("source_commit", "not_run");
            writer.WriteString("application_version", AppVersion);
            writer.WritePropertyName("artifacts");
            writer.WriteStartObject();
            foreach (var row in rows)
            {
                var built = row.Triple == triple;
                writer.WritePropertyName(row.Triple);
                writer.WriteStartObject();
                writer.WriteString("file", row.File);
                writer.WriteNumber("length", built ? payload.Length : 0);
                writer.WriteString("sha256", built ? sha : "");
                writer.WriteString("os", row.Os);
                writer.WriteString("arch", row.Arch);
                writer.WriteString("build_status", built ? "built" : "not_run");
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    public static SshDeviceSettings Settings() => SshFixtures.Settings(jump: null, identity: null, agent: null);

    public static RemoteHelperDeploymentService Service(
        string root,
        ISshProcessRunner runner,
        ITrustedHelperManifest? manifest = null,
        HelperReceiptStoreHooks? hooks = null)
    {
        var paths = AppDataPaths.FromRoot(root);
        Directory.CreateDirectory(paths.SettingsDirectory);
        return new RemoteHelperDeploymentService(
            paths,
            manifest ?? BuiltProvider(),
            SshFixtures.Locator(),
            runner,
            receiptHooks: hooks);
    }

    public static HelperDeploymentPlanner Planner(
        ISshProcessRunner runner, ITrustedHelperManifest? manifest = null, string? knownHosts = null)
    {
        manifest ??= BuiltProvider();
        var hosts = knownHosts ?? (OperatingSystem.IsWindows()
            ? @"C:\HerdDesk\settings\ssh-known-hosts.json"
            : "/tmp/herddesk/ssh-known-hosts.json");
        var probe = new RemotePlatformProbe(SshFixtures.Locator(), runner, hosts, manifest.Targets);
        return new HelperDeploymentPlanner(manifest, probe);
    }

    public static RemoteHelperPublisher Publisher(ISshProcessRunner runner, string? knownHosts = null)
    {
        var hosts = knownHosts ?? (OperatingSystem.IsWindows()
            ? @"C:\HerdDesk\settings\ssh-known-hosts.json"
            : "/tmp/herddesk/ssh-known-hosts.json");
        return new RemoteHelperPublisher(SshFixtures.Locator(), runner, hosts);
    }

    public static string CanaryHost => "canary.example.invalid";
    public static string CanaryPath => "/home/canary/secret-helper";
    public static string CanaryStderr => "Permission denied canary-stderr-token";
}

internal sealed class FakeRemoteFileSystem
{
    private readonly Dictionary<string, FakeRemoteNode> _nodes = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private int _inode = 1;

    public string Home { get; set; } = "/home/lab";
    public string Os { get; set; } = "linux";
    public string Arch { get; set; } = "x86_64";
    public bool HardLinkSupported { get; set; } = true;
    public bool SelftestSucceeds { get; set; } = true;
    public string? ProbeStdout { get; set; }
    public string ProbeStderr { get; set; } = "";
    public int ProbeExit { get; set; }
    public bool ProbeTimeout { get; set; }
    public string BootstrapStderr { get; set; } = "";
    public int Mutations { get; private set; }

    public int StagingCount
    {
        get
        {
            lock (_gate)
                return _nodes.Keys.Count(key => key.Contains("/.stage-", StringComparison.Ordinal));
        }
    }

    public bool Exists(string path)
    {
        lock (_gate)
            return _nodes.ContainsKey(Normalize(path));
    }

    public byte[]? Read(string path)
    {
        lock (_gate)
            return _nodes.TryGetValue(Normalize(path), out var node) && !node.IsDirectory ? node.Bytes : null;
    }

    public string FinalPath(string version, string triple) =>
        Home.TrimEnd('/') + "/" + HelperRemoteScripts.PrivateRoot + "/" + version + "/" + triple + "/" +
        HelperRemoteScripts.FinalName;

    public string StagingPath(string version, string triple, string stagingId) =>
        Home.TrimEnd('/') + "/" + HelperRemoteScripts.PrivateRoot + "/" + version + "/" + triple + "/" +
        HelperRemoteScripts.StagingName(stagingId);

    internal string Bootstrap(ReadOnlyMemory<byte> payload, string version, string triple, string expectHash,
        long expectLen, string stagingId)
    {
        lock (_gate)
        {
            if (!string.IsNullOrEmpty(BootstrapStderr))
                return "stderr";
            var destDir = Home.TrimEnd('/') + "/" + HelperRemoteScripts.PrivateRoot + "/" + version + "/" + triple;
            var stage = destDir + "/" + HelperRemoteScripts.StagingName(stagingId);
            var payloadPath = stage + "/" + HelperRemoteScripts.PayloadName;
            var finalPath = destDir + "/" + HelperRemoteScripts.FinalName;
            try
            {
                EnsureDir(Home + "/.herddesk");
                EnsureDir(Home + "/.herddesk/helper");
                EnsureDir(Home + "/.herddesk/helper/" + version);
                EnsureDir(destDir);
                if (_nodes.ContainsKey(stage))
                    return HelperCodes.UploadFailed;
                Mutations++;
                _nodes[stage] = new FakeRemoteNode { IsDirectory = true, Inode = _inode++ };
                Mutations++;
                _nodes[payloadPath] = new FakeRemoteNode
                {
                    Bytes = payload.ToArray(),
                    Inode = _inode++
                };
                if (payload.Length != expectLen || HelperRemoteScripts.PayloadSha256(payload.Span) != expectHash)
                {
                    _nodes.Remove(payloadPath);
                    _nodes.Remove(stage);
                    return HelperCodes.HashMismatch;
                }

                var createdFinal = false;
                if (_nodes.TryGetValue(finalPath, out var existing))
                {
                    if (existing.IsSymlink)
                    {
                        RemoveStaging(payloadPath, stage);
                        return HelperCodes.VersionCollision;
                    }

                    var existHash = HelperRemoteScripts.PayloadSha256(existing.Bytes);
                    if (existHash != expectHash || existing.Bytes.Length != expectLen)
                    {
                        RemoveStaging(payloadPath, stage);
                        return HelperCodes.VersionCollision;
                    }
                }
                else
                {
                    if (!HardLinkSupported)
                    {
                        RemoveStaging(payloadPath, stage);
                        return HelperCodes.PlatformUnsupported;
                    }

                    var payloadNode = _nodes[payloadPath];
                    Mutations++;
                    _nodes[finalPath] = new FakeRemoteNode
                    {
                        Bytes = payloadNode.Bytes,
                        Inode = payloadNode.Inode
                    };
                    createdFinal = true;
                }

                if (!SelftestSucceeds)
                {
                    if (createdFinal)
                        _nodes.Remove(finalPath);
                    RemoveStaging(payloadPath, stage);
                    return HelperCodes.SelftestFailed;
                }

                RemoveStaging(payloadPath, stage);
                return HelperCodes.Ok;
            }
            catch (InvalidOperationException)
            {
                return HelperCodes.UploadFailed;
            }
        }
    }

    internal string Cleanup(string version, string triple, string stagingId)
    {
        lock (_gate)
        {
            var stage = StagingPath(version, triple, stagingId);
            var payloadPath = stage + "/" + HelperRemoteScripts.PayloadName;
            _nodes.Remove(payloadPath);
            _nodes.Remove(stage);
            return HelperCodes.Cancelled;
        }
    }

    private void EnsureDir(string path)
    {
        path = Normalize(path);
        if (_nodes.TryGetValue(path, out var node))
        {
            if (node.IsSymlink || !node.IsDirectory)
                throw new InvalidOperationException(HelperCodes.UploadFailed);
            return;
        }

        Mutations++;
        _nodes[path] = new FakeRemoteNode { IsDirectory = true, Inode = _inode++ };
    }

    private void RemoveStaging(string payloadPath, string stage)
    {
        _nodes.Remove(payloadPath);
        _nodes.Remove(stage);
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}

internal sealed class FakeRemoteNode
{
    public bool IsDirectory { get; init; }
    public bool IsSymlink { get; init; }
    public byte[] Bytes { get; init; } = [];
    public int Inode { get; init; }
}

internal sealed class FakeHelperRemoteRunner : ISshProcessRunner
{
    public FakeHelperRemoteRunner() : this(new FakeRemoteFileSystem())
    {
    }

    public FakeHelperRemoteRunner(FakeRemoteFileSystem remote)
    {
        ArgumentNullException.ThrowIfNull(remote);
        Remote = remote;
    }

    public FakeRemoteFileSystem Remote { get; }
    public List<SshProcessSpec> Started { get; } = [];
    public TimeSpan Delay { get; set; }

    public async ValueTask<SshProcessRunResult> RunAsync(
        SshProcessSpec spec, CancellationToken cancellationToken = default)
    {
        Started.Add(spec);
        if (Delay > TimeSpan.Zero && spec.Kind != SshProcessKind.HelperCleanup)
        {
            try
            {
                await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new(255, "", "", false, true, 4242);
            }
        }

        if (cancellationToken.IsCancellationRequested)
            return new(255, "", "", false, true, 7);

        return spec.Kind switch
        {
            SshProcessKind.PlatformProbe => Probe(),
            SshProcessKind.HelperBootstrap => Bootstrap(spec),
            SshProcessKind.HelperCleanup => Cleanup(spec),
            _ => new(255, "", "", false, false, 1)
        };
    }

    private SshProcessRunResult Probe()
    {
        if (Remote.ProbeTimeout)
            return new(255, "", "", true, false, 8);
        var stdout = Remote.ProbeStdout ??
                     (Remote.Os + "\n" + Remote.Arch + "\n" + Remote.Home + "\n");
        return new(Remote.ProbeExit, stdout, Remote.ProbeStderr, false, false, 9);
    }

    private SshProcessRunResult Bootstrap(SshProcessSpec spec)
    {
        HelperFixtures.Check(spec.Arguments.Contains("-T"));
        HelperFixtures.Check(spec.Arguments.Contains(HelperRemoteScripts.Bootstrap));
        HelperFixtures.Check(!spec.Arguments.Any(RemoteHelperPublisher.ForbiddenArgument));
        var args = AfterMarker(spec.Arguments, "herddesk-helper");
        HelperFixtures.Check(args.Count == 6);
        var code = Remote.Bootstrap(
            spec.StandardInput,
            args[0],
            args[1],
            args[2],
            long.Parse(args[3]),
            args[4]);
        if (!string.IsNullOrEmpty(Remote.BootstrapStderr) || code == "stderr")
            return new(12, "", Remote.BootstrapStderr, false, false, 10);
        var ok = code == HelperCodes.Ok;
        return new(ok ? 0 : 15, MapLine(code), "", false, false, 10);
    }

    private SshProcessRunResult Cleanup(SshProcessSpec spec)
    {
        HelperFixtures.Check(spec.Arguments.Contains(HelperRemoteScripts.Cleanup));
        var args = AfterMarker(spec.Arguments, "herddesk-helper");
        HelperFixtures.Check(args.Count == 3);
        var code = Remote.Cleanup(args[0], args[1], args[2]);
        return new(0, MapLine(code), "", false, false, 11);
    }

    private static string MapLine(string code) => code switch
    {
        HelperCodes.Ok => "helper_ok\n",
        HelperCodes.HashMismatch => "helper_hash_mismatch\n",
        HelperCodes.VersionCollision => "helper_version_collision\n",
        HelperCodes.PlatformUnsupported => "helper_platform_unsupported\n",
        HelperCodes.SelftestFailed => "helper_selftest_failed\n",
        HelperCodes.Cancelled => "helper_cancelled\n",
        _ => "helper_upload_failed\n"
    };

    private static List<string> AfterMarker(IReadOnlyList<string> arguments, string marker)
    {
        var index = arguments.ToList().IndexOf(marker);
        HelperFixtures.Check(index >= 0);
        return arguments.Skip(index + 1).ToList();
    }
}
