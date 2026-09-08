using System.Globalization;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Diagnostics;

namespace HerdDesk.App;

public enum NotificationCenterLifecycle
{
    Loading,
    Ready,
    Empty,
    Error,
    Offline,
    Muted
}

public enum NotificationFilter
{
    All,
    Blocked,
    Done,
    Unread
}

public sealed record NotificationActivationResult(
    bool Succeeded,
    bool Expired,
    bool Cancelled,
    bool Pending,
    NotificationTarget? Target,
    string? Code,
    string? Token);

public sealed record NotificationListItem(
    string TransitionId,
    AttentionKey Key,
    NotificationTarget Target,
    BusinessState From,
    BusinessState To,
    AttentionFreshness Freshness,
    bool IsRead,
    bool IsExpired,
    bool IsSelected,
    bool IsHovered,
    bool IsFocusVisible,
    string Headline,
    string Body,
    string Breadcrumb,
    string RelativeTime,
    DateTimeOffset ObservedAt,
    string Reason,
    string AutomationName);

public sealed class NotificationCenterViewModel
{
    private readonly ProjectionCatalog _catalog;
    private readonly AttentionReducer _reducer;
    private readonly INotificationSink _sink;
    private readonly IDiagnosticSink? _diagnostics;
    private readonly IClock _clock;
    private readonly DiagnosticAliasProjector? _aliases;
    private readonly GlobalTargetResolver _resolver;
    private readonly List<NotificationListItem> _items = [];
    private string? _hoverId;
    private string? _focusId;
    private string? _selectedId;
    private int _activationSerial;
    private PendingActivation? _pending;
    private bool _opened;

    public NotificationCenterViewModel(
        ProjectionCatalog catalog,
        AttentionReducer? reducer = null,
        INotificationSink? sink = null,
        IDiagnosticSink? diagnostics = null,
        IClock? clock = null,
        DiagnosticAliasProjector? aliases = null,
        GlobalTargetResolver? resolver = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        _reducer = reducer ?? new AttentionReducer();
        _sink = sink ?? new WindowsNotificationSink();
        _diagnostics = diagnostics;
        _clock = clock ?? new SystemClock();
        _aliases = aliases;
        _resolver = resolver ?? new GlobalTargetResolver(catalog.Aggregate, catalog);
        Lifecycle = NotificationCenterLifecycle.Loading;
    }

    public GlobalTargetResolver Resolver => _resolver;

    public AttentionReducer Reducer => _reducer;
    public NotificationCenterLifecycle Lifecycle { get; private set; }
    public NotificationFilter Filter { get; private set; } = NotificationFilter.All;
    public IReadOnlyList<NotificationListItem> Items => _items;
    public IReadOnlyList<NotificationListItem> VisibleItems { get; private set; } = [];
    public NotificationListItem? Selected =>
        _selectedId is null ? null : _items.FirstOrDefault(item => item.TransitionId == _selectedId);
    public int UnreadCount => _reducer.UnreadTotal;
    public bool IsMuted { get; private set; }
    public bool IsEmpty => VisibleItems.Count == 0;
    public string BannerCode { get; private set; } = ShellCodes.Loading;
    public string BannerText { get; private set; } = ShellStrings.Loading;
    public string AutomationName { get; private set; } = ShellStrings.Notifications;
    public string LiveRegionText { get; private set; } = "";
    public string? LastDeliveryCode { get; private set; }
    public int LastDeliverAttempts { get; private set; }
    public int LastCenterOnlyCount { get; private set; }

    public void Open()
    {
        _opened = true;
        Rebuild();
    }

    public void SetFilter(NotificationFilter filter)
    {
        Filter = filter;
        Rebuild();
    }

    public void SetGlobalMute(bool muted)
    {
        IsMuted = muted;
        _reducer.SetGlobalMute(muted);
    }

    public void SetMuteScope(AttentionScope scope, bool muted)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.Kind == AttentionScopeKind.Device && scope.Device is { } device)
            _reducer.SetDeviceMute(device, muted);
        else if (scope.Kind == AttentionScopeKind.Session && scope.Session is { } session)
            _reducer.SetSessionMute(session, muted);
        else if (scope.Kind == AttentionScopeKind.All)
            SetGlobalMute(muted);
        Rebuild();
    }

    public void MarkDeviceStale(DeviceId device) => _reducer.MarkDeviceStale(device);

    public void MarkDeviceOffline(DeviceId device) => _reducer.MarkDeviceOffline(device);

    public AttentionApplyResult Synchronize(AttentionSyncKind kind, bool starting = false)
    {
        if (starting)
            Lifecycle = NotificationCenterLifecycle.Loading;
        AttentionApplyResult result;
        if (_catalog.Aggregate is { } store)
        {
            var feeds = new List<AttentionPartitionFeed>();
            foreach (var partition in store.Read().Partitions)
            {
                if (partition.Freshness == DeviceFreshness.Stale)
                    _reducer.MarkDeviceStale(partition.Device);
                else if (partition.Phase == ConnectionPhase.Offline)
                    _reducer.MarkDeviceOffline(partition.Device);
                feeds.AddRange(partition.ToAttentionFeeds(_clock.UtcNow));
            }

            result = _reducer.ApplyAggregate(feeds, kind);
        }
        else
        {
            var stamp = new ProjectionStamp(_catalog.Snapshot.Epoch, 0, _clock.UtcNow);
            result = _reducer.Apply(_catalog.Snapshot, stamp, kind);
        }

        PushUnread();
        Deliver(result);
        Diagnose(result);
        Rebuild();
        Announce(result, kind);
        return result;
    }

    public void MarkRead(string transitionId)
    {
        _reducer.MarkRead(transitionId);
        PushUnread();
        Rebuild();
    }

    public void MarkScopeRead(AttentionScope scope)
    {
        _reducer.MarkScopeRead(scope);
        PushUnread();
        Rebuild();
    }

    public void ClearExpired()
    {
        _reducer.ClearExpired();
        PushUnread();
        Rebuild();
    }

    public void Select(NotificationListItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _selectedId = item.TransitionId;
        _focusId = item.TransitionId;
        Rebuild();
    }

    public void SetHover(NotificationListItem? item)
    {
        _hoverId = item?.TransitionId;
        Rebuild();
    }

    public void SetFocusVisible(NotificationListItem? item)
    {
        _focusId = item?.TransitionId;
        Rebuild();
    }

    public bool Move(TreeMove move)
    {
        if (VisibleItems.Count == 0)
            return false;
        var index = SelectedIndex();
        switch (move)
        {
            case TreeMove.Down:
                if (index < VisibleItems.Count - 1)
                    Select(VisibleItems[index + 1]);
                else if (index < 0)
                    Select(VisibleItems[0]);
                return true;
            case TreeMove.Up:
                if (index > 0)
                    Select(VisibleItems[index - 1]);
                else if (index < 0)
                    Select(VisibleItems[0]);
                return true;
            case TreeMove.Activate:
                if (index >= 0)
                    Select(VisibleItems[index]);
                return true;
            default:
                return false;
        }
    }

    public NotificationActivationResult Activate(string token)
    {
        CancelPending();
        var generation = ++_activationSerial;
        if (string.IsNullOrWhiteSpace(token))
            return Fail(AttentionCodes.TargetExpired, expired: true);
        var entry = _reducer.Find(token);
        if (entry is null || entry.IsExpired || entry.Target.RouteVersion != NotificationTarget.CurrentRouteVersion)
        {
            if (entry is not null)
                _reducer.MarkExpired(token);
            Rebuild();
            return Fail(AttentionCodes.TargetExpired, expired: true, token: token);
        }

        var target = entry.Target;
        var resolved = _resolver.ResolveNotification(target);
        if (resolved.Status == ResolveStatus.Expired || _catalog.FindPane(target.Key.Pane) is null)
        {
            _reducer.MarkExpired(token);
            Rebuild();
            return Fail(AttentionCodes.TargetExpired, expired: true, token: token, target: target);
        }

        if (!_catalog.RendererReadyFor(target.Key.Pane))
        {
            _pending = new PendingActivation(generation, token, target);
            return new(false, false, false, true, target, null, token);
        }

        _reducer.MarkRead(token);
        PushUnread();
        Rebuild();
        return new(true, false, false, false, target, null, token);
    }

    public NotificationActivationResult CompletePending()
    {
        if (_pending is null)
            return new(false, false, true, false, null, AttentionCodes.Cancelled, null);
        var pending = _pending;
        if (pending.Generation != _activationSerial)
        {
            _pending = null;
            return new(false, false, true, false, pending.Target, AttentionCodes.Cancelled, pending.Token);
        }

        var pendingResolved = _resolver.ResolveNotification(pending.Target);
        if (pendingResolved.Status == ResolveStatus.Expired ||
            _catalog.FindPane(pending.Target.Key.Pane) is null)
        {
            _reducer.MarkExpired(pending.Token);
            _pending = null;
            Rebuild();
            return Fail(AttentionCodes.TargetExpired, expired: true, token: pending.Token, target: pending.Target);
        }

        if (!_catalog.RendererReadyFor(pending.Target.Key.Pane))
            return new(false, false, false, true, pending.Target, null, pending.Token);

        _pending = null;
        _reducer.MarkRead(pending.Token);
        PushUnread();
        Rebuild();
        return new(true, false, false, false, pending.Target, null, pending.Token);
    }

    public void CancelPending()
    {
        _activationSerial++;
        _pending = null;
    }

    private void PushUnread()
    {
        foreach (var device in _catalog.DevicesForTree())
        {
            foreach (var session in device.Sessions)
            {
                foreach (var pane in session.Panes)
                    _catalog.SetUnread(pane.Key, _reducer.UnreadForPane(pane.Key));
            }
        }
    }

    private void Deliver(AttentionApplyResult result)
    {
        LastDeliverAttempts = 0;
        LastCenterOnlyCount = result.Decisions.Count(item => item.Action == NotificationAction.CenterOnly);
        LastDeliveryCode = null;
        foreach (var decision in result.Decisions)
        {
            if (decision.Action != NotificationAction.Deliver)
                continue;
            LastDeliverAttempts++;
            var delivery = _sink.TryDeliver(decision.Transition, decision.Transition.Target);
            LastDeliveryCode = delivery.Code;
            var outcome = delivery.Kind == NotificationDeliveryKind.Delivered
                ? DiagnosticOutcome.Success
                : DiagnosticOutcome.Failure;
            DiagnoseOne(decision, delivery.Code ?? AttentionCodes.WindowsToastUnverified, outcome);
        }
    }

    private void Diagnose(AttentionApplyResult result)
    {
        if (_diagnostics is null)
            return;
        foreach (var decision in result.Decisions)
        {
            if (decision.Action == NotificationAction.Deliver)
                continue;
            DiagnoseOne(decision, decision.Reason, Outcome(decision.Action));
        }
    }

    private void DiagnoseOne(NotificationDecision decision, string? code, DiagnosticOutcome outcome)
    {
        if (_diagnostics is null || decision.Transition.Stamp.Epoch.Value <= 0)
            return;
        var operation = Operation(decision);
        var device = decision.Transition.Key.Pane.Session.Device.Value.ToString("N");
        var sessionRaw = decision.Transition.Key.Pane.Session.EndpointKey + ":" +
                         (decision.Transition.Key.Pane.Session.SessionName ?? "");
        _diagnostics.TryWrite(new DiagnosticEvent(
            _clock.UtcNow.ToUniversalTime(),
            "attention",
            operation,
            outcome,
            code == AttentionCodes.Deliver ? null : TruncateCode(code),
            decision.Transition.Stamp.Epoch.Value,
            null,
            null,
            Alias("device", device),
            Alias("session", sessionRaw)));
    }

    private void Announce(AttentionApplyResult result, AttentionSyncKind kind)
    {
        if (kind is AttentionSyncKind.Baseline or AttentionSyncKind.ReconnectBaseline)
        {
            LiveRegionText = "";
            return;
        }

        var high = result.Decisions.FirstOrDefault(item =>
            item.Action is NotificationAction.Deliver or NotificationAction.CenterOnly &&
            NotificationPolicy.IsHighValue(item.Transition.To) &&
            item.Reason is not (AttentionCodes.Muted or AttentionCodes.RateLimited));
        LiveRegionText = high is null ? "" : HeadlineFor(high.Transition.To);
    }

    private void Rebuild()
    {
        _items.Clear();
        foreach (var entry in _reducer.Entries)
            _items.Add(ToItem(entry));
        VisibleItems = _items.Where(MatchesFilter).ToArray();
        UpdateLifecycle();
        AutomationName = ShellStrings.Notifications + " " +
                         UnreadCount.ToString(CultureInfo.InvariantCulture);
    }

    private bool MatchesFilter(NotificationListItem item) =>
        Filter switch
        {
            NotificationFilter.Blocked => item.To.Kind == BusinessStateKind.Blocked,
            NotificationFilter.Done => item.To.Kind == BusinessStateKind.Done,
            NotificationFilter.Unread => !item.IsRead,
            _ => true
        };

    private void UpdateLifecycle()
    {
        if (Lifecycle == NotificationCenterLifecycle.Loading && !_opened && _items.Count == 0 &&
            _catalog.Snapshot.Devices.Count == 0)
        {
            BannerCode = ShellCodes.Loading;
            BannerText = ShellStrings.Loading;
            return;
        }

        if (_catalog.Aggregate is { } store)
        {
            var view = store.Read();
            if (view.Readiness == AggregateReadiness.Empty)
            {
                Lifecycle = NotificationCenterLifecycle.Empty;
                BannerCode = ShellCodes.Empty;
                BannerText = ShellStrings.Empty;
                return;
            }

            if (view.ReadyCount == 0 &&
                view.Partitions.All(item => item.Readiness == PartitionReadiness.Error))
            {
                Lifecycle = NotificationCenterLifecycle.Error;
                BannerCode = ShellCodes.Error;
                BannerText = ShellStrings.Error;
                return;
            }

            if (view.ReadyCount == 0 &&
                view.Partitions.All(item => item.Phase == ConnectionPhase.Offline))
            {
                Lifecycle = NotificationCenterLifecycle.Offline;
                BannerCode = ShellCodes.Offline;
                BannerText = ShellStrings.Offline;
                return;
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(_catalog.LastErrorCode))
            {
                Lifecycle = NotificationCenterLifecycle.Error;
                BannerCode = ShellCodes.Error;
                BannerText = ShellStrings.Error;
                return;
            }

            if (_catalog.Snapshot.Phase == ConnectionPhase.Offline)
            {
                Lifecycle = NotificationCenterLifecycle.Offline;
                BannerCode = ShellCodes.Offline;
                BannerText = ShellStrings.Offline;
                return;
            }
        }

        if (IsMuted)
        {
            Lifecycle = NotificationCenterLifecycle.Muted;
            BannerCode = AttentionCodes.Muted;
            BannerText = ShellStrings.Muted;
        }
        else if (VisibleItems.Count == 0)
        {
            Lifecycle = NotificationCenterLifecycle.Empty;
            BannerCode = ShellCodes.Empty;
            BannerText = ShellStrings.Empty;
        }
        else
        {
            Lifecycle = NotificationCenterLifecycle.Ready;
            BannerCode = "ready";
            BannerText = ShellStrings.Ready;
        }
    }

    private NotificationListItem ToItem(AttentionEntry entry)
    {
        var headline = HeadlineFor(entry.To);
        var body = BodyFor(entry);
        var breadcrumb = string.Join(" / ",
        [
            entry.Key.Pane.Session.Device.Value.ToString("D")[..8],
            entry.Key.Pane.Session.SessionName ?? entry.Key.Pane.Session.EndpointKey,
            entry.Key.Pane.WorkspaceId,
            entry.Key.Pane.PaneId
        ]);
        var relative = FormatRelative(entry.Stamp.ObservedAt);
        return new(
            entry.TransitionId,
            entry.Key,
            entry.Target,
            entry.From,
            entry.To,
            entry.Freshness,
            entry.IsRead,
            entry.IsExpired,
            entry.TransitionId == _selectedId,
            entry.TransitionId == _hoverId,
            entry.TransitionId == _focusId,
            headline,
            body,
            breadcrumb,
            relative,
            entry.Stamp.ObservedAt,
            entry.Reason,
            headline + " " + breadcrumb);
    }

    private static string HeadlineFor(BusinessState state) =>
        state.Kind switch
        {
            BusinessStateKind.Blocked => ShellStrings.NotificationBlocked,
            BusinessStateKind.Done => ShellStrings.NotificationDone,
            BusinessStateKind.Working => ShellStrings.NotificationWorking,
            BusinessStateKind.Idle => ShellStrings.NotificationIdle,
            _ => ShellStrings.Unknown
        };

    private static string BodyFor(AttentionEntry entry)
    {
        if (entry.IsExpired)
            return ShellStrings.Expired;
        if (entry.Freshness is AttentionFreshness.Stale or AttentionFreshness.Offline)
            return ShellStrings.NotificationStale;
        if (entry.To.Kind == BusinessStateKind.Done)
            return ShellStrings.NotificationDoneBody;
        if (entry.To.Kind == BusinessStateKind.Unknown)
            return ShellStrings.Unknown;
        return HeadlineFor(entry.To);
    }

    private string FormatRelative(DateTimeOffset observed)
    {
        var seconds = Math.Max(0, (int)(_clock.UtcNow - observed).TotalSeconds);
        return seconds.ToString(CultureInfo.InvariantCulture) + "s";
    }

    private int SelectedIndex()
    {
        if (_selectedId is null)
            return -1;
        for (var i = 0; i < VisibleItems.Count; i++)
        {
            if (VisibleItems[i].TransitionId == _selectedId)
                return i;
        }

        return -1;
    }

    private static string Operation(NotificationDecision decision)
    {
        var to = decision.Transition.To.Kind.ToString().ToLowerInvariant();
        var prefix = decision.Action switch
        {
            NotificationAction.Deliver => "deliver",
            NotificationAction.CenterOnly => "center",
            _ => "suppress"
        };
        var text = prefix + "." + to;
        return text.Length <= DiagnosticEvent.MaxTokenLength ? text : text[..DiagnosticEvent.MaxTokenLength];
    }

    private static DiagnosticOutcome Outcome(NotificationAction action) =>
        action switch
        {
            NotificationAction.Deliver => DiagnosticOutcome.Success,
            NotificationAction.CenterOnly => DiagnosticOutcome.Success,
            _ => DiagnosticOutcome.Dropped
        };

    private static string? TruncateCode(string? code)
    {
        if (string.IsNullOrEmpty(code))
            return null;
        return code.Length <= DiagnosticEvent.MaxTokenLength ? code : code[..DiagnosticEvent.MaxTokenLength];
    }

    private string? Alias(string kind, string raw)
    {
        if (_aliases is null)
            return null;
        return _aliases.Alias(kind, raw);
    }

    private static NotificationActivationResult Fail(
        string code,
        bool expired,
        string? token = null,
        NotificationTarget? target = null) =>
        new(false, expired, false, false, target, code, token);

    private sealed record PendingActivation(int Generation, string Token, NotificationTarget Target);
}
