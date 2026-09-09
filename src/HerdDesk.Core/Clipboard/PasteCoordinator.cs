using HerdDesk.Contracts;

namespace HerdDesk.Core;

public sealed class PasteCoordinator
{
    readonly IClipboardSnapshotReader _reader;
    readonly IAttachmentInputSink _input;
    readonly IDiagnosticSink? _diagnostics;
    ClipboardSnapshot? _pending;
    ClipboardIntent? _intent;
    PasteTargetLease? _lease;
    ReadOnlyMemory<byte> _textBytes;

    public PasteCoordinator(
        IClipboardSnapshotReader reader,
        IAttachmentInputSink input,
        IDiagnosticSink? diagnostics = null)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _diagnostics = diagnostics;
        State = PasteUiState.Idle;
        LastSentBytes = ReadOnlyMemory<byte>.Empty;
    }

    public PasteUiState State { get; private set; }
    public ClipboardIntent? Intent => _intent;
    public PasteTargetLease? Lease => _lease;
    public ClipboardSnapshot? PendingSnapshot => _pending;
    public string? LastCode { get; private set; }
    public ReadOnlyMemory<byte> LastSentBytes { get; private set; }
    public int BytesSent => LastSentBytes.Length;
    public bool ClaimsAgentAccepted => false;
    public bool PreviewImpliesReceipt => false;
    public bool DefaultActionIsCancel =>
        State is PasteUiState.NeedsMultilineConfirm or PasteUiState.NeedsIntentChoice;
    public bool WatcherEnabled => false;

    public PasteActionResult BeginPaste(PasteConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        DiscardPending();
        LastSentBytes = ReadOnlyMemory<byte>.Empty;
        if (!IsValid(request.Pane) || request.Epoch.Value <= 0
            || string.IsNullOrWhiteSpace(request.ControlLeaseId))
            return Fail(ClipboardCodes.InvalidIdentity, PasteUiState.Failed);
        if (!request.ControlVerified || request.Access != TerminalAccess.Controlling)
            return Fail(ClipboardCodes.ControlNotVerified, PasteUiState.Failed);

        State = PasteUiState.LoadingClipboard;
        ClipboardSnapshot snapshot;
        try
        {
            snapshot = _reader.Read();
        }
        catch (Exception)
        {
            return Fail(ClipboardCodes.Unknown, PasteUiState.Failed);
        }

        ArgumentNullException.ThrowIfNull(snapshot);
        _pending = snapshot;
        _lease = new PasteTargetLease(
            request.Pane,
            request.Epoch,
            request.ControlLeaseId,
            request.ControlLeaseGeneration,
            snapshot.Id);
        var intent = ClipboardIntentResolver.Resolve(snapshot);
        _intent = intent;
        _textBytes = snapshot.TextBytes;
        WriteDiagnostic(request.Now, "begin", DiagnosticOutcome.Success, intent.Code, request.Epoch.Value);
        return ApplyIntent(intent, snapshot);
    }

    public PasteActionResult ChooseIntent(ClipboardIntentKind kind)
    {
        if (_pending is null || _intent is null || _lease is null)
            return Fail(ClipboardCodes.Empty, PasteUiState.Failed);
        if (State == PasteUiState.Stale)
            return Fail(ClipboardCodes.TargetStale, PasteUiState.Stale);
        if (_intent.Kind != ClipboardIntentKind.Mixed)
            return Fail(ClipboardCodes.Unsupported, State);
        if (!_intent.MixedOptions.Contains(kind)
            || kind is ClipboardIntentKind.Mixed or ClipboardIntentKind.Empty
                or ClipboardIntentKind.Unsupported or ClipboardIntentKind.AccessDenied)
            return Fail(ClipboardCodes.MixedIntentRequired, PasteUiState.NeedsIntentChoice);

        var narrowed = kind switch
        {
            ClipboardIntentKind.Text => ClipboardIntentResolver.Resolve(_pending with
            {
                HasFileList = false,
                HasImage = false
            }),
            ClipboardIntentKind.FileList => ClipboardIntentResolver.Resolve(_pending with
            {
                HasText = false,
                HasImage = false
            }),
            _ => ClipboardIntentResolver.Resolve(_pending with
            {
                HasText = false,
                HasFileList = false
            })
        };
        _intent = narrowed;
        return ApplyIntent(narrowed, _pending);
    }

    public async ValueTask<PasteActionResult> ConfirmAsync(
        PasteLiveTarget live,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(live);
        if (_pending is null || _intent is null || _lease is null)
            return Fail(ClipboardCodes.Empty, PasteUiState.Failed);
        if (State == PasteUiState.Cancelled)
            return Fail(ClipboardCodes.Cancelled, PasteUiState.Cancelled);
        if (State == PasteUiState.Stale)
            return Fail(LastCode ?? ClipboardCodes.TargetStale, PasteUiState.Stale);
        var stale = NoteLiveTarget(live);
        if (!stale.Succeeded)
            return stale;
        if (State == PasteUiState.NeedsIntentChoice)
            return Fail(ClipboardCodes.MixedIntentRequired, PasteUiState.NeedsIntentChoice);
        if (_intent.Kind is ClipboardIntentKind.FileList or ClipboardIntentKind.Image)
            return ContinueAttachment(_intent, _pending);
        if (_intent.Kind is not ClipboardIntentKind.Text)
            return Fail(_intent.Code ?? ClipboardCodes.Unsupported, State);
        if (_textBytes.Length > InputPolicy.MaxInputBytes)
            return Fail(ClipboardCodes.Oversize, PasteUiState.Failed);
        if (_textBytes.Length == 0)
            return Fail(ClipboardCodes.Empty, PasteUiState.Failed);

        var bytes = _textBytes;
        var input = new RendererInput(_lease.Pane, _lease.Epoch, InputOrigin.ExplicitPaste, bytes);
        var context = new InputContext(_lease.Pane, _lease.Epoch, TerminalAccess.Controlling, true);
        var decision = InputPolicy.Evaluate(context, input);
        if (!decision.Allowed)
            return Fail(MapInput(decision.Code), PasteUiState.Failed);

        State = PasteUiState.Sending;
        var receipt = await _input.SubmitAsync(input, cancellationToken).ConfigureAwait(false);
        if (State == PasteUiState.Cancelled)
            return Fail(ClipboardCodes.Cancelled, PasteUiState.Cancelled);
        if (State == PasteUiState.Stale)
            return Fail(LastCode ?? ClipboardCodes.TargetStale, PasteUiState.Stale);
        if (_lease is null || !_lease.Matches(live))
        {
            if (_lease is not null)
                NoteLiveTarget(live);
            return Fail(LastCode ?? ClipboardCodes.TargetStale, PasteUiState.Stale);
        }

        if (!receipt.Submitted)
        {
            LastSentBytes = ReadOnlyMemory<byte>.Empty;
            var code = receipt.Code == ControlLeaseCodes.InputOutcomeUnknown
                ? ClipboardCodes.ResultUnknown
                : ClipboardCodes.InputRejected;
            WriteDiagnostic(live.Now, "confirm", DiagnosticOutcome.Failure, code, live.Epoch.Value);
            return Fail(code, code == ClipboardCodes.ResultUnknown
                ? PasteUiState.UnknownOutcome
                : PasteUiState.Failed);
        }

        LastSentBytes = bytes;
        LastCode = ClipboardCodes.Allowed;
        State = PasteUiState.Ready;
        DiscardPending(keepSent: true);
        WriteDiagnostic(live.Now, "confirm", DiagnosticOutcome.Success, ClipboardCodes.Allowed, live.Epoch.Value);
        return new(true, ClipboardCodes.Allowed, State, bytes, _intent);
    }

    public PasteActionResult Cancel()
    {
        LastSentBytes = ReadOnlyMemory<byte>.Empty;
        LastCode = ClipboardCodes.Cancelled;
        State = PasteUiState.Cancelled;
        DiscardPending();
        return new(true, ClipboardCodes.Cancelled, State, ReadOnlyMemory<byte>.Empty, _intent);
    }

    public PasteActionResult NoteLiveTarget(PasteLiveTarget live)
    {
        ArgumentNullException.ThrowIfNull(live);
        if (State == PasteUiState.Stale)
            return Fail(LastCode ?? ClipboardCodes.TargetStale, PasteUiState.Stale);
        if (_lease is null || State is PasteUiState.Cancelled or PasteUiState.Idle)
            return new(true, LastCode ?? ClipboardCodes.Allowed, State, LastSentBytes, _intent);
        if (_lease.Matches(live))
            return new(true, LastCode ?? ClipboardCodes.Allowed, State, LastSentBytes, _intent);
        var code = !live.ControlVerified || live.Access != TerminalAccess.Controlling
            ? ClipboardCodes.ControlRevoked
            : live.Epoch != _lease.Epoch
                ? ClipboardCodes.StaleEpoch
                : ClipboardCodes.TargetStale;
        State = PasteUiState.Stale;
        LastCode = code;
        LastSentBytes = ReadOnlyMemory<byte>.Empty;
        WriteDiagnostic(live.Now, "stale", DiagnosticOutcome.Failure, code, live.Epoch.Value);
        return Fail(code, PasteUiState.Stale);
    }

    PasteActionResult ApplyIntent(ClipboardIntent intent, ClipboardSnapshot snapshot)
    {
        LastCode = intent.Code;
        if (intent.Kind == ClipboardIntentKind.AccessDenied)
            return Fail(intent.Code ?? ClipboardCodes.AccessDenied, PasteUiState.AccessDenied);
        if (intent.Kind == ClipboardIntentKind.Empty)
            return Fail(ClipboardCodes.Empty, PasteUiState.Failed);
        if (intent.Kind == ClipboardIntentKind.Unsupported)
            return Fail(ClipboardCodes.Unsupported, PasteUiState.Failed);
        if (intent.Code == ClipboardCodes.Oversize)
            return Fail(ClipboardCodes.Oversize, PasteUiState.Failed);
        if (intent.Kind == ClipboardIntentKind.Mixed)
        {
            State = PasteUiState.NeedsIntentChoice;
            LastCode = ClipboardCodes.MixedIntentRequired;
            return new(true, ClipboardCodes.MixedIntentRequired, State, ReadOnlyMemory<byte>.Empty, intent);
        }

        if (intent.Kind is ClipboardIntentKind.FileList or ClipboardIntentKind.Image)
            return ContinueAttachment(intent, snapshot);

        if (intent.RequiresMultilineConfirm)
        {
            State = PasteUiState.NeedsMultilineConfirm;
            LastCode = ClipboardCodes.MultilineConfirmRequired;
            return new(true, ClipboardCodes.MultilineConfirmRequired, State, ReadOnlyMemory<byte>.Empty, intent);
        }

        State = PasteUiState.Ready;
        LastCode = ClipboardCodes.Allowed;
        return new(true, ClipboardCodes.Allowed, State, ReadOnlyMemory<byte>.Empty, intent);
    }

    PasteActionResult ContinueAttachment(ClipboardIntent intent, ClipboardSnapshot snapshot)
    {
        State = PasteUiState.Ready;
        LastCode = ClipboardCodes.ContinueToAttachment;
        return new(
            true,
            ClipboardCodes.ContinueToAttachment,
            State,
            ReadOnlyMemory<byte>.Empty,
            intent,
            ToAttachmentSource(intent.Kind, snapshot));
    }

    static AttachmentSource ToAttachmentSource(ClipboardIntentKind kind, ClipboardSnapshot snapshot)
    {
        if (kind == ClipboardIntentKind.Image && snapshot.Image is { } image)
        {
            return new AttachmentSource(
                AttachmentSourceKind.ImageBuffer,
                image.OpaqueId,
                "clipboard-image",
                image.SizeBytes,
                image.ContentType);
        }

        var file = snapshot.Files.Count > 0
            ? snapshot.Files[0]
            : new ClipboardFileHandle("file", "clipboard-file", null);
        return new AttachmentSource(
            AttachmentSourceKind.LocalFile,
            file.OpaqueId,
            file.DisplayName,
            file.SizeBytes,
            "application/octet-stream");
    }

    static string MapInput(string code) => code switch
    {
        "stale_epoch" => ClipboardCodes.StaleEpoch,
        "control_not_verified" => ClipboardCodes.ControlNotVerified,
        "input_bytes_limit" => ClipboardCodes.InputBytesLimit,
        "invalid_identity" => ClipboardCodes.InvalidIdentity,
        _ => ClipboardCodes.InputRejected
    };

    static bool IsValid(PaneKey pane) =>
        pane.Session.Device.Value != Guid.Empty
        && !string.IsNullOrWhiteSpace(pane.Session.EndpointKey)
        && !string.IsNullOrWhiteSpace(pane.WorkspaceId)
        && !string.IsNullOrWhiteSpace(pane.PaneId);

    void DiscardPending(bool keepSent = false)
    {
        _pending = null;
        _lease = null;
        _textBytes = ReadOnlyMemory<byte>.Empty;
        if (!keepSent)
            _intent = null;
    }

    PasteActionResult Fail(string code, PasteUiState state)
    {
        State = state;
        LastCode = code;
        if (state is PasteUiState.Failed or PasteUiState.AccessDenied or PasteUiState.Stale
            or PasteUiState.Cancelled or PasteUiState.UnknownOutcome)
            LastSentBytes = ReadOnlyMemory<byte>.Empty;
        return new(false, code, State, ReadOnlyMemory<byte>.Empty, _intent);
    }

    void WriteDiagnostic(
        DateTimeOffset now,
        string operation,
        DiagnosticOutcome outcome,
        string? code,
        long epoch)
    {
        _diagnostics?.TryWrite(new DiagnosticEvent(
            now,
            "clipboard",
            operation,
            outcome,
            code,
            epoch,
            null,
            null,
            null,
            null));
    }
}
