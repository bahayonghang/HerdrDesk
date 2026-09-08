using HerdDesk.Contracts;

namespace HerdDesk.App;

public sealed class RecentAccessStore
{
    public const int MaxEntries = 50;
    private readonly List<RecentEntry> _items = [];

    public IReadOnlyList<RecentEntry> Items => _items;

    public void Record(RecentEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _items.RemoveAll(item => SameTarget(item, entry));
        _items.Insert(0, entry);
        if (_items.Count > MaxEntries)
            _items.RemoveRange(MaxEntries, _items.Count - MaxEntries);
    }

    public void Remove(PaneKey pane) =>
        _items.RemoveAll(item => item.Pane is { } key && key == pane);

    public void Remove(RecentEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _items.RemoveAll(item => SameTarget(item, entry));
    }

    private static bool SameTarget(RecentEntry left, RecentEntry right) =>
        left.Device == right.Device &&
        left.Session == right.Session &&
        left.WorkspaceId == right.WorkspaceId &&
        left.Pane == right.Pane;
}
