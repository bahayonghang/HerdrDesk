using System.Security.Cryptography;
using System.Text;
using HerdDesk.Contracts;
using HerdDesk.Core;
using Microsoft.Win32.SafeHandles;

namespace HerdDesk.Infrastructure.Files;

public sealed class LocalFileEndpoint : IFileEndpoint
{
    readonly string _root;

    public LocalFileEndpoint(string root, TransferEndpointKey key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (!Path.IsPathFullyQualified(root))
            throw new ArgumentException(FileOpCodes.InvalidPath, nameof(root));
        _root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Key = key;
        ReplaceSupported = OperatingSystem.IsWindows();
    }

    public TransferEndpointKey Key { get; }
    public bool ReplaceSupported { get; }

    public ValueTask<FileOpResult> ListAsync(
        FileLocator path,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        if (limit is < 1 or > 1000)
            return ValueTask.FromResult(FileOpResult.Fail(FileOpCodes.InvalidPath));
        if (!TryResolve(path, out var full, out var components, out var error))
            return ValueTask.FromResult(FileOpResult.Fail(error));
        if (!Directory.Exists(full))
            return ValueTask.FromResult(FileOpResult.Fail(
                File.Exists(full) ? FileOpCodes.NotDirectory : FileOpCodes.NotFound));
        if (IsReparse(full))
            return ValueTask.FromResult(FileOpResult.Fail(FileOpCodes.Unsupported));

        byte[]? after = null;
        if (cursor is not null)
        {
            try
            {
                after = FileBridgeText.BoundedBase64(cursor, FileBridgeCodes.InvalidCursor, FileBridgeLimits.MaxCursor);
            }
            catch (FileBridgeProtocolException)
            {
                return ValueTask.FromResult(FileOpResult.Fail(FileBridgeCodes.InvalidCursor));
            }
        }

        var names = Directory.GetFileSystemEntries(full);
        Array.Sort(names, StringComparer.Ordinal);
        var entries = new List<FileEntry>();
        var seenCursor = after is null;
        var hasMore = false;
        foreach (var entryPath in names)
        {
            var name = Path.GetFileName(entryPath);
            if (name is "." or "..")
                continue;
            var raw = Encoding.UTF8.GetBytes(name);
            try
            {
                WirePath.CheckComponent(raw);
            }
            catch (FileBridgeProtocolException)
            {
                continue;
            }

            if (!seenCursor)
            {
                if (raw.AsSpan().SequenceEqual(after))
                    seenCursor = true;
                continue;
            }

            if (entries.Count >= limit)
            {
                hasMore = true;
                break;
            }

            if (!TryStatPath(path.Append(new FileComponent(raw)), entryPath, out var stat, out _))
                continue;
            entries.Add(new FileEntry(
                new FileComponent(raw),
                name,
                stat.Kind,
                stat.Size,
                stat.Mtime,
                stat.MtimePrecision,
                stat.Identity,
                stat.Symlink,
                stat.Observation));
        }

        TryStatPath(path, full, out var dirStat, out _);
        var next = hasMore && entries.Count > 0
            ? Convert.ToBase64String(entries[^1].Name.Raw)
            : null;
        return ValueTask.FromResult(new FileOpResult(
            true,
            FileOpCodes.Ok,
            dirStat,
            entries,
            hasMore,
            next,
            path,
            dirStat?.Observation,
            FileHash.Empty,
            (ulong)entries.Count));
    }

    public ValueTask<FileOpResult> StatAsync(
        FileLocator path,
        FileObservation? observation,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        if (!TryResolve(path, out var full, out _, out var error))
            return ValueTask.FromResult(FileOpResult.Fail(error));
        if (!TryStatPath(path, full, out var stat, out error))
            return ValueTask.FromResult(FileOpResult.Fail(error));
        if (observation is not null && observation.Hex != stat.Observation.Hex)
            return ValueTask.FromResult(FileOpResult.Fail(FileOpCodes.StaleTarget));
        return ValueTask.FromResult(new FileOpResult(
            true, FileOpCodes.Ok, stat, FinalPath: path, Observation: stat.Observation,
            Sha256: stat.Sha256, Length: stat.Size));
    }

    public ValueTask<FileReadOpen> OpenReadAsync(
        FileLocator path,
        FileObservation? observation,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        if (!TryResolve(path, out var full, out _, out var error))
            return ValueTask.FromResult(new FileReadOpen(FileOpResult.Fail(error), null));
        if (!TryStatPath(path, full, out var stat, out error))
            return ValueTask.FromResult(new FileReadOpen(FileOpResult.Fail(error), null));
        if (stat.Kind != FileEntryKind.File)
            return ValueTask.FromResult(new FileReadOpen(FileOpResult.Fail(FileOpCodes.IsDirectory), null));
        if (observation is not null && observation.Hex != stat.Observation.Hex)
            return ValueTask.FromResult(new FileReadOpen(FileOpResult.Fail(FileOpCodes.StaleTarget), null));
        try
        {
            var stream = OpenReadNoFollow(full);
            var session = new ReadSession(stat, stream);
            return ValueTask.FromResult(new FileReadOpen(
                new FileOpResult(true, FileOpCodes.Ok, stat, Sha256: stat.Sha256, Length: stat.Size),
                session));
        }
        catch (UnauthorizedAccessException)
        {
            return ValueTask.FromResult(new FileReadOpen(FileOpResult.Fail(FileOpCodes.PermissionDenied), null));
        }
        catch (IOException)
        {
            return ValueTask.FromResult(new FileReadOpen(FileOpResult.Fail(FileOpCodes.NotFound), null));
        }
    }

    public ValueTask<FileWriteOpen> BeginWriteAsync(
        FileWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        if (request.Mode == FileConflictMode.Replace)
        {
            if (request.TargetObservation is null)
                return ValueTask.FromResult(new FileWriteOpen(
                    FileOpResult.Fail(FileOpCodes.ReplaceObservationRequired), null));
            if (!ReplaceSupported)
                return ValueTask.FromResult(new FileWriteOpen(
                    FileOpResult.Fail(FileOpCodes.Unsupported), null));
        }

        if (!TryResolve(request.Parent, out var parentFull, out _, out var error))
            return ValueTask.FromResult(new FileWriteOpen(FileOpResult.Fail(error), null));
        if (!Directory.Exists(parentFull))
            return ValueTask.FromResult(new FileWriteOpen(
                FileOpResult.Fail(File.Exists(parentFull) ? FileOpCodes.NotDirectory : FileOpCodes.ParentMissing),
                null));
        if (IsReparse(parentFull))
            return ValueTask.FromResult(new FileWriteOpen(FileOpResult.Fail(FileOpCodes.Unsupported), null));
        if (!TryStatPath(request.Parent, parentFull, out var parentStat, out error))
            return ValueTask.FromResult(new FileWriteOpen(FileOpResult.Fail(error), null));
        if (request.ParentObservation.Hex != parentStat.Observation.Hex)
            return ValueTask.FromResult(new FileWriteOpen(FileOpResult.Fail(FileOpCodes.StaleTarget), null));

        string destName;
        try
        {
            destName = Encoding.UTF8.GetString(request.Name.Raw);
            WirePath.CheckComponent(request.Name.Raw);
        }
        catch (FileBridgeProtocolException)
        {
            return ValueTask.FromResult(new FileWriteOpen(FileOpResult.Fail(FileOpCodes.InvalidPath), null));
        }

        if (WindowsLocalNameMapping.MappingRequired(destName))
            return ValueTask.FromResult(new FileWriteOpen(FileOpResult.Fail(FileOpCodes.MappingRequired), null));

        var destFull = Path.Combine(parentFull, destName);
        if (!IsInsideRoot(destFull))
            return ValueTask.FromResult(new FileWriteOpen(FileOpResult.Fail(FileOpCodes.InvalidPath), null));

        if (request.Mode == FileConflictMode.Replace)
        {
            if (!TryStatPath(request.Parent.Append(request.Name), destFull, out var target, out error))
                return ValueTask.FromResult(new FileWriteOpen(FileOpResult.Fail(error), null));
            if (target.Observation.Hex != request.TargetObservation!.Hex)
                return ValueTask.FromResult(new FileWriteOpen(FileOpResult.Fail(FileOpCodes.StaleTarget), null));
        }

        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var tempName = ".herddesk-upload-" + request.JobId.ToString("N") + "-" + nonce + ".tmp";
        var tempFull = Path.Combine(parentFull, tempName);
        try
        {
            var stream = CreateNewNoFollow(tempFull);
            if (!TryIdentity(tempFull, stream, out var identity, out var mtime, out var precision, out var kind))
            {
                stream.Dispose();
                TryDeleteExact(tempFull, null);
                return ValueTask.FromResult(new FileWriteOpen(FileOpResult.Fail(FileOpCodes.Unsupported), null));
            }

            var session = new WriteSession(
                this, request, parentFull, destFull, destName, tempFull, stream, identity, parentStat);
            return ValueTask.FromResult(new FileWriteOpen(
                new FileOpResult(true, FileOpCodes.Ok, Length: request.Length, Sha256: request.Sha256),
                session));
        }
        catch (UnauthorizedAccessException)
        {
            return ValueTask.FromResult(new FileWriteOpen(FileOpResult.Fail(FileOpCodes.PermissionDenied), null));
        }
        catch (IOException)
        {
            return ValueTask.FromResult(new FileWriteOpen(FileOpResult.Fail(FileOpCodes.NameExists), null));
        }
    }

    internal FileOpResult Commit(WriteSession session)
    {
        session.Stream.Flush();
        var digest = FileHash.Finish(session.Hash);
        if (session.Bytes != session.Request.Length)
            return Abort(session, FileOpCodes.LengthMismatch);
        if (!string.Equals(digest, session.Request.Sha256, StringComparison.Ordinal))
            return Abort(session, FileOpCodes.HashMismatch);
        if (!TryIdentity(session.TempFull, session.Stream, out var identity, out _, out _, out _))
            return Abort(session, FileOpCodes.OutcomeUnknown);
        if (!identity.SameAs(session.TempIdentity))
            return Abort(session, FileOpCodes.Conflict);

        session.Stream.Dispose();
        session.StreamClosed = true;
        session.Finished = true;

        if (!TryStatPath(session.Request.Parent, session.ParentFull, out var parentNow, out var error))
            return AbortClosed(session, error);
        if (parentNow.Observation.Hex != session.Parent.Observation.Hex)
            return AbortClosed(session, FileOpCodes.StaleTarget);

        var mode = session.Request.Mode;
        if (mode == FileConflictMode.Replace)
        {
            if (!TryStatPath(session.Request.Parent.Append(session.Request.Name), session.DestFull, out var target, out error))
                return AbortClosed(session, error);
            if (target.Observation.Hex != session.Request.TargetObservation!.Hex)
                return AbortClosed(session, FileOpCodes.StaleTarget);
            if (!ReplaceSupported)
                return AbortClosed(session, FileOpCodes.Unsupported);
            var backup = Path.Combine(
                session.ParentFull,
                ".herddesk-replace-backup-" + session.Request.JobId.ToString("N") + ".tmp");
            try
            {
                if (!WindowsNativeIo.TryReplace(session.DestFull, session.TempFull, backup))
                    return AbortClosed(session, FileOpCodes.Conflict);
                if (!TryStatPath(session.Request.Parent.Append(session.Request.Name), backup, out var backupStat, out _))
                {
                    return new FileOpResult(false, FileOpCodes.OutcomeUnknown);
                }

                if (!backupStat.Identity.SameAs(target.Identity))
                {
                    var scratch = Path.Combine(
                        session.ParentFull,
                        ".herddesk-replace-restore-" + session.Request.JobId.ToString("N") + ".tmp");
                    if (!WindowsNativeIo.TryReplace(session.DestFull, backup, scratch))
                        return new FileOpResult(false, FileOpCodes.OutcomeUnknown);
                    TryDeleteExact(scratch, session.TempIdentity);
                    return FileOpResult.Fail(FileOpCodes.Conflict);
                }
                TryDeleteExact(backup, backupStat.Identity);
            }
            catch (IOException)
            {
                return AbortClosed(session, FileOpCodes.Conflict);
            }
        }
        else
        {
            var attempts = mode == FileConflictMode.KeepBoth ? KeepBothNames.MaxAttempts : 1;
            var committed = false;
            var finalName = session.DestName;
            var finalFull = session.DestFull;
            for (var i = 0; i < attempts; i++)
            {
                var raw = KeepBothNames.Candidate(session.Request.Name.Raw, i);
                string name;
                try
                {
                    name = Encoding.UTF8.GetString(raw);
                    WirePath.CheckComponent(raw);
                }
                catch (FileBridgeProtocolException)
                {
                    continue;
                }

                if (WindowsLocalNameMapping.MappingRequired(name))
                    continue;
                var candidate = Path.Combine(session.ParentFull, name);
                if (!IsInsideRoot(candidate))
                    continue;
                try
                {
                    if (MoveNoReplace(session.TempFull, candidate))
                    {
                        committed = true;
                        finalName = name;
                        finalFull = candidate;
                        break;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    return AbortClosed(session, FileOpCodes.PermissionDenied);
                }
                catch (IOException)
                {
                }

                if (mode == FileConflictMode.Fail)
                    return AbortClosed(session, FileOpCodes.NameExists);
            }

            if (!committed)
                return AbortClosed(session, FileOpCodes.NameExists);

            session.DestFull = finalFull;
            session.DestName = finalName;
        }

        var finalLocator = session.Request.Parent.Append(new FileComponent(Encoding.UTF8.GetBytes(session.DestName)));
        if (!TryStatPath(finalLocator, session.DestFull, out var finalStat, out error))
            return new FileOpResult(false, error);
        return new FileOpResult(
            true,
            FileOpCodes.Ok,
            finalStat,
            FinalPath: finalLocator,
            Observation: finalStat.Observation,
            Sha256: digest,
            Length: session.Request.Length);
    }

    internal FileOpResult Abort(WriteSession session, string code)
    {
        try
        {
            session.Stream.Dispose();
        }
        catch (IOException)
        {
        }

        session.StreamClosed = true;
        session.Finished = true;
        return AbortClosed(session, code);
    }

    FileOpResult AbortClosed(WriteSession session, string code)
    {
        session.Finished = true;
        TryDeleteExact(session.TempFull, session.TempIdentity);
        return FileOpResult.Fail(code);
    }

    bool TryResolve(FileLocator path, out string full, out List<byte[]> components, out string error)
    {
        full = _root;
        components = [];
        error = FileOpCodes.Ok;
        for (var i = 0; i < path.Components.Count; i++)
        {
            var component = path.Components[i];
            try
            {
                WirePath.CheckComponent(component.Raw);
            }
            catch (FileBridgeProtocolException)
            {
                error = FileOpCodes.InvalidPath;
                return false;
            }

            string name;
            try
            {
                name = Encoding.UTF8.GetString(component.Raw);
            }
            catch (DecoderFallbackException)
            {
                error = FileOpCodes.InvalidPath;
                return false;
            }

            if (name.IndexOfAny(['/', '\\', '\0']) >= 0)
            {
                error = FileOpCodes.InvalidPath;
                return false;
            }

            full = Path.Combine(full, name);
            if (!IsInsideRoot(full))
            {
                error = FileOpCodes.InvalidPath;
                return false;
            }

            var last = i + 1 == path.Components.Count;
            if (!last && IsReparse(full))
            {
                error = FileOpCodes.Unsupported;
                return false;
            }

            components.Add(component.Raw);
        }

        return true;
    }

    bool IsInsideRoot(string full)
    {
        var resolved = Path.GetFullPath(full);
        return resolved.Equals(_root, StringComparison.OrdinalIgnoreCase) ||
               resolved.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    bool TryStatPath(FileLocator locator, string full, out FileStat stat, out string error)
    {
        stat = null!;
        error = FileOpCodes.NotFound;
        if (!File.Exists(full) && !Directory.Exists(full))
            return false;
        try
        {
            var info = OperatingSystem.IsWindows()
                ? StatWindows(full, out var winKind, out var identity, out var mtime, out var precision, out var size, out var symlink)
                : StatPortable(full, out winKind, out identity, out mtime, out precision, out size, out symlink);
            var kind = winKind;
            var sha = kind == FileEntryKind.File ? HashFile(full) : FileHash.Empty;
            var components = new byte[locator.Components.Count][];
            for (var i = 0; i < locator.Components.Count; i++)
                components[i] = locator.Components[i].Raw;
            var observation = FileObservationCodec.Compute(
                components, true, kind, identity, size, mtime, precision);
            stat = new FileStat(locator, kind, size, mtime, precision, identity, observation, symlink, sha);
            error = FileOpCodes.Ok;
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            error = FileOpCodes.PermissionDenied;
            return false;
        }
        catch (IOException)
        {
            error = FileOpCodes.NotFound;
            return false;
        }
    }

    static bool StatWindows(
        string full,
        out FileEntryKind kind,
        out FileIdentity identity,
        out ulong mtime,
        out ulong precision,
        out ulong size,
        out bool symlink)
    {
        using var handle = WindowsNativeIo.OpenNoFollow(full, false);
        if (!WindowsNativeIo.TryGetInformation(handle, out var info))
            throw new IOException(FileOpCodes.NotFound);
        symlink = WindowsNativeIo.IsReparse(info);
        kind = symlink
            ? FileEntryKind.Symlink
            : WindowsNativeIo.IsDirectory(info)
                ? FileEntryKind.Directory
                : FileEntryKind.File;
        identity = FileObservationCodec.WindowsIdentity(info.VolumeSerialNumber, WindowsNativeIo.FileIndex(info));
        mtime = WindowsNativeIo.MtimeSeconds(info);
        precision = 1;
        size = WindowsNativeIo.IsDirectory(info) ? 0 : WindowsNativeIo.Size(info);
        return true;
    }

    static bool StatPortable(
        string full,
        out FileEntryKind kind,
        out FileIdentity identity,
        out ulong mtime,
        out ulong precision,
        out ulong size,
        out bool symlink)
    {
        var attr = File.GetAttributes(full);
        symlink = attr.HasFlag(FileAttributes.ReparsePoint);
        kind = symlink
            ? FileEntryKind.Symlink
            : attr.HasFlag(FileAttributes.Directory)
                ? FileEntryKind.Directory
                : FileEntryKind.File;
        size = kind == FileEntryKind.File ? (ulong)new FileInfo(full).Length : 0;
        var write = File.GetLastWriteTimeUtc(full);
        mtime = write.Ticks < DateTime.UnixEpoch.Ticks
            ? 0
            : (ulong)(write - DateTime.UnixEpoch).TotalSeconds;
        precision = 1;
        identity = OperatingSystem.IsLinux() && UnixNativeIo.TryStatNoFollow(full, out var dev, out var ino)
            ? FileObservationCodec.UnixIdentity(dev, ino)
            : FileObservationCodec.PortableIdentity(full, size, mtime);
        return true;
    }

    bool TryIdentity(
        string path,
        FileStream stream,
        out FileIdentity identity,
        out ulong mtime,
        out ulong precision,
        out FileEntryKind kind)
    {
        identity = null!;
        mtime = 0;
        precision = 1;
        kind = FileEntryKind.File;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (!WindowsNativeIo.TryGetInformation(stream.SafeFileHandle, out var info))
                    return false;
                identity = FileObservationCodec.WindowsIdentity(
                    info.VolumeSerialNumber, WindowsNativeIo.FileIndex(info));
                mtime = WindowsNativeIo.MtimeSeconds(info);
                kind = WindowsNativeIo.IsDirectory(info) ? FileEntryKind.Directory : FileEntryKind.File;
                return true;
            }

            var size = (ulong)stream.Length;
            var write = File.GetLastWriteTimeUtc(path);
            mtime = write.Ticks < DateTime.UnixEpoch.Ticks
                ? 0
                : (ulong)(write - DateTime.UnixEpoch).TotalSeconds;
            identity = UnixNativeIo.TryStatFd(stream.SafeFileHandle, out var dev, out var ino)
                ? FileObservationCodec.UnixIdentity(dev, ino)
                : FileObservationCodec.PortableIdentity(path, size, mtime);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    static string HashFile(string path)
    {
        using var stream = OpenReadNoFollow(path);
        using var sha = FileHash.Create();
        var buffer = new byte[64 * 1024];
        int n;
        while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
            sha.AppendData(buffer.AsSpan(0, n));
        return FileHash.Finish(sha);
    }

    static FileStream OpenReadNoFollow(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var handle = WindowsNativeIo.OpenNoFollow(path, false);
            return new FileStream(handle, FileAccess.Read);
        }

        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.None);
    }

    static FileStream CreateNewNoFollow(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var handle = WindowsNativeIo.CreateNewExclusive(path);
            return new FileStream(handle, FileAccess.ReadWrite);
        }

        return new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.None);
    }

    static bool MoveNoReplace(string from, string to)
    {
        if (OperatingSystem.IsWindows())
            return WindowsNativeIo.MoveNoReplace(from, to);
        try
        {
            File.Move(from, to, overwrite: false);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    static bool IsReparse(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (IOException)
        {
            return false;
        }
    }

    void TryDeleteExact(string path, FileIdentity? expected)
    {
        try
        {
            if (!File.Exists(path))
                return;
            if (expected is not null)
            {
                if (!TryStatPath(FileLocator.Root, path, out var stat, out _))
                    return;
                if (!stat.Identity.SameAs(expected))
                    return;
            }

            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    sealed class ReadSession : IFileReadSession
    {
        readonly FileStream _stream;
        public ReadSession(FileStat stat, FileStream stream)
        {
            Stat = stat;
            _stream = stream;
        }

        public FileStat Stat { get; }

        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _stream.ReadAsync(buffer, cancellationToken);

        public ValueTask DisposeAsync() => _stream.DisposeAsync();
    }

    internal sealed class WriteSession : IFileWriteSession
    {
        readonly LocalFileEndpoint _owner;
        public readonly FileWriteRequest Request;
        public readonly string ParentFull;
        public string DestFull;
        public string DestName;
        public readonly string TempFull;
        public FileStream Stream;
        public bool StreamClosed;
        public bool Finished;
        public readonly IncrementalHash Hash = FileHash.Create();
        public ulong Bytes;
        public readonly FileStat Parent;

        public WriteSession(
            LocalFileEndpoint owner,
            FileWriteRequest request,
            string parentFull,
            string destFull,
            string destName,
            string tempFull,
            FileStream stream,
            FileIdentity tempIdentity,
            FileStat parent)
        {
            _owner = owner;
            Request = request;
            ParentFull = parentFull;
            DestFull = destFull;
            DestName = destName;
            TempFull = tempFull;
            Stream = stream;
            TempIdentity = tempIdentity;
            Parent = parent;
        }

        public Guid JobId => Request.JobId;
        public FileIdentity TempIdentity { get; }

        public async ValueTask WriteAsync(ReadOnlyMemory<byte> chunk, CancellationToken cancellationToken = default)
        {
            await Stream.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
            Hash.AppendData(chunk.Span);
            Bytes += (ulong)chunk.Length;
        }

        public ValueTask<FileOpResult> CompleteAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return ValueTask.FromResult(_owner.Commit(this));
        }

        public ValueTask<FileOpResult> AbortAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return ValueTask.FromResult(_owner.Abort(this, FileOpCodes.Cancelled));
        }

        public ValueTask DisposeAsync()
        {
            if (!Finished)
                _owner.Abort(this, FileOpCodes.Cancelled);
            Hash.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
