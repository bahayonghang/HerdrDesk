using HerdDesk.Contracts;
using HerdDesk.Core;

namespace HerdDesk.App;

public static class GlobalEntityMapping
{
    public static GlobalEntityRef FromSearchHit(SearchHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        var kind = hit.Kind switch
        {
            SearchResultKind.Device => GlobalEntityKind.Device,
            SearchResultKind.Session => GlobalEntityKind.Session,
            SearchResultKind.Workspace => GlobalEntityKind.Workspace,
            SearchResultKind.Agent => GlobalEntityKind.Agent,
            _ => GlobalEntityKind.Pane
        };
        return new GlobalEntityRef(
            kind, hit.Device, hit.Session, hit.WorkspaceId, hit.Pane, null,
            new FreshnessStamp(hit.Epoch, 0));
    }

    public static GlobalEntityRef FromNotification(NotificationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new GlobalEntityRef(
            GlobalEntityKind.Pane,
            target.Key.Pane.Session.Device,
            target.Key.Pane.Session,
            target.Key.Pane.WorkspaceId,
            target.Key.Pane,
            target.Key.EntityId,
            new FreshnessStamp(target.Epoch, 0));
    }
}
