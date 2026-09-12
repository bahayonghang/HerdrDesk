using HerdDesk.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HerdDesk.App.Views;

public sealed partial class SettingsPage : UserControl
{
    private ShellViewModel? _shell;
    private bool _suppress;

    public SettingsPage()
    {
        InitializeComponent();
        ThemeBox.ItemsSource = new[]
        {
            UiThemeKind.System.ToString(),
            UiThemeKind.Light.ToString(),
            UiThemeKind.Dark.ToString(),
            UiThemeKind.HighContrast.ToString()
        };
    }

    public void Bind(ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
        _suppress = true;
        var draft = shell.Settings.Draft;
        LabelBox.Text = draft.Label;
        PathBox.Text = draft.VerifiedHerdrPath;
        FontBox.Text = shell.Settings.UiDraft.FontFamily;
        SizeBox.Text = shell.Settings.UiDraft.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ZoomBox.Text = shell.Settings.UiDraft.ZoomPercent.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ThemeBox.SelectedItem = shell.Settings.UiDraft.Theme.ToString();
        SessionList.ItemsSource = draft.Sessions
            .Select(item => item.SessionName ?? item.CanonicalLocation ?? item.Kind.ToString())
            .ToArray();
        StatusText.Text = shell.Settings.ErrorCode ?? shell.Settings.LifecycleLabel;
        ConnectStatus.Text = shell.Settings.ConnectExplanation;
        var ssh = shell.SshAvailability;
        SshBanner.Text = ssh.ReasonText ?? ShellStrings.SshPending;
        SshBanner.Opacity = ssh.Kind == RouteAvailabilityKind.Enabled ? 1 : 0.7;
        _suppress = false;
    }

    private void OnDraftChanged(object sender, TextChangedEventArgs args)
    {
        _ = (sender, args);
        if (_suppress || _shell is null)
            return;
        _shell.Settings.SetDeviceLabel(LabelBox.Text ?? "");
        _shell.Settings.SetHerdrPath(PathBox.Text ?? "");
        StatusText.Text = _shell.Settings.LifecycleLabel;
    }

    private void OnAddNamed(object sender, RoutedEventArgs args)
    {
        _ = (sender, args);
        if (_shell is null || string.IsNullOrWhiteSpace(NamedSessionBox.Text))
            return;
        _shell.Settings.AddNamedSession(NamedSessionBox.Text.Trim());
        Bind(_shell);
    }

    private void OnAddEndpoint(object sender, RoutedEventArgs args)
    {
        _ = (sender, args);
        if (_shell is null || string.IsNullOrWhiteSpace(EndpointBox.Text))
            return;
        _shell.Settings.AddExplicitEndpoint(EndpointBox.Text.Trim(), EndpointKind.NamedPipe);
        Bind(_shell);
    }

    private void OnDisplayChanged(object sender, TextChangedEventArgs args)
    {
        _ = (sender, args);
        ApplyDisplay();
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs args)
    {
        _ = (sender, args);
        if (_suppress || _shell is null)
            return;
        if (ThemeBox.SelectedItem is string name && Enum.TryParse<UiThemeKind>(name, out var theme))
            _shell.Settings.SetTheme(theme);
    }

    private void ApplyDisplay()
    {
        if (_suppress || _shell is null)
            return;
        if (!int.TryParse(SizeBox.Text, out var size))
            size = 12;
        if (!int.TryParse(ZoomBox.Text, out var zoom))
            zoom = 100;
        _shell.Settings.PreviewDisplay(new TerminalDisplayPreferences(
            FontBox.Text ?? "Cascadia Mono",
            size,
            zoom));
        StatusText.Text = _shell.Settings.ErrorCode ?? _shell.Settings.LifecycleLabel;
    }

    private async void OnSave(object sender, RoutedEventArgs args)
    {
        _ = (sender, args);
        if (_shell is null)
            return;
        if (_shell.Settings.Draft.Device.Value == Guid.Empty)
            _shell.Settings.BeginNewDevice();
        _shell.Settings.SetDeviceLabel(LabelBox.Text ?? "");
        _shell.Settings.SetHerdrPath(PathBox.Text ?? "");
        await _shell.Settings.SaveLocalDeviceAsync().ConfigureAwait(true);
        await _shell.Settings.SaveUiPreferencesAsync().ConfigureAwait(true);
        Bind(_shell);
    }

    private void OnDiscard(object sender, RoutedEventArgs args)
    {
        _ = (sender, args);
        if (_shell is null)
            return;
        _shell.Settings.Discard();
        Bind(_shell);
    }

    private void OnConnect(object sender, RoutedEventArgs args)
    {
        _ = (sender, args);
        if (_shell is null)
            return;
        _shell.Settings.RequestConnect(0);
        _ = _shell.ConnectPendingAsync();
        Bind(_shell);
    }
}
