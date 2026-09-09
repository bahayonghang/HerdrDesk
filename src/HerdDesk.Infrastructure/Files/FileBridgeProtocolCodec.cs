using System.Text.Json;

namespace HerdDesk.Infrastructure.Files;

public sealed class FileBridgeProtocolCodec
{
    enum Phase
    {
        Start,
        Requested,
        Open,
        WriteEnding,
        Terminal
    }

    sealed class DirBuf
    {
        public readonly byte[] Header = new byte[FileBridgeLimits.HeaderLength];
        public int HeaderGot;
        public byte[] Payload = [];
        public int PayloadNeed;
        public int PayloadGot;
        public bool ReadingPayload;
        public uint NextSeq;
        public bool SeqExhausted;
        public FileBridgeHeader? Pending;

        public void ResetHeader()
        {
            HeaderGot = 0;
            ReadingPayload = false;
            Payload = [];
            PayloadNeed = 0;
            PayloadGot = 0;
            Pending = null;
        }
    }

    readonly DirBuf _client = new();
    readonly DirBuf _helper = new();
    bool _failed;
    Phase _phase = Phase.Start;
    string? _job;
    FileBridgeOp? _op;
    ulong _declaredLength;
    ulong _acceptedSize;
    ulong _dataBytes;
    bool _accepted;
    bool _commitLinearized;
    bool _exclusiveTemp;
    bool _exclusiveTempDeleted;
    bool _cancelRequested;
    bool _terminalComplete;
    string? _terminalError;
    FileBridgeOutcome _outcome = FileBridgeOutcome.Pending;
    bool _wouldExit;
    long _controlBytes;
    bool _autoReplayForbidden;

    public FileBridgeOutcome Outcome => _outcome;
    public bool ExclusiveTempActive => _exclusiveTemp;
    public bool ExclusiveTempDeleted => _exclusiveTempDeleted;
    public bool WouldExit => _wouldExit;
    public bool AutoReplayForbidden => _autoReplayForbidden;

    public void MarkCommitLinearized() => _commitLinearized = true;

    public IReadOnlyList<FileBridgeFrame> PushClient(ReadOnlySpan<byte> bytes) =>
        Push(FileBridgeDirection.Client, bytes);

    public IReadOnlyList<FileBridgeFrame> PushHelper(ReadOnlySpan<byte> bytes) =>
        Push(FileBridgeDirection.Helper, bytes);

    public void OnStdinClosed()
    {
        Guard();
        if (_wouldExit)
            return;
        _cancelRequested = true;
        DropTempIfUncommitted();
    }

    public FileBridgeOutcome OnEof()
    {
        Guard();
        if (_client.HeaderGot > 0 || _client.ReadingPayload)
            Die(_client.ReadingPayload ? FileBridgeCodes.TruncatedPayload : FileBridgeCodes.TruncatedHeader);
        if (_helper.HeaderGot > 0 || _helper.ReadingPayload)
            Die(_helper.ReadingPayload ? FileBridgeCodes.TruncatedPayload : FileBridgeCodes.TruncatedHeader);
        if (_phase != Phase.Terminal)
        {
            _outcome = FileBridgeOutcome.Unknown;
            if (_op is FileBridgeOp.Write or FileBridgeOp.Rename)
                _autoReplayForbidden = true;
            // Disconnect without a terminal result is final. A later Complete
            // must not overwrite Unknown or clear AutoReplayForbidden.
            _failed = true;
        }
        return _outcome;
    }

    public FileBridgeOutcome OnProcessExit(int code)
    {
        Guard();
        if (_terminalComplete && code == 0)
        {
            _outcome = FileBridgeOutcome.Success;
            return _outcome;
        }
        if (_terminalError is not null && code != 0)
        {
            _outcome = _terminalError == "cancelled" ? FileBridgeOutcome.Cancelled : FileBridgeOutcome.Failed;
            return _outcome;
        }
        _outcome = FileBridgeOutcome.Unknown;
        if (_op is FileBridgeOp.Write or FileBridgeOp.Rename)
            _autoReplayForbidden = true;
        Die(FileBridgeCodes.TerminalResultMismatch);
        return _outcome;
    }

    internal void DebugForceNextSeq(FileBridgeDirection direction, uint seq)
    {
        var buf = Buf(direction);
        buf.NextSeq = seq;
        buf.SeqExhausted = false;
    }

    List<FileBridgeFrame> Push(FileBridgeDirection direction, ReadOnlySpan<byte> bytes)
    {
        Guard();
        var frames = new List<FileBridgeFrame>();
        var remaining = bytes;
        while (!remaining.IsEmpty)
        {
            try
            {
                var frame = TakeFrame(direction, ref remaining);
                if (frame is null)
                    break;
                frames.Add(frame);
            }
            catch (FileBridgeProtocolException)
            {
                _failed = true;
                throw;
            }
        }
        return frames;
    }

    FileBridgeFrame? TakeFrame(FileBridgeDirection direction, ref ReadOnlySpan<byte> bytes)
    {
        if (_wouldExit)
            throw new FileBridgeProtocolException(FileBridgeCodes.ProcessWouldExit);
        var buf = Buf(direction);
        if (!buf.ReadingPayload)
        {
            var need = FileBridgeLimits.HeaderLength - buf.HeaderGot;
            var n = Math.Min(need, bytes.Length);
            bytes[..n].CopyTo(buf.Header.AsSpan(buf.HeaderGot));
            buf.HeaderGot += n;
            bytes = bytes[n..];
            if (buf.HeaderGot < FileBridgeLimits.HeaderLength)
                return null;
            var header = FileBridgeKindCodec.ParseHeader(buf.Header);
            if (buf.SeqExhausted)
                throw new FileBridgeProtocolException(FileBridgeCodes.SequenceWrap);
            if (header.Sequence < buf.NextSeq)
                throw new FileBridgeProtocolException(FileBridgeCodes.SequenceReplay);
            if (header.Sequence > buf.NextSeq)
                throw new FileBridgeProtocolException(FileBridgeCodes.SequenceGap);
            if (header.PayloadLength == 0)
            {
                AdvanceSeq(buf);
                buf.ResetHeader();
                Dispatch(direction, header.Kind, header.Sequence, []);
                return new FileBridgeFrame(direction, header.Kind, header.Sequence, []);
            }
            buf.Payload = new byte[header.PayloadLength];
            buf.PayloadNeed = (int)header.PayloadLength;
            buf.PayloadGot = 0;
            buf.ReadingPayload = true;
            buf.Pending = header;
        }
        buf = Buf(direction);
        var remain = buf.PayloadNeed - buf.PayloadGot;
        var take = Math.Min(remain, bytes.Length);
        bytes[..take].CopyTo(buf.Payload.AsSpan(buf.PayloadGot));
        buf.PayloadGot += take;
        bytes = bytes[take..];
        if (buf.PayloadGot < buf.PayloadNeed)
            return null;
        var done = buf.Pending ?? throw new FileBridgeProtocolException(FileBridgeCodes.ProtocolNotActive);
        var payload = buf.Payload;
        AdvanceSeq(buf);
        buf.ResetHeader();
        Dispatch(direction, done.Kind, done.Sequence, payload);
        return new FileBridgeFrame(direction, done.Kind, done.Sequence, payload);
    }

    void Dispatch(FileBridgeDirection direction, FileBridgeKind kind, uint _, byte[] payload)
    {
        if (kind == FileBridgeKind.RequestJson)
        {
            if (direction != FileBridgeDirection.Client)
                throw new FileBridgeProtocolException(FileBridgeCodes.WrongDirection);
            OnRequest(payload);
            return;
        }
        if (_job is null)
        {
            if (kind == FileBridgeKind.Data)
                throw new FileBridgeProtocolException(FileBridgeCodes.UnexpectedKind);
            try
            {
                var natural = FileBridgeKindCodec.DirectionOf(kind, null);
                if (natural != direction)
                    throw new FileBridgeProtocolException(FileBridgeCodes.WrongDirection);
            }
            catch (FileBridgeProtocolException ex) when (ex.Message == FileBridgeCodes.UnexpectedKind)
            {
            }
            throw new FileBridgeProtocolException(FileBridgeCodes.UnexpectedKind);
        }
        var expected = FileBridgeKindCodec.DirectionOf(kind, _op);
        if (expected != direction)
            throw new FileBridgeProtocolException(FileBridgeCodes.WrongDirection);
        switch (kind)
        {
            case FileBridgeKind.CancelJson:
                OnCancel(payload);
                break;
            case FileBridgeKind.AcceptedJson:
                OnAccepted(payload);
                break;
            case FileBridgeKind.EntryJson:
                OnEntry(payload);
                break;
            case FileBridgeKind.ProgressJson:
                OnProgress(payload);
                break;
            case FileBridgeKind.CompleteJson:
                OnComplete(payload);
                break;
            case FileBridgeKind.ErrorJson:
                OnError(payload);
                break;
            case FileBridgeKind.Data:
                OnData(payload);
                break;
            case FileBridgeKind.EndData:
                OnEndData();
                break;
            default:
                throw new FileBridgeProtocolException(FileBridgeCodes.SecondRequest);
        }
    }

    void OnRequest(byte[] payload)
    {
        if (_job is not null || _phase != Phase.Start)
            throw new FileBridgeProtocolException(FileBridgeCodes.SecondRequest);
        AddControl(payload.Length);
        using var doc = FileBridgeJson.ParseObject(payload);
        var root = doc.RootElement;
        if (FileBridgeJson.ReqString(root, "protocol") != "1.0")
            throw new FileBridgeProtocolException(FileBridgeCodes.UnsupportedVersion);
        var job = FileBridgeJson.ReqString(root, "job");
        FileBridgeText.RequireUuid(job);
        var op = ParseOp(FileBridgeJson.ReqString(root, "op"));
        switch (op)
        {
            case FileBridgeOp.List:
                ParseList(root);
                break;
            case FileBridgeOp.Stat:
            case FileBridgeOp.Read:
                ParseStatRead(root);
                break;
            case FileBridgeOp.Write:
                ParseWrite(root);
                break;
            case FileBridgeOp.Rename:
                ParseRename(root);
                break;
        }
        _job = job;
        _op = op;
        _phase = Phase.Requested;
        if (op is FileBridgeOp.Write or FileBridgeOp.Rename)
            _exclusiveTemp = true;
    }

    void ParseList(JsonElement root)
    {
        FileBridgeJson.RejectUnknown(root, "protocol", "job", "op", "path", "limit", "cursor");
        WirePath.Decode(FileBridgeJson.ReqString(root, "path"));
        var limit = FileBridgeJson.ReqInt(root, "limit");
        if (limit is < 1 or > 1000)
            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidLimit);
        var cursor = FileBridgeJson.OptString(root, "cursor");
        if (cursor is not null)
            FileBridgeText.BoundedBase64(cursor, FileBridgeCodes.InvalidCursor, FileBridgeLimits.MaxCursor);
    }

    void ParseStatRead(JsonElement root)
    {
        FileBridgeJson.RejectUnknown(root, "protocol", "job", "op", "path", "observation");
        WirePath.Decode(FileBridgeJson.ReqString(root, "path"));
        var obs = FileBridgeJson.OptString(root, "observation");
        if (obs is not null)
            FileBridgeText.RequireHex64(obs, FileBridgeCodes.InvalidObservation);
    }

    void ParseWrite(JsonElement root)
    {
        var mode = ParseMode(FileBridgeJson.ReqString(root, "mode"));
        var allowed = new List<string>
        {
            "protocol", "job", "op", "parent", "name", "mode",
            "expected_parent_observation", "length", "sha256"
        };
        if (mode == FileBridgeMode.Replace)
        {
            if (!FileBridgeJson.Has(root, "expected_target_observation"))
                throw new FileBridgeProtocolException(FileBridgeCodes.ReplaceObservationRequired);
            allowed.Add("expected_target_observation");
        }
        FileBridgeJson.RejectUnknown(root, [.. allowed]);
        WirePath.Decode(FileBridgeJson.ReqString(root, "parent"));
        WirePath.DecodeComponent(FileBridgeJson.ReqString(root, "name"));
        FileBridgeText.RequireHex64(FileBridgeJson.ReqString(root, "expected_parent_observation"),
            FileBridgeCodes.InvalidObservation);
        if (mode == FileBridgeMode.Replace)
            FileBridgeText.RequireHex64(FileBridgeJson.ReqString(root, "expected_target_observation"),
                FileBridgeCodes.InvalidObservation);
        _declaredLength = FileBridgeText.RequireDecimalU64(FileBridgeJson.ReqString(root, "length"));
        FileBridgeText.RequireHex64(FileBridgeJson.ReqString(root, "sha256"), FileBridgeCodes.InvalidHash);
    }

    void ParseRename(JsonElement root)
    {
        var mode = ParseMode(FileBridgeJson.ReqString(root, "mode"));
        var allowed = new List<string>
        {
            "protocol", "job", "op", "source", "source_observation", "parent", "name", "mode",
            "expected_parent_observation"
        };
        if (mode == FileBridgeMode.Replace)
        {
            if (!FileBridgeJson.Has(root, "expected_target_observation"))
                throw new FileBridgeProtocolException(FileBridgeCodes.ReplaceObservationRequired);
            allowed.Add("expected_target_observation");
        }
        FileBridgeJson.RejectUnknown(root, [.. allowed]);
        WirePath.Decode(FileBridgeJson.ReqString(root, "source"));
        FileBridgeText.RequireHex64(FileBridgeJson.ReqString(root, "source_observation"),
            FileBridgeCodes.InvalidObservation);
        WirePath.Decode(FileBridgeJson.ReqString(root, "parent"));
        WirePath.DecodeComponent(FileBridgeJson.ReqString(root, "name"));
        FileBridgeText.RequireHex64(FileBridgeJson.ReqString(root, "expected_parent_observation"),
            FileBridgeCodes.InvalidObservation);
        if (mode == FileBridgeMode.Replace)
            FileBridgeText.RequireHex64(FileBridgeJson.ReqString(root, "expected_target_observation"),
                FileBridgeCodes.InvalidObservation);
    }

    void OnCancel(byte[] payload)
    {
        if (_phase == Phase.Start)
            throw new FileBridgeProtocolException(FileBridgeCodes.UnexpectedKind);
        AddControl(payload.Length);
        using var doc = FileBridgeJson.ParseObject(payload);
        var root = doc.RootElement;
        FileBridgeJson.RejectUnknown(root, "job", "reason");
        SameJob(FileBridgeJson.ReqString(root, "job"));
        if (FileBridgeJson.ReqString(root, "reason") != "user")
            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidReason);
        _cancelRequested = true;
        DropTempIfUncommitted();
    }

    void OnAccepted(byte[] payload)
    {
        if (_phase != Phase.Requested)
            throw new FileBridgeProtocolException(FileBridgeCodes.UnexpectedKind);
        AddControl(payload.Length);
        using var doc = FileBridgeJson.ParseObject(payload);
        var root = doc.RootElement;
        FileBridgeJson.RejectUnknown(root, "job", "op", "identity", "size");
        SameJob(FileBridgeJson.ReqString(root, "job"));
        var op = ParseOp(FileBridgeJson.ReqString(root, "op"));
        if (op != _op)
            throw new FileBridgeProtocolException(FileBridgeCodes.OpMismatch);
        FileBridgeText.BoundedBase64(FileBridgeJson.ReqString(root, "identity"),
            FileBridgeCodes.InvalidIdentity, FileBridgeLimits.MaxIdentity);
        _acceptedSize = FileBridgeText.RequireDecimalU64(FileBridgeJson.ReqString(root, "size"));
        _accepted = true;
        _phase = Phase.Open;
    }

    void OnEntry(byte[] payload)
    {
        if (_op != FileBridgeOp.List || _phase != Phase.Open)
            throw new FileBridgeProtocolException(FileBridgeCodes.UnexpectedKind);
        AddControl(payload.Length);
        using var doc = FileBridgeJson.ParseObject(payload);
        var root = doc.RootElement;
        FileBridgeJson.RejectUnknown(root, "job", "name", "display_name", "type", "size", "mtime",
            "mtime_precision", "identity", "symlink", "observation");
        SameJob(FileBridgeJson.ReqString(root, "job"));
        WirePath.DecodeComponent(FileBridgeJson.ReqString(root, "name"));
        var display = FileBridgeJson.ReqString(root, "display_name");
        if (display.Length == 0 || display.Contains('\0'))
            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidFieldType);
        var type = FileBridgeJson.ReqString(root, "type");
        if (type is not ("file" or "directory" or "symlink" or "other"))
            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidFieldType);
        FileBridgeText.RequireDecimalU64(FileBridgeJson.ReqString(root, "size"));
        FileBridgeText.RequireDecimalU64(FileBridgeJson.ReqString(root, "mtime"));
        FileBridgeText.RequireDecimalU64(FileBridgeJson.ReqString(root, "mtime_precision"));
        FileBridgeText.BoundedBase64(FileBridgeJson.ReqString(root, "identity"),
            FileBridgeCodes.InvalidIdentity, FileBridgeLimits.MaxIdentity);
        var symlink = FileBridgeJson.ReqBool(root, "symlink");
        if ((type == "symlink") != symlink)
            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidFieldType);
        FileBridgeText.RequireHex64(FileBridgeJson.ReqString(root, "observation"),
            FileBridgeCodes.InvalidObservation);
    }

    void OnProgress(byte[] payload)
    {
        if (_phase is not (Phase.Open or Phase.WriteEnding))
            throw new FileBridgeProtocolException(FileBridgeCodes.UnexpectedKind);
        AddControl(payload.Length);
        using var doc = FileBridgeJson.ParseObject(payload);
        var root = doc.RootElement;
        FileBridgeJson.RejectUnknown(root, "job", "bytes");
        SameJob(FileBridgeJson.ReqString(root, "job"));
        FileBridgeText.RequireDecimalU64(FileBridgeJson.ReqString(root, "bytes"));
    }

    void OnComplete(byte[] payload)
    {
        if (_cancelRequested && !_commitLinearized)
            throw new FileBridgeProtocolException(FileBridgeCodes.UnexpectedKind);
        if (_op == FileBridgeOp.Write && _phase != Phase.WriteEnding)
            throw new FileBridgeProtocolException(FileBridgeCodes.UnexpectedKind);
        if ((_op is FileBridgeOp.List or FileBridgeOp.Stat or FileBridgeOp.Read or FileBridgeOp.Rename)
            && _phase != Phase.Open)
            throw new FileBridgeProtocolException(FileBridgeCodes.UnexpectedKind);
        if (_op is null)
            throw new FileBridgeProtocolException(FileBridgeCodes.UnexpectedKind);
        AddControl(payload.Length);
        using var doc = FileBridgeJson.ParseObject(payload);
        var root = doc.RootElement;
        var allowed = new List<string> { "job", "path", "length", "sha256", "observation", "commit" };
        if (_op == FileBridgeOp.List)
            allowed.Add("has_more");
        FileBridgeJson.RejectUnknown(root, [.. allowed]);
        SameJob(FileBridgeJson.ReqString(root, "job"));
        WirePath.Decode(FileBridgeJson.ReqString(root, "path"));
        var length = FileBridgeText.RequireDecimalU64(FileBridgeJson.ReqString(root, "length"));
        FileBridgeText.RequireHex64(FileBridgeJson.ReqString(root, "sha256"), FileBridgeCodes.InvalidHash);
        FileBridgeText.RequireHex64(FileBridgeJson.ReqString(root, "observation"),
            FileBridgeCodes.InvalidObservation);
        var commit = FileBridgeJson.ReqString(root, "commit");
        if (_op is FileBridgeOp.Write or FileBridgeOp.Rename)
        {
            if (commit != "committed")
                throw new FileBridgeProtocolException(FileBridgeCodes.InvalidCommit);
        }
        else if (commit != "not_applicable")
            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidCommit);
        if (_op == FileBridgeOp.List)
            FileBridgeJson.ReqBool(root, "has_more");
        if (_op == FileBridgeOp.Write && length != _declaredLength)
            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidLength);
        if (_op == FileBridgeOp.Read && (length != _dataBytes || length != _acceptedSize))
            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidLength);
        _exclusiveTemp = false;
        FinishTerminal(true, null);
    }

    void OnError(byte[] payload)
    {
        if (_phase is Phase.Start or Phase.Terminal)
            throw new FileBridgeProtocolException(FileBridgeCodes.UnexpectedKind);
        AddControl(payload.Length);
        using var doc = FileBridgeJson.ParseObject(payload);
        var root = doc.RootElement;
        FileBridgeJson.RejectUnknown(root, "job", "code", "stage", "retryable");
        SameJob(FileBridgeJson.ReqString(root, "job"));
        var code = FileBridgeJson.ReqString(root, "code");
        if (!FileBridgeText.ErrorCodeSyntax(code))
            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidFieldType);
        var stage = FileBridgeJson.ReqString(root, "stage");
        if (stage is not ("request" or "transfer" or "commit" or "cancel" or "unknown"))
            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidFieldType);
        FileBridgeJson.ReqBool(root, "retryable");
        if (_commitLinearized && code == "cancelled")
            throw new FileBridgeProtocolException(FileBridgeCodes.UnexpectedKind);
        if (_cancelRequested && !_commitLinearized && code != "cancelled")
            throw new FileBridgeProtocolException(FileBridgeCodes.UnexpectedKind);
        if (_cancelRequested && !_commitLinearized)
            DropTempIfUncommitted();
        FinishTerminal(false, code);
    }

    void OnData(byte[] payload)
    {
        if (!_accepted)
            throw new FileBridgeProtocolException(FileBridgeCodes.DataBeforeAccepted);
        if (_op == FileBridgeOp.Write && _phase == Phase.Open)
        {
            AddDataBytes(payload.Length, _declaredLength);
            return;
        }
        if (_op == FileBridgeOp.Read && _phase == Phase.Open)
        {
            AddDataBytes(payload.Length, _acceptedSize);
            return;
        }
        throw new FileBridgeProtocolException(FileBridgeCodes.UnexpectedKind);
    }

    void OnEndData()
    {
        if (!_accepted)
            throw new FileBridgeProtocolException(FileBridgeCodes.DataBeforeAccepted);
        if (_op != FileBridgeOp.Write || _phase != Phase.Open)
            throw new FileBridgeProtocolException(FileBridgeCodes.UnexpectedKind);
        if (_dataBytes != _declaredLength)
            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidLength);
        _phase = Phase.WriteEnding;
    }

    void FinishTerminal(bool complete, string? error)
    {
        _phase = Phase.Terminal;
        _wouldExit = true;
        if (complete)
        {
            _terminalComplete = true;
            _outcome = FileBridgeOutcome.Pending;
            return;
        }
        var cancelled = error == "cancelled";
        _terminalError = error;
        _outcome = cancelled ? FileBridgeOutcome.Cancelled : FileBridgeOutcome.Failed;
        if (cancelled)
            DropTempIfUncommitted();
        if (_op is FileBridgeOp.Write or FileBridgeOp.Rename)
            _autoReplayForbidden = true;
    }

    void DropTempIfUncommitted()
    {
        if (!_commitLinearized && _exclusiveTemp)
        {
            _exclusiveTempDeleted = true;
            _exclusiveTemp = false;
        }
    }

    void SameJob(string job)
    {
        FileBridgeText.RequireUuid(job);
        if (_job != job)
            throw new FileBridgeProtocolException(FileBridgeCodes.JobMismatch);
    }

    void AddDataBytes(int n, ulong limit)
    {
        if (n < 0 || _dataBytes > ulong.MaxValue - (ulong)n)
            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidLength);
        _dataBytes += (ulong)n;
        if (_dataBytes > limit)
            throw new FileBridgeProtocolException(FileBridgeCodes.InvalidLength);
    }

    void AddControl(int n)
    {
        var next = _controlBytes + n;
        if (next > FileBridgeLimits.MaxControlTotal)
            throw new FileBridgeProtocolException(FileBridgeCodes.ControlBudgetExceeded);
        _controlBytes = next;
    }

    void Guard()
    {
        if (_failed)
            throw new FileBridgeProtocolException(FileBridgeCodes.ProtocolNotActive);
    }

    void Die(string code)
    {
        _failed = true;
        throw new FileBridgeProtocolException(code);
    }

    static void AdvanceSeq(DirBuf buf)
    {
        if (buf.NextSeq == uint.MaxValue)
            buf.SeqExhausted = true;
        else
            buf.NextSeq++;
    }

    DirBuf Buf(FileBridgeDirection direction) =>
        direction == FileBridgeDirection.Client ? _client : _helper;

    static FileBridgeOp ParseOp(string value) =>
        value switch
        {
            "list" => FileBridgeOp.List,
            "stat" => FileBridgeOp.Stat,
            "read" => FileBridgeOp.Read,
            "write" => FileBridgeOp.Write,
            "rename" => FileBridgeOp.Rename,
            _ => throw new FileBridgeProtocolException(FileBridgeCodes.InvalidOp)
        };

    static FileBridgeMode ParseMode(string value) =>
        value switch
        {
            "create" => FileBridgeMode.Create,
            "replace" => FileBridgeMode.Replace,
            _ => throw new FileBridgeProtocolException(FileBridgeCodes.InvalidMode)
        };
}
