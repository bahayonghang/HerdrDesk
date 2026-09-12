using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HerdDesk.App.Controls;

public sealed partial class WorkspacePaneTree : UserControl
{
    public WorkspacePaneTree()
    {
        InitializeComponent();
    }

    public event EventHandler<NavigationItem>? ItemChosen;
    public event EventHandler? AddDeviceRequested;
    public event EventHandler? DiagnosticsRequested;

    public void SetItems(IEnumerable<NavigationItem> items, string? selectedKey)
    {
        ArgumentNullException.ThrowIfNull(items);
        var rows = items.Where(item => item.Kind != NavigationKind.Pane)
            .Select(item => new ShellNavRow(item))
            .ToList();
        TreeList.ItemsSource = rows;
        EmptyHint.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (selectedKey is null)
            return;
        var match = rows.FirstOrDefault(row => row.IdentityKey == selectedKey);
        if (match is not null)
            TreeList.SelectedItem = match;
    }

    private void OnItemClick(object sender, ItemClickEventArgs args)
    {
        _ = sender;
        if (args.ClickedItem is ShellNavRow row)
            ItemChosen?.Invoke(this, row.Item);
    }

    private void OnAddDevice(object sender, RoutedEventArgs args)
    {
        _ = (sender, args);
        AddDeviceRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnDiagnostics(object sender, RoutedEventArgs args)
    {
        _ = (sender, args);
        DiagnosticsRequested?.Invoke(this, EventArgs.Empty);
    }
}
