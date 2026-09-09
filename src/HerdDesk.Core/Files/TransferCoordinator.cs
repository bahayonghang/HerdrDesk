using HerdDesk.Contracts;

namespace HerdDesk.Core;

public sealed class TransferCoordinator
{
    public const int ChunkBytes = 1024 * 1024;
    private readonly ConnectionAdmissionPolicy _admission;

    public TransferCoordinator(ConnectionAdmissionPolicy admission)
    {
        _admission = admission ?? throw new ArgumentNullException(nameof(admission));
    }

    public async ValueTask<TransferResult> CopyAsync(
        TransferRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.JobId == Guid.Empty)
            return Fail(request, FileOpCodes.InvalidPath, TransferStage.Failed);
        if (request.Conflict == FileConflictMode.Replace)
        {
            if (request.DestinationTargetObservation is null)
                return Fail(request, FileOpCodes.ReplaceObservationRequired, TransferStage.Failed);
            if (!request.Destination.ReplaceSupported)
                return Fail(request, FileOpCodes.Unsupported, TransferStage.Failed);
        }

        var jobId = request.JobId.ToString("D");
        var destKey = request.Destination.Key;
        var destAdmit = _admission.TryAcquireFileJob(destKey.Session, destKey.Epoch, jobId);
        if (!destAdmit.Admitted)
        {
            return new TransferResult(
                false,
                destAdmit.Code,
                TransferStage.Queued,
                request.JobId,
                null,
                null,
                0,
                null);
        }

        ConnectionLease? sourceLease = null;
        var sourceKey = request.Source.Key;
        if (sourceKey.Device != destKey.Device)
        {
            var sourceAdmit = _admission.TryAcquireFileJob(sourceKey.Session, sourceKey.Epoch, jobId + "-src");
            if (!sourceAdmit.Admitted)
            {
                destAdmit.Lease?.Dispose();
                return new TransferResult(
                    false,
                    sourceAdmit.Code,
                    TransferStage.Queued,
                    request.JobId,
                    null,
                    null,
                    0,
                    null);
            }

            sourceLease = sourceAdmit.Lease;
        }

        IFileWriteSession? write = null;
        IFileReadSession? read = null;
        var stage = TransferStage.Admitted;
        try
        {
            stage = TransferStage.Starting;
            var openRead = await request.Source.OpenReadAsync(
                request.SourcePath, null, cancellationToken).ConfigureAwait(false);
            if (!openRead.Result.Succeeded || openRead.Session is null || openRead.Result.Stat is null)
                return Fail(request, openRead.Result.Code, TransferStage.Failed);
            read = openRead.Session;
            var sourceStat = openRead.Result.Stat;
            if (sourceStat.Kind != FileEntryKind.File)
                return Fail(request, FileOpCodes.IsDirectory, TransferStage.Failed);

            var parentStat = await request.Destination.StatAsync(
                request.DestinationParent, null, cancellationToken).ConfigureAwait(false);
            if (!parentStat.Succeeded || parentStat.Stat is null)
                return Fail(request, parentStat.Code == FileOpCodes.NotFound
                    ? FileOpCodes.ParentMissing
                    : parentStat.Code, TransferStage.Failed);
            if (parentStat.Stat.Kind != FileEntryKind.Directory)
                return Fail(request, FileOpCodes.NotDirectory, TransferStage.Failed);

            stage = TransferStage.Staging;
            var writeOpen = await request.Destination.BeginWriteAsync(
                new FileWriteRequest(
                    request.JobId,
                    request.DestinationParent,
                    request.DestinationName,
                    request.Conflict,
                    parentStat.Stat.Observation,
                    request.DestinationTargetObservation,
                    sourceStat.Size,
                    sourceStat.Sha256),
                cancellationToken).ConfigureAwait(false);
            if (!writeOpen.Result.Succeeded || writeOpen.Session is null)
                return Fail(request, writeOpen.Result.Code, TransferStage.Failed);
            write = writeOpen.Session;

            stage = TransferStage.Transferring;
            var buffer = new byte[ChunkBytes];
            ulong accepted = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var n = await read.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (n == 0)
                    break;
                await write.WriteAsync(buffer.AsMemory(0, n), cancellationToken).ConfigureAwait(false);
                accepted += (ulong)n;
                if (accepted > sourceStat.Size)
                    return await Cleanup(request, write, FileOpCodes.LengthMismatch).ConfigureAwait(false);
            }

            if (accepted != sourceStat.Size)
                return await Cleanup(request, write, FileOpCodes.LengthMismatch).ConfigureAwait(false);

            stage = TransferStage.Verifying;
            var restat = await request.Source.StatAsync(
                request.SourcePath, null, cancellationToken).ConfigureAwait(false);
            if (!restat.Succeeded || restat.Stat is null ||
                !restat.Stat.Identity.SameAs(sourceStat.Identity) ||
                restat.Stat.Size != sourceStat.Size ||
                restat.Stat.Sha256 != sourceStat.Sha256)
                return await Cleanup(request, write, FileOpCodes.SourceChanged).ConfigureAwait(false);

            stage = TransferStage.Committing;
            var committed = await write.CompleteAsync(cancellationToken).ConfigureAwait(false);
            if (!committed.Succeeded)
                return Fail(request, committed.Code, TransferStage.Failed);

            var progress = new TransferProgress(
                request.JobId, sourceKey, destKey, accepted, sourceStat.Size);
            return new TransferResult(
                true,
                FileOpCodes.Ok,
                TransferStage.Completed,
                request.JobId,
                committed.FinalPath ?? request.DestinationParent.Append(request.DestinationName),
                committed.Sha256 ?? sourceStat.Sha256,
                committed.Length,
                progress);
        }
        catch (OperationCanceledException)
        {
            if (write is not null)
                await write.AbortAsync(CancellationToken.None).ConfigureAwait(false);
            write = null;
            return Fail(request, FileOpCodes.Cancelled, TransferStage.Cancelled);
        }
        finally
        {
            if (write is not null)
                await write.DisposeAsync().ConfigureAwait(false);
            if (read is not null)
                await read.DisposeAsync().ConfigureAwait(false);
            sourceLease?.Dispose();
            destAdmit.Lease?.Dispose();
            _ = stage;
        }
    }

    private static TransferResult Fail(TransferRequest request, string code, TransferStage stage) =>
        new(false, code, stage, request.JobId, null, null, 0, null);

    private static async ValueTask<TransferResult> Cleanup(
        TransferRequest request,
        IFileWriteSession write,
        string code)
    {
        _ = await write.AbortAsync().ConfigureAwait(false);
        return Fail(request, code, TransferStage.Failed);
    }
}
