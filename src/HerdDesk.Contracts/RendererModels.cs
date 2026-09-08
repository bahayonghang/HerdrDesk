namespace HerdDesk.Contracts;

// Host-side renderer spike types. Never deserialize InputContext from a
// renderer message. ITerminalRenderer stays a future replacement port.

public enum ImeHostEvent { PreeditUpdate, Commit, KeyWhileComposing, KeyIdle }

public enum WebMessageDirection { HostToRenderer, RendererToHost }

public enum RendererQueueState { Ready, Backpressured, Faulted }

public sealed record WebMessage(
    string Type,
    int Version,
    ConnectionEpoch Epoch,
    PaneKey Pane,
    int PayloadBytes,
    WebMessageDirection Direction,
    InputOrigin? Origin = null);

public sealed record RendererQueueDecision(
    bool Accepted,
    RendererQueueState State,
    string Code,
    bool ParseConsumedIsPresented,
    bool RequiresFullReset);
