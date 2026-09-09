using System.Text;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal sealed class CountingClipboardReader : IClipboardSnapshotReader
{
    public int Invocations { get; private set; }
    public ClipboardSnapshot Next { get; set; }

    public CountingClipboardReader(ClipboardSnapshot next) => Next = next;

    public ClipboardSnapshot Read()
    {
        Invocations++;
        return Next;
    }
}

internal sealed class RecordingPasteInput : IAttachmentInputSink
{
    public List<RendererInput> Submitted { get; } = [];
    public bool SubmitNext { get; set; } = true;
    public string NextCode { get; set; } = ClipboardCodes.Allowed;

    public ValueTask<AttachmentInputReceipt> SubmitAsync(
        RendererInput input,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        if (SubmitNext)
            Submitted.Add(input);
        return ValueTask.FromResult(new AttachmentInputReceipt(SubmitNext, NextCode, input.Bytes));
    }
}

internal sealed class RecordingPasteDiagnostics : IDiagnosticSink
{
    public List<DiagnosticEvent> Events { get; } = [];
    public long DroppedCount => 0;

    public bool TryWrite(DiagnosticEvent evt)
    {
        Events.Add(evt);
        return true;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal static class ClipboardHarness
{
    public static readonly DateTimeOffset T0 = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    public static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    public static DeviceId Device() => new(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));

    public static PaneKey Pane(string pane = "p1") =>
        new(new SessionKey(Device(), "endpoint", "dev"), "ws", pane);

    public static PasteConfirmRequest Confirm(
        string pane = "p1",
        long epoch = 1,
        string leaseId = "lease-1",
        long generation = 1,
        bool verified = true,
        TerminalAccess access = TerminalAccess.Controlling) =>
        new(
            Pane(pane),
            new ConnectionEpoch(epoch),
            leaseId,
            generation,
            verified,
            access,
            T0);

    public static PasteLiveTarget Live(
        string pane = "p1",
        long epoch = 1,
        string leaseId = "lease-1",
        long generation = 1,
        bool verified = true,
        TerminalAccess access = TerminalAccess.Controlling) =>
        new(
            Pane(pane),
            new ConnectionEpoch(epoch),
            leaseId,
            generation,
            verified,
            access,
            T0);

    public static ClipboardSnapshot Snapshot(
        bool text = false,
        bool files = false,
        bool image = false,
        string? body = null,
        string? error = null,
        IReadOnlyList<string>? formats = null,
        IReadOnlyList<ClipboardFileHandle>? fileHandles = null,
        ClipboardImageHandle? imageHandle = null) =>
        new(
            Guid.NewGuid(),
            T0,
            text,
            files,
            image,
            formats ?? [],
            body is null ? ReadOnlyMemory<byte>.Empty : Encoding.UTF8.GetBytes(body),
            fileHandles ?? [],
            imageHandle,
            (ulong)(body?.Length ?? 0),
            error);

    public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    public static bool HasExtraEnter(ReadOnlySpan<byte> sent, ReadOnlySpan<byte> original)
    {
        if (sent.Length != original.Length)
            return true;
        if (!sent.SequenceEqual(original))
            return true;
        return false;
    }
}
