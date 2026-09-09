using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HerdDesk.Infrastructure.Files;

internal static class FileBridgeProtocolVectorTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("hd-027 golden vectors whole and one-byte", GoldenWholeAndSplit),
        ("hd-027 golden vector hashes match manifest", ManifestHashes),
        ("hd-027 json 1MiB boundary", JsonOneMibBoundary),
        ("hd-027 data 1MiB boundary", DataOneMibBoundary)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void GoldenWholeAndSplit()
    {
        foreach (var vector in LoadVectors())
        {
            var whole = Run(vector, null);
            var split = Run(vector, 1);
            Check(whole.Outcome == split.Outcome);
            if (vector.Expect == "accept")
            {
                CheckOk(vector, whole);
                CheckOk(vector, split);
            }
        }
    }

    static void ManifestHashes()
    {
        var root = FindRepoRoot();
        var dir = Path.Combine(root, "filebridge", "spec", "test-vectors");
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "manifest.json")));
        foreach (var vector in doc.RootElement.GetProperty("vectors").EnumerateArray())
        {
            var file = vector.GetProperty("file").GetString()!;
            var expected = vector.GetProperty("sha256").GetString()!;
            var bytes = File.ReadAllBytes(Path.Combine(dir, file));
            var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            Check(actual == expected);
        }
    }

    static void JsonOneMibBoundary()
    {
        var payload = EntryPayload(FileBridgeLimits.MaxJson);
        var (session, seq) = ListPrefix();
        var frame = Header(FileBridgeKind.EntryJson, seq, (uint)payload.Length);
        session.PushHelper(Concat(frame, payload));

        try
        {
            FileBridgeKindCodec.EncodeHeader(FileBridgeKind.EntryJson, seq, FileBridgeLimits.MaxJson + 1u);
            throw new Exception("assertion_failed");
        }
        catch (FileBridgeProtocolException ex)
        {
            Check(ex.Message == FileBridgeCodes.PayloadTooLarge);
        }

        var (oversize, seq2) = ListPrefix();
        var header = new byte[FileBridgeLimits.HeaderLength];
        "HDFB"u8.CopyTo(header);
        header[4] = 1;
        header[6] = (byte)FileBridgeKind.EntryJson;
        WriteU32(header, 8, FileBridgeLimits.MaxJson + 1u);
        WriteU32(header, 12, seq2);
        try
        {
            oversize.PushHelper(header);
            throw new Exception("assertion_failed");
        }
        catch (FileBridgeProtocolException ex)
        {
            Check(ex.Message == FileBridgeCodes.PayloadTooLarge);
        }
    }

    static void DataOneMibBoundary()
    {
        var session = new FileBridgeProtocolCodec();
        var req = """{"protocol":"1.0","job":"01234567-89ab-4def-8123-456789abcdef","op":"read","path":"/YQ=="}"""u8;
        session.PushClient(Concat(Header(FileBridgeKind.RequestJson, 0, (uint)req.Length), req.ToArray()));
        var acc = """{"job":"01234567-89ab-4def-8123-456789abcdef","op":"read","identity":"YQ==","size":"1048576"}"""u8;
        session.PushHelper(Concat(Header(FileBridgeKind.AcceptedJson, 0, (uint)acc.Length), acc.ToArray()));
        var data = new byte[FileBridgeLimits.MaxData];
        session.PushHelper(Concat(Header(FileBridgeKind.Data, 1, (uint)data.Length), data));
        var header = new byte[FileBridgeLimits.HeaderLength];
        "HDFB"u8.CopyTo(header);
        header[4] = 1;
        header[6] = (byte)FileBridgeKind.Data;
        WriteU32(header, 8, FileBridgeLimits.MaxData + 1u);
        WriteU32(header, 12, 2);
        try
        {
            session.PushHelper(header);
            throw new Exception("assertion_failed");
        }
        catch (FileBridgeProtocolException ex)
        {
            Check(ex.Message == FileBridgeCodes.PayloadTooLarge);
        }
    }

    static FileBridgeProtocolCodec Run(Vector vector, int? chunk)
    {
        var session = new FileBridgeProtocolCodec();
        var offset = 0;
        string? error = null;
        for (var i = 0; i < vector.Frames.Count; i++)
        {
            var frame = vector.Frames[i];
            var slice = vector.Bytes.AsSpan(offset, frame.Length);
            offset += frame.Length;
            try
            {
                Feed(session, frame.Dir, slice, chunk);
            }
            catch (FileBridgeProtocolException ex)
            {
                error = ex.Message;
                if (vector.Expect == "reject")
                {
                    Check(vector.Error is null || error == vector.Error);
                    return session;
                }
                throw;
            }
            if (vector.CommitAfter == i)
                session.MarkCommitLinearized();
        }
        if (vector.Eof)
        {
            try
            {
                session.OnEof();
            }
            catch (FileBridgeProtocolException ex)
            {
                if (vector.Expect == "reject")
                {
                    Check(vector.Error is null || ex.Message == vector.Error);
                    return session;
                }
                throw;
            }
        }
        Check(vector.Expect != "reject");
        if (!vector.SkipExit && vector.ExitCode is { } code)
        {
            try
            {
                session.OnProcessExit(code);
            }
            catch (FileBridgeProtocolException ex)
            {
                Check(vector.Error == ex.Message);
                return session;
            }
        }
        return session;
    }

    static void Feed(FileBridgeProtocolCodec session, FileBridgeDirection dir, ReadOnlySpan<byte> slice,
        int? chunk)
    {
        if (chunk is null)
        {
            if (dir == FileBridgeDirection.Client)
                session.PushClient(slice);
            else
                session.PushHelper(slice);
            return;
        }
        var size = chunk.Value;
        for (var i = 0; i < slice.Length; i += size)
        {
            var n = Math.Min(size, slice.Length - i);
            if (dir == FileBridgeDirection.Client)
                session.PushClient(slice.Slice(i, n));
            else
                session.PushHelper(slice.Slice(i, n));
        }
    }

    static void CheckOk(Vector vector, FileBridgeProtocolCodec session)
    {
        if (vector.Outcome is { } name)
            Check(session.Outcome.ToString().Equals(name, StringComparison.OrdinalIgnoreCase));
        if (vector.ExclusiveDeleted is { } flag)
            Check(session.ExclusiveTempDeleted == flag);
        if (vector.ReplayForbidden)
            Check(session.AutoReplayForbidden);
    }

    sealed class FrameSpec
    {
        public FileBridgeDirection Dir;
        public int Length;
    }

    sealed class Vector
    {
        public string Id = "";
        public string Expect = "";
        public string? Error;
        public string? Outcome;
        public int? ExitCode;
        public bool Eof = true;
        public bool SkipExit;
        public int? CommitAfter;
        public bool? ExclusiveDeleted;
        public bool ReplayForbidden;
        public byte[] Bytes = [];
        public List<FrameSpec> Frames = [];
    }

    static List<Vector> LoadVectors()
    {
        var dir = Path.Combine(FindRepoRoot(), "filebridge", "spec", "test-vectors");
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "manifest.json")));
        var list = new List<Vector>();
        foreach (var item in doc.RootElement.GetProperty("vectors").EnumerateArray())
        {
            var frames = new List<FrameSpec>();
            foreach (var frame in item.GetProperty("frames").EnumerateArray())
            {
                frames.Add(new FrameSpec
                {
                    Dir = frame.GetProperty("dir").GetString() == "client"
                        ? FileBridgeDirection.Client
                        : FileBridgeDirection.Helper,
                    Length = frame.GetProperty("length").GetInt32()
                });
            }
            list.Add(new Vector
            {
                Id = item.GetProperty("id").GetString()!,
                Expect = item.GetProperty("expect").GetString()!,
                Error = item.TryGetProperty("error", out var err) ? err.GetString() : null,
                Outcome = item.TryGetProperty("outcome", out var outcome) ? outcome.GetString() : null,
                ExitCode = item.TryGetProperty("exit_code", out var exit) && exit.ValueKind == JsonValueKind.Number
                    ? exit.GetInt32()
                    : null,
                Eof = !item.TryGetProperty("eof_after_frames", out var eof) || eof.GetBoolean(),
                SkipExit = item.TryGetProperty("skip_exit", out var skip) && skip.GetBoolean(),
                CommitAfter = item.TryGetProperty("commit_linearized_after_frame", out var commit) &&
                              commit.ValueKind == JsonValueKind.Number
                    ? commit.GetInt32()
                    : null,
                ExclusiveDeleted = item.TryGetProperty("exclusive_temp_deleted", out var del)
                    ? del.GetBoolean()
                    : null,
                ReplayForbidden = item.TryGetProperty("auto_replay_forbidden", out var replay) && replay.GetBoolean(),
                Bytes = File.ReadAllBytes(Path.Combine(dir, item.GetProperty("file").GetString()!)),
                Frames = frames
            });
        }
        return list;
    }

    static (FileBridgeProtocolCodec Session, uint Seq) ListPrefix()
    {
        var session = new FileBridgeProtocolCodec();
        var req = """{"protocol":"1.0","job":"01234567-89ab-4def-8123-456789abcdef","op":"list","path":"/","limit":1}"""u8;
        session.PushClient(Concat(Header(FileBridgeKind.RequestJson, 0, (uint)req.Length), req.ToArray()));
        var acc = """{"job":"01234567-89ab-4def-8123-456789abcdef","op":"list","identity":"YQ==","size":"0"}"""u8;
        session.PushHelper(Concat(Header(FileBridgeKind.AcceptedJson, 0, (uint)acc.Length), acc.ToArray()));
        return (session, 1);
    }

    static byte[] EntryPayload(int target)
    {
        const string prefix =
            "{\"job\":\"01234567-89ab-4def-8123-456789abcdef\",\"name\":\"YQ==\",\"display_name\":\"";
        const string suffix =
            "\",\"type\":\"file\",\"size\":\"0\",\"mtime\":\"0\",\"mtime_precision\":\"1\",\"identity\":\"YQ==\",\"symlink\":false,\"observation\":\"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\"}";
        var used = Encoding.UTF8.GetByteCount(prefix) + Encoding.UTF8.GetByteCount(suffix);
        var pad = new string('x', target - used);
        var json = prefix + pad + suffix;
        var bytes = Encoding.UTF8.GetBytes(json);
        Check(bytes.Length == target);
        return bytes;
    }

    static byte[] Header(FileBridgeKind kind, uint seq, uint length) =>
        FileBridgeKindCodec.EncodeHeader(kind, seq, length);

    static byte[] Concat(byte[] header, byte[] payload)
    {
        var all = new byte[header.Length + payload.Length];
        header.CopyTo(all, 0);
        payload.CopyTo(all, header.Length);
        return all;
    }

    static void WriteU32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    static string FindRepoRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "HerdDesk.slnx")))
                return directory;
            directory = Directory.GetParent(directory)?.FullName ?? "";
        }
        throw new Exception("repo_root_missing");
    }
}
