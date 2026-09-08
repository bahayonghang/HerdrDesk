using HerdDesk.Contracts;
using HerdDesk.Terminal.Web;

namespace HerdDesk.App;

/// <summary>
/// Search / notification / pane-switch focus restore. Focus does not set ControlVerified.
/// </summary>
public sealed class TerminalFocusCoordinator
{
    private int serial;
    private int pendingSerial = -1;
    private PaneKey? pendingPane;
    private ConnectionEpoch pendingEpoch;
    private bool controlVerified;

    public HostFocusState State { get; private set; } = HostFocusState.Unfocused;
    public bool ContentFocused { get; private set; }
    public bool ControlVerified => controlVerified;
    public string LastCode { get; private set; } = "unfocused";
    public PaneKey? FocusedPane { get; private set; }
    public ConnectionEpoch? FocusedEpoch { get; private set; }
    public PaneKey? PendingPane => pendingSerial == serial ? pendingPane : null;

    public void SetControlVerified(bool value) => controlVerified = value;

    public HostInputResult RequestFocus(
        PaneKey pane,
        ConnectionEpoch epoch,
        bool rendererReady,
        bool targetValid)
    {
        serial++;
        if (!targetValid || epoch.Value <= 0)
        {
            ClearPending();
            ContentFocused = false;
            State = HostFocusState.Unfocused;
            FocusedPane = null;
            FocusedEpoch = null;
            LastCode = epoch.Value <= 0 ? HostInputCodes.StaleEpoch : HostInputCodes.WrongPane;
            return HostInputResult.Deny(LastCode);
        }

        if (!rendererReady)
        {
            ContentFocused = false;
            FocusedPane = null;
            FocusedEpoch = null;
            State = HostFocusState.FocusRequested;
            pendingSerial = serial;
            pendingPane = pane;
            pendingEpoch = epoch;
            LastCode = HostInputCodes.RendererNotReady;
            return HostInputResult.Deny(LastCode);
        }

        return Complete(pane, epoch, serial);
    }

    public HostInputResult NotifyRendererReady(
        PaneKey pane,
        ConnectionEpoch epoch,
        bool rendererReady,
        bool targetValid)
    {
        if (pendingSerial != serial || pendingPane != pane || pendingEpoch != epoch)
            return HostInputResult.Deny(LastCode);
        if (!targetValid)
        {
            ClearPending();
            ContentFocused = false;
            State = HostFocusState.Unfocused;
            LastCode = HostInputCodes.StaleEpoch;
            return HostInputResult.Deny(LastCode);
        }

        if (!rendererReady)
        {
            LastCode = HostInputCodes.RendererNotReady;
            return HostInputResult.Deny(LastCode);
        }

        return Complete(pane, epoch, pendingSerial);
    }

    public HostInputResult RetryFocus(bool rendererReady, bool targetValid)
    {
        if (pendingPane is not { } pane)
            return HostInputResult.Deny(HostInputCodes.RendererNotReady);
        return RequestFocus(pane, pendingEpoch, rendererReady, targetValid);
    }

    public void Blur()
    {
        ContentFocused = false;
        State = HostFocusState.Unfocused;
    }

    public void Reset()
    {
        serial++;
        ClearPending();
        ContentFocused = false;
        State = HostFocusState.Unfocused;
        FocusedPane = null;
        FocusedEpoch = null;
        LastCode = "reset";
    }

    private HostInputResult Complete(PaneKey pane, ConnectionEpoch epoch, int expected)
    {
        if (expected != serial)
            return HostInputResult.Deny(HostInputCodes.StaleEpoch);
        ClearPending();
        ContentFocused = true;
        State = HostFocusState.Focused;
        FocusedPane = pane;
        FocusedEpoch = epoch;
        LastCode = HostInputCodes.Allowed;
        return HostInputResult.Local(HostInputCodes.Allowed);
    }

    private void ClearPending()
    {
        pendingSerial = -1;
        pendingPane = null;
    }
}
