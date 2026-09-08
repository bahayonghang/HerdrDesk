using HerdDesk.Contracts;

namespace HerdDesk.Core;

public static class TerminalInputCoordinator
{
    public static InputDecision Evaluate(
        ControlLeaseState lease,
        LeaseTargetSnapshot store,
        RendererInput input)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(input);
        if (!store.Exists || store.Freshness != DeviceFreshness.Current)
            return new(false, ControlLeaseCodes.TargetStale);
        if (lease.ControlBinding is null ||
            lease.Access != TerminalAccess.Controlling ||
            !lease.ControlVerified)
            return new(false, ControlLeaseCodes.ControlNotVerified);
        var context = new InputContext(
            lease.ControlBinding.Pane,
            lease.ControlBinding.Epoch,
            lease.Access,
            lease.ControlVerified);
        return InputPolicy.Evaluate(context, input);
    }

    public static InputSubmissionOutcome MapReceipt(
        TerminalWriteReceipt receipt,
        long leaseGeneration,
        bool disconnected)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.Disposition == TerminalWriteDisposition.NotSent)
            return new(receipt.CommandId, receipt.Disposition, ControlLeaseCodes.InputNotSent, leaseGeneration);
        if (disconnected || receipt.Disposition == TerminalWriteDisposition.UnknownAfterDisconnect)
            return new(receipt.CommandId, receipt.Disposition, ControlLeaseCodes.InputOutcomeUnknown, leaseGeneration);
        return new(receipt.CommandId, receipt.Disposition, receipt.Code, leaseGeneration);
    }
}
