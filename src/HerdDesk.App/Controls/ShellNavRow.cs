namespace HerdDesk.App.Controls;

public sealed class ShellNavRow
{
    public ShellNavRow(NavigationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Item = item;
        IdentityKey = item.IdentityKey;
        Label = item.Label;
        Status = item.Status.Text;
        Kind = item.KindLabel;
        Unread = item.UnreadCount;
        Display = item.Label + " · " + item.Status.Text;
    }

    public NavigationItem Item { get; }
    public string IdentityKey { get; }
    public string Label { get; }
    public string Status { get; }
    public string Kind { get; }
    public int Unread { get; }
    public string Display { get; }
}
