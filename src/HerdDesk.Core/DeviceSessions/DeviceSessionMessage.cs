using System.Text.Json;
using HerdDesk.Contracts;

namespace HerdDesk.Core;

internal abstract record ActorMessage;

internal sealed record ConnectCommand(string SocketPath, TaskCompletionSource Completion) : ActorMessage;

internal sealed record DisconnectCommand(TaskCompletionSource Completion) : ActorMessage;

internal sealed record RetryNowCommand(TaskCompletionSource Completion) : ActorMessage;

internal sealed record CancelRetryCommand(TaskCompletionSource Completion) : ActorMessage;

internal sealed record NoteFailureCommand(RecoveryFailure Failure, TaskCompletionSource Completion) : ActorMessage;

internal sealed record AppStoppingCommand(TaskCompletionSource Completion) : ActorMessage;

internal sealed record StopCommand(TaskCompletionSource Completion) : ActorMessage;

internal sealed record RetryDueMessage(ulong OperationId) : ActorMessage;

internal sealed record ConnectionsOpenedMessage(
    ConnectionEpoch Epoch,
    IRpcRequestConnection Request,
    IRpcSubscriptionConnection Subscription) : ActorMessage;

internal sealed record SubscriptionAckMessage(ConnectionEpoch Epoch, ulong OperationId) : ActorMessage;

internal sealed record InvalidatedMessage(
    ConnectionEpoch Epoch,
    ulong OperationId,
    JsonElement Document) : ActorMessage;

internal sealed record SnapshotCompletedMessage(
    ConnectionEpoch Epoch,
    ulong OperationId,
    long DirtyGenerationAtRequest,
    RpcRequestOutcome? Outcome) : ActorMessage;

internal sealed record EntityReadCompletedMessage(
    ConnectionEpoch Epoch,
    ulong OperationId,
    long DirtyGenerationAtRequest,
    ProjectionEntityChangeSet? ChangeSet,
    string? FailureCode) : ActorMessage;

internal sealed record ReconcileDueMessage(ConnectionEpoch Epoch, ulong OperationId) : ActorMessage;

internal sealed record CalibrationDueMessage(ConnectionEpoch Epoch, ulong OperationId) : ActorMessage;

internal sealed record ConnectionEndedMessage(
    ConnectionEpoch Epoch,
    ulong OperationId,
    string Code,
    RpcFailureKind Kind,
    bool FromRequest,
    bool FromSubscription) : ActorMessage;
