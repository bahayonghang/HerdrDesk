using System.Text.Json;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Process;

namespace HerdDesk.Infrastructure.Files;

public sealed class FileBridgeClient : IAsyncDisposable
{
    public const int StderrRetainBytes = 4096;
    readonly OwnedChildProcess _process;
    readonly FileBridgeProtocolCodec _codec = new();
    readonly SemaphoreSlim _gate = new(1, 1);
    readonly byte[] _stderrWindow;
    int _stderrLength;
    long _stderrBytes;
    uint _clientSeq;
    bool _used;
    bool _writeReady;
    byte[]? _readLeftover;
    int _readLeftoverOffset;
    int _readLeftoverLength;
    bool _readEof;
    int _disposed;
    FileBridgeOutcome _outcome = FileBridgeOutcome.Pending;
    string? _job;

    FileBridgeClient(OwnedChildProcess process)
    {
        _process = process;
        _stderrWindow = new byte[StderrRetainBytes];
        _ = DrainStderr();
    }

    public FileBridgeOutcome Outcome => _outcome;
    public bool AutoReplayForbidden => _codec.AutoReplayForbidden;

    public static FileBridgeClient Start(string executable, string workingDirectory)
    {
        var process = OwnedChildProcess.Start(
            executable,
            ["serve", "--stdio", "--protocol", "1.0"],
            workingDirectory);
        return new FileBridgeClient(process);
    }

    public static FileBridgeClient Start(string executable, IReadOnlyList<string> arguments)
    {
        var process = OwnedChildProcess.Start(executable, arguments);
        return new FileBridgeClient(process);
    }

    public async ValueTask<FileOpResult> ListAsync(
        FileLocator path,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        var job = NewJob();
        var payload = FileBridgeFrames.ListRequest(job, WirePath.Encode(Raw(path)), limit, cursor);
        return await RoundTrip(job, FileBridgeOp.List, payload, null, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<FileOpResult> StatAsync(
        FileLocator path,
        FileObservation? observation,
        CancellationToken cancellationToken = default)
    {
        var job = NewJob();
        var payload = FileBridgeFrames.StatRequest(
            job, "stat", WirePath.Encode(Raw(path)), observation?.Hex);
        return await RoundTrip(job, FileBridgeOp.Stat, payload, null, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<FileOpResult> ReadAsync(
        FileLocator path,
        FileObservation? observation,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        var job = NewJob();
        var payload = FileBridgeFrames.StatRequest(
            job, "read", WirePath.Encode(Raw(path)), observation?.Hex);
        return await RoundTrip(job, FileBridgeOp.Read, payload, destination, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<FileOpResult> WriteAsync(
        FileWriteRequest request,
        Stream source,
        CancellationToken cancellationToken = default)
    {
        if (request.Mode == FileConflictMode.Replace && request.TargetObservation is null)
            return FileOpResult.Fail(FileOpCodes.ReplaceObservationRequired);
        var job = request.JobId.ToString("D");
        _job = job;
        var mode = request.Mode == FileConflictMode.Replace ? "replace" : "create";
        var payload = FileBridgeFrames.WriteRequest(
            job,
            WirePath.Encode(Raw(request.Parent)),
            WirePath.EncodeComponent(request.Name.Raw),
            mode,
            request.ParentObservation.Hex,
            request.TargetObservation?.Hex,
            FileBridgeFrames.Decimal(request.Length),
            request.Sha256);
        return await RoundTrip(job, FileBridgeOp.Write, payload, null, cancellationToken, source, request.Length)
            .ConfigureAwait(false);
    }

    public async ValueTask<FileOpResult> StartWriteAsync(
        FileWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Mode == FileConflictMode.Replace && request.TargetObservation is null)
            return FileOpResult.Fail(FileOpCodes.ReplaceObservationRequired);
        if (_used)
            throw new FileBridgeProtocolException(FileBridgeCodes.SecondRequest);
        _used = true;
        var job = request.JobId.ToString("D");
        _job = job;
        var mode = request.Mode == FileConflictMode.Replace ? "replace" : "create";
        var payload = FileBridgeFrames.WriteRequest(
            job,
            WirePath.Encode(Raw(request.Parent)),
            WirePath.EncodeComponent(request.Name.Raw),
            mode,
            request.ParentObservation.Hex,
            request.TargetObservation?.Hex,
            FileBridgeFrames.Decimal(request.Length),
            request.Sha256);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteFrame(FileBridgeKind.RequestJson, payload, cancellationToken).ConfigureAwait(false);
            var frame = await NextHelperFrame(cancellationToken).ConfigureAwait(false);
            if (frame is null)
            {
                _outcome = _codec.OnEof();
                return FileOpResult.Fail(FileOpCodes.OutcomeUnknown);
            }

            if (frame.Kind == FileBridgeKind.ErrorJson)
                return ParseError(frame.Payload);
            if (frame.Kind != FileBridgeKind.AcceptedJson)
                return FileOpResult.Fail(FileBridgeCodes.UnexpectedKind);
            _writeReady = true;
            return FileOpResult.Ok();
        }
        catch (FileBridgeProtocolException ex)
        {
            _outcome = FileBridgeOutcome.Failed;
            return FileOpResult.Fail(ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask WritePayloadAsync(ReadOnlyMemory<byte> chunk, CancellationToken cancellationToken = default)
    {
        if (!_writeReady)
            throw new FileBridgeProtocolException(FileBridgeCodes.ProtocolNotActive);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var offset = 0;
            while (offset < chunk.Length)
            {
                var take = Math.Min(FileBridgeLimits.MaxData, chunk.Length - offset);
                await WriteFrame(FileBridgeKind.Data, chunk.Slice(offset, take), cancellationToken)
                    .ConfigureAwait(false);
                offset += take;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<FileOpResult> FinishWriteAsync(CancellationToken cancellationToken = default)
    {
        if (!_writeReady)
            return FileOpResult.Fail(FileBridgeCodes.ProtocolNotActive);
        _writeReady = false;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteFrame(FileBridgeKind.EndData, ReadOnlyMemory<byte>.Empty, cancellationToken)
                .ConfigureAwait(false);
            var frame = await NextHelperFrame(cancellationToken).ConfigureAwait(false);
            if (frame is null)
            {
                _outcome = _codec.OnEof();
                return FileOpResult.Fail(FileOpCodes.OutcomeUnknown);
            }

            return frame.Kind switch
            {
                FileBridgeKind.CompleteJson => ParseComplete(frame.Payload, [], null),
                FileBridgeKind.ErrorJson => ParseError(frame.Payload),
                _ => FileOpResult.Fail(FileBridgeCodes.UnexpectedKind)
            };
        }
        catch (FileBridgeProtocolException ex)
        {
            _outcome = FileBridgeOutcome.Failed;
            return FileOpResult.Fail(ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<FileOpResult> StartReadAsync(
        FileLocator path,
        FileObservation? observation,
        CancellationToken cancellationToken = default)
    {
        var job = NewJob();
        var payload = FileBridgeFrames.StatRequest(
            job, "read", WirePath.Encode(Raw(path)), observation?.Hex);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteFrame(FileBridgeKind.RequestJson, payload, cancellationToken).ConfigureAwait(false);
            var frame = await NextHelperFrame(cancellationToken).ConfigureAwait(false);
            if (frame is null)
            {
                _outcome = _codec.OnEof();
                return FileOpResult.Fail(FileOpCodes.OutcomeUnknown);
            }

            if (frame.Kind == FileBridgeKind.ErrorJson)
                return ParseError(frame.Payload);
            if (frame.Kind != FileBridgeKind.AcceptedJson)
                return FileOpResult.Fail(FileBridgeCodes.UnexpectedKind);
            using var doc = FileBridgeJson.ParseObject(frame.Payload);
            var identity = FileBridgeText.BoundedBase64(
                FileBridgeJson.ReqString(doc.RootElement, "identity"),
                FileBridgeCodes.InvalidIdentity,
                FileBridgeLimits.MaxIdentity);
            var size = FileBridgeText.RequireDecimalU64(FileBridgeJson.ReqString(doc.RootElement, "size"));
            var stat = new FileStat(
                path,
                FileEntryKind.File,
                size,
                0,
                1,
                new FileIdentity(identity),
                observation ?? new FileObservation(new string('0', 64)),
                false,
                FileHash.Empty);
            return new FileOpResult(true, FileOpCodes.Ok, stat, Length: size, Sha256: FileHash.Empty);
        }
        catch (FileBridgeProtocolException ex)
        {
            _outcome = FileBridgeOutcome.Failed;
            return FileOpResult.Fail(ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<int> ReadPayloadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var copied = 0;
        while (copied < buffer.Length)
        {
            if (_readLeftover is not null && _readLeftoverOffset < _readLeftoverLength)
            {
                var n = Math.Min(buffer.Length - copied, _readLeftoverLength - _readLeftoverOffset);
                _readLeftover.AsSpan(_readLeftoverOffset, n).CopyTo(buffer.Span[copied..]);
                _readLeftoverOffset += n;
                copied += n;
                continue;
            }

            if (_readEof)
                return copied;
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            FileBridgeFrame? frame;
            try
            {
                frame = await NextHelperFrame(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }

            if (frame is null)
            {
                _readEof = true;
                _outcome = _codec.OnEof();
                return copied;
            }

            switch (frame.Kind)
            {
                case FileBridgeKind.Data:
                    _readLeftover = frame.Payload;
                    _readLeftoverOffset = 0;
                    _readLeftoverLength = frame.Payload.Length;
                    break;
                case FileBridgeKind.CompleteJson:
                    _readEof = true;
                    _ = ParseComplete(frame.Payload, [], null);
                    return copied;
                case FileBridgeKind.ErrorJson:
                    _readEof = true;
                    throw new FileBridgeProtocolException(ParseError(frame.Payload).Code);
                default:
                    throw new FileBridgeProtocolException(FileBridgeCodes.UnexpectedKind);
            }
        }

        return copied;
    }

    async ValueTask<FileBridgeFrame?> NextHelperFrame(CancellationToken cancellationToken)
    {
        while (true)
        {
            var frame = await ReadFrame(cancellationToken).ConfigureAwait(false);
            if (frame is null || frame.Kind != FileBridgeKind.ProgressJson)
                return frame;
        }
    }

    public async ValueTask CancelAsync(CancellationToken cancellationToken = default)
    {
        if (_job is null)
            return;
        var payload = FileBridgeFrames.CancelRequest(_job);
        await WriteFrame(FileBridgeKind.CancelJson, payload, cancellationToken).ConfigureAwait(false);
        _codec.OnStdinClosed();
        _process.CloseStandardInput();
    }

    async Task DrainStderr()
    {
        var buffer = new byte[4096];
        try
        {
            while (true)
            {
                var n = await _process.StandardError.ReadAsync(buffer).ConfigureAwait(false);
                if (n == 0)
                    return;
                _stderrBytes += n;
                var chunk = buffer.AsSpan(0, n);
                if (chunk.Length >= _stderrWindow.Length)
                {
                    chunk[^_stderrWindow.Length..].CopyTo(_stderrWindow);
                    _stderrLength = _stderrWindow.Length;
                }
                else if (_stderrLength + chunk.Length <= _stderrWindow.Length)
                {
                    chunk.CopyTo(_stderrWindow.AsSpan(_stderrLength));
                    _stderrLength += chunk.Length;
                }
                else
                {
                    var keep = _stderrWindow.Length - chunk.Length;
                    _stderrWindow.AsSpan(_stderrLength - keep, keep).CopyTo(_stderrWindow);
                    chunk.CopyTo(_stderrWindow.AsSpan(keep));
                    _stderrLength = _stderrWindow.Length;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    string NewJob()
    {
        if (_used)
            throw new FileBridgeProtocolException(FileBridgeCodes.SecondRequest);
        _used = true;
        _job = Guid.NewGuid().ToString("D");
        return _job;
    }

    async ValueTask<FileOpResult> RoundTrip(
        string job,
        FileBridgeOp op,
        byte[] requestJson,
        Stream? readInto,
        CancellationToken cancellationToken,
        Stream? writeFrom = null,
        ulong writeLength = 0)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteFrame(FileBridgeKind.RequestJson, requestJson, cancellationToken).ConfigureAwait(false);
            var entries = new List<FileEntry>();
            FileStat? stat = null;
            while (true)
            {
                var frame = await ReadFrame(cancellationToken).ConfigureAwait(false);
                if (frame is null)
                {
                    _outcome = _codec.OnEof();
                    return FileOpResult.Fail(FileOpCodes.OutcomeUnknown);
                }

                switch (frame.Kind)
                {
                    case FileBridgeKind.AcceptedJson:
                        using (var doc = FileBridgeJson.ParseObject(frame.Payload))
                        {
                            var identity = FileBridgeText.BoundedBase64(
                                FileBridgeJson.ReqString(doc.RootElement, "identity"),
                                FileBridgeCodes.InvalidIdentity,
                                FileBridgeLimits.MaxIdentity);
                            var size = FileBridgeText.RequireDecimalU64(
                                FileBridgeJson.ReqString(doc.RootElement, "size"));
                            stat = new FileStat(
                                FileLocator.Root,
                                FileEntryKind.Other,
                                size,
                                0,
                                1,
                                new FileIdentity(identity),
                                new FileObservation(new string('0', 64)),
                                false,
                                FileHash.Empty);
                        }

                        if (op == FileBridgeOp.Write && writeFrom is not null)
                            await WriteData(writeFrom, writeLength, cancellationToken).ConfigureAwait(false);
                        break;
                    case FileBridgeKind.EntryJson:
                        using (var doc = FileBridgeJson.ParseObject(frame.Payload))
                            entries.Add(FileBridgeFrames.ParseEntry(doc.RootElement));
                        break;
                    case FileBridgeKind.Data:
                        if (readInto is not null)
                            await readInto.WriteAsync(frame.Payload, cancellationToken).ConfigureAwait(false);
                        break;
                    case FileBridgeKind.ProgressJson:
                        break;
                    case FileBridgeKind.CompleteJson:
                        return ParseComplete(frame.Payload, entries, stat);
                    case FileBridgeKind.ErrorJson:
                        return ParseError(frame.Payload);
                    default:
                        return FileOpResult.Fail(FileBridgeCodes.UnexpectedKind);
                }
            }
        }
        catch (FileBridgeProtocolException ex)
        {
            _outcome = FileBridgeOutcome.Failed;
            return FileOpResult.Fail(ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    async ValueTask WriteData(Stream source, ulong length, CancellationToken cancellationToken)
    {
        var remaining = length;
        var buffer = new byte[FileBridgeLimits.MaxData];
        while (remaining > 0)
        {
            var take = (int)Math.Min((ulong)buffer.Length, remaining);
            var n = await source.ReadAsync(buffer.AsMemory(0, take), cancellationToken).ConfigureAwait(false);
            if (n == 0)
                throw new FileBridgeProtocolException(FileBridgeCodes.InvalidLength);
            await WriteFrame(FileBridgeKind.Data, buffer.AsMemory(0, n), cancellationToken).ConfigureAwait(false);
            remaining -= (ulong)n;
        }

        await WriteFrame(FileBridgeKind.EndData, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
    }

    FileOpResult ParseComplete(byte[] payload, List<FileEntry> entries, FileStat? accepted)
    {
        using var doc = FileBridgeJson.ParseObject(payload);
        var root = doc.RootElement;
        var path = WirePath.Decode(FileBridgeJson.ReqString(root, "path"));
        var locator = new FileLocator(path.Select(item => new FileComponent(item)).ToArray());
        var length = FileBridgeText.RequireDecimalU64(FileBridgeJson.ReqString(root, "length"));
        var sha = FileBridgeJson.ReqString(root, "sha256");
        var observation = FileBridgeJson.ReqString(root, "observation");
        var hasMore = FileBridgeJson.Has(root, "has_more") && FileBridgeJson.ReqBool(root, "has_more");
        _outcome = FileBridgeOutcome.Success;
        return new FileOpResult(
            true,
            FileOpCodes.Ok,
            accepted,
            entries,
            hasMore,
            null,
            locator,
            new FileObservation(observation),
            sha,
            length);
    }

    FileOpResult ParseError(byte[] payload)
    {
        using var doc = FileBridgeJson.ParseObject(payload);
        var code = FileBridgeJson.ReqString(doc.RootElement, "code");
        _outcome = code == FileOpCodes.Cancelled ? FileBridgeOutcome.Cancelled : FileBridgeOutcome.Failed;
        return FileOpResult.Fail(code);
    }

    async ValueTask WriteFrame(FileBridgeKind kind, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var bytes = FileBridgeFrames.Frame(kind, _clientSeq, payload.Span);
        _clientSeq++;
        _codec.PushClient(bytes);
        await _process.StandardInput.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    async ValueTask<FileBridgeFrame?> ReadFrame(CancellationToken cancellationToken)
    {
        var header = new byte[FileBridgeLimits.HeaderLength];
        var got = await ReadExact(_process.StandardOutput, header, cancellationToken).ConfigureAwait(false);
        if (got == 0)
            return null;
        if (got < header.Length)
        {
            _codec.PushHelper(header.AsSpan(0, got));
            _codec.OnEof();
            return null;
        }

        var parsed = FileBridgeKindCodec.ParseHeader(header);
        byte[] payload = [];
        if (parsed.PayloadLength > 0)
        {
            payload = new byte[parsed.PayloadLength];
            var n = await ReadExact(_process.StandardOutput, payload, cancellationToken).ConfigureAwait(false);
            if (n < payload.Length)
            {
                var partial = new byte[header.Length + n];
                header.CopyTo(partial, 0);
                payload.AsSpan(0, n).CopyTo(partial.AsSpan(header.Length));
                _codec.PushHelper(partial);
                _codec.OnEof();
                return null;
            }
        }

        var frameBytes = new byte[header.Length + payload.Length];
        header.CopyTo(frameBytes, 0);
        payload.CopyTo(frameBytes, header.Length);
        var frames = _codec.PushHelper(frameBytes);
        return frames.Count > 0 ? frames[^1] : new FileBridgeFrame(
            FileBridgeDirection.Helper, parsed.Kind, parsed.Sequence, payload);
    }

    static async ValueTask<int> ReadExact(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (n == 0)
                return offset;
            offset += n;
        }

        return offset;
    }

    static IReadOnlyList<byte[]> Raw(FileLocator path)
    {
        var list = new byte[path.Components.Count][];
        for (var i = 0; i < path.Components.Count; i++)
            list[i] = path.Components[i].Raw;
        return list;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        try
        {
            if (_codec.Outcome == FileBridgeOutcome.Pending && !_codec.WouldExit)
            {
                try
                {
                    _codec.OnStdinClosed();
                }
                catch (FileBridgeProtocolException)
                {
                }
            }

            await _process.DisposeAsync().ConfigureAwait(false);
            if (_process.ExitCode is int code)
            {
                try
                {
                    _outcome = _codec.OnProcessExit(code);
                }
                catch (FileBridgeProtocolException)
                {
                    _outcome = FileBridgeOutcome.Unknown;
                }
            }
        }
        catch (IOException)
        {
        }

        _gate.Dispose();
        _ = _stderrBytes;
    }
}
