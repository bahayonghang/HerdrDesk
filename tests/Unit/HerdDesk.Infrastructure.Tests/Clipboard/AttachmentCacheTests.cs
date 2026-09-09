using System.Text;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Clipboard;
using HerdDesk.Infrastructure.Configuration;

internal static class AttachmentCacheTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("ttl eviction removes expired unleased objects only", TtlEviction),
        ("capacity full refuses new objects", CapacityFull),
        ("lease protects an object from ttl cleanup", LeaseProtects),
        ("cleanup uses exact owned ids only", ExactIdCleanup),
        ("cleanup failed is not a fake success", CleanupFailed),
        ("windows reader uses injected capture and has no watcher", ReaderHasNoWatcher)
    ];

    static void TtlEviction()
    {
        using var env = new CacheEnv(maxBytes: 1024, ttl: TimeSpan.FromMinutes(1));
        var stored = env.Cache.Store("alpha"u8.ToArray());
        Check(stored.Succeeded);
        Check(stored.ObjectId is Guid);
        var decoy = Path.Combine(env.Root, "not-owned.bin");
        File.WriteAllText(decoy, "other-job");
        var source = Path.Combine(env.Outside, "source.txt");
        File.WriteAllText(source, "do-not-delete");
        env.Time.Advance(TimeSpan.FromMinutes(2));
        var cleaned = env.Cache.CleanupExpired();
        Check(cleaned.Succeeded);
        Check(cleaned.RemovedCount == 1);
        Check(env.Cache.Usage.ObjectCount == 0);
        Check(File.Exists(decoy));
        Check(File.Exists(source));
        Check(!File.Exists(Path.Combine(env.Cache.Root, stored.ObjectId!.Value.ToString("N"))));
    }

    static void CapacityFull()
    {
        using var env = new CacheEnv(maxBytes: 8, ttl: TimeSpan.FromHours(1));
        var first = env.Cache.Store("12345678"u8.ToArray());
        Check(first.Succeeded);
        var decoy = Path.Combine(env.Cache.Root, "other-job.bin");
        File.WriteAllBytes(decoy, "keep"u8.ToArray());
        var second = env.Cache.Store("zzzz"u8.ToArray());
        Check(!second.Succeeded);
        Check(second.Code == ClipboardCodes.CacheFull);
        Check(env.Cache.Usage.ObjectCount == 1);
        Check(File.Exists(decoy));
        Check(File.Exists(Path.Combine(env.Cache.Root, first.ObjectId!.Value.ToString("N"))));
    }

    static void LeaseProtects()
    {
        using var env = new CacheEnv(maxBytes: 1024, ttl: TimeSpan.FromMinutes(1));
        var stored = env.Cache.Store("held"u8.ToArray());
        Check(stored.Succeeded);
        using var lease = env.Cache.Acquire(stored.ObjectId!.Value);
        Check(env.Cache.IsHeld(stored.ObjectId.Value));
        env.Time.Advance(TimeSpan.FromHours(2));
        var cleaned = env.Cache.CleanupExpired();
        Check(cleaned.RemovedCount == 0);
        Check(File.Exists(Path.Combine(env.Cache.Root, stored.ObjectId.Value.ToString("N"))));
        var exact = env.Cache.CleanupExact(stored.ObjectId.Value);
        Check(!exact.Succeeded || exact.RemovedCount == 0);
        Check(File.Exists(Path.Combine(env.Cache.Root, stored.ObjectId.Value.ToString("N"))));
        lease.Dispose();
        env.Time.Advance(TimeSpan.FromMinutes(1));
        var after = env.Cache.CleanupExpired();
        Check(after.RemovedCount == 1);
        Check(!File.Exists(Path.Combine(env.Cache.Root, stored.ObjectId.Value.ToString("N"))));
    }

    static void ExactIdCleanup()
    {
        using var env = new CacheEnv(maxBytes: 1024, ttl: TimeSpan.FromHours(1));
        var first = env.Cache.Store("one"u8.ToArray());
        var second = env.Cache.Store("two"u8.ToArray());
        Check(first.Succeeded && second.Succeeded);
        var decoy = Path.Combine(env.Cache.Root, "other-job.bin");
        File.WriteAllText(decoy, "glob-name");
        var other = Path.Combine(env.Cache.Root, "ffffffffffffffffffffffffffffffff");
        File.WriteAllText(other, "foreign");
        var source = Path.Combine(env.Outside, "origin.png");
        File.WriteAllText(source, "origin");
        var cleaned = env.Cache.CleanupExact(first.ObjectId!.Value);
        Check(cleaned.Succeeded);
        Check(cleaned.RemovedCount == 1);
        Check(!File.Exists(Path.Combine(env.Cache.Root, first.ObjectId.Value.ToString("N"))));
        Check(File.Exists(Path.Combine(env.Cache.Root, second.ObjectId!.Value.ToString("N"))));
        Check(File.Exists(decoy));
        Check(File.Exists(other));
        Check(File.Exists(source));
    }

    static void CleanupFailed()
    {
        using var env = new CacheEnv(maxBytes: 1024, ttl: TimeSpan.FromMinutes(1));
        var stored = env.Cache.Store("locked"u8.ToArray());
        Check(stored.Succeeded);
        var path = Path.Combine(env.Cache.Root, stored.ObjectId!.Value.ToString("N"));
        var decoy = Path.Combine(env.Cache.Root, "other-job.bin");
        File.WriteAllText(decoy, "keep");
        var source = Path.Combine(env.Outside, "origin.bin");
        File.WriteAllText(source, "origin");
        env.Time.Advance(TimeSpan.FromMinutes(2));
        using var hold = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var cleaned = env.Cache.CleanupExpired();
        Check(File.Exists(decoy));
        Check(File.Exists(source));
        if (OperatingSystem.IsWindows())
        {
            Check(!cleaned.Succeeded);
            Check(cleaned.Code == ClipboardCodes.CleanupFailed);
            Check(File.Exists(path));
            Check(env.Cache.Usage.ObjectCount == 1);
        }
    }

    static void ReaderHasNoWatcher()
    {
        var invocations = 0;
        var snapshot = new ClipboardSnapshot(
            Guid.NewGuid(),
            DateTimeOffset.UnixEpoch,
            true,
            false,
            false,
            ["std"],
            "hi"u8.ToArray(),
            [],
            null,
            2,
            null);
        var reader = new WindowsClipboardSnapshotReader(() =>
        {
            invocations++;
            return snapshot;
        });
        Check(!reader.RegistersWatcher);
        Check(typeof(WindowsClipboardSnapshotReader).GetMethod("AddClipboardFormatListener") is null);
        Check(typeof(WindowsClipboardSnapshotReader).GetMethod("SetClipboardViewer") is null);
        Check(invocations == 0);
        var read = reader.Read();
        Check(invocations == 1);
        Check(read.HasText);
        Check(Encoding.UTF8.GetString(read.TextBytes.Span) == "hi");
    }

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    sealed class ManualTimeProvider : TimeProvider
    {
        DateTimeOffset _now;

        public ManualTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }

    sealed class CacheEnv : IDisposable
    {
        readonly string _root;

        public CacheEnv(long maxBytes, TimeSpan ttl)
        {
            _root = Path.Combine(Path.GetTempPath(), "herddesk-hd031-cache-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            Outside = Path.Combine(_root, "outside");
            Directory.CreateDirectory(Outside);
            Time = new ManualTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));
            var paths = AppDataPaths.FromRoot(_root);
            Directory.CreateDirectory(paths.CacheDirectory);
            Cache = new AttachmentCache(
                paths,
                Time,
                new AttachmentCacheOptions { MaxBytes = maxBytes, DefaultTtl = ttl, MaxObjects = 8 });
        }

        public string Root => _root;
        public string Outside { get; }
        public ManualTimeProvider Time { get; }
        public AttachmentCache Cache { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
