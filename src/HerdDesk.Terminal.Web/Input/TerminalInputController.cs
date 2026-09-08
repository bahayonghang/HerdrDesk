using System.Text;
using HerdDesk.Contracts;

namespace HerdDesk.Terminal.Web;

/// <summary>
/// Host-side input orchestration. Binding comes from the trusted host.
/// JS identity is ignored. This type does not grant a lease.
/// </summary>
public sealed class TerminalInputController
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly Func<ImeHostEvent, InputContext, RendererInput?, InputDecision> composition;
    private readonly Func<InputContext, RendererInput, InputDecision> input;
    private readonly ImeCompositionBridge bridge = new();
    private readonly SelectionAndMousePolicy selection = new();
    private readonly List<RendererInput> sent = [];
    private readonly AgentInputProfile profile;
    private InputContext context;
    private PaneKey? pane;
    private ConnectionEpoch? epoch;
    private bool rendererReady;
    private bool readOnly = true;
    private int focusSerial;
    private int pendingFocusSerial = -1;
    private (int Columns, int Rows)? lastResize;

    public TerminalInputController(
        InputContext context,
        Func<ImeHostEvent, InputContext, RendererInput?, InputDecision> composition,
        Func<InputContext, RendererInput, InputDecision> input,
        AgentInputProfile? profile = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(input);
        this.context = context;
        this.composition = composition;
        this.input = input;
        this.profile = profile ?? AgentInputProfiles.Unknown;
    }

    public InputContext Context => context;
    public PaneKey? Pane => pane;
    public ConnectionEpoch? Epoch => epoch;
    public AgentInputProfile Profile => profile;
    public HostFocusState FocusState { get; private set; } = HostFocusState.Unfocused;
    public bool HasFocus { get; private set; }
    public bool IsComposing => bridge.IsComposing;
    public bool HasPreedit => bridge.HasPreedit;
    public bool IsReadOnly => readOnly;
    public bool IsSuspended { get; private set; }
    public bool RendererReady => rendererReady;
    public bool ControlVerified => context.ControlVerified;
    public IReadOnlyList<RendererInput> Sent => sent;
    public int TransportByteCount => sent.Sum(item => item.Bytes.Length);
    public int LocalDisplayCount { get; private set; }
    public int UpstreamResizeCount { get; private set; }
    public int LocalScrollCount => selection.LocalScrollCount;
    public int UpstreamScrollCount => selection.UpstreamScrollCount;
    public int ControlRequestCount { get; private set; }
    public int ReleaseRequestCount { get; private set; }
    public string? LastRejectCode { get; private set; }
    public InputOrigin? LastRejectOrigin { get; private set; }
    public int LastRejectLength { get; private set; }
    public string? LastRejectKey { get; private set; }
    public SelectionSnapshot Selection => selection.Selection;
    public bool ApplicationMouseMode => selection.ApplicationMouseMode;
    public string? ReadOnlyScrollNotice => selection.ReadOnlyScrollNotice;
    public CandidateAnchor? Anchor { get; private set; }
    public string? ActiveCommitToken => bridge.ActiveToken;

    public void Bind(PaneKey bindPane, ConnectionEpoch bindEpoch)
    {
        focusSerial++;
        pendingFocusSerial = -1;
        HasFocus = false;
        FocusState = HostFocusState.Unfocused;
        bridge.Cancel();
        if (bindEpoch.Value <= 0 || !IsValid(bindPane))
        {
            Suspend(HostInputCodes.StaleEpoch);
            return;
        }

        if (epoch is { } current && bindEpoch.Value < current.Value)
        {
            Suspend(HostInputCodes.StaleEpoch);
            return;
        }

        IsSuspended = false;
        pane = bindPane;
        epoch = bindEpoch;
    }

    public void SetInputContext(InputContext next)
    {
        ArgumentNullException.ThrowIfNull(next);
        context = next;
    }

    public void SetRendererReady(bool ready)
    {
        rendererReady = ready;
        if (ready && pendingFocusSerial == focusSerial && !IsSuspended)
            CompleteFocus(pendingFocusSerial);
    }

    public void SetReadOnly(bool value) => readOnly = value;

    public void SetCandidateAnchor(CandidateAnchor anchor) => Anchor = anchor;

    public void ApplyLocalDisplay(string fontFamily, int fontSize, int zoomPercent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontFamily);
        _ = (fontSize, zoomPercent);
        LocalDisplayCount++;
    }

    public HostInputResult RequestFocus()
    {
        if (IsSuspended || pane is null || epoch is null)
            return Reject(HostInputCodes.StaleEpoch);
        var serial = ++focusSerial;
        if (!rendererReady)
        {
            pendingFocusSerial = serial;
            HasFocus = false;
            FocusState = HostFocusState.FocusRequested;
            return Reject(HostInputCodes.RendererNotReady);
        }

        return CompleteFocus(serial);
    }

    public void Blur()
    {
        HasFocus = false;
        FocusState = IsSuspended ? HostFocusState.Suspended : HostFocusState.Unfocused;
        pendingFocusSerial = -1;
    }

    public HostInputResult StartComposition()
    {
        if (IsSuspended)
            return Reject(HostInputCodes.InputPaused);
        if (pane is null || epoch is null)
            return Reject(HostInputCodes.StaleEpoch);
        var token = bridge.Start();
        _ = token;
        var decision = composition(ImeHostEvent.PreeditUpdate, context, null);
        return Reject(decision.Code);
    }

    public HostInputResult UpdatePreedit(string? claimedPaneIgnored = null)
    {
        _ = claimedPaneIgnored;
        if (IsSuspended)
            return Reject(HostInputCodes.InputPaused);
        bridge.Update();
        var decision = composition(ImeHostEvent.PreeditUpdate, context, null);
        return Reject(decision.Code);
    }

    public HostInputResult Commit(string token, string text)
    {
        if (IsSuspended)
        {
            bridge.TryConsume(token, out _);
            return Reject(HostInputCodes.InputPaused);
        }

        if (!bridge.TryConsume(token, out var duplicate))
        {
            if (duplicate)
                return Reject(HostInputCodes.CommitAlreadyAccepted, InputOrigin.CommittedText, 0);
            return Reject(HostInputCodes.InputOriginDenied);
        }

        if (readOnly)
            return Reject(HostInputCodes.ControlNotVerified, InputOrigin.CommittedText, 0);

        byte[] bytes;
        try
        {
            bytes = StrictUtf8.GetBytes(text ?? "");
        }
        catch (EncoderFallbackException)
        {
            return Reject(HostInputCodes.InputBytesLimit, InputOrigin.CommittedText, 0);
        }

        var proposed = Propose(InputOrigin.CommittedText, bytes);
        if (proposed is null)
            return Reject(HostInputCodes.StaleEpoch);
        var decision = composition(ImeHostEvent.Commit, context, proposed);
        return Finish(decision, proposed);
    }

    public HostInputResult HandleKey(PhysicalKeyEvent key)
    {
        if (IsSuspended)
            return Reject(HostInputCodes.InputPaused, key: KeySequenceTranslator.RedactedName(key));
        if (pane is null || epoch is null)
            return Reject(HostInputCodes.StaleEpoch, key: KeySequenceTranslator.RedactedName(key));

        if (bridge.IsComposing)
        {
            var ime = composition(ImeHostEvent.KeyWhileComposing, context, null);
            RecordReject(ime.Code, InputOrigin.UserKey, 0, KeySequenceTranslator.RedactedName(key));
            return HostInputResult.Deny(ime.Code, acceleratorYielded: KeySequenceTranslator.IsImeShortcut(key));
        }

        if (KeySequenceTranslator.IsSearchShortcut(key))
            return HostInputResult.Local(HostInputCodes.Allowed);

        if (KeySequenceTranslator.IsCopyShortcut(key))
            return selection.Copy();

        if (KeySequenceTranslator.IsPasteShortcut(key))
            return Reject(HostInputCodes.InputOriginDenied, key: KeySequenceTranslator.RedactedName(key));

        var translated = KeySequenceTranslator.Translate(key, profile);
        if (!translated.Allowed || translated.LocalCopy)
            return translated;
        if (readOnly)
            return Reject(HostInputCodes.ControlNotVerified, InputOrigin.UserKey, translated.TransportBytes.Length,
                KeySequenceTranslator.RedactedName(key));

        var proposed = Propose(InputOrigin.UserKey, translated.TransportBytes);
        if (proposed is null)
            return Reject(HostInputCodes.StaleEpoch, key: KeySequenceTranslator.RedactedName(key));
        var decision = composition(ImeHostEvent.KeyIdle, context, proposed);
        return Finish(decision, proposed, KeySequenceTranslator.RedactedName(key));
    }

    public HostInputResult HandlePaste(string text)
    {
        if (IsSuspended)
            return Reject(HostInputCodes.InputPaused, InputOrigin.ExplicitPaste, text?.Length ?? 0);
        if (bridge.IsComposing)
            return Reject(HostInputCodes.CompositionActive, InputOrigin.ExplicitPaste, text?.Length ?? 0);
        byte[] bytes;
        try
        {
            bytes = StrictUtf8.GetBytes(text ?? "");
        }
        catch (EncoderFallbackException)
        {
            return Reject(HostInputCodes.InputBytesLimit, InputOrigin.ExplicitPaste, 0);
        }

        if (readOnly)
            return Reject(HostInputCodes.ControlNotVerified, InputOrigin.ExplicitPaste, bytes.Length);
        var proposed = Propose(InputOrigin.ExplicitPaste, bytes);
        if (proposed is null)
            return Reject(HostInputCodes.StaleEpoch, InputOrigin.ExplicitPaste, bytes.Length);
        var decision = input(context, proposed);
        return Finish(decision, proposed);
    }

    public HostInputResult HandleSelection(string visibleText, bool shift = false) =>
        selection.DragSelect(visibleText, shift);

    public HostInputResult CopySelection() => selection.Copy();

    public HostInputResult HandleScroll(int delta, bool mouseReporting = false)
    {
        if (IsSuspended)
            return Reject(HostInputCodes.InputPaused);
        return selection.Scroll(delta, context, mouseReporting, writeAllowed: !readOnly);
    }

    public HostInputResult SwitchPane(PaneKey nextPane, ConnectionEpoch nextEpoch)
    {
        bridge.Cancel();
        Bind(nextPane, nextEpoch);
        if (IsSuspended)
            return Reject(LastRejectCode ?? HostInputCodes.StaleEpoch);
        return HostInputResult.Local(HostInputCodes.Allowed);
    }

    public bool TryRequestResize(int columns, int rows, CapabilityProfile? capabilities = null)
    {
        if (IsSuspended ||
            readOnly ||
            context.Access != TerminalAccess.Controlling ||
            !context.ControlVerified)
            return false;
        if (capabilities is not null && !capabilities.HasOperation("pane.resize"))
            return false;
        if (columns <= 0 || rows <= 0)
            return false;
        if (lastResize == (columns, rows))
            return true;
        lastResize = (columns, rows);
        UpstreamResizeCount++;
        return true;
    }

    public HostInputResult RequestControl()
    {
        ControlRequestCount++;
        return Reject(HostInputCodes.LeaseNotGranted);
    }

    public HostInputResult ReleaseControl()
    {
        ReleaseRequestCount++;
        return Reject(HostInputCodes.LeaseNotGranted);
    }

    public void Suspend(string code)
    {
        IsSuspended = true;
        HasFocus = false;
        FocusState = HostFocusState.Suspended;
        pendingFocusSerial = -1;
        bridge.Suspend();
        LastRejectCode = code;
    }

    public void Reset()
    {
        focusSerial++;
        pendingFocusSerial = -1;
        HasFocus = false;
        FocusState = HostFocusState.Unfocused;
        bridge.Cancel();
        selection.Clear();
        IsSuspended = false;
        lastResize = null;
    }

    private HostInputResult CompleteFocus(int serial)
    {
        if (serial != focusSerial || pane is null || epoch is null)
            return Reject(HostInputCodes.StaleEpoch);
        pendingFocusSerial = -1;
        HasFocus = true;
        FocusState = HostFocusState.Focused;
        return HostInputResult.Local(HostInputCodes.Allowed);
    }

    private RendererInput? Propose(InputOrigin origin, byte[] bytes)
    {
        if (pane is null || epoch is null)
            return null;
        return new RendererInput(pane.Value, epoch.Value, origin, bytes);
    }

    private HostInputResult Finish(InputDecision decision, RendererInput proposed, string? key = null)
    {
        if (!decision.Allowed)
            return Reject(decision.Code, proposed.Origin, proposed.Bytes.Length, key);
        sent.Add(proposed);
        LastRejectCode = null;
        LastRejectOrigin = null;
        LastRejectLength = 0;
        LastRejectKey = null;
        return HostInputResult.Accept(decision.Code, proposed.Bytes.ToArray(), proposed.Origin);
    }

    private HostInputResult Reject(
        string code,
        InputOrigin? origin = null,
        int length = 0,
        string? key = null)
    {
        RecordReject(code, origin, length, key);
        return HostInputResult.Deny(code);
    }

    private void RecordReject(string code, InputOrigin? origin, int length, string? key)
    {
        LastRejectCode = code;
        LastRejectOrigin = origin;
        LastRejectLength = length;
        LastRejectKey = key;
    }

    private static bool IsValid(PaneKey value) =>
        value.Session.Device.Value != Guid.Empty &&
        !string.IsNullOrWhiteSpace(value.Session.EndpointKey) &&
        !string.IsNullOrWhiteSpace(value.WorkspaceId) &&
        !string.IsNullOrWhiteSpace(value.PaneId);
}
