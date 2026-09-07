namespace HerdDesk.Contracts;

// HerdDesk-owned types. They are not an upstream SDK or an RPC schema.
public readonly record struct DeviceId(Guid Value);
public readonly record struct SessionKey(DeviceId Device, string EndpointKey, string? SessionName);
public readonly record struct PaneKey(SessionKey Session, string WorkspaceId, string PaneId);
public readonly record struct ConnectionEpoch(long Value);

public enum TerminalAccess { Disconnected, Observing, Acquiring, Controlling, Unknown }
public enum InputOrigin { UserKey, CommittedText, ExplicitPaste, EmulatorReply }

public abstract record TerminalEnvelope;
public sealed record TerminalFrame(ulong Sequence, ushort Columns, ushort Rows,
    bool Full, ReadOnlyMemory<byte> Bytes) : TerminalEnvelope;
public sealed record TerminalClosed(bool ReasonPresent) : TerminalEnvelope;

public sealed record RendererInput(PaneKey Pane, ConnectionEpoch Epoch,
    InputOrigin Origin, ReadOnlyMemory<byte> Bytes);

// Created by the trusted host, never deserialized from a renderer message.
// ControlVerified must remain false until a real adapter has proved ownership.
public sealed record InputContext(PaneKey ActivePane, ConnectionEpoch Epoch,
    TerminalAccess Access, bool ControlVerified = false);

public sealed record InputDecision(bool Allowed, string Code);
