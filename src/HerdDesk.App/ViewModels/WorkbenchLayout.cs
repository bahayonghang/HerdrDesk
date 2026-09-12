using HerdDesk.Contracts;
using HerdDesk.Core;

namespace HerdDesk.App;

public readonly record struct MosaicRect(double X, double Y, double Width, double Height)
{
    public static MosaicRect Full { get; } = new(0, 0, 1, 1);
}

public sealed record WorkbenchTabItem(
    string TabId,
    string WorkspaceId,
    ulong Number,
    string Label,
    bool Selected,
    bool ProjectionFocused,
    ulong PaneCount)
{
    public string Display => Number == 0 ? Label : Number + " · " + Label;
}

public sealed record MosaicSlot(
    PaneKey Pane,
    ConnectionEpoch Epoch,
    PaneVisibilityKind Visibility,
    MosaicRect Rect,
    bool Focused,
    string StatusText,
    bool BindHost,
    bool NeedsPlaceholder);

public sealed class WorkbenchLayout
{
    private readonly PaneVisibilityCoordinator _visibility;
    private readonly HashSet<PaneKey> _active = [];
    private SessionProjection? _session;
    private string? _workspaceId;
    private PaneKey? _selectedPane;
    private Func<PaneKey, bool>? _rendererReady;
    private string? _userTabId;

    public WorkbenchLayout(PaneVisibilityCoordinator? visibility = null)
    {
        _visibility = visibility ?? CreateProductVisibility();
    }

    public static WorkbenchLayout CreateProduct() => new(CreateProductVisibility());

    public PaneVisibilityCoordinator Visibility => _visibility;
    public IReadOnlyList<WorkbenchTabItem> Tabs { get; private set; } = [];
    public string? SelectedTabId { get; private set; }
    public bool Zoomed { get; private set; }
    public bool HasLayout { get; private set; }
    public IReadOnlyList<MosaicSlot> Slots { get; private set; } = [];

    public void ClearTabOverride() => _userTabId = null;

    public void SelectTab(string tabId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tabId);
        _userTabId = tabId;
        if (_session is not null && _workspaceId is not null)
            Project(_session, _workspaceId, _selectedPane, _rendererReady);
    }

    public void Clear()
    {
        HideLeft([]);
        _userTabId = null;
        _session = null;
        _workspaceId = null;
        _selectedPane = null;
        _rendererReady = null;
        Tabs = [];
        SelectedTabId = null;
        Zoomed = false;
        HasLayout = false;
        Slots = [];
    }

    public void Project(
        SessionProjection? session,
        string? workspaceId,
        PaneKey? selectedPane = null,
        Func<PaneKey, bool>? rendererReady = null)
    {
        if (session is null || string.IsNullOrWhiteSpace(workspaceId))
        {
            Clear();
            return;
        }

        if (_session?.Session != session.Session || _workspaceId != workspaceId)
            _userTabId = null;

        _session = session;
        _workspaceId = workspaceId;
        _selectedPane = selectedPane is { } pane && pane.WorkspaceId == workspaceId ? pane : null;
        _rendererReady = rendererReady;

        var tabs = session.Tabs.Where(item => item.WorkspaceId == workspaceId).ToArray();
        var selectedTabId = ResolveTabId(session, workspaceId, tabs, _selectedPane);
        Tabs = tabs.Select(item => new WorkbenchTabItem(
            item.TabId,
            item.WorkspaceId,
            item.Number,
            string.IsNullOrWhiteSpace(item.Label) ? item.TabId : item.Label,
            item.TabId == selectedTabId,
            item.Focused,
            item.PaneCount)).ToArray();
        SelectedTabId = selectedTabId;

        var layout = selectedTabId is null
            ? null
            : session.Layouts.FirstOrDefault(item =>
                item.WorkspaceId == workspaceId && item.TabId == selectedTabId);
        Zoomed = layout?.Zoomed == true;
        HasLayout = layout is not null;
        var desired = HasLayout
            ? SlotsFromLayout(session, workspaceId, layout!)
            : SlotsWithoutLayout(session, workspaceId, _selectedPane);
        HideLeft(desired.Select(item => item.Pane));
        Slots = desired.Select(item => ShowSlot(item, rendererReady)).ToArray();
    }

    private string? ResolveTabId(
        SessionProjection session,
        string workspaceId,
        IReadOnlyList<TabProjection> tabs,
        PaneKey? selectedPane)
    {
        if (tabs.Count == 0)
            return null;
        if (_userTabId is not null && tabs.Any(item => item.TabId == _userTabId))
            return _userTabId;
        if (selectedPane is { } pane)
        {
            var projected = session.Panes.FirstOrDefault(item => item.Key == pane);
            if (projected is not null &&
                projected.Key.WorkspaceId == workspaceId &&
                tabs.Any(item => item.TabId == projected.TabId))
                return projected.TabId;
        }

        return tabs.FirstOrDefault(item => item.Focused)?.TabId ?? tabs[0].TabId;
    }

    private List<DesiredSlot> SlotsFromLayout(
        SessionProjection session,
        string workspaceId,
        LayoutProjection layout)
    {
        if (layout.Zoomed)
        {
            var paneId = layout.FocusedPaneId;
            if (string.IsNullOrWhiteSpace(paneId) && _selectedPane is { } selected)
                paneId = selected.PaneId;
            if (string.IsNullOrWhiteSpace(paneId) && layout.Panes.Count > 0)
                paneId = layout.Panes[0].PaneId;
            if (string.IsNullOrWhiteSpace(paneId))
                return [];
            var key = new PaneKey(session.Session, workspaceId, paneId);
            return [new DesiredSlot(key, true, MosaicRect.Full, true)];
        }

        if (layout.Panes.Count == 0)
            return SlotsWithoutLayout(session, workspaceId, _selectedPane);

        var usable = layout.Panes.Where(item =>
            !string.IsNullOrWhiteSpace(item.PaneId) && item.Width > 0 && item.Height > 0).ToArray();
        if (usable.Length == 0)
            return SlotsWithoutLayout(session, workspaceId, _selectedPane);

        var rects = Normalize(usable);
        var focusedId = ResolveLayoutFocus(layout, usable);
        var slots = new List<DesiredSlot>(usable.Length);
        for (var i = 0; i < usable.Length; i++)
        {
            var cell = usable[i];
            var key = new PaneKey(session.Session, workspaceId, cell.PaneId);
            slots.Add(new DesiredSlot(key, cell.PaneId == focusedId, rects[i], true));
        }

        return slots;
    }

    private string? ResolveLayoutFocus(
        LayoutProjection layout,
        IReadOnlyList<LayoutPaneProjection> usable)
    {
        if (_selectedPane is { } selected &&
            usable.Any(item => item.PaneId == selected.PaneId))
            return selected.PaneId;
        if (!string.IsNullOrWhiteSpace(layout.FocusedPaneId) &&
            usable.Any(item => item.PaneId == layout.FocusedPaneId))
            return layout.FocusedPaneId;
        return usable.FirstOrDefault(item => item.Focused)?.PaneId;
    }

    private List<DesiredSlot> SlotsWithoutLayout(
        SessionProjection session,
        string workspaceId,
        PaneKey? selectedPane)
    {
        var pane = ResolveFallbackPane(session, workspaceId, selectedPane);
        if (pane is null)
            return [];
        return [new DesiredSlot(pane.Value, true, MosaicRect.Full, false)];
    }

    private static PaneKey? ResolveFallbackPane(
        SessionProjection session,
        string workspaceId,
        PaneKey? selectedPane)
    {
        if (selectedPane is { } selected &&
            selected.Session == session.Session &&
            selected.WorkspaceId == workspaceId &&
            session.Panes.Any(item => item.Key == selected))
            return selected;

        if (!string.IsNullOrWhiteSpace(session.FocusedPaneId))
        {
            var focused = session.Panes.FirstOrDefault(item =>
                item.Key.WorkspaceId == workspaceId && item.Key.PaneId == session.FocusedPaneId);
            if (focused is not null)
                return focused.Key;
        }

        var marked = session.Panes.FirstOrDefault(item =>
            item.Key.WorkspaceId == workspaceId && item.Focused);
        if (marked is not null)
            return marked.Key;
        return session.Panes.FirstOrDefault(item => item.Key.WorkspaceId == workspaceId)?.Key;
    }

    private MosaicSlot ShowSlot(DesiredSlot desired, Func<PaneKey, bool>? rendererReady)
    {
        var decision = _visibility.Show(desired.Pane, desired.Focused);
        if (decision.Visibility is PaneVisibilityKind.Visible or PaneVisibilityKind.WaitingForCapacity)
            _active.Add(desired.Pane);
        var bindHost = decision.Accepted && decision.Visibility == PaneVisibilityKind.Visible;
        var ready = rendererReady?.Invoke(desired.Pane) == true;
        var needsPlaceholder = !desired.HasLayout && (!ready || !bindHost);
        var status = decision.Visibility == PaneVisibilityKind.WaitingForCapacity
            ? decision.StatusText
            : needsPlaceholder
                ? ShellStrings.TerminalPlaceholder
                : decision.StatusText;
        return new MosaicSlot(
            desired.Pane,
            decision.Epoch,
            decision.Visibility,
            desired.Rect,
            desired.Focused,
            status,
            bindHost,
            needsPlaceholder);
    }

    private void HideLeft(IEnumerable<PaneKey> desired)
    {
        var keep = desired.ToHashSet();
        foreach (var pane in _active.ToArray())
        {
            if (keep.Contains(pane))
                continue;
            _visibility.Hide(pane);
            _active.Remove(pane);
        }
    }

    private static MosaicRect[] Normalize(IReadOnlyList<LayoutPaneProjection> panes)
    {
        var result = new MosaicRect[panes.Count];
        if (panes.Count == 0)
            return result;
        var minX = panes.Min(item => (int)item.X);
        var minY = panes.Min(item => (int)item.Y);
        var maxX = panes.Max(item => (int)item.X + item.Width);
        var maxY = panes.Max(item => (int)item.Y + item.Height);
        var spanX = maxX - minX;
        var spanY = maxY - minY;
        if (spanX <= 0 || spanY <= 0)
        {
            Array.Fill(result, MosaicRect.Full);
            return result;
        }

        for (var i = 0; i < panes.Count; i++)
        {
            var pane = panes[i];
            result[i] = new MosaicRect(
                (pane.X - minX) / (double)spanX,
                (pane.Y - minY) / (double)spanY,
                pane.Width / (double)spanX,
                pane.Height / (double)spanY);
        }

        return result;
    }

    private static PaneVisibilityCoordinator CreateProductVisibility() =>
        new(new ConnectionAdmissionPolicy(ResourceBudgets.Product), new NoOpPaneVisibilityHost());

    private readonly record struct DesiredSlot(PaneKey Pane, bool Focused, MosaicRect Rect, bool HasLayout);
}
