using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.Files;

public sealed class RemoteFileService : IFileEndpoint, IRemoteFileService
{
    readonly FileBridgeProcessFactory _factory;
    readonly string _workingDirectory;

    public RemoteFileService(FileBridgeProcessFactory factory, string workingDirectory, TransferEndpointKey key)
    {
        _factory = factory;
        _workingDirectory = workingDirectory;
        Key = key;
        ReplaceSupported = false;
    }

    public IFileEndpoint Endpoint => this;
    public TransferEndpointKey Key { get; }
    public bool ReplaceSupported { get; }

    public async ValueTask<FileOpResult> ListAsync(
        FileLocator path,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        await using var client = _factory.Start(_workingDirectory);
        return await client.ListAsync(path, limit, cursor, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<FileOpResult> StatAsync(
        FileLocator path,
        FileObservation? observation,
        CancellationToken cancellationToken = default)
    {
        await using var client = _factory.Start(_workingDirectory);
        return await client.StatAsync(path, observation, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<FileReadOpen> OpenReadAsync(
        FileLocator path,
        FileObservation? observation,
        CancellationToken cancellationToken = default)
    {
        FileOpResult hashed;
        await using (var statClient = _factory.Start(_workingDirectory))
        {
            hashed = await statClient.StatAsync(path, observation, cancellationToken).ConfigureAwait(false);
        }

        if (!hashed.Succeeded || hashed.Stat is null)
            return new FileReadOpen(hashed, null);
        if (hashed.Stat.Kind != FileEntryKind.File)
            return new FileReadOpen(FileOpResult.Fail(FileOpCodes.IsDirectory), null);

        var client = _factory.Start(_workingDirectory);
        try
        {
            var started = await client.StartReadAsync(path, observation, cancellationToken).ConfigureAwait(false);
            if (!started.Succeeded)
            {
                await client.DisposeAsync().ConfigureAwait(false);
                return new FileReadOpen(started, null);
            }

            return new FileReadOpen(hashed, new StreamReadSession(hashed.Stat, client));
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask<FileWriteOpen> BeginWriteAsync(
        FileWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Mode == FileConflictMode.Replace && request.TargetObservation is null)
            return new FileWriteOpen(FileOpResult.Fail(FileOpCodes.ReplaceObservationRequired), null);
        var client = _factory.Start(_workingDirectory);
        try
        {
            var started = await client.StartWriteAsync(request, cancellationToken).ConfigureAwait(false);
            if (!started.Succeeded)
            {
                await client.DisposeAsync().ConfigureAwait(false);
                return new FileWriteOpen(started, null);
            }

            return new FileWriteOpen(started, new BridgeWriteSession(client, request));
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    sealed class StreamReadSession : IFileReadSession
    {
        readonly FileBridgeClient _client;
        public StreamReadSession(FileStat stat, FileBridgeClient client)
        {
            Stat = stat;
            _client = client;
        }

        public FileStat Stat { get; }

        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _client.ReadPayloadAsync(buffer, cancellationToken);

        public ValueTask DisposeAsync() => _client.DisposeAsync();
    }

    sealed class BridgeWriteSession : IFileWriteSession
    {
        readonly FileBridgeClient _client;
        readonly FileWriteRequest _request;
        bool _done;

        public BridgeWriteSession(FileBridgeClient client, FileWriteRequest request)
        {
            _client = client;
            _request = request;
            TempIdentity = new FileIdentity("tmp"u8.ToArray());
        }

        public Guid JobId => _request.JobId;
        public FileIdentity TempIdentity { get; }

        public ValueTask WriteAsync(ReadOnlyMemory<byte> chunk, CancellationToken cancellationToken = default) =>
            _client.WritePayloadAsync(chunk, cancellationToken);

        public async ValueTask<FileOpResult> CompleteAsync(CancellationToken cancellationToken = default)
        {
            _done = true;
            return await _client.FinishWriteAsync(cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask<FileOpResult> AbortAsync(CancellationToken cancellationToken = default)
        {
            _done = true;
            await _client.CancelAsync(cancellationToken).ConfigureAwait(false);
            return FileOpResult.Fail(FileOpCodes.Cancelled);
        }

        public async ValueTask DisposeAsync()
        {
            if (!_done)
                await AbortAsync().ConfigureAwait(false);
            await _client.DisposeAsync().ConfigureAwait(false);
        }
    }
}
