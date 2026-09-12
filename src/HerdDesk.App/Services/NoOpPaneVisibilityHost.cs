using HerdDesk.Contracts;

namespace HerdDesk.App;

public sealed class NoOpPaneVisibilityHost : IPaneVisibilityHost
{
    public bool InputReplayed => false;
    public bool ControlRestored => false;

    public void SetReadOnly(PaneKey pane, bool readOnly) => _ = (pane, readOnly);

    public void CancelTerminal(PaneKey pane) => _ = pane;

    public void DestroyRenderer(PaneKey pane) => _ = pane;

    public bool TryKeepRpcPair(SessionKey session)
    {
        _ = session;
        return false;
    }

    public ConnectionEpoch BeginObserve(PaneKey pane, ConnectionEpoch epoch)
    {
        _ = pane;
        return epoch;
    }
}
