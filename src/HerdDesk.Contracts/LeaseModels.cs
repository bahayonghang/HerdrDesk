namespace HerdDesk.Contracts;

// Trusted-host observations of terminal-session CLI/protocol facts.
// Never deserialized from a renderer message. Upstream has no grant event.

public enum TerminalLeaseOperation
{
    Observe,
    RequestControl,
    RequestTakeover,
    ResizeWhileVerified,
    Release
}

public enum TerminalStreamEndKind
{
    None,
    StdoutEof,
    TerminalClosed,
    BridgeProcessExit,
    Unknown
}

public enum TerminalControlSignal
{
    None,
    Busy,
    Rejected,
    TakeoverRequired,
    TakeoverConfirmed,
    Released,
    Unknown
}

public sealed record TerminalLeaseObservation(
    TerminalLeaseOperation Operation,
    TerminalAccess AccessBefore,
    bool ControlVerifiedBefore,
    bool StdoutEofSeen = false,
    bool TerminalClosedSeen = false,
    bool BridgeProcessExited = false,
    bool? PaneAliveObserved = null,
    bool? DaemonAliveObserved = null,
    bool FirstFrameSeen = false,
    bool ProcessAlive = false,
    bool WindowFocused = false,
    bool InputSent = false,
    bool InputAcknowledged = false,
    bool AdapterProvedWriteOwnership = false,
    bool TakeoverConfirmed = false,
    bool ResizeAttempted = false,
    bool ResizeAcknowledged = false,
    bool ReleaseAcknowledged = false,
    TerminalControlSignal ControlSignal = TerminalControlSignal.None,
    string? ObservedWireType = null);

public sealed record TerminalLeaseResult(
    TerminalAccess Access,
    bool ControlVerified,
    TerminalStreamEndKind StreamEnd,
    bool PaneExitVerified,
    string Code,
    string DiagnosticId);
