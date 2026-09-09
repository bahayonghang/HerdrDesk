using Microsoft.UI.Xaml.Controls;

namespace HerdDesk.App.Controls;

public sealed partial class WorkspacePaneTree : UserControl
{
    public WorkspacePaneTree()
    {
        InitializeComponent();
    }

    public event EventHandler<NavigationItem>? ItemChosen;

    public void SetItems(IEnumerable<NavigationItem> items, string? selectedKey)
    {
        ArgumentNullException.ThrowIfNull(items);
        var rows = items.Select(item => new ShellNavRow(item)).ToList();
        TreeList.ItemsSource = rows;
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
}
