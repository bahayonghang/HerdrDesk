using System.Runtime.CompilerServices;
using System.Threading.Channels;
using HerdDesk.Contracts;

namespace HerdDesk.Core;

public sealed class ControlLeaseCoordinator : IControlLeaseCoordinator
{
    private readonly ILeaseTargetStore _store;
    private readonly IControlBindingHost _host;
    private readonly ITerminalRenderer _renderer;
    private readonly IDiagnosticSink? _diagnostics;
    private readonly ControlLeaseOptions _options;
    private readonly Channel<ControlLeaseMessage> _mailbox;
    private readonly Channel<ControlLeaseState> _states;
    private readonly List<Task> _effects = [];
    private readonly List<InputSubmissionOutcome> _ledger = [];
    private readonly Task _loop;

    private volatile ControlLeaseState _current = ControlLeaseState.Disconnected;
    private ITerminalTransport? _observe;
    private ITerminalTransport? _candidate;
    private ITerminalTransport? _control;
    private CandidateFrameBuffer? _buffer;
    private TakeoverChallenge? _challenge;
    private CancellationTokenSource? _lifetime;
    private PaneKey? _target;
    private ConnectionEpoch _projectionEpoch;
    private long _projectionRevision;
    private TerminalAccess _access = TerminalAccess.Disconnected;
    private bool _verified;
    private string? _attemptId;
    private long _leaseGeneration = 1;
    private long _bindingEpoch;
    private ControlAttemptOutcome _lastAttempt = ControlAttemptOutcome.None;
    private string? _lastCode;
    private InputSubmissionOutcome? _lastInput;
    private bool _ownershipProved;
    private bool _rendererAcked;
    private bool _promotionInFlight;
    private bool _writable;
    private bool _stopping;
    private bool _disconnected;
    private ulong _nextCommandId;
    private string? _pendingConfirmHandle;
    private int _rendererGeneration;

    public ControlLeaseCoordinator(
        ILeaseTargetStore store,
        IControlBindingHost host,
        ITerminalRenderer renderer,
        IDiagnosticSink? diagnostics = null,
        ControlLeaseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(renderer);
        _store = store;
        _host = host;
        _renderer = renderer;
        _diagnostics = diagnostics;
        _options = options ?? ControlLeaseOptions.Default;
        if (_options.MailboxCapacity < 1 || _options.CandidateByteLimit < 1)
            throw new ArgumentOutOfRangeException(nameof(options));
        _mailbox = Channel.CreateBounded<ControlLeaseMessage>(new BoundedChannelOptions(_options.MailboxCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        _states = Channel.CreateBounded<ControlLeaseState>(new BoundedChannelOptions(1)
        {
            SingleReader = false,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        _lifetime = new CancellationTokenSource();
        _current = Snapshot();
        _states.Writer.TryWrite(_current);
        _loop = Task.Run(RunAsync);
        _ = _renderer.SetReadOnlyAsync(true);
    }

    public ControlLeaseState Current => _current;

    public async IAsyncEnumerable<ControlLeaseState> ReadStatesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return _current;
        await foreach (var state in _states.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return state;
    }

    public ValueTask OpenPaneAsync(PaneKey pane, CancellationToken cancellationToken = default) =>
        Call(new OpenPaneCommand(pane, NewCompletion()), cancellationToken);

    public ValueTask SelectPaneAsync(PaneKey pane, CancellationToken cancellationToken = default) =>
        Call(new SelectPaneCommand(pane, NewCompletion()), cancellationToken);

    public ValueTask NoteFocusAsync(CancellationToken cancellationToken = default) =>
        Call(new NoteFocusCommand(NewCompletion()), cancellationToken);

    public ValueTask NoteProcessAliveAsync(CancellationToken cancellationToken = default) =>
        Call(new NoteProcessAliveCommand(NewCompletion()), cancellationToken);

    public ValueTask RequestControlAsync(CancellationToken cancellationToken = default) =>
        Call(new RequestControlCommand(NewCompletion()), cancellationToken);

    public ValueTask ConfirmTakeoverAsync(string handle, CancellationToken cancellationToken = default) =>
        Call(new ConfirmTakeoverCommand(handle, NewCompletion()), cancellationToken);

    public ValueTask CancelAcquireAsync(CancellationToken cancellationToken = default) =>
        Call(new CancelAcquireCommand(NewCompletion()), cancellationToken);

    public ValueTask ReleaseControlAsync(CancellationToken cancellationToken = default) =>
        Call(new ReleaseControlCommand(NewCompletion()), cancellationToken);

    public ValueTask RecoverObserveAsync(CancellationToken cancellationToken = default) =>
        Call(new RecoverObserveCommand(NewCompletion()), cancellationToken);

    public ValueTask<InputSubmissionOutcome> SubmitInputAsync(
        RendererInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        return CallOutcome(new SubmitInputCommand(input, NewOutcome()), cancellationToken);
    }

    public ValueTask<InputSubmissionOutcome> SubmitResizeAsync(
        TerminalResizeCommand size,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(size);
        return CallOutcome(new SubmitResizeCommand(size, NewOutcome()), cancellationToken);
    }

    public ValueTask<InputSubmissionOutcome> SubmitScrollAsync(
        TerminalScrollCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CallOutcome(new SubmitScrollCommand(request, NewOutcome()), cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_stopping && _loop.IsCompleted)
            return;
        var completion = NewCompletion();
        try
        {
            await _mailbox.Writer.WriteAsync(new LeaseStopCommand(completion)).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            completion.TrySetResult();
        }

        await completion.Task.ConfigureAwait(false);
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }

        _states.Writer.TryComplete();
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach (var message in _mailbox.Reader.ReadAllAsync().ConfigureAwait(false))
                Handle(message);
        }
        finally
        {
            await TeardownAsync().ConfigureAwait(false);
        }
    }

    private void Handle(ControlLeaseMessage message)
    {
        switch (message)
        {
            case OpenPaneCommand command:
                HandleOpen(command.Pane, ControlEvent.OpenPane, command.Completion);
                break;
            case SelectPaneCommand command:
                HandleOpen(command.Pane, ControlEvent.SelectPane, command.Completion);
                break;
            case NoteFocusCommand command:
                Apply(ControlEvent.Focus, ReadStoreOrMissing());
                command.Completion.TrySetResult();
                break;
            case NoteProcessAliveCommand command:
                Apply(ControlEvent.ProcessAlive, ReadStoreOrMissing());
                command.Completion.TrySetResult();
                break;
            case RequestControlCommand command:
                Apply(ControlEvent.RequestControl, ReadStoreOrMissing());
                command.Completion.TrySetResult();
                break;
            case ConfirmTakeoverCommand command:
                HandleConfirm(command);
                break;
            case CancelAcquireCommand command:
                Apply(ControlEvent.CancelAcquire, ReadStoreOrMissing());
                command.Completion.TrySetResult();
                break;
            case ReleaseControlCommand command:
                Apply(ControlEvent.ReleaseControl, ReadStoreOrMissing());
                command.Completion.TrySetResult();
                break;
            case RecoverObserveCommand command:
                Apply(ControlEvent.RecoverObserve, ReadStoreOrMissing());
                command.Completion.TrySetResult();
                break;
            case SubmitInputCommand command:
                HandleInput(command);
                break;
            case SubmitResizeCommand command:
                HandleResize(command);
                break;
            case SubmitScrollCommand command:
                HandleScroll(command);
                break;
            case LeaseStopCommand command:
                _stopping = true;
                RevokeWrite();
                _mailbox.Writer.TryComplete();
                command.Completion.TrySetResult();
                break;
            case TransportOpenedMessage opened:
                HandleOpened(opened);
                break;
            case TransportOpenFailedMessage failed:
                HandleOpenFailed(failed);
                break;
            case TransportEventMessage ev:
                HandleTransportEvent(ev);
                break;
            case ObserveAppliedMessage applied:
                HandleObserveApplied(applied);
                break;
            case PromotionAckedMessage acked:
                HandlePromotionAcked(acked);
                break;
            case PromotionFailedMessage failed:
                HandlePromotionFailed(failed);
                break;
            case WritableAppliedMessage writable:
                if (writable.LeaseGeneration == _leaseGeneration)
                    Publish();
                break;
            case WriteCompletedMessage written:
                HandleWriteCompleted(written);
                break;
        }
    }

    private void HandleOpen(PaneKey pane, ControlEvent evt, TaskCompletionSource completion)
    {
        if (!IsValid(pane))
        {
            _lastCode = ControlLeaseCodes.InvalidIdentity;
            Publish();
            completion.TrySetResult();
            return;
        }

        _target = pane;
        Apply(evt, ReadStore(pane));
        completion.TrySetResult();
    }

    private void HandleConfirm(ConfirmTakeoverCommand command)
    {
        var store = ReadStoreOrMissing();
        var state = Snapshot();
        _pendingConfirmHandle = command.Handle;
        var valid = _challenge is not null && _challenge.Matches(command.Handle, store, state);
        Apply(valid ? ControlEvent.ConfirmTakeover : ControlEvent.ConfirmInvalid, store);
        _pendingConfirmHandle = null;
        command.Completion.TrySetResult();
    }

    private void HandleInput(SubmitInputCommand command)
    {
        var store = ReadStoreOrMissing();
        var decision = TerminalInputCoordinator.Evaluate(Snapshot(), store, command.Input);
        if (!decision.Allowed)
        {
            CompleteDenied(command.Completion, decision.Code);
            return;
        }

        var transport = _control;
        if (transport is null || !_writable)
        {
            CompleteDenied(command.Completion, ControlLeaseCodes.ControlNotVerified);
            return;
        }

        var generation = _leaseGeneration;
        var bytes = command.Input.Bytes.ToArray();
        var pane = _control!.Pane;
        var epoch = _control.Epoch;
        StartEffect(async () =>
        {
            TerminalWriteReceipt receipt;
            try
            {
                receipt = await transport.SendInputAsync(
                    new TerminalInputCommand(pane, epoch, Bytes: bytes)).ConfigureAwait(false);
            }
            catch (Exception)
            {
                receipt = new TerminalWriteReceipt(0, TerminalWriteDisposition.NotSent, ControlLeaseCodes.InputNotSent);
            }
            finally
            {
                Array.Clear(bytes);
            }

            Post(new WriteCompletedMessage(
                generation,
                TerminalInputCoordinator.MapReceipt(receipt, generation, false),
                command.Completion));
        });
    }

    private void HandleResize(SubmitResizeCommand command)
    {
        if (!CanMutateUpstream())
        {
            CompleteDenied(command.Completion, ControlLeaseCodes.ObserveNoResize);
            return;
        }

        var transport = _control;
        if (transport is null ||
            command.Size.Pane != transport.Pane ||
            command.Size.Epoch != transport.Epoch)
        {
            CompleteDenied(
                command.Completion,
                transport is null || command.Size.Pane != transport.Pane
                    ? ControlLeaseCodes.WrongPane
                    : ControlLeaseCodes.StaleEpoch);
            return;
        }

        var generation = _leaseGeneration;
        var size = command.Size;
        StartEffect(async () =>
        {
            TerminalWriteReceipt receipt;
            try
            {
                receipt = await transport.ResizeAsync(size).ConfigureAwait(false);
            }
            catch (Exception)
            {
                receipt = new TerminalWriteReceipt(0, TerminalWriteDisposition.NotSent, ControlLeaseCodes.InputNotSent);
            }

            Post(new WriteCompletedMessage(
                generation,
                TerminalInputCoordinator.MapReceipt(receipt, generation, false),
                command.Completion));
        });
    }

    private void HandleScroll(SubmitScrollCommand command)
    {
        if (!CanMutateUpstream())
        {
            CompleteDenied(command.Completion, ControlLeaseCodes.ObserveScrollDenied);
            return;
        }

        var transport = _control;
        if (transport is null ||
            command.Request.Pane != transport.Pane ||
            command.Request.Epoch != transport.Epoch)
        {
            CompleteDenied(
                command.Completion,
                transport is null || command.Request.Pane != transport.Pane
                    ? ControlLeaseCodes.WrongPane
                    : ControlLeaseCodes.StaleEpoch);
            return;
        }

        var generation = _leaseGeneration;
        var request = command.Request;
        StartEffect(async () =>
        {
            TerminalWriteReceipt receipt;
            try
            {
                receipt = await transport.ScrollAsync(request).ConfigureAwait(false);
            }
            catch (Exception)
            {
                receipt = new TerminalWriteReceipt(0, TerminalWriteDisposition.NotSent, ControlLeaseCodes.InputNotSent);
            }

            Post(new WriteCompletedMessage(
                generation,
                TerminalInputCoordinator.MapReceipt(receipt, generation, false),
                command.Completion));
        });
    }

    private void HandleOpened(TransportOpenedMessage opened)
    {
        if (_stopping || opened.LeaseGeneration != _leaseGeneration)
        {
            StartDispose(opened.Transport);
            return;
        }

        if (opened.Mode == TerminalMode.Observe)
        {
            ReplaceObserve(opened.Transport, opened.Epoch);
            return;
        }

        if (_access != TerminalAccess.Acquiring ||
            !string.Equals(opened.AttemptId, _attemptId, StringComparison.Ordinal))
        {
            StartDispose(opened.Transport);
            return;
        }

        _candidate = opened.Transport;
        _buffer = new CandidateFrameBuffer(_options.CandidateByteLimit);
        _ownershipProved = false;
        _rendererAcked = false;
        _promotionInFlight = false;
        StartPump(opened.Transport, opened.Epoch, TerminalMode.Control, opened.AttemptId, opened.LeaseGeneration);
        Publish();
    }

    private void HandleOpenFailed(TransportOpenFailedMessage failed)
    {
        if (failed.LeaseGeneration != _leaseGeneration)
            return;
        if (failed.Mode == TerminalMode.Observe)
        {
            _access = TerminalAccess.Disconnected;
            _verified = false;
            _lastCode = ControlLeaseCodes.TerminalDisconnected;
            Publish();
            return;
        }

        Apply(ControlEvent.UnknownClassified, ReadStoreOrMissing());
    }

    private void HandleTransportEvent(TransportEventMessage message)
    {
        if (message.LeaseGeneration != _leaseGeneration)
        {
            if (message.Event is IDisposable disposable)
                disposable.Dispose();
            return;
        }

        switch (message.Event)
        {
            case TerminalFrameArrived arrived:
                try
                {
                    HandleFrame(arrived, message.Mode);
                }
                finally
                {
                    arrived.Dispose();
                }

                break;
            case TerminalOwnershipObserved ownership:
                HandleOwnership(ownership, message.Mode, message.AttemptId);
                break;
            case TerminalClosedObserved:
            case TerminalStdoutEnded:
            case TerminalProcessExited:
            case TerminalProtocolFailed:
            case TerminalConsumerBackpressure:
            case TerminalTransportEnded:
                HandleEnded(message.Mode, message.AttemptId);
                break;
        }
    }

    private void HandleFrame(TerminalFrameArrived arrived, TerminalMode mode)
    {
        if (mode == TerminalMode.Observe)
        {
            if (_access == TerminalAccess.Acquiring && _promotionInFlight)
                return;
            if (_control is not null || _observe is null)
                return;
            if (arrived.Pane != _observe.Pane || arrived.Epoch != _observe.Epoch ||
                arrived.Frame.Pane != _observe.Pane || arrived.Frame.Epoch != _observe.Epoch)
                return;
            ApplyObserveFrame(arrived.Frame);
            return;
        }

        if (mode != TerminalMode.Control || _candidate is null || _buffer is null)
            return;
        if (arrived.Pane != _candidate.Pane || arrived.Epoch != _candidate.Epoch ||
            arrived.Frame.Pane != _candidate.Pane || arrived.Frame.Epoch != _candidate.Epoch)
            return;
        if (!_buffer.TryAdd(arrived.Frame, out var code))
        {
            _lastCode = code ?? ControlLeaseCodes.CandidateBackpressure;
            Apply(ControlEvent.UnknownClassified, ReadStoreOrMissing());
            return;
        }

        if (_buffer.HasFullBaseline)
            TryPromote();
    }

    private void HandleOwnership(TerminalOwnershipObserved observed, TerminalMode mode, string? attemptId)
    {
        if (mode != TerminalMode.Control || _candidate is null || _access != TerminalAccess.Acquiring)
            return;
        if (observed.Pane != _candidate.Pane || observed.Epoch != _candidate.Epoch)
            return;
        if (!string.Equals(attemptId, _attemptId, StringComparison.Ordinal) ||
            observed.ControlAttemptId is not null &&
            !string.Equals(observed.ControlAttemptId, _attemptId, StringComparison.Ordinal))
            return;

        var result = observed.Result;
        if (result.Code == "busy" && result.Access == TerminalAccess.Observing && !result.ControlVerified)
        {
            Apply(ControlEvent.BusyClassified, ReadStoreOrMissing(), observed.EventId.ToString());
            return;
        }

        if (result.Code is "rejected" or "takeover_not_confirmed")
        {
            Apply(ControlEvent.RejectedClassified, ReadStoreOrMissing());
            return;
        }

        if (result.ControlVerified &&
            result.Access == TerminalAccess.Controlling &&
            result.Code == "control_verified")
        {
            Apply(ControlEvent.OwnershipMatched, ReadStoreOrMissing());
            return;
        }

        if (result.Code is "unknown_control_signal" or "fictional_granted_rejected" ||
            result.Access == TerminalAccess.Unknown)
        {
            Apply(ControlEvent.OwnershipMissing, ReadStoreOrMissing());
        }
    }

    private void HandleEnded(TerminalMode mode, string? attemptId)
    {
        if (mode == TerminalMode.Control)
        {
            if (!string.Equals(attemptId, _attemptId, StringComparison.Ordinal) && _attemptId is not null)
                return;
            if (_access == TerminalAccess.Controlling)
            {
                Apply(ControlEvent.TransportLost, ReadStoreOrMissing());
                return;
            }

            if (_access == TerminalAccess.Acquiring)
            {
                Apply(ControlEvent.UnknownClassified, ReadStoreOrMissing());
                return;
            }

            CloseCandidate();
            Apply(ControlEvent.CandidateClosed, ReadStoreOrMissing());
            return;
        }

        if (_control is not null && mode == TerminalMode.Observe)
            return;
        Apply(ControlEvent.TransportLost, ReadStoreOrMissing());
    }

    private void HandleObserveApplied(ObserveAppliedMessage applied)
    {
        if (applied.LeaseGeneration != _leaseGeneration)
            return;
        if (applied.Accepted)
            Apply(ControlEvent.FirstFrame, ReadStoreOrMissing());
    }

    private void HandlePromotionAcked(PromotionAckedMessage acked)
    {
        if (acked.LeaseGeneration != _leaseGeneration ||
            !string.Equals(acked.AttemptId, _attemptId, StringComparison.Ordinal))
            return;
        _rendererAcked = true;
        _promotionInFlight = false;
        var store = ReadStoreOrMissing();
        if (!store.Exists || store.Freshness != DeviceFreshness.Current)
        {
            Apply(ControlEvent.StoreRecheckStale, store);
            return;
        }

        Apply(ControlEvent.PromotionCheck, store);
    }

    private void HandlePromotionFailed(PromotionFailedMessage failed)
    {
        if (failed.LeaseGeneration != _leaseGeneration ||
            !string.Equals(failed.AttemptId, _attemptId, StringComparison.Ordinal))
            return;
        _promotionInFlight = false;
        _lastCode = failed.Code;
        Apply(ControlEvent.UnknownClassified, ReadStoreOrMissing());
    }

    private void HandleWriteCompleted(WriteCompletedMessage written)
    {
        var outcome = written.Outcome;
        if (written.LeaseGeneration != _leaseGeneration)
        {
            outcome = outcome with
            {
                Disposition = TerminalWriteDisposition.UnknownAfterDisconnect,
                Code = ControlLeaseCodes.InputOutcomeUnknown
            };
            written.Completion?.TrySetResult(outcome);
            return;
        }

        if (_disconnected)
            outcome = TerminalInputCoordinator.MapReceipt(
                new TerminalWriteReceipt(outcome.CommandId, outcome.Disposition, outcome.Code),
                written.LeaseGeneration,
                true);
        _lastInput = outcome;
        _ledger.Add(outcome);
        if (_ledger.Count > 64)
            _ledger.RemoveRange(0, _ledger.Count - 64);
        Publish();
        written.Completion?.TrySetResult(outcome);
    }

    private void Apply(ControlEvent evt, LeaseTargetSnapshot store, string? busyEvidenceId = null)
    {
        if (_target is { } target)
        {
            _projectionEpoch = store.Pane == target ? store.ProjectionEpoch : _projectionEpoch;
            _projectionRevision = store.Pane == target ? store.ProjectionRevision : _projectionRevision;
        }

        var input = new ControlTransitionInput(
            _access,
            _verified,
            _lastAttempt,
            _challenge is not null && !_challenge.Consumed,
            _candidate is not null,
            _control is not null,
            _observe is not null,
            _ownershipProved,
            _buffer?.HasFullBaseline == true,
            _rendererAcked,
            store.Exists && _target is not null && store.Pane == _target,
            store.Exists && store.Freshness == DeviceFreshness.Current,
            store.Capabilities.HasMutationControl,
            ChallengeValid(store),
            evt);
        var result = ControlTransition.Apply(input);
        Execute(result, store, busyEvidenceId);
    }

    private void Execute(ControlTransitionResult result, LeaseTargetSnapshot store, string? busyEvidenceId)
    {
        var effects = result.Effects;
        if ((effects & ControlTransitionEffects.RevokeWrite) != 0)
            RevokeWrite();
        if ((effects & ControlTransitionEffects.IncrementGeneration) != 0)
            _leaseGeneration++;
        if ((effects & ControlTransitionEffects.InvalidateChallenge) != 0)
            _challenge = null;
        if ((effects & ControlTransitionEffects.ConsumeChallenge) != 0)
        {
            if (_challenge is not null && _pendingConfirmHandle is not null)
                _challenge.TryConsume(_pendingConfirmHandle, store, Snapshot());
            _challenge = null;
        }
        if ((effects & ControlTransitionEffects.LatchOwnership) != 0)
            _ownershipProved = true;
        if ((effects & ControlTransitionEffects.CloseCandidate) != 0)
            CloseCandidate();
        if ((effects & ControlTransitionEffects.CloseControl) != 0)
            CloseControl();
        if ((effects & ControlTransitionEffects.CloseObserve) != 0 &&
            (effects & ControlTransitionEffects.Promote) != 0)
            PromoteCandidate();
        else if ((effects & ControlTransitionEffects.CloseObserve) != 0)
            CloseObserve();

        _access = result.Access;
        _verified = result.ControlVerified;
        _lastAttempt = result.LastAttempt;
        _lastCode = result.Code;
        if (_access != TerminalAccess.Controlling)
            _verified = false;

        if ((effects & ControlTransitionEffects.CreateChallenge) != 0 &&
            _target is { } &&
            _observe is not null)
        {
            _challenge = TakeoverChallenge.Create(
                store,
                _observe.Epoch,
                _attemptId ?? Guid.NewGuid().ToString("N"),
                busyEvidenceId ?? "busy");
        }

        if ((effects & ControlTransitionEffects.OpenObserve) != 0 && _target is { } openPane)
            StartObserve(openPane);
        if ((effects & ControlTransitionEffects.OpenNoTakeoverCandidate) != 0 && _target is { } controlPane)
            StartCandidate(controlPane, null);
        if ((effects & ControlTransitionEffects.OpenTakeoverCandidate) != 0 && _target is { } takeoverPane)
        {
            var attempt = Guid.NewGuid().ToString("N");
            StartCandidate(takeoverPane, new TerminalTakeoverAuthorization(true, attempt), attempt);
        }

        if (_access == TerminalAccess.Acquiring &&
            _ownershipProved &&
            _buffer?.HasFullBaseline == true &&
            !_rendererAcked)
            TryPromote();
        if ((effects & ControlTransitionEffects.SetWritable) != 0 && _access == TerminalAccess.Controlling && _verified)
            SetWritable();

        Diagnose(result.Code, _access == TerminalAccess.Unknown ? DiagnosticOutcome.Failure : DiagnosticOutcome.Success);
        Publish();
    }

    private void TryPromote()
    {
        if (_access != TerminalAccess.Acquiring ||
            !_ownershipProved ||
            _buffer is null ||
            !_buffer.HasFullBaseline ||
            _candidate is null ||
            _promotionInFlight)
            return;
        if (_rendererAcked)
        {
            Apply(ControlEvent.PromotionCheck, ReadStoreOrMissing());
            return;
        }

        _promotionInFlight = true;
        Interlocked.Increment(ref _rendererGeneration);
        var generation = _leaseGeneration;
        var attempt = _attemptId ?? "";
        var epoch = _candidate.Epoch;
        var pane = _candidate.Pane;
        var frames = _buffer.Snapshot();
        var renderer = _renderer;
        StartEffect(async () =>
        {
            try
            {
                await renderer.SetReadOnlyAsync(true).ConfigureAwait(false);
                await renderer.BindAsync(pane, epoch).ConfigureAwait(false);
                ulong last = 0;
                foreach (var frame in frames)
                {
                    var applied = await renderer.ApplyAsync(frame).ConfigureAwait(false);
                    if (!applied.Accepted)
                    {
                        Post(new PromotionFailedMessage(generation, attempt, applied.Code));
                        return;
                    }

                    last = applied.Consumption?.LastParsedSeq ?? frame.Sequence;
                }

                Post(new PromotionAckedMessage(generation, attempt, epoch, last));
            }
            catch (Exception)
            {
                Post(new PromotionFailedMessage(generation, attempt, ControlLeaseCodes.OwnershipUnverified));
            }
        });
    }

    private void PromoteCandidate()
    {
        CloseObserve();
        _control = _candidate;
        _candidate = null;
        _buffer?.Dispose();
        _buffer = null;
        _ownershipProved = true;
        _rendererAcked = true;
    }

    private void SetWritable()
    {
        _writable = true;
        var generation = _leaseGeneration;
        var renderer = _renderer;
        StartEffect(async () =>
        {
            await renderer.SetReadOnlyAsync(false).ConfigureAwait(false);
            Post(new WritableAppliedMessage(generation));
        });
    }

    private void RevokeWrite()
    {
        _writable = false;
        _verified = false;
        if (_access == TerminalAccess.Controlling)
            _access = TerminalAccess.Unknown;
        StartEffect(() => _renderer.SetReadOnlyAsync(true).AsTask());
        MarkLedgerUnknown();
    }

    private void MarkLedgerUnknown()
    {
        _disconnected = true;
        if (_lastInput is { } last && last.LeaseGeneration == _leaseGeneration &&
            last.Disposition != TerminalWriteDisposition.NotSent)
        {
            _lastInput = last with
            {
                Disposition = last.Disposition == TerminalWriteDisposition.NotSent
                    ? TerminalWriteDisposition.NotSent
                    : TerminalWriteDisposition.UnknownAfterDisconnect,
                Code = last.Disposition == TerminalWriteDisposition.NotSent
                    ? ControlLeaseCodes.InputNotSent
                    : ControlLeaseCodes.InputOutcomeUnknown
            };
        }
    }

    private void StartObserve(PaneKey pane)
    {
        CloseObserve();
        _disconnected = false;
        _bindingEpoch++;
        var epoch = new ConnectionEpoch(_bindingEpoch);
        var generation = _leaseGeneration;
        var host = _host;
        StartEffect(async () =>
        {
            try
            {
                var transport = await host.OpenObserveAsync(pane, epoch).ConfigureAwait(false);
                Post(new TransportOpenedMessage(generation, epoch, TerminalMode.Observe, null, false, transport));
            }
            catch (Exception)
            {
                Post(new TransportOpenFailedMessage(
                    generation, TerminalMode.Observe, null, ControlLeaseCodes.TerminalDisconnected));
            }
        });
    }

    private void StartCandidate(PaneKey pane, TerminalTakeoverAuthorization? takeover, string? attempt = null)
    {
        CloseCandidate();
        _attemptId = attempt ?? Guid.NewGuid().ToString("N");
        if (takeover is not null)
            takeover = new TerminalTakeoverAuthorization(true, _attemptId);
        _ownershipProved = false;
        _rendererAcked = false;
        _promotionInFlight = false;
        _bindingEpoch++;
        var epoch = new ConnectionEpoch(_bindingEpoch);
        var generation = _leaseGeneration;
        var id = _attemptId;
        var host = _host;
        StartEffect(async () =>
        {
            try
            {
                var transport = await host.OpenControlCandidateAsync(pane, epoch, id, takeover)
                    .ConfigureAwait(false);
                Post(new TransportOpenedMessage(
                    generation, epoch, TerminalMode.Control, id, takeover?.Confirmed == true, transport));
            }
            catch (Exception)
            {
                Post(new TransportOpenFailedMessage(
                    generation, TerminalMode.Control, id, ControlLeaseCodes.OwnershipUnverified));
            }
        });
    }

    private void ReplaceObserve(ITerminalTransport transport, ConnectionEpoch epoch)
    {
        CloseObserve();
        _observe = transport;
        Interlocked.Increment(ref _rendererGeneration);
        if (_access is TerminalAccess.Disconnected or TerminalAccess.Unknown)
        {
            _access = TerminalAccess.Observing;
            if (_lastCode != ControlLeaseCodes.ControlBusy)
                _lastCode = ControlLeaseCodes.Observing;
        }
        StartEffect(async () =>
        {
            await _renderer.SetReadOnlyAsync(true).ConfigureAwait(false);
            await _renderer.BindAsync(transport.Pane, epoch).ConfigureAwait(false);
        });
        StartPump(transport, epoch, TerminalMode.Observe, null, _leaseGeneration);
        Publish();
    }

    private void ApplyObserveFrame(TerminalOwnedFrame frame)
    {
        var generation = _leaseGeneration;
        var renderGeneration = Volatile.Read(ref _rendererGeneration);
        var epoch = frame.Epoch;
        var sequence = frame.Sequence;
        var copy = new TerminalFrame(frame.Sequence, frame.Columns, frame.Rows, frame.Full, frame.Bytes.ToArray());
        var renderer = _renderer;
        StartEffect(async () =>
        {
            try
            {
                if (renderGeneration != Volatile.Read(ref _rendererGeneration))
                    return;
                var applied = await renderer.ApplyAsync(copy).ConfigureAwait(false);
                Post(new ObserveAppliedMessage(generation, epoch, sequence, applied.Accepted, applied.Code));
            }
            catch (Exception)
            {
                Post(new ObserveAppliedMessage(
                    generation, epoch, sequence, false, ControlLeaseCodes.TerminalDisconnected));
            }
        });
    }

    private void StartPump(
        ITerminalTransport transport,
        ConnectionEpoch epoch,
        TerminalMode mode,
        string? attemptId,
        long generation)
    {
        var ct = _lifetime?.Token ?? CancellationToken.None;
        StartEffect(async () =>
        {
            try
            {
                await foreach (var item in transport.ReadEventsAsync(ct).ConfigureAwait(false))
                    Post(new TransportEventMessage(generation, epoch, mode, attemptId, item));
            }
            catch (OperationCanceledException)
            {
            }
            catch (ChannelClosedException)
            {
            }
            catch (Exception)
            {
                Post(new TransportEventMessage(
                    generation,
                    epoch,
                    mode,
                    attemptId,
                    new TerminalTransportEnded(0, transport.Pane, epoch, ControlLeaseCodes.TerminalDisconnected, null, false)));
            }
        });
    }

    private void CloseCandidate()
    {
        var candidate = _candidate;
        _candidate = null;
        _buffer?.Dispose();
        _buffer = null;
        _ownershipProved = false;
        _rendererAcked = false;
        _promotionInFlight = false;
        if (candidate is null)
            return;
        StartEffect(async () =>
        {
            try
            {
                if (candidate.Mode == TerminalMode.Control)
                    await candidate.ReleaseAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
            }

            await candidate.DisposeAsync().ConfigureAwait(false);
        });
    }

    private void CloseControl()
    {
        var control = _control;
        _control = null;
        _writable = false;
        if (control is null)
            return;
        StartEffect(async () =>
        {
            try
            {
                await control.ReleaseAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
            }

            await control.DisposeAsync().ConfigureAwait(false);
        });
    }

    private void CloseObserve()
    {
        var observe = _observe;
        _observe = null;
        if (observe is null)
            return;
        StartDispose(observe);
    }

    private void StartDispose(ITerminalTransport transport) =>
        StartEffect(() => transport.DisposeAsync().AsTask());

    private bool CanMutateUpstream() =>
        _writable &&
        _control is not null &&
        _access == TerminalAccess.Controlling &&
        _verified;

    private bool ChallengeValid(LeaseTargetSnapshot store) =>
        _challenge is not null &&
        !_challenge.Consumed &&
        store.Exists &&
        store.Freshness == DeviceFreshness.Current &&
        _target == _challenge.Pane &&
        store.Pane == _challenge.Pane &&
        store.ProjectionEpoch == _challenge.ProjectionEpoch &&
        store.ProjectionRevision == _challenge.ProjectionRevision &&
        _access == TerminalAccess.Observing &&
        _lastAttempt == ControlAttemptOutcome.Busy &&
        _observe is not null &&
        _observe.Epoch == _challenge.ObserveEpoch;

    private LeaseTargetSnapshot ReadStoreOrMissing()
    {
        if (_target is not { } pane)
        {
            return new LeaseTargetSnapshot(
                new PaneKey(new SessionKey(new DeviceId(Guid.Empty), "", null), "", ""),
                false,
                new ConnectionEpoch(0),
                0,
                DeviceFreshness.Unknown,
                new CapabilityProfile("", null, 0, 0, "", "UNVERIFIED",
                    System.Collections.Frozen.FrozenSet<string>.Empty),
                "",
                "");
        }

        return ReadStore(pane);
    }

    private LeaseTargetSnapshot ReadStore(PaneKey pane) => _store.Read(pane);

    private ControlLeaseState Snapshot()
    {
        ControlBinding? Bind(ITerminalTransport? transport, string? attempt) =>
            transport is null ? null : new ControlBinding(transport.Pane, transport.Epoch, transport.Mode, attempt);

        var access = _access;
        var verified = _verified && access == TerminalAccess.Controlling;
        if (access != TerminalAccess.Controlling)
            verified = false;
        return new ControlLeaseState(
            _target,
            _projectionEpoch,
            _projectionRevision,
            Bind(_observe, null),
            Bind(_control, _attemptId),
            Bind(_candidate, _attemptId),
            access,
            verified,
            _attemptId,
            _leaseGeneration,
            _lastAttempt,
            _lastCode,
            _challenge?.Consumed == true ? null : _challenge?.View,
            _lastInput);
    }

    private void Publish()
    {
        PruneEffects();
        var state = Snapshot();
        _current = state;
        _states.Writer.TryWrite(state);
    }

    private void Diagnose(string code, DiagnosticOutcome outcome)
    {
        _diagnostics?.TryWrite(new DiagnosticEvent(
            DateTimeOffset.UtcNow,
            "control-lease",
            "transition",
            outcome,
            code,
            _leaseGeneration,
            null,
            null,
            null,
            null));
    }

    private void CompleteDenied(TaskCompletionSource<InputSubmissionOutcome> completion, string code)
    {
        var outcome = new InputSubmissionOutcome(
            NextCommandId(),
            TerminalWriteDisposition.NotSent,
            code,
            _leaseGeneration);
        _lastInput = outcome;
        Publish();
        completion.TrySetResult(outcome);
    }

    private ulong NextCommandId() => ++_nextCommandId;

    private void StartEffect(Func<Task> work)
    {
        var task = Task.Run(async () =>
        {
            try
            {
                await work().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ChannelClosedException)
            {
            }
        });
        _effects.Add(task);
        if (_effects.Count > 16)
            _effects.RemoveAll(item => item.IsCompleted);
    }

    private void Post(ControlLeaseMessage message)
    {
        if (_mailbox.Writer.TryWrite(message))
            return;
        _ = WritePosted(message);
    }

    private async Task WritePosted(ControlLeaseMessage message)
    {
        try
        {
            await _mailbox.Writer.WriteAsync(message).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
        }
    }

    private void PruneEffects()
    {
        if (_effects.Count > 8)
            _effects.RemoveAll(item => item.IsCompleted);
    }

    private async Task TeardownAsync()
    {
        RevokeWrite();
        _challenge = null;
        CloseCandidate();
        CloseControl();
        CloseObserve();
        try
        {
            _lifetime?.Cancel();
        }
        catch (Exception)
        {
        }

        _lifetime?.Dispose();
        _lifetime = null;
        if (_effects.Count > 0)
        {
            try
            {
                await Task.WhenAll(_effects).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }

        try
        {
            await _renderer.SetReadOnlyAsync(true).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private async ValueTask Call(ControlLeaseMessage message, CancellationToken cancellationToken)
    {
        var completion = message switch
        {
            OpenPaneCommand command => command.Completion,
            SelectPaneCommand command => command.Completion,
            NoteFocusCommand command => command.Completion,
            NoteProcessAliveCommand command => command.Completion,
            RequestControlCommand command => command.Completion,
            ConfirmTakeoverCommand command => command.Completion,
            CancelAcquireCommand command => command.Completion,
            ReleaseControlCommand command => command.Completion,
            RecoverObserveCommand command => command.Completion,
            LeaseStopCommand command => command.Completion,
            _ => throw new ArgumentOutOfRangeException(nameof(message))
        };
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        await _mailbox.Writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
        await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<InputSubmissionOutcome> CallOutcome(
        ControlLeaseMessage message,
        CancellationToken cancellationToken)
    {
        var completion = message switch
        {
            SubmitInputCommand command => command.Completion,
            SubmitResizeCommand command => command.Completion,
            SubmitScrollCommand command => command.Completion,
            _ => throw new ArgumentOutOfRangeException(nameof(message))
        };
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        await _mailbox.Writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static TaskCompletionSource NewCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource<InputSubmissionOutcome> NewOutcome() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static bool IsValid(PaneKey pane) =>
        pane.Session.Device.Value != Guid.Empty &&
        !string.IsNullOrWhiteSpace(pane.Session.EndpointKey) &&
        !string.IsNullOrWhiteSpace(pane.WorkspaceId) &&
        !string.IsNullOrWhiteSpace(pane.PaneId);
}
