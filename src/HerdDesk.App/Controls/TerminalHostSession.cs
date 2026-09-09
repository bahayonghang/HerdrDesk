using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Terminal.Web;

namespace HerdDesk.App;

public sealed class TerminalHostSession : ITerminalDisplaySurface, IAsyncDisposable
{
    private readonly List<byte[]> outbound = [];
    private WebTerminalRenderer? renderer;
    private TerminalInputController? input;
    private InputContext? context;
    private bool visible = true;
    private string theme = "dark";
    private bool disposed;
    private string? displayKey;

    public PaneKey? Pane => renderer?.Pane;
    public ConnectionEpoch? Epoch => renderer?.Epoch;
    public RendererSurfaceState SurfaceState =>
        renderer?.SurfaceState ?? RendererSurfaceState.Uninitialized;
    public string LastCode => renderer?.LastCode ?? "uninitialized";
    public bool IsReadOnly => renderer?.IsReadOnly ?? true;
    public bool Visible => visible;
    public int LocalApplyCount { get; private set; }
    public int UpstreamResizeCount { get; private set; }
    public int OutboundCount => outbound.Count;
    public IReadOnlyList<byte[]> Outbound => outbound;
    public TerminalInputController? Input => input;
    public bool IsComposing => input?.IsComposing == true;
    public bool ControlVerified => context?.ControlVerified == true;
    public int TransportByteCount => input?.TransportByteCount ?? 0;

    public string OverlayText => SurfaceState switch
    {
        RendererSurfaceState.Loading => ShellStrings.Loading,
        RendererSurfaceState.Observing => ShellStrings.Observing,
        RendererSurfaceState.Ready => ShellStrings.Ready,
        RendererSurfaceState.Backpressured => ShellStrings.OverloadedReobserve,
        RendererSurfaceState.Resetting => ShellStrings.ReobservingTerminal,
        RendererSurfaceState.Offline => ShellStrings.Offline,
        RendererSurfaceState.Faulted => ShellStrings.Error,
        RendererSurfaceState.Disabled => ShellStrings.PausedForCapacity,
        _ => ShellStrings.Starting
    };

    public bool OverlayVisible => SurfaceState is not (
        RendererSurfaceState.Ready or RendererSurfaceState.Observing);
    public bool RetryVisible => SurfaceState is
        RendererSurfaceState.Faulted or RendererSurfaceState.Resetting or RendererSurfaceState.Offline;

    public void Bind(PaneKey pane, ConnectionEpoch epoch, bool readOnly)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (renderer is not null && Pane == pane && Epoch == epoch && IsReadOnly == readOnly)
            return;
        var access = readOnly ? TerminalAccess.Observing : TerminalAccess.Controlling;
        context = new InputContext(pane, epoch, access, !readOnly);
        renderer ??= new WebTerminalRenderer(
            new RenderFlowController(epoch),
            context,
            WebMessagePolicy.Evaluate);
        renderer.SetInputContext(context);
        renderer.BindAsync(pane, epoch).AsTask().GetAwaiter().GetResult();
        renderer.SetReadOnlyAsync(readOnly).AsTask().GetAwaiter().GetResult();
        if (input is null)
        {
            input = new TerminalInputController(
                context, CompositionPolicy.Evaluate, InputPolicy.Evaluate);
        }
        else
        {
            input.SetInputContext(context);
        }

        input.Bind(pane, epoch);
        input.SetReadOnly(readOnly);
        Queue(WebMessageCodec.Initialize(epoch, theme, readOnly));
    }

    public RenderApplyResult ApplyFrame(TerminalFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (disposed || renderer is null || renderer.Epoch is null)
            return new(false, null, RendererQueueState.Faulted, RendererSurfaceState.Uninitialized,
                "terminal_stream_not_active");
        if (!visible)
            return new(false, null, RendererQueueState.Backpressured, RendererSurfaceState.Disabled,
                "pane_hidden");
        var result = renderer.ApplyAsync(frame).AsTask().GetAwaiter().GetResult();
        if (result.Accepted)
            Queue(WebMessageCodec.Frame(renderer.Epoch.Value, frame.Sequence, frame.Full, frame.Bytes.Span));
        return result;
    }

    public InputDecision AcceptFromWeb(ReadOnlyMemory<byte> json)
    {
        if (disposed || renderer is null || input is null || renderer.Epoch is null)
            return new(false, "terminal_stream_not_active");
        var parsed = WebMessageValidator.Evaluate(json, renderer.Epoch.Value);
        if (!parsed.Accepted)
            return renderer.AcceptWebMessage(json);
        if (WebMessageKinds.IsHostInputKind(parsed.Kind))
            return ToDecision(DispatchInput(parsed));
        if (parsed.Kind == WebMessageKinds.Ready)
            input.SetRendererReady(true);
        return renderer.AcceptWebMessage(json);
    }

    public void SetVisible(bool value)
    {
        visible = value;
        if (!value)
        {
            input?.CancelComposition();
            renderer?.RetryObserve();
        }
    }

    public void RetryObserve()
    {
        if (renderer is null || renderer.Epoch is null)
            return;
        var epoch = renderer.Epoch.Value;
        input?.Reset();
        input?.SetRendererReady(false);
        renderer.RetryObserve();
        Queue(WebMessageCodec.Dispose(epoch));
        Queue(WebMessageCodec.Initialize(epoch, theme, renderer.IsReadOnly));
    }

    public void Focus(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if (renderer is null || renderer.Epoch is null)
            return;
        renderer.FocusAsync().AsTask().GetAwaiter().GetResult();
        input?.RequestFocus();
        Queue(WebMessageCodec.Focus(renderer.Epoch.Value, token));
    }

    public void ApplyLocal(TerminalDisplayPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var key = preferences.FontFamily + "|" + preferences.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                  "|" + preferences.ZoomPercent.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (key == displayKey)
            return;
        displayKey = key;
        LocalApplyCount++;
        renderer?.ApplyLocalDisplay(preferences.FontFamily, preferences.FontSize, preferences.ZoomPercent);
        if (renderer?.Epoch is { } epoch)
            Queue(WebMessageCodec.Display(epoch, preferences.FontFamily, preferences.FontSize,
                preferences.ZoomPercent));
    }

    public bool TryUpstreamResize(int columns, int rows)
    {
        if (renderer is null || context is null)
            return false;
        if (!renderer.TryRequestUpstreamResize(columns, rows, context))
            return false;
        UpstreamResizeCount++;
        return true;
    }

    public byte[]? DequeueOutbound()
    {
        if (outbound.Count == 0)
            return null;
        var next = outbound[0];
        outbound.RemoveAt(0);
        return next;
    }

    public ValueTask DisposeAsync()
    {
        if (disposed)
            return ValueTask.CompletedTask;
        disposed = true;
        input?.Reset();
        if (renderer?.Epoch is { } epoch)
            Queue(WebMessageCodec.Dispose(epoch));
        return renderer?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    private HostInputResult DispatchInput(WebMessageValidation parsed)
    {
        if (input is null)
            return HostInputResult.Deny("terminal_stream_not_active");
        return parsed.Kind switch
        {
            WebMessageKinds.Composition => DispatchComposition(parsed),
            WebMessageKinds.Key => input.HandleKey(new PhysicalKeyEvent(
                parsed.KeyName ?? "", parsed.Ctrl, parsed.Shift, parsed.Alt, parsed.AltGr,
                parsed.CapsLock)),
            WebMessageKinds.PasteIntent => input.HandlePaste(parsed.Text ?? ""),
            WebMessageKinds.SelectionChanged => input.HandleSelection(
                parsed.VisibleText ?? "", parsed.Shift),
            WebMessageKinds.MouseIntent when parsed.MouseAction == "scroll" =>
                input.HandleScroll(parsed.Delta),
            WebMessageKinds.MouseIntent => input.HandleSelection("", parsed.Shift),
            WebMessageKinds.Input when parsed.Origin is { } origin =>
                input.HandleOriginBytes(origin, parsed.Bytes.ToArray()),
            _ => HostInputResult.Deny(HostInputCodes.InputOriginDenied)
        };
    }

    private HostInputResult DispatchComposition(WebMessageValidation parsed)
    {
        if (input is null)
            return HostInputResult.Deny("terminal_stream_not_active");
        return parsed.Phase switch
        {
            "start" => input.StartComposition(parsed.Token),
            "update" => input.UpdatePreedit(),
            "cancel" => input.CancelComposition(),
            "end" => input.Commit(parsed.Token ?? "", parsed.Text ?? ""),
            _ => HostInputResult.Deny(HostInputCodes.InputOriginDenied)
        };
    }

    private static InputDecision ToDecision(HostInputResult result) =>
        new(result.Allowed, result.Code);

    private void Queue(byte[] json)
    {
        if (json.Length is <= 0 or > WebMessageLimits.MaxJsonBytes)
            return;
        outbound.Add(json);
    }
}
