using HerdDesk.Contracts;

namespace HerdDesk.Core;

internal abstract record ResourceCommandMessage;

internal sealed record OpenCreateCommand(
    ResourceOperationKind Kind,
    ResourceKey Parent,
    string Breadcrumb,
    TaskCompletionSource Completion) : ResourceCommandMessage;

internal sealed record ValidateDraftCommand(
    string? Name,
    string? WorkingDirectory,
    KnownAgentKind? AgentKind,
    TaskCompletionSource Completion) : ResourceCommandMessage;

internal sealed record SubmitCreateCommand(
    string? Name,
    string? WorkingDirectory,
    KnownAgentKind? AgentKind,
    TaskCompletionSource Completion) : ResourceCommandMessage;

internal sealed record OpenRenameCommand(
    ResourceKey Target,
    ResourceProjectionStamp Expected,
    string Breadcrumb,
    string CurrentName,
    TaskCompletionSource Completion) : ResourceCommandMessage;

internal sealed record SubmitRenameCommand(
    ResourceConfirmationToken Confirmation,
    string NewName,
    TaskCompletionSource Completion) : ResourceCommandMessage;

internal sealed record OpenCloseCommand(
    ResourceKey Target,
    ResourceProjectionStamp Expected,
    string Breadcrumb,
    TaskCompletionSource Completion) : ResourceCommandMessage;

internal sealed record ConfirmCloseCommand(
    ResourceConfirmationToken Confirmation,
    bool CloseGroup,
    TaskCompletionSource Completion) : ResourceCommandMessage;

internal sealed record NoteSelectionChangedCommand(
    ResourceKey? Selected,
    TaskCompletionSource Completion) : ResourceCommandMessage;

internal sealed record CancelDraftCommand(TaskCompletionSource Completion) : ResourceCommandMessage;

internal sealed record CancelPendingWaitCommand(TaskCompletionSource Completion) : ResourceCommandMessage;

internal sealed record NotifyProjectionCommand(TaskCompletionSource Completion) : ResourceCommandMessage;

internal sealed record RefreshOutcomeCommand(TaskCompletionSource Completion) : ResourceCommandMessage;

internal sealed record RetryAfterAbsentCommand(TaskCompletionSource Completion) : ResourceCommandMessage;

internal sealed record ResourceStopCommand(TaskCompletionSource Completion) : ResourceCommandMessage;

internal sealed record SubmitCompletedMessage(
    string CorrelationId,
    long Generation,
    ResourceTransportReceipt Receipt) : ResourceCommandMessage;

internal sealed record QueryCompletedMessage(
    string CorrelationId,
    long Generation,
    ResourceQueryReceipt Receipt) : ResourceCommandMessage;

internal sealed record TimeoutMessage(string CorrelationId, long Generation, bool Rpc) : ResourceCommandMessage;
