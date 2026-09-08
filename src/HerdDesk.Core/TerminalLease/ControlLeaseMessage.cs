using HerdDesk.Contracts;

namespace HerdDesk.Core;

internal abstract record ControlLeaseMessage;

internal sealed record OpenPaneCommand(PaneKey Pane, TaskCompletionSource Completion) : ControlLeaseMessage;

internal sealed record SelectPaneCommand(PaneKey Pane, TaskCompletionSource Completion) : ControlLeaseMessage;

internal sealed record NoteFocusCommand(TaskCompletionSource Completion) : ControlLeaseMessage;

internal sealed record NoteProcessAliveCommand(TaskCompletionSource Completion) : ControlLeaseMessage;

internal sealed record RequestControlCommand(TaskCompletionSource Completion) : ControlLeaseMessage;

internal sealed record ConfirmTakeoverCommand(string Handle, TaskCompletionSource Completion) : ControlLeaseMessage;

internal sealed record CancelAcquireCommand(TaskCompletionSource Completion) : ControlLeaseMessage;

internal sealed record ReleaseControlCommand(TaskCompletionSource Completion) : ControlLeaseMessage;

internal sealed record RecoverObserveCommand(TaskCompletionSource Completion) : ControlLeaseMessage;

internal sealed record RecoverySignalCommand(
    LeaseRecoverySignal Signal,
    TaskCompletionSource Completion) : ControlLeaseMessage;

internal sealed record SubmitInputCommand(
    RendererInput Input,
    TaskCompletionSource<InputSubmissionOutcome> Completion) : ControlLeaseMessage;

internal sealed record SubmitResizeCommand(
    TerminalResizeCommand Size,
    TaskCompletionSource<InputSubmissionOutcome> Completion) : ControlLeaseMessage;

internal sealed record SubmitScrollCommand(
    TerminalScrollCommand Request,
    TaskCompletionSource<InputSubmissionOutcome> Completion) : ControlLeaseMessage;

internal sealed record LeaseStopCommand(TaskCompletionSource Completion) : ControlLeaseMessage;

internal sealed record TransportOpenedMessage(
    long LeaseGeneration,
    ConnectionEpoch Epoch,
    TerminalMode Mode,
    string? AttemptId,
    bool Takeover,
    ITerminalTransport Transport) : ControlLeaseMessage;

internal sealed record TransportOpenFailedMessage(
    long LeaseGeneration,
    TerminalMode Mode,
    string? AttemptId,
    string Code) : ControlLeaseMessage;

internal sealed record TransportEventMessage(
    long LeaseGeneration,
    ConnectionEpoch Epoch,
    TerminalMode Mode,
    string? AttemptId,
    TerminalTransportEvent Event) : ControlLeaseMessage;

internal sealed record ObserveAppliedMessage(
    long LeaseGeneration,
    ConnectionEpoch Epoch,
    ulong Sequence,
    bool Accepted,
    string Code) : ControlLeaseMessage;

internal sealed record PromotionAckedMessage(
    long LeaseGeneration,
    string AttemptId,
    ConnectionEpoch Epoch,
    ulong LastParsedSeq) : ControlLeaseMessage;

internal sealed record PromotionFailedMessage(
    long LeaseGeneration,
    string AttemptId,
    string Code) : ControlLeaseMessage;

internal sealed record WritableAppliedMessage(long LeaseGeneration) : ControlLeaseMessage;

internal sealed record WriteCompletedMessage(
    long LeaseGeneration,
    InputSubmissionOutcome Outcome,
    TaskCompletionSource<InputSubmissionOutcome>? Completion,
    bool Attempted) : ControlLeaseMessage;
