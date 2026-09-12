using HerdDesk.Contracts;
using HerdDesk.Core;

namespace HerdDesk.App;

public sealed class TerminalControlViewModel
{
    private readonly ControlLeaseCoordinator coordinator;

    public TerminalControlViewModel(ControlLeaseCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        this.coordinator = coordinator;
    }

    public ControlLeaseCoordinator Coordinator => coordinator;
    public ControlLeaseState State => coordinator.Current;
    public bool AlwaysTakeoverEnabled => false;
    public bool HasGlobalSkip => false;
    public bool ChallengeVisible => State.Challenge is not null;
    public string? ChallengeHandle => State.Challenge?.Handle;
    public string Breadcrumb => State.Challenge?.Breadcrumb ?? "";
    public string TargetSummary => State.Challenge?.TargetSummary ?? "";
    public string TakeoverWarning => ShellStrings.TakeoverReplacesController;
    public string RequestControlAutomationName => ShellStrings.RequestControl;
    public string CancelAutomationName => ShellStrings.CancelAcquire;
    public string KeepObservingAutomationName => ShellStrings.KeepObserving;
    public string TakeOverAutomationName => ShellStrings.TakeOver;
    public string ReleaseAutomationName => ShellStrings.ReleaseControl;
    public string ReturnToObserveAutomationName => ShellStrings.ReturnToObserve;

    public string AccessLabel => State.Access switch
    {
        TerminalAccess.Observing => ShellStrings.Observing,
        TerminalAccess.Acquiring => ShellStrings.AcquiringControl,
        TerminalAccess.Controlling when State.ControlVerified => ShellStrings.Controlling,
        TerminalAccess.Unknown => ShellStrings.Unknown,
        _ => ShellStrings.Disconnected
    };

    public string PrimaryActionName
    {
        get
        {
            if (State.LastAttempt == ControlAttemptOutcome.Busy && State.Access == TerminalAccess.Observing)
                return ShellStrings.KeepObserving;
            return State.Access switch
            {
                TerminalAccess.Acquiring => ShellStrings.CancelAcquire,
                TerminalAccess.Unknown => ShellStrings.ReturnToObserve,
                TerminalAccess.Controlling => ShellStrings.ReleaseControl,
                _ => ShellStrings.RequestControl
            };
        }
    }

    public string? SecondaryActionName =>
        State.Challenge is not null ? ShellStrings.TakeOver : null;

    public bool PrimaryActionEnabled => State.Target is not null;

    public string? DisabledReason =>
        State.Target is null ? ShellStrings.ControlRequiresPane : null;

    public void HandleSelectionChanged(PaneKey pane) =>
        coordinator.SelectPaneAsync(pane).AsTask().GetAwaiter().GetResult();

    public void InvokePrimary()
    {
        if (!PrimaryActionEnabled)
            return;
        if (State.LastAttempt == ControlAttemptOutcome.Busy && State.Access == TerminalAccess.Observing)
        {
            KeepObserving();
            return;
        }

        switch (State.Access)
        {
            case TerminalAccess.Acquiring:
                KeepObserving();
                break;
            case TerminalAccess.Controlling:
                ReleaseControl();
                break;
            case TerminalAccess.Unknown:
                RecoverObserve();
                break;
            case TerminalAccess.Observing:
            case TerminalAccess.Disconnected:
                RequestControl();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(State.Access), State.Access, null);
        }
    }

    public void InvokeSecondary()
    {
        if (State.Challenge is null)
            return;
        ConfirmTakeover();
    }

    public void RequestControl() =>
        coordinator.RequestControlAsync().AsTask().GetAwaiter().GetResult();

    public void RequestControlFromKeyboard() => RequestControl();

    public void RequestControlFromMouse() => RequestControl();

    public void RequestControlFromScreenReader() => RequestControl();

    public void ConfirmTakeover()
    {
        if (State.Challenge is null)
            return;
        coordinator.ConfirmTakeoverAsync(State.Challenge.Handle).AsTask().GetAwaiter().GetResult();
    }

    public void ConfirmTakeoverFromKeyboard() => ConfirmTakeover();

    public void ConfirmTakeoverFromScreenReader() => ConfirmTakeover();

    public void KeepObserving() =>
        coordinator.CancelAcquireAsync().AsTask().GetAwaiter().GetResult();

    public void ReleaseControl() =>
        coordinator.ReleaseControlAsync().AsTask().GetAwaiter().GetResult();

    public void RecoverObserve() =>
        coordinator.RecoverObserveAsync().AsTask().GetAwaiter().GetResult();
}
