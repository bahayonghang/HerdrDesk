using HerdDesk.Contracts;

namespace HerdDesk.Core;

public enum GlobalEntityKind
{
    Device,
    Session,
    Workspace,
    Pane,
    Agent
}

public enum PartitionReadiness
{
    Empty,
    Loading,
    Ready,
    Offline,
    Stale,
    AuthRequired,
    PermissionDenied,
    Incompatible,
    Error,
    Cancelling
}

public enum AggregateReadiness
{
    Empty,
    Loading,
    PartialReady,
    Ready,
    Degraded
}

public enum ResolveStatus
{
    Resolved,
    Expired,
    Offline,
    Stale,
    Incompatible,
    AuthRequired,
    PermissionDenied
}

public enum AggregationWriteKind
{
    Event,
    Ack,
    Input,
    Activate
}

public static class AggregationCodes
{
    public const string InvalidIdentity = "invalid_identity";
    public const string StaleEpoch = "stale_epoch";
    public const string WrongDevice = "wrong_device";
    public const string Expired = "expired";
    public const string Offline = "offline";
    public const string Stale = "stale";
    public const string Incompatible = "incompatible";
    public const string AuthRequired = "auth_required";
    public const string PermissionDenied = "permission_denied";
    public const string Cancelling = "cancelling";
    public const string Error = "error";
    public const string Allowed = "allowed";
    public const string Cancelled = "cancelled";
    public const string AggregateFault = "aggregate_fault";
    public const string ReconnectProviderUnverified = "reconnect_provider_unverified";
}

public readonly record struct FreshnessStamp(ConnectionEpoch Epoch, long Generation);

public readonly record struct SessionEpochStamp(SessionKey Session, ConnectionEpoch Epoch);

public sealed record GlobalEntityRef(
    GlobalEntityKind Kind,
    DeviceId Device,
    SessionKey? Session,
    string? WorkspaceId,
    PaneKey? Pane,
    string? EntityId,
    FreshnessStamp Stamp)
{
    public bool SameTarget(GlobalEntityRef other) =>
        Kind == other.Kind &&
        Device == other.Device &&
        Session == other.Session &&
        WorkspaceId == other.WorkspaceId &&
        Pane == other.Pane &&
        EntityId == other.EntityId;
}

public sealed record DevicePartition(
    DeviceId Device,
    ConnectionEpoch Epoch,
    long Generation,
    ConnectionPhase Phase,
    DeviceFreshness Freshness,
    CapabilityProfile Capabilities,
    PartitionReadiness Readiness,
    string? ErrorCode,
    string DisplayLabel,
    int UserOrder,
    IReadOnlyList<SessionProjection> Sessions,
    IReadOnlyList<SessionEpochStamp> SessionEpochs)
{
    public ConnectionEpoch EpochFor(SessionKey session)
    {
        foreach (var item in SessionEpochs)
        {
            if (item.Session == session)
                return item.Epoch;
        }

        return default;
    }

    public DeviceProjectionSnapshot ToSnapshot() =>
        new(Epoch, Generation, Phase, [new ProjectedDevice(Device, Capabilities, Sessions)]);

    public IReadOnlyList<AttentionPartitionFeed> ToAttentionFeeds(DateTimeOffset observed)
    {
        if (Sessions.Count == 0)
        {
            return
            [
                new AttentionPartitionFeed(
                    ToSnapshot(),
                    new ProjectionStamp(Epoch, Generation, observed))
            ];
        }

        var feeds = new AttentionPartitionFeed[Sessions.Count];
        for (var i = 0; i < Sessions.Count; i++)
        {
            var session = Sessions[i];
            var epoch = EpochFor(session.Session);
            feeds[i] = new AttentionPartitionFeed(
                new DeviceProjectionSnapshot(
                    epoch, Generation, Phase, [new ProjectedDevice(Device, Capabilities, [session])]),
                new ProjectionStamp(epoch, Generation, observed));
        }

        return feeds;
    }
}

public sealed record SearchDocument(
    GlobalEntityRef Ref,
    string NormalizedLabel,
    GlobalEntityKind Kind,
    string Breadcrumb,
    string DisplayLabel,
    BusinessStateKind BusinessStatus,
    ConnectionPhase ConnectionStatus,
    int UserOrder,
    int RecentRank,
    bool IsRecent);

public sealed record AggregationRecent(
    GlobalEntityRef Ref,
    string Label,
    DateTimeOffset Utc);

public sealed record GlobalSearchQuery(
    string Text,
    DeviceId? ScopeDevice,
    long Generation,
    bool Cancelled = false);

public sealed record GlobalSearchResult(
    long Generation,
    IReadOnlyList<SearchDocument> Documents,
    bool Partial,
    bool Cancelled,
    AggregateReadiness Readiness,
    int TerminalProcessDelta)
{
    public static GlobalSearchResult Empty(long generation, AggregateReadiness readiness) =>
        new(generation, [], false, false, readiness, 0);
}

public sealed record AggregateView(
    IReadOnlyList<DevicePartition> Partitions,
    AggregateReadiness Readiness,
    int ReadyCount,
    int TotalCount,
    string? AggregateErrorCode,
    int TerminalProcessDelta,
    long IndexGeneration)
{
    public static AggregateView Empty { get; } =
        new([], AggregateReadiness.Empty, 0, 0, null, 0, 0);

    public IReadOnlyList<ProjectedDevice> ToProjectedDevices()
    {
        var devices = new ProjectedDevice[Partitions.Count];
        for (var i = 0; i < Partitions.Count; i++)
        {
            var partition = Partitions[i];
            devices[i] = new ProjectedDevice(partition.Device, partition.Capabilities, partition.Sessions);
        }

        return devices;
    }
}

public sealed record PartitionApplyResult(AggregateView? View, string? Code)
{
    public bool Succeeded => View is not null && Code is null;

    public static PartitionApplyResult Ok(AggregateView view) => new(view, null);

    public static PartitionApplyResult Fail(string code) => new(null, code);
}

public sealed record ResolveResult(
    ResolveStatus Status,
    GlobalEntityRef? Target,
    DevicePartition? Partition,
    string Code,
    string? DisplayLabel)
{
    public bool IsResolved => Status == ResolveStatus.Resolved;

    public static ResolveResult Expired(string? code = null) =>
        new(ResolveStatus.Expired, null, null, code ?? AggregationCodes.Expired, null);
}

public sealed record AggregationWriteIntent(
    AggregationWriteKind Kind,
    DeviceId Device,
    SessionKey? Session,
    PaneKey? Pane,
    ConnectionEpoch Epoch,
    GlobalEntityRef? Target);

public sealed record AggregationWriteResult(bool Allowed, string Code);
