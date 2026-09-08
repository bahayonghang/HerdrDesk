using HerdDesk.Core;

namespace HerdDesk.App;

public sealed class WindowsNotificationSink : INotificationSink
{
    public bool Available => false;

    public NotificationDelivery TryDeliver(AttentionTransition transition, NotificationTarget target)
    {
        ArgumentNullException.ThrowIfNull(transition);
        ArgumentNullException.ThrowIfNull(target);
        _ = (transition.TransitionId, target.Key, target.Epoch, target.RouteVersion);
        return new(NotificationDeliveryKind.Unavailable, AttentionCodes.WindowsToastUnverified);
    }
}
