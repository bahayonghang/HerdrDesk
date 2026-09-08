using System.Diagnostics;
using HerdDesk.Contracts;

namespace HerdDesk.App;

public sealed class SearchPaletteViewModel
{
    private readonly ProjectionCatalog _catalog;
    private readonly RecentAccessStore _recents;
    private List<SearchHit> _results = [];

    public SearchPaletteViewModel(ProjectionCatalog catalog, RecentAccessStore recents)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(recents);
        _catalog = catalog;
        _recents = recents;
    }

    public string Query { get; private set; } = "";
    public IReadOnlyList<SearchHit> Results => _results;
    public int SelectedIndex { get; private set; }
    public bool IsOpen { get; set; }
    public bool IsComposing { get; set; }
    public double LatencyMs { get; private set; }
    public int HiddenTerminalBridgeCount { get; } = 0;
    public FocusToken? SavedFocus { get; set; }

    public SearchState State =>
        new(Query, _results, SelectedIndex, IsOpen, IsComposing, LatencyMs,
            _results.Any(item => item.IsExpired));

    public void SetQuery(string query)
    {
        Query = query ?? "";
        Refresh();
    }

    public void Refresh()
    {
        var watch = Stopwatch.StartNew();
        _results = SearchIndex.Query(Query, _catalog, _recents);
        watch.Stop();
        LatencyMs = watch.Elapsed.TotalMilliseconds;
        if (_results.Count == 0)
            SelectedIndex = 0;
        else if (SelectedIndex >= _results.Count)
            SelectedIndex = _results.Count - 1;
    }

    public void MoveSelection(int delta)
    {
        if (_results.Count == 0)
            return;
        SelectedIndex = (SelectedIndex + delta + _results.Count * 8) % _results.Count;
    }

    public SearchHit? Selected =>
        SelectedIndex >= 0 && SelectedIndex < _results.Count ? _results[SelectedIndex] : null;

    public void MarkExpired(SearchHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        for (var i = 0; i < _results.Count; i++)
        {
            if (SameTarget(_results[i], hit))
                _results[i] = _results[i] with { IsExpired = true };
        }
    }

    private static bool SameTarget(SearchHit left, SearchHit hit) =>
        left.Device == hit.Device &&
        left.Session == hit.Session &&
        left.WorkspaceId == hit.WorkspaceId &&
        left.Pane == hit.Pane;
}

public static class SearchIndex
{
    public static List<SearchHit> Query(string query, ProjectionCatalog catalog, RecentAccessStore recents)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(recents);
        var needle = query.Trim().ToLowerInvariant();
        var hits = new List<SearchHit>();
        var snapshot = catalog.Snapshot;
        foreach (var recent in recents.Items)
        {
            var expired = IsExpired(recent, snapshot);
            var label = LabelFor(recent, snapshot);
            if (Matches(needle, label, ShellStrings.Pane, recent.Device, recent.Session))
            {
                hits.Add(new SearchHit(
                    SearchResultKind.Recent,
                    label,
                    ShellStrings.Pane,
                    recent.Device,
                    recent.Session,
                    recent.WorkspaceId,
                    recent.Pane,
                    recent.Epoch,
                    recent.Utc,
                    expired,
                    true));
            }
        }

        foreach (var device in snapshot.Devices)
        {
            var deviceLabel = device.Device.Value.ToString("D");
            if (Matches(needle, deviceLabel, ShellStrings.Device, device.Device, null))
            {
                hits.Add(new SearchHit(
                    SearchResultKind.Device, deviceLabel, ShellStrings.Device, device.Device,
                    null, null, null, snapshot.Epoch, null, false, false));
            }

            foreach (var session in device.Sessions)
            {
                var sessionLabel = session.Session.SessionName ?? session.Session.EndpointKey;
                if (Matches(needle, sessionLabel, ShellStrings.Session, device.Device, session.Session))
                {
                    hits.Add(new SearchHit(
                        SearchResultKind.Session, sessionLabel, ShellStrings.Session, device.Device,
                        session.Session, null, null, snapshot.Epoch, null, false, false));
                }

                foreach (var workspace in session.Workspaces)
                {
                    if (Matches(needle, workspace.Label, ShellStrings.Workspace, device.Device, session.Session))
                    {
                        hits.Add(new SearchHit(
                            SearchResultKind.Workspace, workspace.Label, ShellStrings.Workspace, device.Device,
                            session.Session, workspace.WorkspaceId, null, snapshot.Epoch, null, false, false));
                    }
                }

                foreach (var pane in session.Panes)
                {
                    var label = string.IsNullOrEmpty(pane.Label) ? pane.Key.PaneId : pane.Label;
                    if (Matches(needle, label, ShellStrings.Pane, device.Device, session.Session))
                    {
                        hits.Add(new SearchHit(
                            SearchResultKind.Pane, label, ShellStrings.Pane, device.Device,
                            session.Session, pane.Key.WorkspaceId, pane.Key, snapshot.Epoch, null, false, false));
                    }
                }
            }
        }

        hits.Sort(Compare);
        return hits;
    }

    private static bool Matches(string needle, string label, string kind, DeviceId device, SessionKey? session)
    {
        if (needle.Length == 0)
            return true;
        if (label.ToLowerInvariant().Contains(needle, StringComparison.Ordinal))
            return true;
        if (kind.ToLowerInvariant().Contains(needle, StringComparison.Ordinal))
            return true;
        if (device.Value.ToString("D").ToLowerInvariant().Contains(needle, StringComparison.Ordinal))
            return true;
        if (session is { } key)
        {
            if (key.EndpointKey.ToLowerInvariant().Contains(needle, StringComparison.Ordinal))
                return true;
            if (key.SessionName is { } name &&
                name.ToLowerInvariant().Contains(needle, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool IsExpired(RecentEntry recent, DeviceProjectionSnapshot snapshot)
    {
        if (recent.Epoch != snapshot.Epoch)
            return true;
        if (recent.Pane is { } pane)
        {
            foreach (var device in snapshot.Devices)
            {
                foreach (var session in device.Sessions)
                {
                    if (session.Panes.Any(item => item.Key == pane))
                        return false;
                }
            }

            return true;
        }

        return snapshot.Devices.All(item => item.Device != recent.Device);
    }

    private static string LabelFor(RecentEntry recent, DeviceProjectionSnapshot snapshot)
    {
        if (recent.Pane is { } pane)
        {
            foreach (var device in snapshot.Devices)
            {
                foreach (var session in device.Sessions)
                {
                    foreach (var item in session.Panes)
                    {
                        if (item.Key == pane)
                            return string.IsNullOrEmpty(item.Label) ? pane.PaneId : item.Label;
                    }
                }
            }
        }

        return string.IsNullOrEmpty(recent.Label)
            ? recent.Device.Value.ToString("D")
            : recent.Label;
    }

    private static int Compare(SearchHit left, SearchHit right)
    {
        if (left.IsRecent != right.IsRecent)
            return left.IsRecent ? -1 : 1;
        if (left.IsRecent && right.IsRecent)
            return Nullable.Compare(right.RecentUtc, left.RecentUtc);
        var kind = left.Kind.CompareTo(right.Kind);
        if (kind != 0)
            return kind;
        return string.CompareOrdinal(left.Label, right.Label);
    }
}
