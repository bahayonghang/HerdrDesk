using HerdDesk.Contracts;

namespace HerdDesk.Core;

public enum BusinessStateKind
{
    Working,
    Blocked,
    Done,
    Idle,
    Unknown
}

public enum AttentionSyncKind
{
    Baseline,
    ReconnectBaseline,
    Live,
    DirtyRefresh,
    SnapshotRepeat
}

public enum NotificationAction
{
    Deliver,
    CenterOnly,
    Suppress
}

public enum AttentionFreshness
{
    Current,
    Stale,
    Offline,
    Expired
}

public enum AttentionScopeKind
{
    Item,
    Key,
    Pane,
    Session,
    Device,
    All
}

public readonly record struct BusinessState(BusinessStateKind Kind, string Raw)
{
    public static BusinessState From(WireEnum<AgentStatusKind> status)
    {
        var raw = string.IsNullOrEmpty(status.Raw) ? "unknown" : status.Raw;
        if (status.Known is null or AgentStatusKind.Unknown)
            return new(BusinessStateKind.Unknown, raw);
        return status.Known.Value switch
        {
            AgentStatusKind.Working => new(BusinessStateKind.Working, raw),
            AgentStatusKind.Blocked => new(BusinessStateKind.Blocked, raw),
            AgentStatusKind.Done => new(BusinessStateKind.Done, raw),
            AgentStatusKind.Idle => new(BusinessStateKind.Idle, raw),
            _ => new(BusinessStateKind.Unknown, raw)
        };
    }

    public static BusinessState Unknown(string raw) =>
        new(BusinessStateKind.Unknown, string.IsNullOrEmpty(raw) ? "unknown" : raw);
}

public readonly record struct AttentionKey(PaneKey Pane, string EntityKind, string EntityId)
{
    public const string AgentEntityKind = "agent";

    public bool IsValid =>
        Pane.Session.Device.Value != Guid.Empty &&
        !string.IsNullOrWhiteSpace(Pane.Session.EndpointKey) &&
        !string.IsNullOrWhiteSpace(Pane.WorkspaceId) &&
        !string.IsNullOrWhiteSpace(Pane.PaneId) &&
        !string.IsNullOrWhiteSpace(EntityKind) &&
        !string.IsNullOrWhiteSpace(EntityId);
}

public readonly record struct ProjectionStamp(
    ConnectionEpoch Epoch,
    long BaselineGeneration,
    DateTimeOffset ObservedAt);

public sealed record NotificationTarget(
    AttentionKey Key,
    ConnectionEpoch Epoch,
    int RouteVersion)
{
    public const int CurrentRouteVersion = 1;
}

public sealed record AttentionTransition(
    AttentionKey Key,
    BusinessState From,
    BusinessState To,
    ProjectionStamp Stamp,
    string TransitionId,
    bool IsBaseline,
    AttentionFreshness Freshness,
    NotificationTarget Target);

public sealed record NotificationDecision(
    NotificationAction Action,
    string Reason,
    AttentionTransition Transition);

public sealed record AttentionEntry(
    string TransitionId,
    AttentionKey Key,
    NotificationTarget Target,
    BusinessState From,
    BusinessState To,
    ProjectionStamp Stamp,
    AttentionFreshness Freshness,
    bool IsRead,
    bool IsExpired,
    string Reason,
    NotificationAction Action);

public sealed record AttentionScope(
    AttentionScopeKind Kind,
    DeviceId? Device = null,
    SessionKey? Session = null,
    PaneKey? Pane = null,
    AttentionKey? Key = null,
    string? TransitionId = null);

public sealed record AttentionPartitionFeed(
    DeviceProjectionSnapshot Snapshot,
    ProjectionStamp Stamp);

public sealed record AttentionApplyResult(
    IReadOnlyList<NotificationDecision> Decisions,
    IReadOnlyList<AttentionEntry> Entries)
{
    public static AttentionApplyResult Empty { get; } = new([], []);

    public int DeliverCount => Decisions.Count(item => item.Action == NotificationAction.Deliver);
}

public static class AttentionCodes
{
    public const string Deliver = "deliver";
    public const string CenterOnly = "center_only";
    public const string Baseline = "baseline";
    public const string Reconnect = "reconnect";
    public const string Duplicate = "duplicate";
    public const string Stale = "stale";
    public const string Offline = "offline";
    public const string Muted = "muted";
    public const string RateLimited = "rate_limited";
    public const string OldEpoch = "old_epoch";
    public const string NotNotifiable = "not_notifiable";
    public const string InvalidIdentity = "invalid_identity";
    public const string Expired = "expired";
    public const string Cancelled = "cancelled";
    public const string TargetExpired = "target_expired";
    public const string WindowsToastUnverified = "windows_toast_unverified";
}

public static class AttentionTransitionId
{
    public static string Format(
        AttentionKey key,
        BusinessState from,
        BusinessState to,
        ConnectionEpoch epoch,
        int ordinal)
    {
        return string.Join('\u001f',
        [
            key.Pane.Session.Device.Value.ToString("D"),
            key.Pane.Session.EndpointKey,
            key.Pane.Session.SessionName ?? "",
            key.Pane.WorkspaceId,
            key.Pane.PaneId,
            key.EntityKind,
            key.EntityId,
            from.Kind.ToString().ToLowerInvariant(),
            to.Kind.ToString().ToLowerInvariant(),
            epoch.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)
        ]);
    }
}
