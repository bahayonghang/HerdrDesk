using HerdDesk.Contracts;
using HerdDesk.Core;

namespace HerdDesk.App;

public sealed class NavigationCoordinator
{
    private readonly ProjectionCatalog _catalog;
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);
    private string? _hoverKey;
    private string? _focusKey;
    private List<NavigationItem> _roots = [];
    private List<NavigationItem> _all = [];
    private List<NavigationItem> _flat = [];

    public NavigationCoordinator(ProjectionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        Selection = SelectionState.None;
    }

    public IReadOnlyList<NavigationItem> Tree => _roots;
    public IReadOnlyList<NavigationItem> VisibleItems => _flat;
    public SelectionState Selection { get; private set; }
    public NavigationItem? SelectedItem
    {
        get
        {
            var key = KeyFromSelection();
            return key is null ? null : Find(key);
        }
    }
    public bool ContentFocused { get; set; }
    public SearchHit? PendingFocus { get; set; }

    public void Rebuild(bool starting)
    {
        ReapplySelection();
        _roots = BuildRoots(starting);
        _all = Walk(_roots).ToList();
        _flat = Flatten(_roots);
    }

    public NavigationItem? Find(string identityKey) =>
        _all.FirstOrDefault(item => item.IdentityKey == identityKey);

    public void Select(NavigationItem item, bool recordExpiredIfMissing = true)
    {
        ArgumentNullException.ThrowIfNull(item);
        var current = Find(item.IdentityKey);
        if (current is null)
        {
            if (recordExpiredIfMissing)
                Selection = ToSelection(item) with { IsExpired = true };
            ContentFocused = false;
            Rebuild(false);
            return;
        }

        if (current.Epoch != _catalog.EpochFor(current.Device, current.Session))
        {
            Selection = ToSelection(current) with { IsExpired = true };
            ContentFocused = false;
            Rebuild(false);
            return;
        }

        Selection = ToSelection(current);
        _focusKey = current.IdentityKey;
        Rebuild(false);
    }

    public void ToggleExpanded(NavigationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!_expanded.Add(item.IdentityKey))
            _expanded.Remove(item.IdentityKey);
        Rebuild(false);
    }

    public void SetHover(NavigationItem? item)
    {
        _hoverKey = item?.IdentityKey;
        Rebuild(false);
    }

    public void SetFocusVisible(NavigationItem? item)
    {
        _focusKey = item?.IdentityKey;
        Rebuild(false);
    }

    public bool Move(TreeMove move)
    {
        if (_flat.Count == 0)
            return false;
        var index = SelectedIndex();
        switch (move)
        {
            case TreeMove.Down:
                if (index < _flat.Count - 1)
                    Select(_flat[index + 1]);
                return true;
            case TreeMove.Up:
                if (index > 0)
                    Select(_flat[index - 1]);
                else if (index < 0)
                    Select(_flat[0]);
                return true;
            case TreeMove.Parent:
                if (index >= 0 && _flat[index].ParentIdentityKey is { } parentKey)
                {
                    var parent = Find(parentKey);
                    if (parent is not null)
                        Select(parent);
                }
                return true;
            case TreeMove.Child:
            case TreeMove.Expand:
                if (index >= 0)
                {
                    var item = _flat[index];
                    _expanded.Add(item.IdentityKey);
                    Rebuild(false);
                    if (move == TreeMove.Child && item.Children.Count > 0)
                        Select(item.Children[0]);
                }
                return true;
            case TreeMove.Collapse:
                if (index >= 0)
                {
                    _expanded.Remove(_flat[index].IdentityKey);
                    Rebuild(false);
                }
                return true;
            case TreeMove.Activate:
                if (index >= 0)
                    Select(_flat[index]);
                return true;
            default:
                return false;
        }
    }

    public void MarkExpired()
    {
        Selection = Selection with { IsExpired = true };
        ContentFocused = false;
        PendingFocus = null;
        Rebuild(false);
    }

    public void ExpandAll()
    {
        foreach (var item in Walk(_roots))
            _expanded.Add(item.IdentityKey);
        Rebuild(false);
    }

    private int SelectedIndex()
    {
        if (Selection.Kind == SelectionKind.None)
            return -1;
        var key = KeyFromSelection();
        return _flat.FindIndex(item => item.IdentityKey == key);
    }

    private string? KeyFromSelection()
    {
        if (Selection.Device is not { } device)
            return null;
        return NavigationIdentity.Format(
            ToKind(Selection.Kind),
            device,
            Selection.Session,
            Selection.WorkspaceId,
            Selection.Pane);
    }

    private void ReapplySelection()
    {
        if (Selection.Kind == SelectionKind.None)
            return;
        if (Selection.Device is { } selectedDevice &&
            (Selection.Epoch != _catalog.EpochFor(selectedDevice, Selection.Session) ||
             !SelectionExistsInCatalog()))
        {
            Selection = Selection with { IsExpired = true };
            ContentFocused = false;
            PendingFocus = null;
        }
    }

    private bool SelectionExistsInCatalog()
    {
        if (Selection.Device is not { } device || _catalog.FindDevice(device) is null)
            return false;
        if (Selection.Kind == SelectionKind.Device)
            return true;
        if (Selection.Session is not { } session || _catalog.FindSession(session) is null)
            return false;
        if (Selection.Kind == SelectionKind.Session)
            return true;
        if (Selection.WorkspaceId is not { } workspaceId)
            return false;
        if (Selection.Kind == SelectionKind.Workspace)
            return _catalog.FindWorkspace(session, workspaceId) is not null;
        return Selection.Pane is { } pane && _catalog.FindPane(pane) is not null;
    }

    private List<NavigationItem> BuildRoots(bool starting)
    {
        var devices = _catalog.DevicesForTree();
        var roots = new List<NavigationItem>(devices.Count);
        foreach (var device in devices)
        {
            var sessions = new List<NavigationItem>(device.Sessions.Count);
            foreach (var session in device.Sessions)
                sessions.Add(BuildSession(device, session, starting));
            roots.Add(BuildDevice(device, sessions, starting));
        }

        return roots;
    }

    private NavigationItem BuildDevice(
        ProjectedDevice device,
        IReadOnlyList<NavigationItem> sessions,
        bool starting)
    {
        var phase = DevicePhase(device);
        var stale = _catalog.FreshnessFor(device.Device) == DeviceFreshness.Stale ||
                    phase == ConnectionPhase.Stale;
        var incompatible = phase == ConnectionPhase.Incompatible ||
                           device.Capabilities.VerifiedOperations.Count == 0;
        var offline = phase == ConnectionPhase.Offline;
        var identity = NavigationIdentity.Format(NavigationKind.Device, device.Device, null, null, null);
        var selected = Matches(SelectionKind.Device, device.Device, null, null, null);
        var status = StatusFor(
            starting, sessions.Count == 0, stale, incompatible, offline, _catalog.ErrorFor(device.Device),
            _catalog.ReadinessFor(device.Device));
        return new NavigationItem(
            NavigationKind.Device,
            device.Device,
            null,
            null,
            null,
            _catalog.EpochFor(device.Device),
            _catalog.LabelFor(device.Device),
            ShellStrings.Device,
            phase,
            new WireEnum<AgentStatusKind>("unknown", AgentStatusKind.Unknown),
            sessions.Sum(item => item.UnreadCount),
            TerminalAccess.Disconnected,
            false,
            starting,
            sessions.Count == 0,
            status.Code == ShellCodes.Error,
            offline,
            stale,
            incompatible,
            incompatible,
            selected,
            identity == _hoverKey,
            identity == _focusKey,
            _expanded.Contains(identity),
            false,
            status,
            incompatible ? [ShellCodes.CapabilityUnverified] : [],
            null,
            sessions);
    }

    private NavigationItem BuildSession(ProjectedDevice device, SessionProjection session, bool starting)
    {
        var workspaces = new List<NavigationItem>(session.Workspaces.Count);
        foreach (var workspace in session.Workspaces)
            workspaces.Add(BuildWorkspace(device, session, workspace, starting));
        var phase = SessionPhase(device, session);
        var stale = _catalog.FreshnessFor(device.Device) == DeviceFreshness.Stale ||
                    phase == ConnectionPhase.Stale;
        var incompatible = phase == ConnectionPhase.Incompatible;
        var offline = phase == ConnectionPhase.Offline;
        var identity = NavigationIdentity.Format(
            NavigationKind.Session, device.Device, session.Session, null, null);
        var selected = Matches(SelectionKind.Session, device.Device, session.Session, null, null);
        var status = StatusFor(
            starting, workspaces.Count == 0, stale, incompatible, offline, _catalog.ErrorFor(device.Device),
            _catalog.ReadinessFor(device.Device));
        var label = session.Session.SessionName ?? session.Session.EndpointKey;
        return new NavigationItem(
            NavigationKind.Session,
            device.Device,
            session.Session,
            null,
            null,
            _catalog.EpochFor(device.Device, session.Session),
            label,
            ShellStrings.Session,
            phase,
            new WireEnum<AgentStatusKind>("unknown", AgentStatusKind.Unknown),
            workspaces.Sum(item => item.UnreadCount),
            TerminalAccess.Disconnected,
            false,
            starting,
            workspaces.Count == 0,
            status.Code == ShellCodes.Error,
            offline,
            stale,
            incompatible,
            incompatible,
            selected,
            identity == _hoverKey,
            identity == _focusKey,
            _expanded.Contains(identity),
            false,
            status,
            incompatible ? [ShellCodes.CapabilityUnverified] : [],
            NavigationIdentity.Format(NavigationKind.Device, device.Device, null, null, null),
            workspaces);
    }

    private NavigationItem BuildWorkspace(
        ProjectedDevice device,
        SessionProjection session,
        WorkspaceProjection workspace,
        bool starting)
    {
        var panes = session.Panes
            .Where(item => item.Key.WorkspaceId == workspace.WorkspaceId)
            .Select(item => BuildPane(device, session, workspace, item, starting))
            .ToArray();
        var phase = SessionPhase(device, session);
        var stale = _catalog.FreshnessFor(device.Device) == DeviceFreshness.Stale ||
                    phase == ConnectionPhase.Stale;
        var incompatible = phase == ConnectionPhase.Incompatible;
        var offline = phase == ConnectionPhase.Offline;
        var identity = NavigationIdentity.Format(
            NavigationKind.Workspace, device.Device, session.Session, workspace.WorkspaceId, null);
        var selected = Matches(
            SelectionKind.Workspace, device.Device, session.Session, workspace.WorkspaceId, null);
        var status = StatusFor(
            starting, panes.Length == 0, stale, incompatible, offline, _catalog.ErrorFor(device.Device),
            _catalog.ReadinessFor(device.Device));
        return new NavigationItem(
            NavigationKind.Workspace,
            device.Device,
            session.Session,
            workspace.WorkspaceId,
            null,
            _catalog.EpochFor(device.Device, session.Session),
            workspace.Label,
            ShellStrings.Workspace,
            phase,
            workspace.AgentStatus,
            panes.Sum(item => item.UnreadCount),
            TerminalAccess.Disconnected,
            false,
            starting,
            panes.Length == 0,
            status.Code == ShellCodes.Error,
            offline,
            stale,
            incompatible,
            incompatible,
            selected,
            identity == _hoverKey,
            identity == _focusKey,
            _expanded.Contains(identity),
            false,
            status,
            incompatible ? [ShellCodes.CapabilityUnverified] : [],
            NavigationIdentity.Format(NavigationKind.Session, device.Device, session.Session, null, null),
            panes);
    }

    private NavigationItem BuildPane(
        ProjectedDevice device,
        SessionProjection session,
        WorkspaceProjection workspace,
        PaneProjection pane,
        bool starting)
    {
        var phase = SessionPhase(device, session);
        var stale = _catalog.FreshnessFor(device.Device) == DeviceFreshness.Stale ||
                    phase == ConnectionPhase.Stale;
        var incompatible = phase == ConnectionPhase.Incompatible;
        var offline = phase == ConnectionPhase.Offline;
        var identity = NavigationIdentity.Format(
            NavigationKind.Pane, device.Device, session.Session, workspace.WorkspaceId, pane.Key);
        var selected = Matches(SelectionKind.Pane, device.Device, session.Session, workspace.WorkspaceId, pane.Key);
        var expired = selected && Selection.IsExpired;
        var status = StatusFor(
            starting, false, stale, incompatible, offline, _catalog.ErrorFor(device.Device),
            _catalog.ReadinessFor(device.Device));
        var agent = pane.AgentKind is null
            ? new WireEnum<AgentStatusKind>(pane.AgentStatus.Raw, AgentStatusKind.Unknown)
            : pane.AgentStatus;
        var access = _catalog.AccessFor(pane.Key);
        var verified = _catalog.ControlVerifiedFor(pane.Key);
        var label = string.IsNullOrEmpty(pane.Label) ? pane.Key.PaneId : pane.Label;
        return new NavigationItem(
            NavigationKind.Pane,
            device.Device,
            session.Session,
            workspace.WorkspaceId,
            pane.Key,
            _catalog.EpochFor(device.Device, session.Session),
            label,
            ShellStrings.Pane,
            phase,
            agent,
            _catalog.UnreadCount(pane.Key),
            access,
            verified,
            starting,
            false,
            status.Code == ShellCodes.Error,
            offline,
            stale,
            incompatible,
            incompatible,
            selected,
            identity == _hoverKey,
            identity == _focusKey,
            false,
            expired,
            expired
                ? new StatusPresentation(
                    ShellCodes.Expired, ShellStrings.Expired, "expired",
                    RecoveryActionKind.RemoveRecent, ShellStrings.RemoveRecent, true)
                : status,
            incompatible ? [ShellCodes.CapabilityUnverified] : [],
            NavigationIdentity.Format(
                NavigationKind.Workspace, device.Device, session.Session, workspace.WorkspaceId, null),
            []);
    }

    private ConnectionPhase SessionPhase(ProjectedDevice device, SessionProjection session)
    {
        _ = session;
        if (device.Capabilities.VerifiedOperations.Count == 0)
            return ConnectionPhase.Incompatible;
        return DevicePhase(device);
    }

    private ConnectionPhase DevicePhase(ProjectedDevice device) =>
        _catalog.PhaseFor(device.Device);

    private bool Matches(
        SelectionKind kind,
        DeviceId device,
        SessionKey? session,
        string? workspaceId,
        PaneKey? pane)
    {
        if (Selection.IsExpired)
            return Selection.Kind == kind &&
                   Selection.Device == device &&
                   Selection.Session == session &&
                   Selection.WorkspaceId == workspaceId &&
                   Selection.Pane == pane;
        return Selection.Kind == kind &&
               Selection.Device == device &&
               Selection.Session == session &&
               Selection.WorkspaceId == workspaceId &&
               Selection.Pane == pane;
    }

    private static SelectionState ToSelection(NavigationItem item) =>
        new(ToSelectionKind(item.Kind), item.Device, item.Session, item.WorkspaceId, item.Pane, item.Epoch, false);

    private static SelectionKind ToSelectionKind(NavigationKind kind) => kind switch
    {
        NavigationKind.Device => SelectionKind.Device,
        NavigationKind.Session => SelectionKind.Session,
        NavigationKind.Workspace => SelectionKind.Workspace,
        NavigationKind.Pane => SelectionKind.Pane,
        _ => SelectionKind.None
    };

    private static NavigationKind ToKind(SelectionKind kind) => kind switch
    {
        SelectionKind.Device => NavigationKind.Device,
        SelectionKind.Session => NavigationKind.Session,
        SelectionKind.Workspace => NavigationKind.Workspace,
        SelectionKind.Pane => NavigationKind.Pane,
        _ => NavigationKind.Device
    };

    private static StatusPresentation StatusFor(
        bool starting,
        bool empty,
        bool stale,
        bool incompatible,
        bool offline,
        string? error,
        PartitionReadiness? readiness = null)
    {
        if (starting)
            return new(ShellCodes.Loading, ShellStrings.Loading, "loading", RecoveryActionKind.None, "", false);
        if (readiness == PartitionReadiness.AuthRequired)
            return new(ShellCodes.AuthRequired, ShellStrings.AuthRequired, "auth",
                RecoveryActionKind.OpenSettings, ShellStrings.Settings, true);
        if (readiness == PartitionReadiness.PermissionDenied)
            return new(ShellCodes.PermissionDenied, ShellStrings.PermissionDenied, "permission",
                RecoveryActionKind.OpenDiagnostics, ShellStrings.Diagnostics, true);
        if (readiness == PartitionReadiness.Cancelling)
            return new(ShellCodes.Loading, ShellStrings.Loading, "cancelling", RecoveryActionKind.None, "", false);
        if (incompatible || readiness == PartitionReadiness.Incompatible)
            return new(ShellCodes.Incompatible, ShellStrings.Incompatible, "incompatible",
                RecoveryActionKind.OpenDiagnostics, ShellStrings.Diagnostics, true);
        if (readiness == PartitionReadiness.Error ||
            (!string.IsNullOrEmpty(error) && readiness is not PartitionReadiness.Offline
                and not PartitionReadiness.Stale and not PartitionReadiness.Loading))
            return new(ShellCodes.Error, ShellStrings.Error, "error",
                RecoveryActionKind.OpenDiagnostics, ShellStrings.Diagnostics, true, null);
        if (readiness == PartitionReadiness.Loading)
            return new(ShellCodes.Loading, ShellStrings.Loading, "loading", RecoveryActionKind.None, "", false);
        if (stale || readiness == PartitionReadiness.Stale)
            return new(ShellCodes.Stale, ShellStrings.Stale, "stale",
                RecoveryActionKind.RetryProjection, ShellStrings.Reconnect, true);
        if (offline || readiness == PartitionReadiness.Offline)
            return new(ShellCodes.Offline, ShellStrings.Offline, "offline",
                RecoveryActionKind.RetryProjection, ShellStrings.Reconnect, true);
        if (empty || readiness == PartitionReadiness.Empty)
            return new(ShellCodes.Empty, ShellStrings.Empty, "empty",
                RecoveryActionKind.AddDevice, ShellStrings.AddDevice, true);
        return new("ready", ShellStrings.Ready, "ready", RecoveryActionKind.None, "", false);
    }

    private static List<NavigationItem> Flatten(IReadOnlyList<NavigationItem> roots)
    {
        var list = new List<NavigationItem>();
        foreach (var root in roots)
            AppendVisible(root, list);
        return list;
    }

    private static void AppendVisible(NavigationItem item, List<NavigationItem> list)
    {
        list.Add(item);
        if (!item.IsExpanded)
            return;
        foreach (var child in item.Children)
            AppendVisible(child, list);
    }

    private static IEnumerable<NavigationItem> Walk(IReadOnlyList<NavigationItem> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in Walk(root.Children))
                yield return child;
        }
    }
}
