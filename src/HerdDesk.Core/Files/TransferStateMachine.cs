using HerdDesk.Contracts;

namespace HerdDesk.Core;

public static class TransferStateMachine
{
    public static bool IsTerminal(TransferStage stage) =>
        stage is TransferStage.Completed or TransferStage.Cancelled or TransferStage.Failed
            or TransferStage.FailedCleanup;

    public static bool CanCancel(TransferStage stage) =>
        !IsTerminal(stage) && stage is not TransferStage.Cleaning and not TransferStage.Committing;

    public static bool TryTransition(TransferStage from, TransferStage to)
    {
        if (from == to)
            return true;
        if (IsTerminal(from))
            return false;
        return (from, to) switch
        {
            (TransferStage.Queued, TransferStage.Admitted) => true,
            (TransferStage.Queued, TransferStage.Failed) => true,
            (TransferStage.Queued, TransferStage.Cancelled) => true,
            (TransferStage.Admitted, TransferStage.Starting) => true,
            (TransferStage.Starting, TransferStage.Staging) => true,
            (TransferStage.Staging, TransferStage.Transferring) => true,
            (TransferStage.Transferring, TransferStage.Verifying) => true,
            (TransferStage.Verifying, TransferStage.Committing) => true,
            (TransferStage.Committing, TransferStage.Completed) => true,
            (_, TransferStage.CancelRequested) => CanCancel(from),
            (_, TransferStage.Failed) => true,
            (TransferStage.CancelRequested, TransferStage.Cleaning) => true,
            (TransferStage.Failed, TransferStage.Cleaning) => true,
            (TransferStage.Cleaning, TransferStage.Cancelled) => true,
            (TransferStage.Cleaning, TransferStage.FailedCleanup) => true,
            _ => false
        };
    }
}
