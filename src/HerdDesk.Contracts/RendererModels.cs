namespace HerdDesk.Contracts;

// Host-side renderer types. Never deserialize InputContext from a renderer
// message. ITerminalRenderer is the incremental HD-014 port.

public enum ImeHostEvent { PreeditUpdate, Commit, KeyWhileComposing, KeyIdle }

public enum WebMessageDirection { HostToRenderer, RendererToHost }

public enum RendererQueueState { Ready, Backpressured, Faulted }

public enum RendererSurfaceState
{
    Uninitialized,
    Loading,
    Bound,
    Ready,
    Observing,
    Backpressured,
    Resetting,
    Offline,
    Faulted,
    Disabled,
    Disposed
}

public static class WebMessageLimits
{
    public const int SchemaVersion = 1;
    public const int MaxJsonBytes = 16 * 1024 * 1024;
    public const int MaxFrameBytes = 8 * 1024 * 1024;
    public const int MaxInputBytes = 64 * 1024;
    public const int MaxLinkUriChars = 2048;
}

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

public readonly record struct RenderToken(
    ConnectionEpoch Epoch,
    long Generation,
    ulong Sequence,
    int Bytes);

public sealed record RenderConsumption(ConnectionEpoch Epoch, ulong LastParsedSeq);

public sealed record RenderApplyResult(
    bool Accepted,
    RenderConsumption? Consumption,
    RendererQueueState QueueState,
    RendererSurfaceState SurfaceState,
    string Code);
