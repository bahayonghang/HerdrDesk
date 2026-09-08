namespace HerdDesk.Core;

public readonly record struct AttentionPolicyInput(
    AttentionSyncKind Sync,
    bool IsBaseline,
    bool Duplicate,
    bool OldEpoch,
    bool Stale,
    bool Offline,
    bool Muted,
    bool RateLimited,
    BusinessState To);

public readonly record struct AttentionPolicyResult(NotificationAction Action, string Reason);

public sealed class NotificationPolicy
{
    public int MaxDeliversPerSession { get; init; } = 8;
    public TimeSpan RateLimitWindow { get; init; } = TimeSpan.FromSeconds(10);

    public static NotificationPolicy Default { get; } = new();

    public AttentionPolicyResult Evaluate(in AttentionPolicyInput input)
    {
        if (input.OldEpoch)
            return new(NotificationAction.Suppress, AttentionCodes.OldEpoch);
        if (input.Sync is AttentionSyncKind.ReconnectBaseline)
            return new(NotificationAction.Suppress, AttentionCodes.Reconnect);
        if (input.IsBaseline || input.Sync is AttentionSyncKind.Baseline)
            return new(NotificationAction.Suppress, AttentionCodes.Baseline);
        if (input.Duplicate)
            return new(NotificationAction.Suppress, AttentionCodes.Duplicate);
        if (input.Stale)
            return new(NotificationAction.Suppress, AttentionCodes.Stale);
        if (input.Offline)
            return new(NotificationAction.Suppress, AttentionCodes.Offline);
        if (input.Muted)
            return new(NotificationAction.CenterOnly, AttentionCodes.Muted);
        if (!IsHighValue(input.To))
            return new(NotificationAction.CenterOnly, AttentionCodes.NotNotifiable);
        if (input.RateLimited)
            return new(NotificationAction.CenterOnly, AttentionCodes.RateLimited);
        return new(NotificationAction.Deliver, AttentionCodes.Deliver);
    }

    public static bool IsHighValue(BusinessState state) =>
        state.Kind is BusinessStateKind.Blocked or BusinessStateKind.Done;

    public static bool CountsAsUnread(BusinessState state, NotificationAction action) =>
        action is not NotificationAction.Suppress &&
        state.Kind is BusinessStateKind.Blocked or BusinessStateKind.Done or BusinessStateKind.Unknown;
}
