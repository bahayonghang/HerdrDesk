using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HerdDesk.App.Controls;

public sealed partial class ControlBar : UserControl
{
    private TerminalControlViewModel? _control;

    public ControlBar()
    {
        InitializeComponent();
    }

    public event EventHandler? Changed;

    public void Bind(TerminalControlViewModel? control, bool paneSelected)
    {
        _control = control;
        if (control is null)
        {
            AccessText.Text = ShellStrings.Disconnected;
            AutomationProperties.SetName(AccessText, ShellStrings.Disconnected);
            PrimaryButton.Content = ShellStrings.RequestControl;
            AutomationProperties.SetName(PrimaryButton, ShellStrings.RequestControl);
            PrimaryButton.IsEnabled = false;
            SecondaryButton.Visibility = Visibility.Collapsed;
            DisabledReason.Text = ShellStrings.ControlRequiresPane;
            return;
        }

        AccessText.Text = control.AccessLabel;
        AutomationProperties.SetName(AccessText, control.AccessLabel);
        PrimaryButton.Content = control.PrimaryActionName;
        AutomationProperties.SetName(PrimaryButton, control.PrimaryActionName);
        PrimaryButton.IsEnabled = control.PrimaryActionEnabled && paneSelected;
        SecondaryButton.Content = control.SecondaryActionName ?? ShellStrings.TakeOver;
        AutomationProperties.SetName(
            SecondaryButton, control.SecondaryActionName ?? ShellStrings.TakeOver);
        SecondaryButton.Visibility = control.SecondaryActionName is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        DisabledReason.Text = paneSelected
            ? control.DisabledReason ?? ""
            : ShellStrings.ControlRequiresPane;
    }

    private void OnPrimary(object sender, RoutedEventArgs args)
    {
        _ = (sender, args);
        _control?.InvokePrimary();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnSecondary(object sender, RoutedEventArgs args)
    {
        _ = (sender, args);
        _control?.InvokeSecondary();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
