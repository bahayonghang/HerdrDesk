using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HerdDesk.App.Views;

public sealed partial class DiagnosticsPage : UserControl
{
    private ShellViewModel? _shell;

    public DiagnosticsPage()
    {
        InitializeComponent();
    }

    public void Bind(ShellViewModel shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
        shell.Diagnostics.Open();
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (_shell?.Diagnostics.Preview is not { } preview)
        {
            FieldList.ItemsSource = Array.Empty<string>();
            return;
        }

        FieldList.ItemsSource = preview.Fields
            .Select(item => item.Name + "=" + item.Value)
            .ToArray();
        StatusText.Text = preview.Confirmed ? "confirmed" : "preview";
    }

    private void OnPreview(object sender, RoutedEventArgs args)
    {
        _ = (sender, args);
        _shell?.Diagnostics.BuildPreview();
        RefreshPreview();
    }

    private void OnConfirm(object sender, RoutedEventArgs args)
    {
        _ = (sender, args);
        _shell?.Diagnostics.ConfirmExport();
        RefreshPreview();
    }

    private void OnExport(object sender, RoutedEventArgs args)
    {
        _ = (sender, args);
        if (_shell is null || string.IsNullOrWhiteSpace(ExportPathBox.Text))
            return;
        StatusText.Text = _shell.Diagnostics.TryExport(ExportPathBox.Text.Trim())
            ? "exported"
            : "export_blocked";
    }
}
