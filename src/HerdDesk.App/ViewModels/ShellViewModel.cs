using System.Diagnostics;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Diagnostics;

namespace HerdDesk.App;

public sealed class ShellDependencies
{
    public required IDeviceProfileStore Profiles { get; init; }
    public required ProjectionCatalog Catalog { get; init; }
    public IClock Clock { get; init; } = new SystemClock();
    public IConfigurationOwnership Ownership { get; init; } = ConfigurationOwnership.Owner;
    public UiPreferenceStore? UiPreferences { get; init; }
    public RecentAccessStore Recents { get; init; } = new();
    public ITerminalDisplaySurface DisplaySurface { get; init; } = new NullDisplaySurface();
    public AppExitCoordinator Exit { get; init; } = new();
    public DiagnosticAliasProjector? Aliases { get; init; }
    public IReadOnlyList<UnavailableCapability> Unavailable { get; init; } = [];
    public AppActivationCoordinator? Activation { get; init; }
    public AttentionReducer? Attention { get; init; }
    public INotificationSink? NotificationSink { get; init; }
    public IDiagnosticSink? DiagnosticSink { get; init; }
    public TerminalInputViewModel? TerminalInput { get; init; }
    public TerminalControlViewModel? TerminalControl { get; init; }
    public ResourceCommandViewModel? ResourceCommands { get; init; }
    public GlobalProjectionStore? Aggregate { get; init; }
    public IReconnectRequestor? Reconnect { get; init; }
}

public sealed class ShellViewModel
{
    public const int NarrowWidth = 800;
    public const int WideWidth = 1200;
    public const double EmptyShellBudgetMs = 2500;

    private readonly ShellDependencies _deps;
    private readonly NavigationCoordinator _navigation;
    private SearchHit? _pendingFocus;
    private FocusRegion _focusBeforeSearch = FocusRegion.Title;
    private ConnectionEpoch _attentionEpoch;

    public ShellViewModel(ShellDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(dependencies.Profiles);
        ArgumentNullException.ThrowIfNull(dependencies.Catalog);
        _deps = dependencies;
        Catalog = dependencies.Catalog;
        _navigation = new NavigationCoordinator(dependencies.Catalog);
        Catalog.Aggregate = dependencies.Aggregate;
        Resolver = new GlobalTargetResolver(dependencies.Aggregate, Catalog);
        Search = new SearchPaletteViewModel(dependencies.Catalog, dependencies.Recents);
        Display = new TerminalDisplayCoordinator(dependencies.DisplaySurface);
        Settings = new SettingsViewModel(
            dependencies.Profiles, Display, dependencies.Ownership, dependencies.UiPreferences);
        Diagnostics = new DiagnosticsViewModel(dependencies.Catalog, dependencies.Unavailable, dependencies.Aliases);
        Notifications = new NotificationCenterViewModel(
            dependencies.Catalog,
            dependencies.Attention,
            dependencies.NotificationSink,
            dependencies.DiagnosticSink,
            dependencies.Clock,
            dependencies.Aliases,
            Resolver);
        MultiDevice = new MultiDeviceNavigationViewModel(
            Catalog, _navigation, dependencies.Reconnect);
        GlobalSearch = dependencies.Aggregate is { } store
            ? new GlobalSearchViewModel(store, dependencies.Recents, Resolver)
            : null;
        TerminalInput = dependencies.TerminalInput;
        TerminalControl = dependencies.TerminalControl;
        ResourceCommands = dependencies.ResourceCommands;
        Workbench = WorkbenchLayout.CreateProduct();
        FocusRestore = new TerminalFocusCoordinator();
        Exit = dependencies.Exit;
        FilesAvailability = new RouteAvailability(
            RouteAvailabilityKind.Disabled, ShellCodes.FilesProviderPending, ShellStrings.FilesPending);
        SettingsAvailability = new RouteAvailability(RouteAvailabilityKind.Enabled);
        DiagnosticsAvailability = new RouteAvailability(RouteAvailabilityKind.Enabled);
        AboutAvailability = new RouteAvailability(RouteAvailabilityKind.Enabled);
        Lifecycle = ShellLifecycle.Starting;
        ShellVisible = true;
        Route = ShellRoute.Welcome;
        CurrentFocus = new FocusToken("title");
        Layout = LayoutBreakpoint.Wide;
        UpdateChrome();
    }

    public ProjectionCatalog Catalog { get; }
    public GlobalTargetResolver Resolver { get; }
    public MultiDeviceNavigationViewModel MultiDevice { get; }
    public GlobalSearchViewModel? GlobalSearch { get; }
    public SearchPaletteViewModel Search { get; }
    public SettingsViewModel Settings { get; }
    public DiagnosticsViewModel Diagnostics { get; }
    public NotificationCenterViewModel Notifications { get; }
    public TerminalInputViewModel? TerminalInput { get; }
    public TerminalControlViewModel? TerminalControl { get; }
    public ResourceCommandViewModel? ResourceCommands { get; }
    public WorkbenchLayout Workbench { get; }
    public TerminalFocusCoordinator FocusRestore { get; }
    public TerminalDisplayCoordinator Display { get; }
    public AppExitCoordinator Exit { get; }
    public ShellLifecycle Lifecycle { get; private set; }
    public bool ShellVisible { get; private set; }
    public string? ErrorCategory { get; private set; }
    public bool CanRetry { get; private set; }
    public ShellRoute Route { get; private set; }
    public RouteAvailability SettingsAvailability { get; }
    public RouteAvailability DiagnosticsAvailability { get; }
    public RouteAvailability AboutAvailability { get; }
    public RouteAvailability FilesAvailability { get; }
    public RouteAvailability SshAvailability => Settings.SshAvailability;
    public DetailsPaneKind Details { get; private set; } = DetailsPaneKind.Collapsed;
    public LayoutBreakpoint Layout { get; private set; }
    public bool NavigationOverlayOpen { get; private set; }
    public FocusToken CurrentFocus { get; private set; }
    public FocusRegion FocusedRegion { get; private set; } = FocusRegion.Title;
    public int RetryCount { get; private set; }
    public double LastStartMs { get; private set; }
    public bool ActivationExpired { get; private set; }
    public int WindowCount => _deps.Activation?.WindowCount ?? 1;
    public int HiddenTerminalBridgeCount => Search.HiddenTerminalBridgeCount;
    public bool DaemonOnline => Catalog.DaemonAvailable && Lifecycle is not ShellLifecycle.NoDevices;
    public IReadOnlyList<NavigationItem> Tree => _navigation.Tree;
    public IReadOnlyList<NavigationItem> VisibleItems => _navigation.VisibleItems;
    public SelectionState Selection => _navigation.Selection;
    public bool ContentFocused => _navigation.ContentFocused;
    public string Breadcrumb { get; private set; } = ProductInfo.Name;
    public string TitleSummary { get; private set; } = ProductInfo.Name;
    public string ConnectionLabel { get; private set; } = ShellStrings.Offline;
    public string AgentLabel { get; private set; } = ShellStrings.Unknown;
    public string UnreadLabel { get; private set; } = ShellChrome.Unread(0);
    public string AccessLabel { get; private set; } = ShellStrings.Disconnected;
    public string StatusLine { get; private set; } = "";
    public ConnectionPhase ConnectionStatus { get; private set; } = ConnectionPhase.Offline;
    public WireEnum<AgentStatusKind> AgentStatus { get; private set; } =
        new("unknown", AgentStatusKind.Unknown);
    public int UnreadCount { get; private set; }
    public int NotificationUnread => Notifications.UnreadCount;
    public TerminalAccess Access { get; private set; } = TerminalAccess.Disconnected;
    public bool ControlVerified { get; private set; }
    public bool ShowsWorkbench => Route == ShellRoute.Pane && HasWorkbenchSelection;
    public string AddDeviceLabel => ShellStrings.AddDevice;
    public string DiagnosticsLabel => ShellStrings.Diagnostics;

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, Exit.LifetimeToken);
        var watch = Stopwatch.StartNew();
        Lifecycle = ShellLifecycle.Starting;
        ShellVisible = true;
        ErrorCategory = ShellCodes.Starting;
        _navigation.Rebuild(true);
        await Settings.LoadAsync(linked.Token).ConfigureAwait(false);
        if (Settings.ErrorCode is not null &&
            Settings.ErrorCode is not ConfigurationCodes.Missing)
        {
            Lifecycle = ShellLifecycle.Failed;
            ErrorCategory = Settings.ErrorCode;
            CanRetry = true;
        }
        else if (Settings.CommittedSnapshot.Devices.Count == 0 && Catalog.Snapshot.Devices.Count == 0)
        {
            Lifecycle = ShellLifecycle.NoDevices;
            ErrorCategory = ShellCodes.NoDevices;
            CanRetry = false;
        }
        else if (!Catalog.DaemonAvailable)
        {
            Lifecycle = ShellLifecycle.DaemonUnavailable;
            ErrorCategory = ShellCodes.DaemonUnavailable;
            CanRetry = true;
        }
        else
        {
            Lifecycle = ShellLifecycle.Ready;
            ErrorCategory = null;
            CanRetry = false;
        }

        RefreshFromCatalog();
        ResourceCommands?.HandleSelectionChanged(ResourceKeyFromSelection());
        watch.Stop();
        LastStartMs = watch.Elapsed.TotalMilliseconds;
    }

    public void RefreshFromCatalog()
    {
        SyncAttention();
        _navigation.Rebuild(Lifecycle == ShellLifecycle.Starting);
        MultiDevice.Rebuild();
        Search.Refresh();
        GlobalSearch?.UpdateQuery(GlobalSearch.Query);
        ResourceCommands?.Coordinator.NotifyProjectionAsync().AsTask().GetAwaiter().GetResult();
        ApplyPendingFocus();
        CompleteNotificationFocus();
        ProjectWorkbench();
        UpdateChrome();
    }

    public void Select(NavigationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _navigation.Select(item);
        ApplyWorkbenchRoute(item.Kind);

        if (item.Kind == NavigationKind.Pane && item.Pane is { } pane && !_navigation.Selection.IsExpired)
        {
            Workbench.ClearTabOverride();
            _deps.Recents.Record(new RecentEntry(
                item.Device, item.Session, item.WorkspaceId, pane, item.Epoch, _deps.Clock.UtcNow,
                item.Label));
            TryFocusPane(pane, item.Epoch);
            TerminalControl?.HandleSelectionChanged(pane);
        }

        ResourceCommands?.HandleSelectionChanged(ResourceKeyFromSelection());
        ProjectWorkbench();
        UpdateChrome();
    }

    public void SelectTab(string tabId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tabId);
        Workbench.SelectTab(tabId);
        UpdateChrome();
    }

    public bool MoveTree(TreeMove move)
    {
        var moved = _navigation.Move(move);
        ApplyWorkbenchRoute(ToNavigationKind(Selection.Kind));
        if (move == TreeMove.Activate && Selection.Kind == SelectionKind.Pane && Selection.Pane is { } pane)
            TryFocusPane(pane, Selection.Epoch);
        ProjectWorkbench();
        UpdateChrome();
        return moved;
    }

    public void ToggleExpanded(NavigationItem item)
    {
        _navigation.ToggleExpanded(item);
        UpdateChrome();
    }

    public void ExpandAll()
    {
        _navigation.ExpandAll();
        UpdateChrome();
    }

    public void SetHover(NavigationItem? item) => _navigation.SetHover(item);

    public void SetFocusVisible(NavigationItem? item) => _navigation.SetFocusVisible(item);

    public bool HandleAccelerator(ShellAccelerator accelerator)
    {
        if (Search.IsComposing || TerminalInput is { IsComposing: true })
            return false;
        if (accelerator == ShellAccelerator.OpenSearch)
        {
            OpenSearch();
            return true;
        }

        if (accelerator == ShellAccelerator.CloseSearch)
        {
            CloseSearch();
            return true;
        }

        return false;
    }

    public void OpenSearch()
    {
        if (Search.IsComposing || TerminalInput is { IsComposing: true })
            return;
        if (!Search.IsOpen)
        {
            Search.SavedFocus = CurrentFocus;
            _focusBeforeSearch = FocusedRegion;
        }

        Search.IsOpen = true;
        Search.Refresh();
        CurrentFocus = new FocusToken("search");
        FocusedRegion = FocusRegion.Search;
    }

    public void CloseSearch(bool restoreFocus = true)
    {
        Search.IsOpen = false;
        if (!restoreFocus)
            return;
        if (Search.SavedFocus is { } saved)
            CurrentFocus = saved;
        FocusedRegion = _focusBeforeSearch;
    }

    public bool ActivateSearchResult()
    {
        var hit = Search.Selected;
        if (hit is null)
            return false;
        var resolved = Resolver.Resolve(GlobalEntityMapping.FromSearchHit(hit));
        if (hit.IsExpired || resolved.Status == ResolveStatus.Expired ||
            !Resolve(hit, out var item) || item is null)
        {
            Search.MarkExpired(hit);
            ActivationExpired = true;
            _navigation.MarkExpired();
            CloseSearch();
            UpdateChrome();
            return false;
        }

        Select(item);
        CloseSearch(restoreFocus: false);
        if (resolved.IsResolved && hit.Pane is { } pane)
            TryFocusPane(pane, hit.Epoch);
        else
            _navigation.ContentFocused = false;
        UpdateChrome();
        return resolved.IsResolved;
    }

    public void RemoveRecent(RecentEntry entry)
    {
        _deps.Recents.Remove(entry);
        Search.Refresh();
    }

    public void OpenSettings()
    {
        Route = ShellRoute.Settings;
        CurrentFocus = new FocusToken("settings");
    }

    public void OpenDiagnostics()
    {
        Route = ShellRoute.Diagnostics;
        Diagnostics.Open();
        CurrentFocus = new FocusToken("diagnostics");
    }

    public void OpenAbout()
    {
        Route = ShellRoute.About;
        CurrentFocus = new FocusToken("about");
    }

    public void ReturnToWorkbench()
    {
        Route = HasWorkbenchSelection ? ShellRoute.Pane : ShellRoute.Welcome;
        CurrentFocus = new FocusToken("content");
        FocusedRegion = FocusRegion.Content;
        ProjectWorkbench();
        UpdateChrome();
    }

    public void OpenNotifications()
    {
        Route = ShellRoute.Notifications;
        Notifications.Open();
        CurrentFocus = new FocusToken("notifications");
        FocusedRegion = FocusRegion.Notifications;
    }

    public bool ActivateNotification(string token)
    {
        var result = Notifications.Activate(token);
        if (result.Expired)
        {
            ActivationExpired = true;
            _navigation.MarkExpired();
            UpdateChrome();
            return false;
        }

        if (result.Pending || !result.Succeeded || result.Target is null)
        {
            UpdateChrome();
            return false;
        }

        ReceiveActivation(ToIntent(result.Target));
        UpdateChrome();
        return !ActivationExpired;
    }

    public void RequestAddDevice()
    {
        OpenSettings();
        if (Settings.Draft.Device.Value == Guid.Empty)
            Settings.BeginNewDevice();
    }

    public void ToggleDetails()
    {
        Details = Details == DetailsPaneKind.Collapsed ? DetailsPaneKind.Info : DetailsPaneKind.Collapsed;
        UpdateChrome();
    }

    public void SetWidth(int width)
    {
        Layout = width < NarrowWidth
            ? LayoutBreakpoint.Narrow
            : width < WideWidth
                ? LayoutBreakpoint.Medium
                : LayoutBreakpoint.Wide;
        if (Layout != LayoutBreakpoint.Narrow)
            NavigationOverlayOpen = false;
        if (Layout != LayoutBreakpoint.Wide)
            Details = DetailsPaneKind.Collapsed;
        UpdateChrome();
    }

    public void ToggleNavigationOverlay()
    {
        if (Layout == LayoutBreakpoint.Narrow)
            NavigationOverlayOpen = !NavigationOverlayOpen;
    }

    public void CycleRegion()
    {
        FocusedRegion = FocusedRegion switch
        {
            FocusRegion.Title => FocusRegion.DeviceRail,
            FocusRegion.DeviceRail => FocusRegion.Tree,
            FocusRegion.Tree => FocusRegion.Content,
            FocusRegion.Content => FocusRegion.Details,
            _ => FocusRegion.Title
        };
        CurrentFocus = new FocusToken(FocusedRegion.ToString());
    }

    public void RetryProjection()
    {
        RetryCount++;
        RefreshFromCatalog();
    }

    public void ReceiveActivation(ActivationIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (!Exit.AcceptingActivation)
            return;
        ActivationExpired = false;
        if (intent.Kind == ActivationKind.Settings)
        {
            OpenSettings();
            return;
        }

        if (intent.Kind == ActivationKind.Diagnostics)
        {
            OpenDiagnostics();
            return;
        }

        if (intent.Kind == ActivationKind.NotificationTarget && intent.Pane is { } pane)
        {
            var hit = new SearchHit(
                SearchResultKind.Pane, pane.PaneId, ShellStrings.Pane, pane.Session.Device,
                pane.Session, pane.WorkspaceId, pane, intent.Epoch ?? Catalog.Snapshot.Epoch,
                null, false, false);
            if (!Resolve(hit, out var item) || item is null ||
                Resolver.Resolve(GlobalEntityMapping.FromSearchHit(hit)).Status == ResolveStatus.Expired)
            {
                ActivationExpired = true;
                _navigation.MarkExpired();
                UpdateChrome();
                return;
            }

            Select(item);
        }
    }

    public bool TryRequestResize(int columns, int rows)
    {
        if (Selection.IsExpired || Selection.Pane is not { } pane || Selection.Device is not { } device)
            return false;
        if (Selection.Epoch != Catalog.EpochFor(device, Selection.Session))
            return false;
        var context = new InputContext(
            pane, Selection.Epoch, Access, ControlVerified);
        var projected = Catalog.FindDevice(pane.Session.Device);
        var capabilities = projected?.Capabilities ??
                           new CapabilityProfile("", null, 0, 0, "", "UNVERIFIED",
                               System.Collections.Frozen.FrozenSet<string>.Empty);
        return Display.TryRequestResize(columns, rows, context, capabilities);
    }

    public ValueTask ExitAsync() => Exit.ExitAsync();

    private void SyncAttention()
    {
        AttentionSyncKind kind;
        if (Lifecycle == ShellLifecycle.Starting || _attentionEpoch.Value == 0)
            kind = AttentionSyncKind.Baseline;
        else if (Catalog.Aggregate is not null)
            kind = Catalog.Freshness == DeviceFreshness.Refreshing
                ? AttentionSyncKind.DirtyRefresh
                : AttentionSyncKind.Live;
        else if (Catalog.Snapshot.Epoch != _attentionEpoch)
            kind = AttentionSyncKind.ReconnectBaseline;
        else if (Catalog.Freshness == DeviceFreshness.Refreshing)
            kind = AttentionSyncKind.DirtyRefresh;
        else
            kind = AttentionSyncKind.Live;
        if (Catalog.Aggregate is null)
        {
            if (Catalog.Freshness == DeviceFreshness.Stale)
            {
                foreach (var device in Catalog.Snapshot.Devices)
                    Notifications.MarkDeviceStale(device.Device);
            }
            else if (Catalog.Snapshot.Phase == ConnectionPhase.Offline)
            {
                foreach (var device in Catalog.Snapshot.Devices)
                    Notifications.MarkDeviceOffline(device.Device);
            }
        }

        Notifications.SetGlobalMute(!Settings.CommittedUi.NotificationsEnabled);
        Notifications.Synchronize(kind, Lifecycle == ShellLifecycle.Starting);
        _attentionEpoch = Catalog.Aggregate is not null
            ? new ConnectionEpoch(1)
            : Catalog.Snapshot.Epoch;
    }

    private void CompleteNotificationFocus()
    {
        var pending = Notifications.CompletePending();
        if (pending.Cancelled || pending.Pending)
            return;
        if (pending.Expired)
        {
            ActivationExpired = true;
            _navigation.MarkExpired();
            return;
        }

        if (pending.Succeeded && pending.Target is { } target)
            ReceiveActivation(ToIntent(target));
    }

    private static ActivationIntent ToIntent(NotificationTarget target) =>
        new(
            ActivationKind.NotificationTarget,
            target.Key.Pane.Session.Device,
            target.Key.Pane.Session,
            target.Key.Pane.WorkspaceId,
            target.Key.Pane,
            target.Epoch);

    private void TryFocusPane(PaneKey pane, ConnectionEpoch epoch)
    {
        var valid = epoch == Catalog.EpochFor(pane.Session.Device, pane.Session) &&
                    Catalog.FindPane(pane) is not null;
        FocusRestore.SetControlVerified(Catalog.ControlVerifiedFor(pane));
        var result = FocusRestore.RequestFocus(pane, epoch, Catalog.RendererReadyFor(pane), valid);
        if (!valid)
        {
            ActivationExpired = true;
            _navigation.MarkExpired();
            _navigation.ContentFocused = false;
            return;
        }

        if (!result.Allowed)
        {
            _pendingFocus = new SearchHit(
                SearchResultKind.Pane, pane.PaneId, ShellStrings.Pane, pane.Session.Device,
                pane.Session, pane.WorkspaceId, pane, epoch, null, false, false);
            _navigation.ContentFocused = false;
            return;
        }

        _pendingFocus = null;
        _navigation.ContentFocused = true;
        CurrentFocus = new FocusToken("content");
        FocusedRegion = FocusRegion.Content;
    }

    private void ApplyPendingFocus()
    {
        if (_pendingFocus?.Pane is not { } pane)
            return;
        var valid = _pendingFocus.Epoch == Catalog.EpochFor(pane.Session.Device, pane.Session) &&
                    Catalog.FindPane(pane) is not null;
        if (!valid)
        {
            ActivationExpired = true;
            _pendingFocus = null;
            _navigation.MarkExpired();
            FocusRestore.Reset();
            return;
        }

        if (!Catalog.RendererReadyFor(pane))
            return;
        var result = FocusRestore.NotifyRendererReady(pane, _pendingFocus.Epoch, true, true);
        if (!result.Allowed)
            return;
        _navigation.ContentFocused = true;
        CurrentFocus = new FocusToken("content");
        FocusedRegion = FocusRegion.Content;
        _pendingFocus = null;
    }

    private bool Resolve(SearchHit hit, out NavigationItem? item)
    {
        item = null;
        if (hit.Kind != SearchResultKind.Recent &&
            hit.Epoch != Catalog.EpochFor(hit.Device, hit.Session))
            return false;
        if (hit.Pane is { } pane)
        {
            if (Catalog.FindPane(pane) is null ||
                hit.Epoch != Catalog.EpochFor(pane.Session.Device, pane.Session))
                return false;
            var identity = NavigationIdentity.Format(
                NavigationKind.Pane, pane.Session.Device, pane.Session, pane.WorkspaceId, pane);
            _navigation.ExpandAll();
            item = _navigation.Find(identity);
            return item is not null;
        }

        if (hit.Session is { } session && hit.WorkspaceId is not null)
        {
            var identity = NavigationIdentity.Format(
                NavigationKind.Workspace, session.Device, session, hit.WorkspaceId, null);
            _navigation.ExpandAll();
            item = _navigation.Find(identity);
            return item is not null;
        }

        if (hit.Session is { } sessionKey)
        {
            var identity = NavigationIdentity.Format(
                NavigationKind.Session, sessionKey.Device, sessionKey, null, null);
            _navigation.ExpandAll();
            item = _navigation.Find(identity);
            return item is not null;
        }

        var deviceIdentity = NavigationIdentity.Format(
            NavigationKind.Device, hit.Device, null, null, null);
        item = _navigation.Find(deviceIdentity);
        return item is not null;
    }

    private void UpdateChrome()
    {
        var selected = _navigation.SelectedItem;
        if (selected is not null)
        {
            ConnectionStatus = selected.Connection;
            AgentStatus = selected.AgentStatus;
            UnreadCount = selected.UnreadCount;
            Access = selected.Access;
            ControlVerified = selected.Pane is not null && selected.ControlVerified;
            Breadcrumb = selected.Kind switch
            {
                NavigationKind.Pane => string.Join(" / ",
                [
                    selected.Device.Value.ToString("D")[..8],
                    selected.Session?.SessionName ?? selected.Session?.EndpointKey ?? "",
                    selected.WorkspaceId ?? "",
                    selected.Label
                ]),
                _ => selected.Label
            };
            TitleSummary = selected.Label;
        }
        else
        {
            Breadcrumb = ProductInfo.Name;
            TitleSummary = ShellChrome.Lifecycle(Lifecycle);
            ConnectionStatus = Catalog.Snapshot.Phase;
            AgentStatus = new WireEnum<AgentStatusKind>("unknown", AgentStatusKind.Unknown);
            UnreadCount = 0;
            Access = TerminalAccess.Disconnected;
            ControlVerified = false;
        }

        ConnectionLabel = ShellChrome.Connection(ConnectionStatus);
        AgentLabel = ShellChrome.Agent(AgentStatus.Known);
        UnreadLabel = ShellChrome.Unread(UnreadCount);
        AccessLabel = ShellChrome.Access(Access);
        StatusLine = ShellChrome.StatusLine(ConnectionStatus, AgentStatus.Known, UnreadCount, Access);
    }

    private void ApplyWorkbenchRoute(NavigationKind kind)
    {
        if (Route is ShellRoute.Settings or ShellRoute.Diagnostics or ShellRoute.About
            or ShellRoute.Notifications)
            return;
        Route = kind is NavigationKind.Pane or NavigationKind.Workspace
            ? ShellRoute.Pane
            : ShellRoute.Welcome;
    }

    private static NavigationKind ToNavigationKind(SelectionKind kind) => kind switch
    {
        SelectionKind.Device => NavigationKind.Device,
        SelectionKind.Session => NavigationKind.Session,
        SelectionKind.Workspace => NavigationKind.Workspace,
        SelectionKind.Pane => NavigationKind.Pane,
        _ => NavigationKind.Device
    };

    private void ProjectWorkbench()
    {
        if (!HasWorkbenchSelection || Selection.Session is not { } session)
        {
            Workbench.Clear();
            return;
        }

        Workbench.Project(
            Catalog.FindSession(session),
            Selection.WorkspaceId,
            Selection.Pane,
            Catalog.RendererReadyFor);
    }

    private bool HasWorkbenchSelection =>
        !Selection.IsExpired &&
        Selection.Session is not null &&
        !string.IsNullOrWhiteSpace(Selection.WorkspaceId);

    private ResourceKey? ResourceKeyFromSelection()
    {
        var selection = _navigation.Selection;
        if (selection.Session is not { } session)
            return null;
        if (selection.Pane is { } pane)
        {
            var projected = Catalog.FindPane(pane);
            return new ResourceKey(
                session,
                projected?.AgentKind is null ? ResourceKind.Pane : ResourceKind.Agent,
                pane.WorkspaceId,
                projected?.TabId,
                pane.PaneId,
                projected?.TerminalId);
        }

        if (!string.IsNullOrWhiteSpace(selection.WorkspaceId))
            return new ResourceKey(session, ResourceKind.Workspace, selection.WorkspaceId);
        return new ResourceKey(session, ResourceKind.Workspace, "");
    }
}
