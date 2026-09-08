using HerdDesk.Contracts;

namespace HerdDesk.App;

public sealed class ProjectionCatalog
{
    private readonly Dictionary<PaneKey, int> _unread = new();
    private readonly Dictionary<PaneKey, TerminalAccess> _access = new();
    private readonly Dictionary<PaneKey, bool> _verified = new();
    private readonly HashSet<PaneKey> _ready = new();

    public DeviceProjectionSnapshot Snapshot { get; set; } = DeviceProjectionSnapshot.Empty;
    public DeviceFreshness Freshness { get; set; } = DeviceFreshness.Unknown;
    public string? LastErrorCode { get; set; }
    public bool DaemonAvailable { get; set; }
    public long? QueueBytes { get; set; }
    public bool RendererReadyDefault { get; set; }

    public int UnreadCount(PaneKey pane) => _unread.GetValueOrDefault(pane);

    public TerminalAccess AccessFor(PaneKey pane) =>
        _access.GetValueOrDefault(pane, TerminalAccess.Disconnected);

    public bool ControlVerifiedFor(PaneKey pane) => _verified.GetValueOrDefault(pane);

    public bool RendererReadyFor(PaneKey pane) => RendererReadyDefault || _ready.Contains(pane);

    public void SetUnread(PaneKey pane, int count) => _unread[pane] = count;

    public void SetAccess(PaneKey pane, TerminalAccess access, bool controlVerified)
    {
        _access[pane] = access;
        _verified[pane] = controlVerified;
    }

    public void SetRendererReady(PaneKey pane, bool ready)
    {
        if (ready)
            _ready.Add(pane);
        else
            _ready.Remove(pane);
    }

    public PaneProjection? FindPane(PaneKey key)
    {
        foreach (var device in Snapshot.Devices)
        {
            foreach (var session in device.Sessions)
            {
                foreach (var pane in session.Panes)
                {
                    if (pane.Key == key)
                        return pane;
                }
            }
        }

        return null;
    }

    public WorkspaceProjection? FindWorkspace(SessionKey session, string workspaceId)
    {
        foreach (var device in Snapshot.Devices)
        {
            foreach (var projected in device.Sessions)
            {
                if (projected.Session != session)
                    continue;
                foreach (var workspace in projected.Workspaces)
                {
                    if (workspace.WorkspaceId == workspaceId)
                        return workspace;
                }
            }
        }

        return null;
    }

    public ProjectedDevice? FindDevice(DeviceId device)
    {
        foreach (var item in Snapshot.Devices)
        {
            if (item.Device == device)
                return item;
        }

        return null;
    }

    public SessionProjection? FindSession(SessionKey session)
    {
        foreach (var device in Snapshot.Devices)
        {
            foreach (var item in device.Sessions)
            {
                if (item.Session == session)
                    return item;
            }
        }

        return null;
    }
}
