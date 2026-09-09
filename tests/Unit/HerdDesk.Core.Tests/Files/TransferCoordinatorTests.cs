using System.Security.Cryptography;
using System.Text;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class TransferCoordinatorTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("replace without observation is rejected", ReplaceWithoutObservation),
        ("stale replace target is conflict", StaleReplace),
        ("keepboth exclusive create uses next name", KeepBothExclusive),
        ("fail does not overwrite", FailDoesNotOverwrite),
        ("zero and small payload hash match", HashMatch),
        ("cancel deletes only job temp", CancelTempOnly),
        ("file lease two jobs global max third waits", LeaseCap),
        ("progress binds starting session epoch", ProgressBinding),
        ("source change aborts without mixed snapshot", SourceChange)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static DeviceId Dev(byte n) => new(new Guid(n, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));

    static SessionKey Sess(byte device, string name) => new(Dev(device), "endpoint", name);

    static TransferEndpointKey Key(byte device, string name, long epoch = 1) =>
        new(Dev(device), Sess(device, name), new ConnectionEpoch(epoch), "local-" + name);

    static FileComponent Comp(string name) => new(Encoding.UTF8.GetBytes(name));

    static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    static void ReplaceWithoutObservation()
    {
        var src = new MemoryFileEndpoint(Key(1, "src"));
        var dst = new MemoryFileEndpoint(Key(2, "dst"), replaceSupported: true);
        src.AddFile("a.txt", "hello"u8.ToArray());
        var coordinator = new TransferCoordinator(new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(8)));
        var result = coordinator.CopyAsync(new TransferRequest(
            Guid.NewGuid(), src, FileLocator.Root.Append(Comp("a.txt")),
            dst, FileLocator.Root, Comp("a.txt"), FileConflictMode.Replace, null)).AsTask().GetAwaiter().GetResult();
        Check(!result.Succeeded);
        Check(result.Code == FileOpCodes.ReplaceObservationRequired);
    }

    static void StaleReplace()
    {
        var src = new MemoryFileEndpoint(Key(1, "src"));
        var dst = new MemoryFileEndpoint(Key(2, "dst"), replaceSupported: true);
        src.AddFile("a.txt", "hello"u8.ToArray());
        dst.AddFile("a.txt", "old"u8.ToArray());
        var stale = new FileObservation(new string('1', 64));
        var coordinator = new TransferCoordinator(new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(8)));
        var result = coordinator.CopyAsync(new TransferRequest(
            Guid.NewGuid(), src, FileLocator.Root.Append(Comp("a.txt")),
            dst, FileLocator.Root, Comp("a.txt"), FileConflictMode.Replace, stale)).AsTask().GetAwaiter().GetResult();
        Check(!result.Succeeded);
        Check(result.Code == FileOpCodes.StaleTarget);
        Check(Encoding.UTF8.GetString(dst.ReadAll("a.txt")) == "old");
    }

    static void KeepBothExclusive()
    {
        var src = new MemoryFileEndpoint(Key(1, "src"));
        var dst = new MemoryFileEndpoint(Key(2, "dst"));
        src.AddFile("a.txt", "hello"u8.ToArray());
        dst.AddFile("a.txt", "old"u8.ToArray());
        var coordinator = new TransferCoordinator(new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(8)));
        var result = coordinator.CopyAsync(new TransferRequest(
            Guid.NewGuid(), src, FileLocator.Root.Append(Comp("a.txt")),
            dst, FileLocator.Root, Comp("a.txt"), FileConflictMode.KeepBoth, null)).AsTask().GetAwaiter().GetResult();
        Check(result.Succeeded);
        Check(Encoding.UTF8.GetString(dst.ReadAll("a.txt")) == "old");
        Check(Encoding.UTF8.GetString(dst.ReadAll("a (1).txt")) == "hello");
        Check(result.Sha256 == Sha("hello"u8.ToArray()));
    }

    static void FailDoesNotOverwrite()
    {
        var src = new MemoryFileEndpoint(Key(1, "src"));
        var dst = new MemoryFileEndpoint(Key(2, "dst"));
        src.AddFile("a.txt", "hello"u8.ToArray());
        dst.AddFile("a.txt", "old"u8.ToArray());
        var coordinator = new TransferCoordinator(new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(8)));
        var result = coordinator.CopyAsync(new TransferRequest(
            Guid.NewGuid(), src, FileLocator.Root.Append(Comp("a.txt")),
            dst, FileLocator.Root, Comp("a.txt"), FileConflictMode.Fail, null)).AsTask().GetAwaiter().GetResult();
        Check(!result.Succeeded);
        Check(result.Code == FileOpCodes.NameExists);
        Check(Encoding.UTF8.GetString(dst.ReadAll("a.txt")) == "old");
        Check(!dst.HasTemp);
    }

    static void HashMatch()
    {
        var src = new MemoryFileEndpoint(Key(1, "src"));
        var dst = new MemoryFileEndpoint(Key(2, "dst"));
        src.AddFile("empty.bin", []);
        src.AddFile("small.bin", "hello"u8.ToArray());
        var coordinator = new TransferCoordinator(new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(8)));
        var empty = coordinator.CopyAsync(new TransferRequest(
            Guid.NewGuid(), src, FileLocator.Root.Append(Comp("empty.bin")),
            dst, FileLocator.Root, Comp("empty.bin"), FileConflictMode.Fail, null)).AsTask().GetAwaiter().GetResult();
        var small = coordinator.CopyAsync(new TransferRequest(
            Guid.NewGuid(), src, FileLocator.Root.Append(Comp("small.bin")),
            dst, FileLocator.Root, Comp("small.bin"), FileConflictMode.Fail, null)).AsTask().GetAwaiter().GetResult();
        Check(empty.Succeeded && small.Succeeded);
        Check(empty.Sha256 == Sha([]));
        Check(small.Sha256 == Sha("hello"u8.ToArray()));
        Check(dst.ReadAll("empty.bin").Length == 0);
        Check(Encoding.UTF8.GetString(dst.ReadAll("small.bin")) == "hello");
    }

    static void CancelTempOnly()
    {
        var src = new MemoryFileEndpoint(Key(1, "src"));
        var dst = new MemoryFileEndpoint(Key(2, "dst"));
        src.AddFile("a.txt", "hello"u8.ToArray());
        dst.AddFile("sibling.txt", "keep"u8.ToArray());
        dst.FailNextWrite = true;
        var coordinator = new TransferCoordinator(new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(8)));
        var result = coordinator.CopyAsync(new TransferRequest(
            Guid.NewGuid(), src, FileLocator.Root.Append(Comp("a.txt")),
            dst, FileLocator.Root, Comp("out.txt"), FileConflictMode.Fail, null)).AsTask().GetAwaiter().GetResult();
        Check(!result.Succeeded);
        Check(Encoding.UTF8.GetString(src.ReadAll("a.txt")) == "hello");
        Check(Encoding.UTF8.GetString(dst.ReadAll("sibling.txt")) == "keep");
        Check(!dst.Has("out.txt"));
        Check(!dst.HasTemp);
    }

    static void LeaseCap()
    {
        var policy = new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(8));
        var first = policy.TryAcquireFileJob(Sess(1, "a"), new ConnectionEpoch(1), Guid.NewGuid().ToString("D"));
        var second = policy.TryAcquireFileJob(Sess(2, "b"), new ConnectionEpoch(1), Guid.NewGuid().ToString("D"));
        var third = policy.TryAcquireFileJob(Sess(3, "c"), new ConnectionEpoch(1), Guid.NewGuid().ToString("D"));
        try
        {
            Check(first.Admitted && second.Admitted);
            Check(!third.Admitted);
            Check(third.Code == ResourceBudgetCodes.ConnectionBudgetExhausted);
            Check(policy.Snapshot().FileJobs == 2);
        }
        finally
        {
            first.Lease?.Dispose();
            second.Lease?.Dispose();
        }
    }

    static void ProgressBinding()
    {
        var src = new MemoryFileEndpoint(Key(1, "src", 4));
        var dst = new MemoryFileEndpoint(Key(2, "dst", 4));
        src.AddFile("a.txt", "hello"u8.ToArray());
        var coordinator = new TransferCoordinator(new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(8)));
        var result = coordinator.CopyAsync(new TransferRequest(
            Guid.NewGuid(), src, FileLocator.Root.Append(Comp("a.txt")),
            dst, FileLocator.Root, Comp("a.txt"), FileConflictMode.Fail, null)).AsTask().GetAwaiter().GetResult();
        Check(result.Succeeded);
        Check(result.Progress is not null);
        Check(result.Progress!.Source.Epoch.Value == 4);
        Check(result.Progress.Destination.Epoch.Value == 4);
        Check(result.Progress.BytesAccepted == 5);
        Check(result.Progress.BytesTotal == 5);
        Check(result.Progress.Source.Device == Dev(1));
        Check(result.Progress.Destination.Device == Dev(2));
    }

    static void SourceChange()
    {
        var src = new MemoryFileEndpoint(Key(1, "src"));
        var dst = new MemoryFileEndpoint(Key(2, "dst"));
        src.AddFile("a.txt", "hello"u8.ToArray());
        src.MutateAfterRead = "changed"u8.ToArray();
        var coordinator = new TransferCoordinator(new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(8)));
        var result = coordinator.CopyAsync(new TransferRequest(
            Guid.NewGuid(), src, FileLocator.Root.Append(Comp("a.txt")),
            dst, FileLocator.Root, Comp("a.txt"), FileConflictMode.Fail, null)).AsTask().GetAwaiter().GetResult();
        Check(!result.Succeeded);
        Check(result.Code == FileOpCodes.SourceChanged);
        Check(!dst.Has("a.txt"));
        Check(!dst.HasTemp);
    }
}

sealed class MemoryFileEndpoint : IFileEndpoint
{
    readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    readonly Dictionary<string, FileObservation> _obs = new(StringComparer.Ordinal);
    bool _temp;

    public MemoryFileEndpoint(TransferEndpointKey key, bool replaceSupported = false)
    {
        Key = key;
        ReplaceSupported = replaceSupported;
        _files[""] = [];
        _obs[""] = Observe("", [], FileEntryKind.Directory);
    }

    public TransferEndpointKey Key { get; }
    public bool ReplaceSupported { get; }
    public bool FailNextWrite;
    public byte[]? MutateAfterRead;
    public bool HasTemp => _temp;
    public bool Has(string name) => _files.ContainsKey(name);
    public byte[] ReadAll(string name) => _files[name];

    public void AddFile(string name, byte[] data)
    {
        _files[name] = data;
        _obs[name] = Observe(name, data, FileEntryKind.File);
    }

    public ValueTask<FileOpResult> ListAsync(FileLocator path, int limit, string? cursor, CancellationToken cancellationToken = default)
    {
        _ = (path, cursor, cancellationToken);
        var entries = new List<FileEntry>();
        foreach (var pair in _files)
        {
            if (pair.Key.Length == 0 || entries.Count >= limit)
                continue;
            var name = Comp(pair.Key);
            var kind = pair.Key.Length == 0 ? FileEntryKind.Directory : FileEntryKind.File;
            entries.Add(new FileEntry(name, pair.Key, kind, (ulong)pair.Value.Length, 0, 1,
                new FileIdentity(name.Raw), false, _obs[pair.Key]));
        }
        return ValueTask.FromResult(new FileOpResult(true, FileOpCodes.Ok, Entries: entries));
    }

    public ValueTask<FileOpResult> StatAsync(FileLocator path, FileObservation? observation, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var key = KeyOf(path);
        if (!_files.TryGetValue(key, out var data))
            return ValueTask.FromResult(FileOpResult.Fail(key.Length == 0 ? FileOpCodes.NotFound : FileOpCodes.NotFound));
        var kind = key.Length == 0 ? FileEntryKind.Directory : FileEntryKind.File;
        var stat = new FileStat(path, kind, (ulong)data.Length, 0, 1, new FileIdentity(Encoding.UTF8.GetBytes(key + ":" + data.Length)),
            _obs[key], false, Sha(data));
        if (observation is not null && observation.Hex != stat.Observation.Hex)
            return ValueTask.FromResult(FileOpResult.Fail(FileOpCodes.StaleTarget));
        return ValueTask.FromResult(new FileOpResult(true, FileOpCodes.Ok, stat, Sha256: stat.Sha256, Length: stat.Size));
    }

    public ValueTask<FileReadOpen> OpenReadAsync(FileLocator path, FileObservation? observation, CancellationToken cancellationToken = default)
    {
        var statTask = StatAsync(path, observation, cancellationToken).AsTask().GetAwaiter().GetResult();
        if (!statTask.Succeeded || statTask.Stat is null)
            return ValueTask.FromResult(new FileReadOpen(statTask, null));
        var data = _files[KeyOf(path)];
        if (MutateAfterRead is not null)
        {
            var key = KeyOf(path);
            _files[key] = MutateAfterRead;
            _obs[key] = Observe(key, MutateAfterRead, FileEntryKind.File);
        }
        return ValueTask.FromResult(new FileReadOpen(statTask, new MemoryRead(statTask.Stat, data)));
    }

    public ValueTask<FileWriteOpen> BeginWriteAsync(FileWriteRequest request, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        if (request.Mode == FileConflictMode.Replace)
        {
            if (request.TargetObservation is null)
                return ValueTask.FromResult(new FileWriteOpen(FileOpResult.Fail(FileOpCodes.ReplaceObservationRequired), null));
            if (!ReplaceSupported)
                return ValueTask.FromResult(new FileWriteOpen(FileOpResult.Fail(FileOpCodes.Unsupported), null));
            var existing = KeyOf(request.Parent.Append(request.Name));
            if (!_obs.TryGetValue(existing, out var current) || current.Hex != request.TargetObservation.Hex)
                return ValueTask.FromResult(new FileWriteOpen(FileOpResult.Fail(FileOpCodes.StaleTarget), null));
        }
        if (FailNextWrite)
        {
            FailNextWrite = false;
            return ValueTask.FromResult(new FileWriteOpen(FileOpResult.Fail(FileOpCodes.PermissionDenied), null));
        }
        _temp = true;
        return ValueTask.FromResult(new FileWriteOpen(FileOpResult.Ok(), new MemoryWrite(this, request)));
    }

    internal FileOpResult Commit(FileWriteRequest request, byte[] data)
    {
        var name = Encoding.UTF8.GetString(request.Name.Raw);
        if (request.Mode == FileConflictMode.Fail && _files.ContainsKey(name) && name.Length > 0)
        {
            _temp = false;
            return FileOpResult.Fail(FileOpCodes.NameExists);
        }
        if (request.Mode == FileConflictMode.KeepBoth)
        {
            var n = 0;
            var candidate = name;
            while (_files.ContainsKey(candidate))
            {
                n++;
                candidate = Encoding.UTF8.GetString(KeepBothNames.Candidate(request.Name.Raw, n));
                if (n > KeepBothNames.MaxAttempts)
                {
                    _temp = false;
                    return FileOpResult.Fail(FileOpCodes.NameExists);
                }
            }
            name = candidate;
        }
        if (request.Mode == FileConflictMode.Replace)
        {
            var existing = name;
            if (!_obs.TryGetValue(existing, out var current) || current.Hex != request.TargetObservation!.Hex)
            {
                _temp = false;
                return FileOpResult.Fail(FileOpCodes.StaleTarget);
            }
        }
        var digest = Sha(data);
        if ((ulong)data.Length != request.Length)
        {
            _temp = false;
            return FileOpResult.Fail(FileOpCodes.LengthMismatch);
        }
        if (digest != request.Sha256)
        {
            _temp = false;
            return FileOpResult.Fail(FileOpCodes.HashMismatch);
        }
        AddFile(name, data);
        _temp = false;
        var locator = FileLocator.Root.Append(Comp(name));
        return new FileOpResult(true, FileOpCodes.Ok, FinalPath: locator, Sha256: digest, Length: (ulong)data.Length);
    }

    internal void ClearTemp() => _temp = false;

    static string KeyOf(FileLocator path) =>
        path.Components.Count == 0 ? "" : Encoding.UTF8.GetString(path.Components[^1].Raw);

    static FileComponent Comp(string name) => new(Encoding.UTF8.GetBytes(name));

    static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    static FileObservation Observe(string name, byte[] data, FileEntryKind kind) =>
        new(Sha(Encoding.UTF8.GetBytes(name + ":" + kind + ":" + data.Length)));

    sealed class MemoryRead(FileStat stat, byte[] data) : IFileReadSession
    {
        int _offset;
        public FileStat Stat { get; } = stat;
        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            var n = Math.Min(buffer.Length, data.Length - _offset);
            data.AsSpan(_offset, n).CopyTo(buffer.Span);
            _offset += n;
            return ValueTask.FromResult(n);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    sealed class MemoryWrite(MemoryFileEndpoint owner, FileWriteRequest request) : IFileWriteSession
    {
        readonly MemoryStream _buffer = new();
        bool _done;
        public Guid JobId => request.JobId;
        public FileIdentity TempIdentity { get; } = new("tmp"u8.ToArray());
        public ValueTask WriteAsync(ReadOnlyMemory<byte> chunk, CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            _buffer.Write(chunk.Span);
            return ValueTask.CompletedTask;
        }
        public ValueTask<FileOpResult> CompleteAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            _done = true;
            return ValueTask.FromResult(owner.Commit(request, _buffer.ToArray()));
        }
        public ValueTask<FileOpResult> AbortAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            _done = true;
            owner.ClearTemp();
            return ValueTask.FromResult(FileOpResult.Fail(FileOpCodes.Cancelled));
        }
        public ValueTask DisposeAsync()
        {
            if (!_done)
                owner.ClearTemp();
            return ValueTask.CompletedTask;
        }
    }
}
