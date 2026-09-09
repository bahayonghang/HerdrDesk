using HerdDesk.Contracts;

namespace HerdDesk.Core;

public sealed class AttachmentCoordinator
{
    public static readonly TimeSpan DefaultLeaseLifetime = TimeSpan.FromHours(1);

    readonly AttachmentCapabilityCatalog _catalog;
    readonly TransferCoordinator _transfers;
    readonly AttachmentLeaseRegistry _leases;
    readonly IAttachmentInputSink _input;
    readonly IAttachmentClipboard? _clipboard;
    readonly IDiagnosticSink? _diagnostics;
    readonly List<byte> _pendingInsert = [];
    CancellationTokenSource? _uploadCts;
    IFileEndpoint? _sourceEndpoint;
    FileLocator? _sourcePath;
    long _generation;

    public AttachmentCoordinator(
        AttachmentCapabilityCatalog catalog,
        TransferCoordinator transfers,
        AttachmentLeaseRegistry leases,
        IAttachmentInputSink input,
        IAttachmentClipboard? clipboard = null,
        IDiagnosticSink? diagnostics = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _transfers = transfers ?? throw new ArgumentNullException(nameof(transfers));
        _leases = leases ?? throw new ArgumentNullException(nameof(leases));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _clipboard = clipboard;
        _diagnostics = diagnostics;
        State = DeliveryState.Editing;
        Evidence = CapabilityEvidence.Unknown;
        LastInsertedBytes = ReadOnlyMemory<byte>.Empty;
    }

    public DeliveryState State { get; private set; }
    public AttachmentIntent Intent { get; private set; } = AttachmentIntent.PasteText;
    public AttachmentDraft? Draft { get; private set; }
    public AttachmentTargetLease? Lease { get; private set; }
    public CapabilityEvidence Evidence { get; private set; }
    public AttachmentDeliveryMethod? SelectedMethod { get; private set; }
    public string? LastCode { get; private set; }
    public FileLocator? FinalPath { get; private set; }
    public string? Sha256 { get; private set; }
    public ReadOnlyMemory<byte> LastInsertedBytes { get; private set; }
    public string? LastCopiedDisplay { get; private set; }
    public int TransferStarts { get; private set; }
    public bool ClaimsAgentAccepted => false;
    public bool PreviewImpliesReceipt => false;
    public bool CacheHeld => Draft is not null && _leases.IsHeld(Draft.Id);

    public IReadOnlyList<AttachmentDeliveryMethod> AvailableMethods
    {
        get
        {
            if (Evidence.Allows(AttachmentOperations.DirectClipboard))
                return [AttachmentDeliveryMethod.Path, AttachmentDeliveryMethod.VerifiedDirectClipboard, AttachmentDeliveryMethod.Manual];
            return [AttachmentDeliveryMethod.Path, AttachmentDeliveryMethod.Manual];
        }
    }

    public bool VerifiedDirectClipboardEnabled =>
        Evidence.Allows(AttachmentOperations.DirectClipboard)
        && State is DeliveryState.Ready or DeliveryState.PathReady;

    public bool CopyPathEnabled =>
        Draft is not null
        && State is not DeliveryState.Cancelled
        && !string.IsNullOrEmpty(Draft.Source.DisplayName);

    public bool InsertEnabled =>
        SelectedMethod == AttachmentDeliveryMethod.Path
        && _pendingInsert.Count > 0
        && (Intent == AttachmentIntent.ImageAttachment
            ? State == DeliveryState.PathReady
            : State is DeliveryState.Ready or DeliveryState.PathReady);

    public bool UploadEnabled =>
        SelectedMethod == AttachmentDeliveryMethod.Path
        && State == DeliveryState.Ready
        && Intent == AttachmentIntent.ImageAttachment
        && _sourceEndpoint is not null
        && _sourcePath is not null;

    public AttachmentActionResult SetIntent(AttachmentIntent intent)
    {
        if (State is DeliveryState.Uploading or DeliveryState.Inserting)
            return Fail(AttachmentCodes.InFlight);
        Intent = intent;
        ClearSource();
        State = DeliveryState.Editing;
        LastCode = AttachmentCodes.Empty;
        return new(true, AttachmentCodes.Allowed, State, ReadOnlyMemory<byte>.Empty);
    }

    public AttachmentActionResult ChooseSource(
        AttachmentSource source,
        ReadOnlyMemory<byte> insertPayload = default,
        IFileEndpoint? fileSource = null,
        FileLocator? filePath = null,
        PaneKey? initialTarget = null,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (State is DeliveryState.Uploading or DeliveryState.Inserting)
            return Fail(AttachmentCodes.InFlight);
        if (string.IsNullOrWhiteSpace(source.Handle) && insertPayload.Length == 0)
        {
            ClearSource();
            State = DeliveryState.Editing;
            LastCode = AttachmentCodes.Empty;
            return Fail(AttachmentCodes.Empty);
        }

        if (Intent is AttachmentIntent.InsertFilePath or AttachmentIntent.ImageAttachment
            && AttachmentPathCodec.ContainsSubmitKey(insertPayload.Span)
            && insertPayload.Length > 0)
            return Fail(AttachmentCodes.InvalidPath);

        ReleaseCache();
        _generation++;
        Draft = new AttachmentDraft(
            Guid.NewGuid(),
            now ?? DateTimeOffset.UnixEpoch,
            _generation,
            Intent,
            source,
            initialTarget);
        Lease = null;
        SelectedMethod = null;
        Evidence = CapabilityEvidence.Unknown;
        FinalPath = null;
        Sha256 = null;
        LastInsertedBytes = ReadOnlyMemory<byte>.Empty;
        _sourceEndpoint = fileSource;
        _sourcePath = filePath;
        _pendingInsert.Clear();
        if (insertPayload.Length > 0)
            AppendPending(insertPayload.Span);
        State = DeliveryState.Editing;
        LastCode = AttachmentCodes.Allowed;
        return new(true, AttachmentCodes.Allowed, State, ReadOnlyMemory<byte>.Empty);
    }

    public AttachmentActionResult ConfirmTarget(AttachmentConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Draft is null)
            return Fail(AttachmentCodes.Empty);
        if (State is DeliveryState.Uploading or DeliveryState.Inserting)
            return Fail(AttachmentCodes.InFlight);
        if (State is DeliveryState.Cancelled)
            return Fail(AttachmentCodes.Cancelled, DeliveryState.Cancelled);
        if (Lease is not null && !Lease.SameFrozenTarget(request))
            return Fail(AttachmentCodes.TargetStale, DeliveryState.Stale);
        if (request.Access == TerminalAccess.Disconnected)
            return Fail(AttachmentCodes.Offline, DeliveryState.Failed);
        if (!request.ControlVerified || request.Access != TerminalAccess.Controlling)
            return Fail(AttachmentCodes.Disabled, DeliveryState.Failed);
        if (string.IsNullOrWhiteSpace(request.ControlLeaseId) || request.Epoch.Value <= 0)
            return Fail(AttachmentCodes.Disabled, DeliveryState.Failed);

        if (Lease is not null)
        {
            if (State == DeliveryState.Stale)
                State = FinalPath is not null && !string.IsNullOrEmpty(Sha256)
                    ? DeliveryState.PathReady
                    : DeliveryState.Ready;
            LastCode = Evidence.Status switch
            {
                CapabilityStatus.Unknown => AttachmentCodes.CapabilityUnknown,
                CapabilityStatus.Unsupported => AttachmentCodes.CapabilityUnsupported,
                _ => AttachmentCodes.Allowed
            };
            WriteDiagnostic(request.Now, "confirm", DiagnosticOutcome.Success, LastCode, request.Epoch.Value);
            return new(true, LastCode, State, ReadOnlyMemory<byte>.Empty);
        }

        State = DeliveryState.CapabilityLoading;
        Evidence = _catalog.Resolve(request.CapabilityKey);
        var lifetime = request.Lifetime ?? DefaultLeaseLifetime;
        Lease = new AttachmentTargetLease(
            request.Pane,
            request.Epoch,
            request.ControlLeaseId,
            request.ControlLeaseGeneration,
            request.CapabilityKey,
            request.Now + lifetime);
        SelectedMethod = Evidence.Status == CapabilityStatus.Verified
            && Evidence.Allows(AttachmentOperations.PathInsert)
            ? AttachmentDeliveryMethod.Path
            : null;
        State = DeliveryState.Ready;
        LastCode = Evidence.Status switch
        {
            CapabilityStatus.Unknown => AttachmentCodes.CapabilityUnknown,
            CapabilityStatus.Unsupported => AttachmentCodes.CapabilityUnsupported,
            _ => AttachmentCodes.Allowed
        };
        WriteDiagnostic(request.Now, "confirm", DiagnosticOutcome.Success, LastCode, request.Epoch.Value);
        return new(true, LastCode, State, ReadOnlyMemory<byte>.Empty);
    }

    public AttachmentActionResult ChooseDeliveryMethod(AttachmentDeliveryMethod method)
    {
        if (Draft is null || Lease is null || State is DeliveryState.Stale or DeliveryState.Cancelled)
            return Fail(State == DeliveryState.Stale ? AttachmentCodes.TargetStale : AttachmentCodes.Empty);
        if (method == AttachmentDeliveryMethod.VerifiedDirectClipboard
            && !Evidence.Allows(AttachmentOperations.DirectClipboard))
            return Fail(AttachmentCodes.DirectClipboardDenied);
        SelectedMethod = method;
        LastCode = AttachmentCodes.Allowed;
        return new(true, LastCode, State, ReadOnlyMemory<byte>.Empty);
    }

    public AttachmentActionResult NoteLiveTarget(AttachmentLiveTarget live)
    {
        ArgumentNullException.ThrowIfNull(live);
        if (Lease is null || State is DeliveryState.Cancelled or DeliveryState.Failed)
            return new(true, LastCode ?? AttachmentCodes.Allowed, State, ReadOnlyMemory<byte>.Empty);
        if (Lease.Matches(live))
            return new(true, LastCode ?? AttachmentCodes.Allowed, State, ReadOnlyMemory<byte>.Empty);
        var code = live.Now > Lease.Expiry
            ? AttachmentCodes.Expired
            : !live.ControlVerified || live.Access != TerminalAccess.Controlling
                ? AttachmentCodes.ControlRevoked
                : AttachmentCodes.TargetStale;
        State = DeliveryState.Stale;
        LastCode = code;
        WriteDiagnostic(live.Now, "stale", DiagnosticOutcome.Failure, code, live.Epoch.Value);
        return Fail(code, DeliveryState.Stale);
    }

    public async ValueTask<AttachmentActionResult> StartUploadAsync(
        IFileEndpoint destination,
        FileLocator destinationParent,
        FileComponent destinationName,
        AttachmentLiveTarget live,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(destinationParent);
        ArgumentNullException.ThrowIfNull(destinationName);
        ArgumentNullException.ThrowIfNull(live);
        var blocked = GuardMutation(live, requireMethod: true, requireReadyUpload: true);
        if (blocked is not null)
            return blocked;
        if (_sourceEndpoint is null || _sourcePath is null || Draft is null || Lease is null)
            return Fail(AttachmentCodes.SourceUnavailable);

        State = DeliveryState.Uploading;
        LastCode = AttachmentCodes.Allowed;
        _ = _leases.Acquire(Draft.Id);
        _uploadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        TransferStarts++;
        TransferResult result;
        try
        {
            result = await _transfers.CopyAsync(
                new TransferRequest(
                    Draft.Id,
                    _sourceEndpoint,
                    _sourcePath,
                    destination,
                    destinationParent,
                    destinationName,
                    FileConflictMode.Fail,
                    null),
                _uploadCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            result = new TransferResult(
                false, FileOpCodes.Cancelled, TransferStage.Cancelled, Draft.Id, null, null, 0, null);
        }

        if (State == DeliveryState.Cancelled)
            return Fail(AttachmentCodes.Cancelled, DeliveryState.Cancelled);
        if (State == DeliveryState.Stale)
            return Fail(LastCode ?? AttachmentCodes.TargetStale, DeliveryState.Stale);
        if (Lease is null || !Lease.Matches(live))
        {
            NoteLiveTarget(live);
            return Fail(LastCode ?? AttachmentCodes.TargetStale, DeliveryState.Stale);
        }

        if (_uploadCts.IsCancellationRequested || result.Code == FileOpCodes.Cancelled)
        {
            ReleaseCache();
            State = DeliveryState.Cancelled;
            LastCode = AttachmentCodes.Cancelled;
            return Fail(AttachmentCodes.Cancelled, DeliveryState.Cancelled);
        }

        if (!result.Succeeded
            || result.Stage != TransferStage.Completed
            || string.IsNullOrEmpty(result.Sha256)
            || result.FinalPath is null)
        {
            ReleaseCache();
            var code = MapUpload(result.Code);
            State = DeliveryState.Failed;
            LastCode = code;
            WriteDiagnostic(live.Now, "upload", DiagnosticOutcome.Failure, code, live.Epoch.Value);
            return Fail(code);
        }

        FinalPath = result.FinalPath;
        Sha256 = result.Sha256;
        _pendingInsert.Clear();
        try
        {
            AppendPending(AttachmentPathCodec.EncodeLocator(result.FinalPath, Lease.CapabilityKey.TargetOs));
        }
        catch (ArgumentException)
        {
            ReleaseCache();
            State = DeliveryState.Failed;
            LastCode = AttachmentCodes.InvalidPath;
            return Fail(AttachmentCodes.InvalidPath);
        }

        State = DeliveryState.PathReady;
        LastCode = AttachmentCodes.Allowed;
        WriteDiagnostic(live.Now, "upload", DiagnosticOutcome.Success, FileOpCodes.Ok, live.Epoch.Value);
        return new(true, AttachmentCodes.Allowed, State, ReadOnlyMemory<byte>.Empty, FinalPath, Sha256);
    }

    public async ValueTask<AttachmentActionResult> InsertAsync(
        AttachmentLiveTarget live,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(live);
        var blocked = GuardMutation(live, requireMethod: true, requireReadyUpload: false);
        if (blocked is not null)
            return blocked;
        if (Lease is null || Draft is null)
            return Fail(AttachmentCodes.Empty);
        if (_pendingInsert.Count == 0)
            return Fail(AttachmentCodes.Empty);
        if (SelectedMethod == AttachmentDeliveryMethod.Manual)
            return Fail(AttachmentCodes.DeliveryMethodRequired);
        if (SelectedMethod == AttachmentDeliveryMethod.VerifiedDirectClipboard)
            return SubmitDirectAsync();

        var bytes = _pendingInsert.ToArray();
        if (Intent is not AttachmentIntent.PasteText && AttachmentPathCodec.ContainsSubmitKey(bytes))
            return Fail(AttachmentCodes.InvalidPath);
        if (bytes.Length > InputPolicy.MaxInputBytes)
            return Fail(AttachmentCodes.InputBytesLimit);

        State = DeliveryState.Inserting;
        var input = new RendererInput(Lease.Pane, Lease.Epoch, InputOrigin.ExplicitPaste, bytes);
        var context = new InputContext(Lease.Pane, Lease.Epoch, TerminalAccess.Controlling, true);
        var decision = InputPolicy.Evaluate(context, input);
        if (!decision.Allowed)
        {
            State = DeliveryState.Failed;
            LastCode = AttachmentCodes.InputRejected;
            WriteDiagnostic(live.Now, "insert", DiagnosticOutcome.Failure, decision.Code, live.Epoch.Value);
            return Fail(AttachmentCodes.InputRejected);
        }

        var receipt = await _input.SubmitAsync(input, cancellationToken).ConfigureAwait(false);
        LastInsertedBytes = bytes;
        if (State == DeliveryState.Cancelled)
            return Fail(AttachmentCodes.Cancelled, DeliveryState.Cancelled);
        if (State == DeliveryState.Stale)
            return Fail(LastCode ?? AttachmentCodes.TargetStale, DeliveryState.Stale);
        if (Lease is null || !Lease.Matches(live))
        {
            NoteLiveTarget(live);
            return Fail(LastCode ?? AttachmentCodes.TargetStale, DeliveryState.Stale);
        }

        if (!receipt.Submitted)
        {
            State = DeliveryState.Failed;
            LastCode = receipt.Code == ControlLeaseCodes.InputOutcomeUnknown
                ? AttachmentCodes.ResultUnknown
                : AttachmentCodes.InputRejected;
            WriteDiagnostic(live.Now, "insert", DiagnosticOutcome.Failure, LastCode, live.Epoch.Value);
            return Fail(LastCode);
        }

        State = DeliveryState.PathInsertedUnconfirmed;
        LastCode = AttachmentCodes.Allowed;
        WriteDiagnostic(live.Now, "insert", DiagnosticOutcome.Success, AttachmentCodes.Allowed, live.Epoch.Value);
        return new(true, AttachmentCodes.Allowed, State, bytes, FinalPath, Sha256);
    }

    public AttachmentActionResult CopyPath()
    {
        if (!CopyPathEnabled || Draft is null)
            return Fail(AttachmentCodes.Empty);
        var display = Draft.Source.DisplayName;
        LastCopiedDisplay = display;
        _ = _clipboard?.CopyDisplayText(display);
        LastCode = AttachmentCodes.Allowed;
        return new(true, AttachmentCodes.Allowed, State, ReadOnlyMemory<byte>.Empty);
    }

    public async ValueTask<AttachmentActionResult> CancelAsync()
    {
        if (State is DeliveryState.Cancelled)
            return Fail(AttachmentCodes.Cancelled, DeliveryState.Cancelled);
        _uploadCts?.Cancel();
        ReleaseCache();
        State = DeliveryState.Cancelled;
        LastCode = AttachmentCodes.Cancelled;
        await ValueTask.CompletedTask;
        return new(true, AttachmentCodes.Cancelled, State, ReadOnlyMemory<byte>.Empty);
    }

    public AttachmentActionResult DiscardDraft()
    {
        if (State is DeliveryState.Uploading or DeliveryState.Inserting)
            return Fail(AttachmentCodes.InFlight);
        ReleaseCache();
        ClearSource();
        State = DeliveryState.Editing;
        LastCode = AttachmentCodes.Cancelled;
        return new(true, AttachmentCodes.Cancelled, State, ReadOnlyMemory<byte>.Empty);
    }

    AttachmentActionResult? GuardMutation(AttachmentLiveTarget live, bool requireMethod, bool requireReadyUpload)
    {
        if (Draft is null)
            return Fail(AttachmentCodes.Empty);
        if (State is DeliveryState.Stale)
            return Fail(AttachmentCodes.TargetStale, DeliveryState.Stale);
        if (State is DeliveryState.Cancelled)
            return Fail(AttachmentCodes.Cancelled, DeliveryState.Cancelled);
        if (Lease is null)
            return Fail(AttachmentCodes.Empty);
        if (!Lease.Matches(live))
        {
            NoteLiveTarget(live);
            return Fail(LastCode ?? AttachmentCodes.TargetStale, DeliveryState.Stale);
        }

        if (requireMethod && SelectedMethod is null)
            return Fail(AttachmentCodes.DeliveryMethodRequired);
        if (SelectedMethod == AttachmentDeliveryMethod.VerifiedDirectClipboard
            && !Evidence.Allows(AttachmentOperations.DirectClipboard))
            return Fail(AttachmentCodes.DirectClipboardDenied);
        if (requireReadyUpload)
        {
            if (State != DeliveryState.Ready || Intent != AttachmentIntent.ImageAttachment)
                return Fail(AttachmentCodes.Disabled);
        }
        else if (SelectedMethod == AttachmentDeliveryMethod.VerifiedDirectClipboard)
        {
            if (State is not (DeliveryState.Ready or DeliveryState.PathReady))
                return Fail(AttachmentCodes.Disabled);
        }
        else if (Intent == AttachmentIntent.ImageAttachment)
        {
            if (State != DeliveryState.PathReady)
                return Fail(AttachmentCodes.Disabled);
        }
        else if (State is not (DeliveryState.Ready or DeliveryState.PathReady))
            return Fail(AttachmentCodes.Disabled);

        return null;
    }

    AttachmentActionResult SubmitDirectAsync()
    {
        if (!Evidence.Allows(AttachmentOperations.DirectClipboard))
            return Fail(AttachmentCodes.DirectClipboardDenied);
        State = DeliveryState.PathInsertedUnconfirmed;
        LastCode = AttachmentCodes.Allowed;
        LastInsertedBytes = ReadOnlyMemory<byte>.Empty;
        return new(true, AttachmentCodes.Allowed, State, ReadOnlyMemory<byte>.Empty);
    }

    static string MapUpload(string? code) => code switch
    {
        FileOpCodes.PermissionDenied => AttachmentCodes.PermissionDenied,
        FileOpCodes.Cancelled => AttachmentCodes.Cancelled,
        FileOpCodes.HashMismatch or FileOpCodes.LengthMismatch or FileOpCodes.SourceChanged
            => AttachmentCodes.UploadIntegrity,
        FileOpCodes.StaleTarget => AttachmentCodes.TargetStale,
        FileOpCodes.OutcomeUnknown => AttachmentCodes.ResultUnknown,
        FileOpCodes.NotFound or FileOpCodes.ParentMissing => AttachmentCodes.SourceUnavailable,
        _ => string.IsNullOrEmpty(code) ? AttachmentCodes.ResultUnknown : code
    };

    void AppendPending(ReadOnlySpan<byte> bytes)
    {
        for (var i = 0; i < bytes.Length; i++)
            _pendingInsert.Add(bytes[i]);
    }

    void ClearSource()
    {
        Draft = null;
        Lease = null;
        SelectedMethod = null;
        Evidence = CapabilityEvidence.Unknown;
        FinalPath = null;
        Sha256 = null;
        LastInsertedBytes = ReadOnlyMemory<byte>.Empty;
        LastCopiedDisplay = null;
        _sourceEndpoint = null;
        _sourcePath = null;
        _pendingInsert.Clear();
        _uploadCts?.Dispose();
        _uploadCts = null;
    }

    void ReleaseCache()
    {
        if (Draft is not null)
            _leases.Release(Draft.Id);
    }

    AttachmentActionResult Fail(string code, DeliveryState? state = null)
    {
        if (state is DeliveryState next)
            State = next;
        else if (State is not (DeliveryState.Stale or DeliveryState.Cancelled)
                 && code is AttachmentCodes.TargetStale or AttachmentCodes.Expired or AttachmentCodes.ControlRevoked)
            State = DeliveryState.Stale;
        else if (State is not (DeliveryState.Stale or DeliveryState.Cancelled)
                 && code is not (AttachmentCodes.Empty or AttachmentCodes.InFlight
                     or AttachmentCodes.DeliveryMethodRequired or AttachmentCodes.DirectClipboardDenied
                     or AttachmentCodes.Disabled or AttachmentCodes.CapabilityUnknown
                     or AttachmentCodes.CapabilityUnsupported))
            State = DeliveryState.Failed;
        LastCode = code;
        return new(false, code, State, ReadOnlyMemory<byte>.Empty);
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
            "attachments",
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
