// PROPOSED HerdDesk application ports, NOT an upstream SDK and NOT a completed app.
// BCL-only interface draft. No application compilation was performed for this bundle.
#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HerdDesk.Contracts;

public readonly record struct DeviceId(Guid Value);
public readonly record struct SessionKey(DeviceId Device, string EndpointKey, string? SessionName);
public readonly record struct PaneKey(SessionKey Session, string WorkspaceId, string PaneId);
public readonly record struct ConnectionEpoch(long Value);

public enum ConnectionPhase { Offline, Connecting, Synchronizing, Ready, Stale, Incompatible }
public enum TerminalAccess { Disconnected, Observing, Acquiring, Controlling, Unknown }
public enum InputOrigin { UserKey, CommittedText, ExplicitPaste, EmulatorReply }
public enum TerminalMode { Observe, Control }

// Missing capabilities are unknown, not false claims of tested support.
public sealed record CapabilityProfile(
    string CliVersion, string? ServerVersion, int ApiSchemaProtocol,
    int SchemaVersion, string SchemaSha256,
    IReadOnlySet<string> VerifiedOperations,
    bool TextTuiVerified, bool ImeVerified, bool ImagesVerified);

// Bytes must remain valid until the consumer completes: do not recycle a pooled buffer early.
public sealed record TerminalFrame(
    ConnectionEpoch Epoch, ulong Seq, ushort Width, ushort Height,
    bool Full, ReadOnlyMemory<byte> Bytes);

public abstract record TerminalEvent(ConnectionEpoch Epoch);
public sealed record FrameArrived(TerminalFrame Frame) : TerminalEvent(Frame.Epoch);
public sealed record StreamClosed(ConnectionEpoch Epoch, string? Reason) : TerminalEvent(Epoch);
public sealed record TransportEnded(ConnectionEpoch Epoch, int? ExitCode, bool SawClosedEnvelope)
    : TerminalEvent(Epoch);

public sealed record TerminalInput(ConnectionEpoch Epoch, InputOrigin Origin, ReadOnlyMemory<byte> Bytes);
public sealed record TerminalSize(ushort Columns, ushort Rows, uint CellWidthPx, uint CellHeightPx);
public sealed record ScrollRequest(bool Up, ushort Lines, bool IsPageKey,
    ushort? Column, ushort? Row, byte Modifiers);
public sealed record RenderConsumption(ConnectionEpoch Epoch, ulong LastParsedSeq);
public sealed record RendererInput(PaneKey Pane, TerminalInput Input);

public interface IRpcConnection : IAsyncDisposable
{
    // The caller disposes the returned JsonDocument. Implementations never retry mutations blindly.
    ValueTask<JsonDocument> RequestAsync(string method, JsonElement parameters, CancellationToken ct);
    // Each yielded JsonElement must be cloned/owned, not backed by a disposed JsonDocument.
    IAsyncEnumerable<JsonElement> SubscribeAsync(JsonElement subscriptionParameters, CancellationToken ct);
}

public interface ITerminalTransport : IAsyncDisposable
{
    PaneKey Pane { get; }
    ConnectionEpoch Epoch { get; }
    // The domain owner selects mode and authorizes takeover before transport construction.
    IAsyncEnumerable<TerminalEvent> ReadEventsAsync(CancellationToken ct);
    ValueTask SendAsync(TerminalInput input, CancellationToken ct);
    ValueTask ResizeAsync(TerminalSize size, CancellationToken ct);
    ValueTask ScrollAsync(ScrollRequest request, CancellationToken ct);
    // Release only this connection. It must NOT close the pane or stop the daemon.
    ValueTask ReleaseAsync(CancellationToken ct);
}

public interface ITerminalRenderer : IAsyncDisposable
{
    ValueTask BindAsync(PaneKey pane, ConnectionEpoch epoch, CancellationToken ct);
    // Completion acknowledges parser consumption, NOT GPU presentation or remote command execution.
    ValueTask<RenderConsumption> ApplyAsync(TerminalFrame frame, CancellationToken ct);
    IAsyncEnumerable<RendererInput> ReadInputsAsync(CancellationToken ct);
    ValueTask SetReadOnlyAsync(bool readOnly, CancellationToken ct);
    ValueTask FocusAsync(CancellationToken ct);
}

public interface IControlPolicy
{
    // Must validate current pane + epoch + ownership. Never trust renderer-provided IDs alone.
    bool CanSend(PaneKey activePane, ConnectionEpoch currentEpoch,
        TerminalAccess access, RendererInput requestedInput);
}

public sealed record RemotePath(DeviceId Device, string Value);
public sealed record TransferProgress(Guid JobId, long BytesCompleted, long? BytesTotal);
public enum ConflictPolicy { Fail, ReplaceAfterConfirmation, KeepBoth }

public interface IRemoteFileService
{
    // Future file-helper/SFTP adapters must use the same product identity/host-key policy.
    ValueTask CopyAsync(RemotePath source, RemotePath destination, ConflictPolicy policy,
        IProgress<TransferProgress> progress, CancellationToken ct);
}
