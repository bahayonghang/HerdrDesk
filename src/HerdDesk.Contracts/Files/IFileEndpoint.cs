namespace HerdDesk.Contracts;

public interface IFileReadSession : IAsyncDisposable
{
    FileStat Stat { get; }
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);
}

public interface IFileWriteSession : IAsyncDisposable
{
    Guid JobId { get; }
    FileIdentity TempIdentity { get; }
    ValueTask WriteAsync(ReadOnlyMemory<byte> chunk, CancellationToken cancellationToken = default);
    ValueTask<FileOpResult> CompleteAsync(CancellationToken cancellationToken = default);
    ValueTask<FileOpResult> AbortAsync(CancellationToken cancellationToken = default);
}

public sealed record FileReadOpen(FileOpResult Result, IFileReadSession? Session);
public sealed record FileWriteOpen(FileOpResult Result, IFileWriteSession? Session);

public interface IFileEndpoint
{
    TransferEndpointKey Key { get; }
    bool ReplaceSupported { get; }
    ValueTask<FileOpResult> ListAsync(
        FileLocator path,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default);
    ValueTask<FileOpResult> StatAsync(
        FileLocator path,
        FileObservation? observation,
        CancellationToken cancellationToken = default);
    ValueTask<FileReadOpen> OpenReadAsync(
        FileLocator path,
        FileObservation? observation,
        CancellationToken cancellationToken = default);
    ValueTask<FileWriteOpen> BeginWriteAsync(
        FileWriteRequest request,
        CancellationToken cancellationToken = default);
}

public interface IRemoteFileService
{
    IFileEndpoint Endpoint { get; }
}
