using HerdDesk.Core;

namespace HerdDesk.App;

public enum NotificationDeliveryKind
{
    Delivered,
    CenterOnly,
    Failed,
    Unavailable
}

public sealed record NotificationDelivery(NotificationDeliveryKind Kind, string? Code);

public interface INotificationSink
{
    bool Available { get; }
    NotificationDelivery TryDeliver(AttentionTransition transition, NotificationTarget target);
}
