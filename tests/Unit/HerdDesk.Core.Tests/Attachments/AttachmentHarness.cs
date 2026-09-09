using System.Collections.Frozen;
using System.Text;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal sealed class RecordingAttachmentInput : IAttachmentInputSink
{
    public List<RendererInput> Submitted { get; } = [];
    public bool SubmitNext { get; set; } = true;
    public string NextCode { get; set; } = AttachmentCodes.Allowed;

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

internal sealed class RecordingAttachmentClipboard : IAttachmentClipboard
{
    public List<string> Copied { get; } = [];

    public bool CopyDisplayText(string displayText)
    {
        Copied.Add(displayText);
        return true;
    }
}

internal sealed class RecordingAttachmentDiagnostics : IDiagnosticSink
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

internal static class AttachmentHarness
{
    public static readonly DateTimeOffset T0 = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    public static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    public static DeviceId Device() => new(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));

    public static PaneKey Pane(string pane = "p1", string workspace = "ws") =>
        new(new SessionKey(Device(), "endpoint", "dev"), workspace, pane);

    public static AttachmentCapabilityKey Key(
        string agent = "claude",
        string version = "1.2.3",
        string os = "linux",
        string renderer = "web-1") =>
        new(agent, version, os, renderer);

    public static CapabilityEvidence Verified(params string[] operations) =>
        new(
            CapabilityStatus.Verified,
            operations.ToFrozenSet(StringComparer.Ordinal),
            "evidence/hd-030/synthetic",
            T0);

    public static CapabilityEvidence Unsupported() =>
        new(CapabilityStatus.Unsupported, FrozenSet<string>.Empty, "evidence/hd-030/unsupported", T0);

    public static AttachmentCapabilityCatalog Catalog(params AttachmentCapabilityRecord[] records) =>
        new(records);

    public static AttachmentLiveTarget Live(
        PaneKey? pane = null,
        long epoch = 1,
        string leaseId = "lease-1",
        long generation = 1,
        bool verified = true,
        TerminalAccess access = TerminalAccess.Controlling,
        AttachmentCapabilityKey? key = null,
        DateTimeOffset? now = null) =>
        new(
            pane ?? Pane(),
            new ConnectionEpoch(epoch),
            leaseId,
            generation,
            verified,
            access,
            key ?? Key(),
            now ?? T0);

    public static AttachmentConfirmRequest Confirm(
        PaneKey? pane = null,
        long epoch = 1,
        string leaseId = "lease-1",
        long generation = 1,
        bool verified = true,
        TerminalAccess access = TerminalAccess.Controlling,
        AttachmentCapabilityKey? key = null,
        DateTimeOffset? now = null) =>
        new(
            pane ?? Pane(),
            new ConnectionEpoch(epoch),
            leaseId,
            generation,
            verified,
            access,
            key ?? Key(),
            now ?? T0);

    public static TransferCoordinator Transfers() =>
        new(new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(8)));

    public static AttachmentCoordinator Coordinator(
        AttachmentCapabilityCatalog? catalog = null,
        TransferCoordinator? transfers = null,
        AttachmentLeaseRegistry? leases = null,
        RecordingAttachmentInput? input = null,
        RecordingAttachmentClipboard? clipboard = null,
        RecordingAttachmentDiagnostics? diagnostics = null) =>
        new(
            catalog ?? Catalog(),
            transfers ?? Transfers(),
            leases ?? new AttachmentLeaseRegistry(),
            input ?? new RecordingAttachmentInput(),
            clipboard,
            diagnostics);

    public static MemoryFileEndpoint Endpoint(string name, long epoch = 1) =>
        new(new TransferEndpointKey(Device(), new SessionKey(Device(), "endpoint", name),
            new ConnectionEpoch(epoch), "local-" + name));

    public static FileComponent Comp(string name) => new(Encoding.UTF8.GetBytes(name));

    public static AttachmentSource Source(
        string handle = "src-1",
        string display = "note.txt",
        AttachmentSourceKind kind = AttachmentSourceKind.OpaqueHandle) =>
        new(kind, handle, display, 5, "text/plain");

    public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    public static bool HasSubmitKey(ReadOnlySpan<byte> bytes) =>
        AttachmentPathCodec.ContainsSubmitKey(bytes);
}
