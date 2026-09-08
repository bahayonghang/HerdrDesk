using System.Threading.Channels;
using HerdDesk.Contracts;

namespace HerdDesk.Terminal.Web;

public sealed class WebTerminalRenderer : ITerminalRenderer
{
    private readonly IRenderFlowController flow;
    private readonly Func<WebMessage, InputContext, InputDecision> grant;
    private readonly IWebParseProbe parse;
    private readonly Channel<RendererInput> inputs = Channel.CreateBounded<RendererInput>(
        new BoundedChannelOptions(32)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
    private InputContext context;
    private PaneKey? pane;
    private ConnectionEpoch? epoch;
    private RendererSurfaceState surface = RendererSurfaceState.Uninitialized;
    private bool readOnly = true;
    private string lastCode = "uninitialized";

    public WebTerminalRenderer(
        IRenderFlowController flow,
        InputContext context,
        Func<WebMessage, InputContext, InputDecision> grant,
        IWebParseProbe? parse = null)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(grant);
        this.flow = flow;
        this.context = context;
        this.grant = grant;
        this.parse = parse ?? new ImmediateParseProbe();
    }

    public PaneKey? Pane => pane;
    public ConnectionEpoch? Epoch => epoch;
    public RendererSurfaceState SurfaceState => surface;
    public string LastCode => lastCode;
    public bool IsReadOnly => readOnly;
    public bool HasFocus { get; private set; }
    public int LocalDisplayApplyCount { get; private set; }
    public int UpstreamResizeCount { get; private set; }
    public int InFlightBytes => flow.InFlightBytes;

    public void SetInputContext(InputContext next)
    {
        ArgumentNullException.ThrowIfNull(next);
        context = next;
    }

    public ValueTask BindAsync(
        PaneKey bindPane,
        ConnectionEpoch bindEpoch,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (surface == RendererSurfaceState.Disposed)
            return ValueTask.CompletedTask;
        if (!IsValid(bindPane) || bindEpoch.Value <= 0)
        {
            Fault("invalid_identity");
            return ValueTask.CompletedTask;
        }
        if (epoch is { } current && bindEpoch.Value < current.Value)
        {
            Fault("stale_epoch");
            return ValueTask.CompletedTask;
        }

        pane = bindPane;
        epoch = bindEpoch;
        context = context with { ActivePane = bindPane, Epoch = bindEpoch };
        var reset = flow.Reset(bindEpoch);
        if (!reset.Accepted)
        {
            Fault(reset.Code);
            return ValueTask.CompletedTask;
        }

        HasFocus = false;
        surface = RendererSurfaceState.Loading;
        lastCode = "loading";
        return ValueTask.CompletedTask;
    }

    public async ValueTask<RenderApplyResult> ApplyAsync(
        TerminalFrame frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (surface is RendererSurfaceState.Disposed or RendererSurfaceState.Faulted
            or RendererSurfaceState.Resetting
            || pane is null || epoch is null)
            return RejectApply("terminal_stream_not_active");

        var copy = frame.Bytes.ToArray();
        var decision = flow.TryEnqueue(epoch.Value, frame.Sequence, frame.Full, copy.Length, out var token);
        if (!decision.Accepted)
        {
            if (decision.State == RendererQueueState.Backpressured)
            {
                surface = RendererSurfaceState.Backpressured;
                lastCode = decision.Code;
                return Result(false, null, decision.Code);
            }

            Fault(decision.Code);
            return Result(false, null, decision.Code);
        }

        try
        {
            await parse.ParseAsync(copy, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            flow.CancelInFlight();
            surface = RendererSurfaceState.Resetting;
            lastCode = "cancelled";
            throw;
        }

        var ack = flow.AcknowledgeParseConsumed(token);
        if (!ack.Accepted)
        {
            Fault(ack.Code);
            return Result(false, null, ack.Code);
        }

        ReadyOrObserving();
        lastCode = ack.Code;
        return Result(true, new RenderConsumption(epoch.Value, frame.Sequence), ack.Code);
    }

    public async IAsyncEnumerable<RendererInput> ReadInputsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in inputs.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return item;
    }

    public ValueTask SetReadOnlyAsync(bool value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        readOnly = value;
        if (surface is RendererSurfaceState.Ready or RendererSurfaceState.Observing
            or RendererSurfaceState.Backpressured)
            ReadyOrObserving();
        return ValueTask.CompletedTask;
    }

    public ValueTask FocusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (surface is not (RendererSurfaceState.Disposed or RendererSurfaceState.Uninitialized))
            HasFocus = true;
        return ValueTask.CompletedTask;
    }

    public void ApplyLocalDisplay(string fontFamily, int fontSize, int zoomPercent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontFamily);
        _ = (fontSize, zoomPercent);
        LocalDisplayApplyCount++;
    }

    public bool TryRequestUpstreamResize(int columns, int rows, InputContext requestContext)
    {
        ArgumentNullException.ThrowIfNull(requestContext);
        if (readOnly ||
            requestContext.Access != TerminalAccess.Controlling ||
            !requestContext.ControlVerified)
            return false;
        if (columns <= 0 || rows <= 0)
            return false;
        UpstreamResizeCount++;
        return true;
    }

    public InputDecision AcceptWebMessage(ReadOnlyMemory<byte> json)
    {
        if (surface is RendererSurfaceState.Disposed or RendererSurfaceState.Uninitialized
            || pane is null || epoch is null)
            return new(false, "terminal_stream_not_active");
        var parsed = WebMessageValidator.Evaluate(json, epoch.Value);
        if (!parsed.Accepted)
        {
            if (IsProtocolFault(parsed.Code))
                Fault(parsed.Code);
            return new(false, parsed.Code);
        }
        if (parsed.Direction != WebMessageDirection.RendererToHost)
            return new(false, "unknown_web_message_type");
        if (parsed.Kind == WebMessageKinds.Parsed)
            return new(true, "allowed");
        if (parsed.Kind == WebMessageKinds.Ready)
            return new(true, "allowed");
        if (parsed.Kind == WebMessageKinds.Fault)
        {
            Fault("renderer_fault");
            return new(true, "allowed");
        }
        if (parsed.Kind == WebMessageKinds.LinkRequest)
            return AcceptLink(parsed);
        if (readOnly && parsed.Kind == WebMessageKinds.Input)
            return new(false, "control_not_verified");
        if (readOnly && parsed.Kind == WebMessageKinds.Resize)
            return new(false, "observe_no_resize");

        var policyType = WebMessageKinds.PolicyType(parsed.Kind, parsed.Origin);
        var message = new WebMessage(
            policyType, parsed.Version, parsed.Epoch, pane.Value, parsed.PayloadBytes,
            parsed.Direction, parsed.Origin);
        var decision = grant(message, context);
        if (!decision.Allowed)
            return decision;
        if (parsed.Kind == WebMessageKinds.Resize)
        {
            if (!TryRequestUpstreamResize(parsed.Columns ?? 0, parsed.Rows ?? 0, context))
                return new(false, "observe_no_resize");
            return decision;
        }
        if (parsed.Kind == WebMessageKinds.Input)
        {
            if (!inputs.Writer.TryWrite(
                    new RendererInput(pane.Value, epoch.Value, parsed.Origin!.Value, parsed.Bytes)))
                return new(false, "queue_bytes_limit");
        }

        return decision;
    }

    public void RetryObserve()
    {
        if (surface == RendererSurfaceState.Disposed || pane is null || epoch is null)
            return;
        var reset = flow.Reset(epoch.Value);
        if (!reset.Accepted)
        {
            Fault(reset.Code);
            return;
        }

        HasFocus = false;
        surface = RendererSurfaceState.Loading;
        lastCode = "loading";
    }

    public ValueTask DisposeAsync()
    {
        if (surface == RendererSurfaceState.Disposed)
            return ValueTask.CompletedTask;
        flow.CancelInFlight();
        inputs.Writer.TryComplete();
        HasFocus = false;
        surface = RendererSurfaceState.Disposed;
        lastCode = "disposed";
        return ValueTask.CompletedTask;
    }

    private InputDecision AcceptLink(WebMessageValidation parsed)
    {
        var security = WebViewSecurityPolicy.ClassifyLink(parsed.Uri, parsed.UserGesture);
        if (!security.Allowed)
            return new(false, security.Code);
        return new(true, "allowed");
    }

    private void ReadyOrObserving()
    {
        if (flow.State == RendererQueueState.Backpressured)
        {
            surface = RendererSurfaceState.Backpressured;
            return;
        }

        surface = readOnly ? RendererSurfaceState.Observing : RendererSurfaceState.Ready;
    }

    private void Fault(string code)
    {
        lastCode = code;
        surface = RendererSurfaceState.Faulted;
        flow.CancelInFlight();
    }

    private RenderApplyResult RejectApply(string code)
    {
        lastCode = code;
        return Result(false, null, code);
    }

    private RenderApplyResult Result(bool accepted, RenderConsumption? consumption, string code) =>
        new(accepted, consumption, flow.State, surface, code);

    private static bool IsProtocolFault(string code) => code is
        "unknown_web_message_type" or "unsupported_web_message_version" or "malformed_web_message"
        or "duplicate_json_key" or "unknown_web_message_field" or "stale_epoch"
        or "noncanonical_base64" or "object_required" or "string_field_required";

    private static bool IsValid(PaneKey value) =>
        value.Session.Device.Value != Guid.Empty &&
        !string.IsNullOrWhiteSpace(value.Session.EndpointKey) &&
        !string.IsNullOrWhiteSpace(value.WorkspaceId) &&
        !string.IsNullOrWhiteSpace(value.PaneId);
}
