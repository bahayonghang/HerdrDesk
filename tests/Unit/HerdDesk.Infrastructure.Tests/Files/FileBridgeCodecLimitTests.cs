using HerdDesk.Infrastructure.Files;

internal static class FileBridgeCodecLimitTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("filebridge seq wrap is rejected", Wrap),
        ("filebridge stdin close deletes uncommitted temp", StdinCancel),
        ("filebridge eof without terminal latches and forbids replay", EofLatch),
        ("filebridge display name is not a wire path", DisplayNameNotPath),
        ("filebridge list limit bounds", ListLimit)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void Wrap()
    {
        var session = new FileBridgeProtocolCodec();
        session.DebugForceNextSeq(FileBridgeDirection.Client, uint.MaxValue);
        var payload =
            """{"protocol":"1.0","job":"01234567-89ab-4def-8123-456789abcdef","op":"list","path":"/","limit":1}"""u8
                .ToArray();
        var frame = FileBridgeKindCodec.EncodeHeader(FileBridgeKind.RequestJson, uint.MaxValue,
            (uint)payload.Length);
        var all = new byte[frame.Length + payload.Length];
        frame.CopyTo(all, 0);
        payload.CopyTo(all, frame.Length);
        session.PushClient(all);
        var second = FileBridgeKindCodec.EncodeHeader(FileBridgeKind.CancelJson, 0, 0);
        try
        {
            session.PushClient(second);
            throw new Exception("assertion_failed");
        }
        catch (FileBridgeProtocolException ex)
        {
            Check(ex.Message == FileBridgeCodes.SequenceWrap);
        }
        try
        {
            session.PushClient(second);
            throw new Exception("assertion_failed");
        }
        catch (FileBridgeProtocolException ex)
        {
            Check(ex.Message == FileBridgeCodes.ProtocolNotActive);
        }
    }

    static void StdinCancel()
    {
        var session = new FileBridgeProtocolCodec();
        var req =
            """{"protocol":"1.0","job":"01234567-89ab-4def-8123-456789abcdef","op":"write","parent":"/","name":"YQ==","mode":"create","expected_parent_observation":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef","length":"0","sha256":"e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"}"""u8
                .ToArray();
        var frame = FileBridgeKindCodec.EncodeHeader(FileBridgeKind.RequestJson, 0, (uint)req.Length);
        var all = new byte[frame.Length + req.Length];
        frame.CopyTo(all, 0);
        req.CopyTo(all, frame.Length);
        session.PushClient(all);
        Check(session.ExclusiveTempActive);
        session.OnStdinClosed();
        Check(session.ExclusiveTempDeleted);
        Check(!session.ExclusiveTempActive);
    }

    static void EofLatch()
    {
        var session = new FileBridgeProtocolCodec();
        var req =
            """{"protocol":"1.0","job":"01234567-89ab-4def-8123-456789abcdef","op":"write","parent":"/","name":"YQ==","mode":"replace","expected_parent_observation":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef","expected_target_observation":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef","length":"0","sha256":"e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"}"""u8
                .ToArray();
        Push(session, FileBridgeDirection.Client, FileBridgeKind.RequestJson, 0, req);
        var acc =
            """{"job":"01234567-89ab-4def-8123-456789abcdef","op":"write","identity":"YQ==","size":"0"}"""u8
                .ToArray();
        Push(session, FileBridgeDirection.Helper, FileBridgeKind.AcceptedJson, 0, acc);
        Push(session, FileBridgeDirection.Client, FileBridgeKind.EndData, 1, []);
        session.MarkCommitLinearized();
        Check(session.OnEof() == FileBridgeOutcome.Unknown);
        Check(session.AutoReplayForbidden);
        var complete =
            """{"job":"01234567-89ab-4def-8123-456789abcdef","path":"/YQ==","length":"0","sha256":"e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855","observation":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef","commit":"committed"}"""u8
                .ToArray();
        try
        {
            Push(session, FileBridgeDirection.Helper, FileBridgeKind.CompleteJson, 1, complete);
            throw new Exception("assertion_failed");
        }
        catch (FileBridgeProtocolException ex)
        {
            Check(ex.Message == FileBridgeCodes.ProtocolNotActive);
        }
        Check(session.Outcome == FileBridgeOutcome.Unknown);
        Check(session.AutoReplayForbidden);
    }

    static void DisplayNameNotPath()
    {
        var session = new FileBridgeProtocolCodec();
        var req =
            """{"protocol":"1.0","job":"01234567-89ab-4def-8123-456789abcdef","op":"list","path":"/","limit":1}"""u8
                .ToArray();
        Push(session, FileBridgeDirection.Client, FileBridgeKind.RequestJson, 0, req);
        var acc =
            """{"job":"01234567-89ab-4def-8123-456789abcdef","op":"list","identity":"YQ==","size":"0"}"""u8
                .ToArray();
        Push(session, FileBridgeDirection.Helper, FileBridgeKind.AcceptedJson, 0, acc);
        var entry =
            """{"job":"01234567-89ab-4def-8123-456789abcdef","name":"YQ==","display_name":"../etc/passwd","type":"file","size":"0","mtime":"0","mtime_precision":"1","identity":"YQ==","symlink":false,"observation":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"}"""u8
                .ToArray();
        Push(session, FileBridgeDirection.Helper, FileBridgeKind.EntryJson, 1, entry);
        var later = new FileBridgeProtocolCodec();
        var bad =
            """{"protocol":"1.0","job":"01234567-89ab-4def-8123-456789abcdef","op":"list","path":"../etc/passwd","limit":1}"""u8
                .ToArray();
        try
        {
            Push(later, FileBridgeDirection.Client, FileBridgeKind.RequestJson, 0, bad);
            throw new Exception("assertion_failed");
        }
        catch (FileBridgeProtocolException ex)
        {
            Check(ex.Message == FileBridgeCodes.InvalidWirePath);
        }
    }

    static void ListLimit()
    {
        var zero = new FileBridgeProtocolCodec();
        var z =
            """{"protocol":"1.0","job":"01234567-89ab-4def-8123-456789abcdef","op":"list","path":"/","limit":0}"""u8
                .ToArray();
        try
        {
            Push(zero, FileBridgeDirection.Client, FileBridgeKind.RequestJson, 0, z);
            throw new Exception("assertion_failed");
        }
        catch (FileBridgeProtocolException ex)
        {
            Check(ex.Message == FileBridgeCodes.InvalidLimit);
        }

        var over = new FileBridgeProtocolCodec();
        var o =
            """{"protocol":"1.0","job":"01234567-89ab-4def-8123-456789abcdef","op":"list","path":"/","limit":1001}"""u8
                .ToArray();
        try
        {
            Push(over, FileBridgeDirection.Client, FileBridgeKind.RequestJson, 0, o);
            throw new Exception("assertion_failed");
        }
        catch (FileBridgeProtocolException ex)
        {
            Check(ex.Message == FileBridgeCodes.InvalidLimit);
        }
    }

    static void Push(FileBridgeProtocolCodec session, FileBridgeDirection direction, FileBridgeKind kind,
        uint seq, byte[] payload)
    {
        var header = FileBridgeKindCodec.EncodeHeader(kind, seq, (uint)payload.Length);
        var all = new byte[header.Length + payload.Length];
        header.CopyTo(all, 0);
        payload.CopyTo(all, header.Length);
        if (direction == FileBridgeDirection.Client)
            session.PushClient(all);
        else
            session.PushHelper(all);
    }
}
