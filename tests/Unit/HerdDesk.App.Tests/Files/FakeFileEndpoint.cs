using System.Security.Cryptography;
using System.Text;
using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Core;

sealed class FakeFileEndpoint : IFileEndpoint
{
    readonly Dictionary<string, Node> _nodes = new(StringComparer.Ordinal);

    public FakeFileEndpoint(TransferEndpointKey key, bool replaceSupported = true)
    {
        Key = key;
        ReplaceSupported = replaceSupported;
        _nodes[""] = Node.Dir("");
    }

    public TransferEndpointKey Key { get; }
    public bool ReplaceSupported { get; }
    public bool FailNextWrite { get; set; }
    public TaskCompletionSource<FileOpResult>? BlockList { get; set; }
    public TaskCompletionSource<bool>? BlockWrite { get; set; }
    public string? NextListCode { get; set; }
    public int ListCalls { get; private set; }
    public bool HasTemp { get; private set; }

    public void AddFile(string name, byte[] data, string? display = null) =>
        AddFile(FileLocator.Root, name, data, display);

    public void AddFile(FileLocator parent, string name, byte[] data, string? display = null)
    {
        var locator = parent.Append(Comp(name));
        _nodes[KeyOf(locator)] = Node.File(name, data, display ?? name);
    }

    public void AddDirectory(string name) => AddDirectory(FileLocator.Root, name);

    public void AddDirectory(FileLocator parent, string name)
    {
        var locator = parent.Append(Comp(name));
        _nodes[KeyOf(locator)] = Node.Dir(name);
    }

    public void AddSymlink(string name, string? display = null)
    {
        var locator = FileLocator.Root.Append(Comp(name));
        _nodes[KeyOf(locator)] = Node.Link(name, display ?? name);
    }

    public bool Has(string name) => _nodes.ContainsKey(KeyOf(FileLocator.Root.Append(Comp(name))))
        && _nodes[KeyOf(FileLocator.Root.Append(Comp(name)))].Kind == FileEntryKind.File;

    public byte[] ReadAll(string name) => _nodes[KeyOf(FileLocator.Root.Append(Comp(name)))].Data;

    public async ValueTask<FileOpResult> ListAsync(
        FileLocator path, int limit, string? cursor, CancellationToken cancellationToken = default)
    {
        ListCalls++;
        _ = cursor;
        if (BlockList is { } block)
        {
            using (cancellationToken.Register(() => block.TrySetCanceled(cancellationToken)))
            {
                try
                {
                    return await block.Task.ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    return FileOpResult.Fail(FileOpCodes.Cancelled);
                }
            }
        }

        if (NextListCode is not null)
            return FileOpResult.Fail(NextListCode);
        if (limit is < 1 or > 1000)
            return FileOpResult.Fail(FileOpCodes.InvalidPath);
        var parent = KeyOf(path);
        if (!_nodes.TryGetValue(parent, out var dir) || dir.Kind != FileEntryKind.Directory)
            return FileOpResult.Fail(FileOpCodes.NotDirectory);
        var entries = new List<FileEntry>();
        foreach (var pair in _nodes)
        {
            if (pair.Key.Length == 0 || entries.Count >= limit)
                continue;
            if (ParentKey(pair.Key) != parent)
                continue;
            var node = pair.Value;
            var name = Comp(node.Name);
            entries.Add(new FileEntry(
                name, node.Display, node.Kind, (ulong)node.Data.Length, 0, 1,
                node.Identity, node.Symlink, node.Observation));
        }

        return new FileOpResult(true, FileOpCodes.Ok, Entries: entries);
    }

    public ValueTask<FileOpResult> StatAsync(
        FileLocator path, FileObservation? observation, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var key = KeyOf(path);
        if (!_nodes.TryGetValue(key, out var node))
            return ValueTask.FromResult(FileOpResult.Fail(FileOpCodes.NotFound));
        var stat = ToStat(path, node);
        if (observation is not null && observation.Hex != stat.Observation.Hex)
            return ValueTask.FromResult(FileOpResult.Fail(FileOpCodes.StaleTarget));
        return ValueTask.FromResult(new FileOpResult(
            true, FileOpCodes.Ok, stat, Sha256: stat.Sha256, Length: stat.Size));
    }

    public ValueTask<FileReadOpen> OpenReadAsync(
        FileLocator path, FileObservation? observation, CancellationToken cancellationToken = default)
    {
        var stat = StatAsync(path, observation, cancellationToken).AsTask().GetAwaiter().GetResult();
        if (!stat.Succeeded || stat.Stat is null)
            return ValueTask.FromResult(new FileReadOpen(stat, null));
        if (stat.Stat.Kind != FileEntryKind.File)
            return ValueTask.FromResult(new FileReadOpen(FileOpResult.Fail(FileOpCodes.IsDirectory), null));
        var data = _nodes[KeyOf(path)].Data;
        return ValueTask.FromResult(new FileReadOpen(stat, new MemoryRead(stat.Stat, data)));
    }

    public ValueTask<FileWriteOpen> BeginWriteAsync(
        FileWriteRequest request, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        if (request.Mode == FileConflictMode.Replace)
        {
            if (request.TargetObservation is null)
                return ValueTask.FromResult(new FileWriteOpen(FileOpResult.Fail(FileOpCodes.ReplaceObservationRequired), null));
            if (!ReplaceSupported)
                return ValueTask.FromResult(new FileWriteOpen(FileOpResult.Fail(FileOpCodes.Unsupported), null));
            var existing = KeyOf(request.Parent.Append(request.Name));
            if (!_nodes.TryGetValue(existing, out var current) || current.Observation.Hex != request.TargetObservation.Hex)
                return ValueTask.FromResult(new FileWriteOpen(FileOpResult.Fail(FileOpCodes.StaleTarget), null));
        }

        if (FailNextWrite)
        {
            FailNextWrite = false;
            return ValueTask.FromResult(new FileWriteOpen(FileOpResult.Fail(FileOpCodes.PermissionDenied), null));
        }

        HasTemp = true;
        return ValueTask.FromResult(new FileWriteOpen(FileOpResult.Ok(), new MemoryWrite(this, request)));
    }

    internal FileOpResult Commit(FileWriteRequest request, byte[] data)
    {
        var name = Encoding.UTF8.GetString(request.Name.Raw);
        var locator = request.Parent.Append(request.Name);
        var key = KeyOf(locator);
        if (request.Mode == FileConflictMode.Fail && _nodes.ContainsKey(key) && _nodes[key].Kind == FileEntryKind.File)
        {
            HasTemp = false;
            return FileOpResult.Fail(FileOpCodes.NameExists);
        }

        if (request.Mode == FileConflictMode.KeepBoth)
        {
            var n = 0;
            var candidate = name;
            var candidateLocator = locator;
            while (_nodes.ContainsKey(KeyOf(candidateLocator)))
            {
                n++;
                candidate = Encoding.UTF8.GetString(KeepBothNames.Candidate(request.Name.Raw, n));
                candidateLocator = request.Parent.Append(Comp(candidate));
                if (n > KeepBothNames.MaxAttempts)
                {
                    HasTemp = false;
                    return FileOpResult.Fail(FileOpCodes.NameExists);
                }
            }

            name = candidate;
            locator = candidateLocator;
            key = KeyOf(locator);
        }

        if (request.Mode == FileConflictMode.Replace)
        {
            if (!_nodes.TryGetValue(key, out var current) || current.Observation.Hex != request.TargetObservation!.Hex)
            {
                HasTemp = false;
                return FileOpResult.Fail(FileOpCodes.StaleTarget);
            }
        }

        var digest = Sha(data);
        if ((ulong)data.Length != request.Length)
        {
            HasTemp = false;
            return FileOpResult.Fail(FileOpCodes.LengthMismatch);
        }

        if (digest != request.Sha256)
        {
            HasTemp = false;
            return FileOpResult.Fail(FileOpCodes.HashMismatch);
        }

        _nodes[key] = Node.File(name, data, name);
        HasTemp = false;
        return new FileOpResult(true, FileOpCodes.Ok, FinalPath: locator, Sha256: digest, Length: (ulong)data.Length);
    }

    internal void ClearTemp() => HasTemp = false;

    internal static FileComponent Comp(string name) => new(Encoding.UTF8.GetBytes(name));

    internal static string KeyOf(FileLocator path)
    {
        if (path.Components.Count == 0)
            return "";
        var parts = new string[path.Components.Count];
        for (var i = 0; i < parts.Length; i++)
            parts[i] = Convert.ToHexString(path.Components[i].Raw);
        return string.Join('/', parts);
    }

    static string ParentKey(string key)
    {
        var index = key.LastIndexOf('/');
        return index < 0 ? "" : key[..index];
    }

    static FileStat ToStat(FileLocator path, Node node) =>
        new(
            path, node.Kind, (ulong)node.Data.Length, 0, 1, node.Identity,
            node.Observation, node.Symlink, Sha(node.Data));

    static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    sealed class Node
    {
        Node(string name, string display, FileEntryKind kind, byte[] data, bool symlink)
        {
            Name = name;
            Display = display;
            Kind = kind;
            Data = data;
            Symlink = symlink;
            Identity = new FileIdentity(Encoding.UTF8.GetBytes(name + ":" + kind + ":" + data.Length));
            Observation = new FileObservation(Sha(Encoding.UTF8.GetBytes(name + ":" + kind + ":" + data.Length)));
        }

        public string Name { get; }
        public string Display { get; }
        public FileEntryKind Kind { get; }
        public byte[] Data { get; }
        public bool Symlink { get; }
        public FileIdentity Identity { get; }
        public FileObservation Observation { get; }

        public static Node File(string name, byte[] data, string display) =>
            new(name, display, FileEntryKind.File, data, false);

        public static Node Dir(string name) =>
            new(name, name, FileEntryKind.Directory, [], false);

        public static Node Link(string name, string display) =>
            new(name, display, FileEntryKind.Symlink, [], true);
    }

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

    sealed class MemoryWrite(FakeFileEndpoint owner, FileWriteRequest request) : IFileWriteSession
    {
        readonly MemoryStream _buffer = new();
        bool _done;
        public Guid JobId => request.JobId;
        public FileIdentity TempIdentity { get; } = new("tmp"u8.ToArray());

        public async ValueTask WriteAsync(ReadOnlyMemory<byte> chunk, CancellationToken cancellationToken = default)
        {
            if (owner.BlockWrite is { } block)
            {
                using (cancellationToken.Register(() => block.TrySetCanceled(cancellationToken)))
                    await block.Task.ConfigureAwait(false);
            }

            _buffer.Write(chunk.Span);
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

internal static class FileWorkspaceHarness
{
    public static TransferCoordinator Coordinator() =>
        new(new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(8)));

    public static TransferEndpointKey Key(DeviceId device, string endpoint, long epoch = 1) =>
        new(device, AppTestHost.SessionOf(device, endpoint, "files"), new ConnectionEpoch(epoch), endpoint);

    public static FileComponent Comp(string name) => FakeFileEndpoint.Comp(name);

    public static FileWorkspaceViewModel Workspace(
        FakeFileEndpoint left,
        FakeFileEndpoint right)
    {
        var vm = new FileWorkspaceViewModel(Coordinator());
        vm.Left.NavigateAsync(FileLocation.Local(left)).AsTask().GetAwaiter().GetResult();
        vm.Right.NavigateAsync(FileLocation.Remote(right.Key.Device, right)).AsTask().GetAwaiter().GetResult();
        return vm;
    }

    public static void WaitUntil(Func<bool> condition, int ms = 2000)
    {
        var start = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - start > ms)
                throw new Exception("assertion_failed");
            Thread.Sleep(1);
        }
    }
}
