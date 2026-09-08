using HerdDesk.Contracts;
using HerdDesk.Terminal.Web;

namespace HerdDesk.App;

public sealed class TerminalInputViewModel
{
    private readonly TerminalInputController controller;
    private readonly TerminalFocusCoordinator focus;

    public TerminalInputViewModel(TerminalInputController controller, TerminalFocusCoordinator? focus = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        this.controller = controller;
        this.focus = focus ?? new TerminalFocusCoordinator();
    }

    public TerminalInputController Controller => controller;
    public TerminalFocusCoordinator Focus => focus;
    public bool IsComposing => controller.IsComposing;
    public bool IsReadOnly => controller.IsReadOnly;
    public bool HasFocus => controller.HasFocus || focus.ContentFocused;
    public bool ControlVerified => controller.ControlVerified;
    public string? LastRejectCode => controller.LastRejectCode;
    public string CopyAutomationName => "复制";
    public string ClearSelectionAutomationName => "清除选择";
    public string RequestControlAutomationName => "申请控制";
    public string ReleaseControlAutomationName => "释放控制";
    public string AccessStripIcon => AccessStripCode;

    public string AccessStrip => AccessStripCode switch
    {
        "observing" => ShellStrings.Observing,
        "acquiring" => ShellStrings.AcquiringControl,
        "controlling" => ShellStrings.Controlling,
        "paused" => ShellStrings.InputPaused,
        _ => ShellStrings.ConnectionExpired
    };

    public string AccessStripCode
    {
        get
        {
            if (controller.IsSuspended ||
                controller.Context.Access == TerminalAccess.Disconnected ||
                controller.Context.Epoch.Value <= 0)
                return "expired";
            if (controller.IsComposing)
                return "paused";
            return controller.Context.Access switch
            {
                TerminalAccess.Observing => "observing",
                TerminalAccess.Acquiring => "acquiring",
                TerminalAccess.Controlling when controller.ControlVerified => "controlling",
                TerminalAccess.Controlling => "acquiring",
                _ => "expired"
            };
        }
    }

    public HostInputResult HandleKey(PhysicalKeyEvent key) => controller.HandleKey(key);

    public HostInputResult StartComposition() => controller.StartComposition();

    public HostInputResult UpdatePreedit() => controller.UpdatePreedit();

    public HostInputResult Commit(string token, string text) => controller.Commit(token, text);

    public HostInputResult Paste(string text) => controller.HandlePaste(text);

    public HostInputResult CopySelection() => controller.CopySelection();

    public HostInputResult RequestControl() => controller.RequestControl();

    public HostInputResult RequestControlFromKeyboard() => RequestControl();

    public HostInputResult RequestControlFromMouse() => RequestControl();

    public HostInputResult RequestControlFromScreenReader() => RequestControl();

    public HostInputResult ReleaseControl() => controller.ReleaseControl();

    public HostInputResult RetryFocus()
    {
        if (controller.Pane is null || controller.Epoch is null)
            return HostInputResult.Deny(HostInputCodes.RendererNotReady);
        var restored = focus.RetryFocus(controller.RendererReady, true);
        var host = controller.RequestFocus();
        return restored.Allowed || host.Allowed
            ? HostInputResult.Local(HostInputCodes.Allowed)
            : HostInputResult.Deny(host.Code);
    }
}
