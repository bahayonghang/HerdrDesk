using System.Runtime.CompilerServices;
using System.Threading.Channels;
using HerdDesk.Contracts;

namespace HerdDesk.Core;

public sealed class ResourceCommandCoordinator : IResourceCommandCoordinator
{
    private readonly IResourceProjectionStore _store;
    private readonly IResourceCommandTransport _commands;
    private readonly IResourceQueryTransport _queries;
    private readonly IDiagnosticSink? _diagnostics;
    private readonly TimeProvider _time;
    private readonly ResourceCommandOptions _options;
    private readonly Channel<ResourceCommandMessage> _mailbox;
    private readonly Channel<ResourceOperation> _states;
    private readonly List<Task> _effects = [];
    private readonly Task _loop;

    private volatile ResourceOperation _current = ResourceOperation.Idle;
    private ResourceKey? _dialogKey;
    private ResourceProjectionStamp? _dialogStamp;
    private ResourceConfirmationToken _token;
    private bool _allowGroupClose;
    private bool _dialogStale;
    private bool _mutationSent;
    private bool _stopping;
    private long _generation;
    private string[] _baselineIds = [];
    private ResourceExpectation? _expectation;
    private ITimer? _rpcTimer;
    private ITimer? _observeTimer;
    private CancellationTokenSource? _rpcCts;

    public ResourceCommandCoordinator(
        IResourceProjectionStore store,
        IResourceCommandTransport commands,
        IResourceQueryTransport queries,
        IDiagnosticSink? diagnostics = null,
        TimeProvider? time = null,
        ResourceCommandOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(queries);
        _store = store;
        _commands = commands;
        _queries = queries;
        _diagnostics = diagnostics;
        _time = time ?? TimeProvider.System;
        _options = options ?? ResourceCommandOptions.Default;
        if (_options.MailboxCapacity < 1)
            throw new ArgumentOutOfRangeException(nameof(options));
        _mailbox = Channel.CreateBounded<ResourceCommandMessage>(new BoundedChannelOptions(_options.MailboxCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        _states = Channel.CreateBounded<ResourceOperation>(new BoundedChannelOptions(1)
        {
            SingleReader = false,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        _current = ResourceOperation.Idle;
        _states.Writer.TryWrite(_current);
        _loop = Task.Run(RunAsync);
    }

    public ResourceOperation Current => _current;

    public async IAsyncEnumerable<ResourceOperation> ReadStatesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return _current;
        await foreach (var state in _states.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return state;
    }

    public ValueTask OpenCreateAsync(
        ResourceOperationKind kind,
        ResourceKey parent,
        string breadcrumb,
        CancellationToken cancellationToken = default) =>
        Call(new OpenCreateCommand(kind, parent, breadcrumb ?? "", NewCompletion()), cancellationToken);

    public ValueTask ValidateDraftAsync(
        string? name,
        string? workingDirectory,
        KnownAgentKind? agentKind,
        CancellationToken cancellationToken = default) =>
        Call(new ValidateDraftCommand(name, workingDirectory, agentKind, NewCompletion()), cancellationToken);

    public ValueTask SubmitCreateAsync(
        string? name,
        string? workingDirectory,
        KnownAgentKind? agentKind,
        CancellationToken cancellationToken = default) =>
        Call(new SubmitCreateCommand(name, workingDirectory, agentKind, NewCompletion()), cancellationToken);

    public ValueTask OpenRenameAsync(
        ResourceKey target,
        ResourceProjectionStamp expected,
        string breadcrumb,
        string currentName,
        CancellationToken cancellationToken = default) =>
        Call(new OpenRenameCommand(target, expected, breadcrumb ?? "", currentName ?? "", NewCompletion()),
            cancellationToken);

    public ValueTask SubmitRenameAsync(
        ResourceConfirmationToken confirmation,
        string newName,
        CancellationToken cancellationToken = default) =>
        Call(new SubmitRenameCommand(confirmation, newName, NewCompletion()), cancellationToken);

    public ValueTask OpenCloseAsync(
        ResourceKey target,
        ResourceProjectionStamp expected,
        string breadcrumb,
        CancellationToken cancellationToken = default) =>
        Call(new OpenCloseCommand(target, expected, breadcrumb ?? "", NewCompletion()), cancellationToken);

    public ValueTask ConfirmCloseAsync(
        ResourceConfirmationToken confirmation,
        bool closeGroup = false,
        CancellationToken cancellationToken = default) =>
        Call(new ConfirmCloseCommand(confirmation, closeGroup, NewCompletion()), cancellationToken);

    public ValueTask NoteSelectionChangedAsync(
        ResourceKey? selected,
        CancellationToken cancellationToken = default) =>
        Call(new NoteSelectionChangedCommand(selected, NewCompletion()), cancellationToken);

    public ValueTask CancelDraftAsync(CancellationToken cancellationToken = default) =>
        Call(new CancelDraftCommand(NewCompletion()), cancellationToken);

    public ValueTask CancelPendingWaitAsync(CancellationToken cancellationToken = default) =>
        Call(new CancelPendingWaitCommand(NewCompletion()), cancellationToken);

    public ValueTask NotifyProjectionAsync(CancellationToken cancellationToken = default) =>
        Call(new NotifyProjectionCommand(NewCompletion()), cancellationToken);

    public ValueTask RefreshOutcomeAsync(CancellationToken cancellationToken = default) =>
        Call(new RefreshOutcomeCommand(NewCompletion()), cancellationToken);

    public ValueTask RetryAfterVerifiedAbsentAsync(CancellationToken cancellationToken = default) =>
        Call(new RetryAfterAbsentCommand(NewCompletion()), cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_stopping && _loop.IsCompleted)
            return;
        var completion = NewCompletion();
        try
        {
            await _mailbox.Writer.WriteAsync(new ResourceStopCommand(completion)).ConfigureAwait(false);
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
        CancelRpc();
        _rpcTimer?.Dispose();
        _observeTimer?.Dispose();
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
            _mailbox.Writer.TryComplete();
        }
    }

    private void Handle(ResourceCommandMessage message)
    {
        switch (message)
        {
            case OpenCreateCommand command:
                HandleOpenCreate(command);
                break;
            case ValidateDraftCommand command:
                HandleValidate(command);
                break;
            case SubmitCreateCommand command:
                HandleSubmitCreate(command);
                break;
            case OpenRenameCommand command:
                HandleOpenRename(command);
                break;
            case SubmitRenameCommand command:
                HandleSubmitRename(command);
                break;
            case OpenCloseCommand command:
                HandleOpenClose(command);
                break;
            case ConfirmCloseCommand command:
                HandleConfirmClose(command);
                break;
            case NoteSelectionChangedCommand command:
                HandleSelectionChanged(command);
                break;
            case CancelDraftCommand command:
                HandleCancelDraft(command);
                break;
            case CancelPendingWaitCommand command:
                HandleCancelWait(command);
                break;
            case NotifyProjectionCommand command:
                TryCompleteFromStore();
                command.Completion.TrySetResult();
                break;
            case RefreshOutcomeCommand command:
                HandleRefresh(command);
                break;
            case RetryAfterAbsentCommand command:
                HandleRetryAfterAbsent(command);
                break;
            case SubmitCompletedMessage completed:
                HandleSubmitCompleted(completed);
                break;
            case QueryCompletedMessage queried:
                HandleQueryCompleted(queried);
                break;
            case TimeoutMessage timeout:
                HandleTimeout(timeout);
                break;
            case ResourceStopCommand command:
                _stopping = true;
                _mailbox.Writer.TryComplete();
                command.Completion.TrySetResult();
                break;
        }
    }

    private void HandleOpenCreate(OpenCreateCommand command)
    {
        if (!BeginDialog())
        {
            command.Completion.TrySetResult();
            return;
        }

        var snapshot = _store.Read();
        var gate = ResourceCommandGate.EvaluateCreate(
            snapshot, command.Kind, command.Parent, null, null,
            command.Kind == ResourceOperationKind.CreateAgent ? KnownAgentKind.Claude : null,
            requireFields: false);
        _dialogKey = command.Parent;
        _dialogStamp = new ResourceProjectionStamp(snapshot.Projection.Epoch, snapshot.Projection.Revision);
        _dialogStale = false;
        _allowGroupClose = false;
        _token = default;
        _baselineIds = CaptureBaseline(snapshot, command.Kind, command.Parent);
        Publish(new ResourceOperation(
            NewCorrelation(),
            command.Kind,
            gate.Allowed ? ResourceOperationState.Validating : ResourceOperationState.Failed,
            command.Parent,
            command.Breadcrumb,
            null,
            command.Kind == ResourceOperationKind.CreateAgent ? KnownAgentKind.Claude : null,
            null,
            false,
            false,
            false,
            false,
            false,
            gate.ErrorKind,
            gate.Allowed ? null : gate.Code,
            null,
            snapshot.Projection.Epoch,
            snapshot.Projection.Revision,
            null));
        command.Completion.TrySetResult();
    }

    private void HandleValidate(ValidateDraftCommand command)
    {
        if (_current.State is not ResourceOperationState.Validating and not ResourceOperationState.Failed
            and not ResourceOperationState.AwaitingConfirmation ||
            _current.Kind is not ResourceOperationKind.CreateWorkspace
                and not ResourceOperationKind.CreateTerminal
                and not ResourceOperationKind.CreateAgent)
        {
            command.Completion.TrySetResult();
            return;
        }

        if (_dialogStale)
        {
            Fail(ResourceCommandCodes.StaleTarget, ResourceErrorKind.StaleTarget, ResourceOperationState.StaleTarget);
            command.Completion.TrySetResult();
            return;
        }

        var parent = _dialogKey ?? _current.Target;
        if (parent is null)
        {
            command.Completion.TrySetResult();
            return;
        }

        var gate = ResourceCommandGate.EvaluateCreate(
            _store.Read(), _current.Kind, parent, command.Name, command.WorkingDirectory, command.AgentKind);
        Publish(_current with
        {
            State = gate.Allowed ? ResourceOperationState.Validating : ResourceOperationState.Failed,
            DraftName = command.Name,
            WorkingDirectoryDisplay = command.WorkingDirectory,
            AgentKind = command.AgentKind ?? _current.AgentKind,
            ErrorKind = gate.ErrorKind,
            Code = gate.Allowed ? null : gate.Code
        });
        command.Completion.TrySetResult();
    }

    private void HandleSubmitCreate(SubmitCreateCommand command)
    {
        if (_mutationSent)
        {
            Fail(ResourceCommandCodes.MutationAlreadySent, ResourceErrorKind.Conflict, _current.State);
            command.Completion.TrySetResult();
            return;
        }

        if (_dialogStale)
        {
            Fail(ResourceCommandCodes.StaleTarget, ResourceErrorKind.StaleTarget, ResourceOperationState.StaleTarget);
            command.Completion.TrySetResult();
            return;
        }

        var parent = _dialogKey ?? _current.Target;
        if (parent is null ||
            _current.Kind is not ResourceOperationKind.CreateWorkspace
                and not ResourceOperationKind.CreateTerminal
                and not ResourceOperationKind.CreateAgent)
        {
            Fail(ResourceCommandCodes.ValidationError, ResourceErrorKind.Validation, ResourceOperationState.Failed);
            command.Completion.TrySetResult();
            return;
        }

        var snapshot = _store.Read();
        var gate = ResourceCommandGate.EvaluateCreate(
            snapshot, _current.Kind, parent, command.Name, command.WorkingDirectory, command.AgentKind);
        if (!gate.Allowed)
        {
            Fail(gate.Code, gate.ErrorKind, ResourceOperationState.Failed);
            command.Completion.TrySetResult();
            return;
        }

        ResourceIntent intent = _current.Kind switch
        {
            ResourceOperationKind.CreateWorkspace => new CreateWorkspaceIntent(
                parent.Session, Trim(command.Name), Trim(command.WorkingDirectory)),
            ResourceOperationKind.CreateTerminal => new CreateTerminalIntent(
                parent, Trim(command.WorkingDirectory), Trim(command.Name)),
            _ => new CreateAgentIntent(parent, command.AgentKind!.Value, command.Name!.Trim())
        };
        _expectation = new ResourceExpectation(
            _current.Kind,
            parent,
            snapshot.Projection.Epoch,
            Trim(command.Name),
            null,
            null,
            parent.PaneId,
            command.AgentKind,
            _baselineIds,
            true,
            false);
        BeginSubmit(intent, snapshot, command.Name, command.WorkingDirectory, command.AgentKind);
        command.Completion.TrySetResult();
    }

    private void HandleOpenRename(OpenRenameCommand command)
    {
        if (!BeginDialog())
        {
            command.Completion.TrySetResult();
            return;
        }

        var snapshot = _store.Read();
        var request = new RenameResourceRequest(
            command.Target, command.Expected, NewToken(), command.CurrentName);
        _token = request.Confirmation;
        var gate = ResourceCommandGate.EvaluateRename(snapshot, request);
        _dialogKey = command.Target;
        _dialogStamp = command.Expected;
        _dialogStale = false;
        _allowGroupClose = false;
        Publish(new ResourceOperation(
            NewCorrelation(),
            ResourceOperationKind.Rename,
            gate.Allowed ? ResourceOperationState.AwaitingConfirmation : ResourceOperationState.Failed,
            command.Target,
            command.Breadcrumb,
            null,
            null,
            command.CurrentName,
            false,
            false,
            false,
            false,
            false,
            gate.ErrorKind,
            gate.Allowed ? null : gate.Code,
            null,
            snapshot.Projection.Epoch,
            snapshot.Projection.Revision,
            _token));
        command.Completion.TrySetResult();
    }

    private void HandleSubmitRename(SubmitRenameCommand command)
    {
        if (_mutationSent)
        {
            Fail(ResourceCommandCodes.MutationAlreadySent, ResourceErrorKind.Conflict, _current.State);
            command.Completion.TrySetResult();
            return;
        }

        if (_dialogStale || _current.State is ResourceOperationState.StaleTarget)
        {
            Fail(ResourceCommandCodes.StaleTarget, ResourceErrorKind.StaleTarget, ResourceOperationState.StaleTarget);
            command.Completion.TrySetResult();
            return;
        }

        if (_current.Kind != ResourceOperationKind.Rename || _dialogKey is null || _dialogStamp is null)
        {
            Fail(ResourceCommandCodes.ValidationError, ResourceErrorKind.Validation, ResourceOperationState.Failed);
            command.Completion.TrySetResult();
            return;
        }

        var snapshot = _store.Read();
        var request = new RenameResourceRequest(
            _dialogKey, _dialogStamp, command.Confirmation, command.NewName);
        if (command.Confirmation.Value != _token.Value)
        {
            Fail(ResourceCommandCodes.StaleConfirmation, ResourceErrorKind.StaleTarget,
                ResourceOperationState.StaleTarget);
            command.Completion.TrySetResult();
            return;
        }

        var gate = ResourceCommandGate.EvaluateRename(snapshot, request);
        if (!gate.Allowed)
        {
            var state = gate.ErrorKind == ResourceErrorKind.StaleTarget
                ? ResourceOperationState.StaleTarget
                : ResourceOperationState.Failed;
            Fail(gate.Code, gate.ErrorKind, state);
            command.Completion.TrySetResult();
            return;
        }

        _expectation = new ResourceExpectation(
            ResourceOperationKind.Rename,
            _dialogKey,
            snapshot.Projection.Epoch,
            command.NewName.Trim(),
            _dialogKey.WorkspaceId,
            _dialogKey.TabId,
            _dialogKey.PaneId,
            null,
            _baselineIds,
            false,
            false);
        BeginSubmit(new RenameResourceIntent(_dialogKey, command.NewName.Trim()), snapshot, command.NewName, null, null);
        command.Completion.TrySetResult();
    }

    private void HandleOpenClose(OpenCloseCommand command)
    {
        if (!BeginDialog())
        {
            command.Completion.TrySetResult();
            return;
        }

        var snapshot = _store.Read();
        _token = NewToken();
        _dialogKey = command.Target;
        _dialogStamp = command.Expected;
        _dialogStale = false;
        _allowGroupClose = false;
        var request = new CloseResourceRequest(command.Target, command.Expected, _token);
        var gate = ResourceCommandGate.EvaluateClose(snapshot, request, _token, false);
        Publish(new ResourceOperation(
            NewCorrelation(),
            ResourceOperationKind.Close,
            gate.Allowed ? ResourceOperationState.AwaitingConfirmation : ResourceOperationState.Failed,
            command.Target,
            command.Breadcrumb,
            null,
            null,
            null,
            false,
            false,
            false,
            false,
            false,
            gate.ErrorKind,
            gate.Allowed ? null : gate.Code,
            null,
            snapshot.Projection.Epoch,
            snapshot.Projection.Revision,
            _token));
        command.Completion.TrySetResult();
    }

    private void HandleConfirmClose(ConfirmCloseCommand command)
    {
        if (_mutationSent)
        {
            Fail(ResourceCommandCodes.MutationAlreadySent, ResourceErrorKind.Conflict, _current.State);
            command.Completion.TrySetResult();
            return;
        }

        if (_dialogStale || _current.State is ResourceOperationState.StaleTarget)
        {
            Fail(ResourceCommandCodes.StaleTarget, ResourceErrorKind.StaleTarget, ResourceOperationState.StaleTarget);
            command.Completion.TrySetResult();
            return;
        }

        if (_current.Kind != ResourceOperationKind.Close || _dialogKey is null || _dialogStamp is null)
        {
            Fail(ResourceCommandCodes.ValidationError, ResourceErrorKind.Validation, ResourceOperationState.Failed);
            command.Completion.TrySetResult();
            return;
        }

        if (_current.NeedsGroupClose && !command.CloseGroup)
        {
            Fail(ResourceCommandCodes.CloseGroupUnconfirmed, ResourceErrorKind.GroupCloseRequired,
                ResourceOperationState.AwaitingConfirmation);
            command.Completion.TrySetResult();
            return;
        }

        if (command.CloseGroup)
            _allowGroupClose = true;
        var snapshot = _store.Read();
        var request = new CloseResourceRequest(
            _dialogKey, _dialogStamp, command.Confirmation, command.CloseGroup);
        if (command.Confirmation.Value != _token.Value)
        {
            Fail(ResourceCommandCodes.StaleConfirmation, ResourceErrorKind.StaleTarget,
                ResourceOperationState.StaleTarget);
            command.Completion.TrySetResult();
            return;
        }

        var gate = ResourceCommandGate.EvaluateClose(snapshot, request, _token, _allowGroupClose);
        if (!gate.Allowed)
        {
            var state = gate.ErrorKind == ResourceErrorKind.StaleTarget
                ? ResourceOperationState.StaleTarget
                : ResourceOperationState.Failed;
            Fail(gate.Code, gate.ErrorKind, state);
            command.Completion.TrySetResult();
            return;
        }

        _expectation = new ResourceExpectation(
            ResourceOperationKind.Close,
            _dialogKey,
            snapshot.Projection.Epoch,
            null,
            _dialogKey.WorkspaceId,
            _dialogKey.TabId,
            _dialogKey.PaneId,
            null,
            _baselineIds,
            false,
            true);
        BeginSubmit(new CloseResourceIntent(_dialogKey, command.CloseGroup), snapshot, null, null, null);
        command.Completion.TrySetResult();
    }

    private void HandleSelectionChanged(NoteSelectionChangedCommand command)
    {
        if (_dialogKey is not null &&
            _current.State is ResourceOperationState.Validating
                or ResourceOperationState.AwaitingConfirmation
                or ResourceOperationState.Failed &&
            !SameDialogTarget(_dialogKey, command.Selected))
        {
            _dialogStale = true;
            Fail(ResourceCommandCodes.StaleTarget, ResourceErrorKind.StaleTarget, ResourceOperationState.StaleTarget);
        }

        command.Completion.TrySetResult();
    }

    private void HandleCancelDraft(CancelDraftCommand command)
    {
        if (_mutationSent)
        {
            EnterUnknown(ResourceCommandCodes.UnknownOutcome, query: false);
            command.Completion.TrySetResult();
            return;
        }

        Publish(_current with
        {
            State = ResourceOperationState.Cancelled,
            Code = ResourceCommandCodes.Cancelled,
            ErrorKind = ResourceErrorKind.None,
            RetryAllowed = false
        });
        ResetDialog();
        command.Completion.TrySetResult();
    }

    private void HandleCancelWait(CancelPendingWaitCommand command)
    {
        if (_current.State is ResourceOperationState.Submitting or ResourceOperationState.Observing
            || _mutationSent)
            EnterUnknown(ResourceCommandCodes.UnknownOutcome, query: false);
        command.Completion.TrySetResult();
    }

    private void HandleRefresh(RefreshOutcomeCommand command)
    {
        if (_current.State is not ResourceOperationState.UnknownOutcome)
        {
            command.Completion.TrySetResult();
            return;
        }

        StartQuery();
        command.Completion.TrySetResult();
    }

    private void HandleRetryAfterAbsent(RetryAfterAbsentCommand command)
    {
        if (!_current.RetryAllowed || _current.State is not ResourceOperationState.UnknownOutcome
                and not ResourceOperationState.Failed)
        {
            Fail(ResourceCommandCodes.RetryWithoutConfirm, ResourceErrorKind.Conflict, _current.State);
            command.Completion.TrySetResult();
            return;
        }

        if (_current.Code != ResourceCommandCodes.NotSent &&
            _expectation is not null &&
            !ResourcePostcondition.MutationDidNotOccur(_store.Read(), _expectation))
        {
            Fail(ResourceCommandCodes.RetryWithoutConfirm, ResourceErrorKind.Conflict, _current.State);
            command.Completion.TrySetResult();
            return;
        }

        _mutationSent = false;
        _generation++;
        Publish(_current with
        {
            State = _current.Kind is ResourceOperationKind.Rename or ResourceOperationKind.Close
                ? ResourceOperationState.AwaitingConfirmation
                : ResourceOperationState.Validating,
            Code = null,
            ErrorKind = ResourceErrorKind.None,
            RetryAllowed = false,
            QueryAfterTimeout = false,
            MutationSent = false
        });
        command.Completion.TrySetResult();
    }

    private void BeginSubmit(
        ResourceIntent intent,
        ResourceStoreSnapshot snapshot,
        string? name,
        string? workingDirectory,
        KnownAgentKind? agentKind)
    {
        _mutationSent = true;
        var generation = ++_generation;
        var correlation = _current.CorrelationId;
        Publish(_current with
        {
            State = ResourceOperationState.Submitting,
            DraftName = name ?? _current.DraftName,
            WorkingDirectoryDisplay = workingDirectory ?? _current.WorkingDirectoryDisplay,
            AgentKind = agentKind ?? _current.AgentKind,
            MutationSent = true,
            RetryAllowed = false,
            ErrorKind = ResourceErrorKind.None,
            Code = null,
            Epoch = snapshot.Projection.Epoch,
            ProjectionRevision = snapshot.Projection.Revision
        });
        Diagnose("submit", DiagnosticOutcome.Success, null);
        CancelRpc();
        _rpcCts = new CancellationTokenSource();
        var ct = _rpcCts.Token;
        ArmTimer(ref _rpcTimer, _options.RpcTimeout, () => Post(new TimeoutMessage(correlation, generation, true)));
        StartEffect(async () =>
        {
            ResourceTransportReceipt receipt;
            try
            {
                receipt = await _commands.SubmitAsync(intent, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                receipt = new ResourceTransportReceipt(ResourceTransportKind.Timeout);
            }
            catch (Exception)
            {
                receipt = new ResourceTransportReceipt(ResourceTransportKind.Protocol, ResourceCommandCodes.Protocol);
            }

            Post(new SubmitCompletedMessage(correlation, generation, receipt));
        });
    }

    private void HandleSubmitCompleted(SubmitCompletedMessage message)
    {
        if (message.CorrelationId != _current.CorrelationId || message.Generation != _generation)
            return;
        if (_current.State is ResourceOperationState.UnknownOutcome)
        {
            if (message.Receipt.Kind is ResourceTransportKind.Result)
                ApplyCreatedIds(
                    message.Receipt.CreatedWorkspaceId,
                    message.Receipt.CreatedTabId,
                    message.Receipt.CreatedPaneId);
            return;
        }

        if (_current.State is ResourceOperationState.Succeeded
            or ResourceOperationState.Cancelled
            or ResourceOperationState.StaleTarget)
            return;
        Disarm(ref _rpcTimer);
        var receipt = message.Receipt;
        if (receipt.Kind is ResourceTransportKind.NotSent)
        {
            _mutationSent = false;
            Fail(ResourceCommandCodes.NotSent, ResourceErrorKind.Transport, ResourceOperationState.Failed, retry: true);
            return;
        }

        if (receipt.Kind is ResourceTransportKind.Unavailable)
        {
            Fail(ResourceCommandCodes.Unavailable, ResourceErrorKind.Transport, ResourceOperationState.Failed);
            return;
        }

        if (receipt.Kind is ResourceTransportKind.ApplicationError)
        {
            var code = receipt.ApplicationCode ?? ResourceCommandCodes.Protocol;
            if (code == ResourceCommandCodes.WorkspaceGroupCloseRequired)
            {
                Disarm(ref _rpcTimer);
                Disarm(ref _observeTimer);
                _mutationSent = false;
                _allowGroupClose = false;
                Publish(_current with
                {
                    State = ResourceOperationState.AwaitingConfirmation,
                    NeedsGroupClose = true,
                    CloseGroup = false,
                    MutationSent = false,
                    ErrorKind = ResourceErrorKind.GroupCloseRequired,
                    Code = code,
                    RetryAllowed = false
                });
                Diagnose("group-close-required", DiagnosticOutcome.Failure, code);
                return;
            }

            Fail(MapApplication(code), MapKind(code), ResourceOperationState.Failed);
            return;
        }

        if (receipt.Kind is ResourceTransportKind.CancelledAfterWrite or ResourceTransportKind.Timeout
            or ResourceTransportKind.ConnectionLost)
        {
            EnterUnknown(MapTransport(receipt.Kind), query: true);
            return;
        }

        if (receipt.Kind is ResourceTransportKind.Protocol)
        {
            Fail(ResourceCommandCodes.Protocol, ResourceErrorKind.Protocol, ResourceOperationState.Failed);
            return;
        }

        ApplyCreatedIds(receipt.CreatedWorkspaceId, receipt.CreatedTabId, receipt.CreatedPaneId);

        Publish(_current with { State = ResourceOperationState.Observing, ErrorKind = ResourceErrorKind.None, Code = null });
        ArmTimer(ref _observeTimer, _options.ObserveTimeout,
            () => Post(new TimeoutMessage(_current.CorrelationId, _generation, false)));
        TryCompleteFromStore();
    }

    private void HandleTimeout(TimeoutMessage message)
    {
        if (message.CorrelationId != _current.CorrelationId || message.Generation != _generation)
            return;
        if (message.Rpc && _current.State == ResourceOperationState.Submitting)
        {
            CancelRpc();
            EnterUnknown(ResourceCommandCodes.Timeout, query: true);
            return;
        }

        if (!message.Rpc && _current.State == ResourceOperationState.Observing)
            EnterUnknown(ResourceCommandCodes.Timeout, query: true);
    }

    private void HandleQueryCompleted(QueryCompletedMessage message)
    {
        if (message.CorrelationId != _current.CorrelationId || message.Generation != _generation)
            return;
        if (_current.State is not ResourceOperationState.UnknownOutcome)
            return;
        var receipt = message.Receipt;
        if (receipt.Kind is not ResourceTransportKind.Result and not ResourceTransportKind.ApplicationError)
            return;
        if (_expectation is null)
            return;

        if (receipt.Kind is ResourceTransportKind.Result)
            ApplyCreatedIds(receipt.WorkspaceId, receipt.TabId, receipt.PaneId);
        if (TrySucceedFromStore())
            return;

        if (_expectation.ExpectCreated)
        {
            if (CreatedIdentityObserved(receipt))
            {
                var created = CreatedFromQuery(receipt);
                if (created is not null)
                {
                    Succeed(created);
                    return;
                }
            }

            if (ResourcePostcondition.MutationDidNotOccur(_store.Read(), _expectation))
                AllowRetryAfterQuery();
            else
                NoteQueryUnresolved();
            return;
        }

        if (_expectation.ExpectAbsent)
        {
            if (receipt.Kind is ResourceTransportKind.ApplicationError && !receipt.EntityPresent)
            {
                Succeed(null);
                return;
            }

            if (receipt.Kind is ResourceTransportKind.Result && receipt.EntityPresent)
            {
                AllowRetryAfterQuery();
                return;
            }

            if (ResourcePostcondition.MutationDidNotOccur(_store.Read(), _expectation))
                AllowRetryAfterQuery();
            else
                NoteQueryUnresolved();
            return;
        }

        if (receipt.Kind is ResourceTransportKind.Result &&
            receipt.EntityPresent &&
            _expectation.ExpectedName is not null &&
            string.Equals(receipt.Name, _expectation.ExpectedName, StringComparison.Ordinal))
        {
            Succeed(_current.Target);
            return;
        }

        if (ResourcePostcondition.MutationDidNotOccur(_store.Read(), _expectation))
            AllowRetryAfterQuery();
        else
            NoteQueryUnresolved();
    }

    private void TryCompleteFromStore()
    {
        if (_current.State != ResourceOperationState.Observing || _expectation is null)
            return;
        TrySucceedFromStore();
    }

    private bool TrySucceedFromStore()
    {
        if (_expectation is null)
            return false;
        var snapshot = _store.Read();
        if (snapshot.Projection.Epoch != _expectation.Epoch)
            return false;
        if (!ResourcePostcondition.Matches(snapshot, _expectation))
            return false;
        var session = ResourceCommandGate.FindSession(snapshot.Projection, _expectation.Target.Session);
        ResourceKey? result = _current.Target;
        if (session is not null)
            result = ResourcePostcondition.CreatedKey(session, _expectation) ?? result;
        Succeed(result);
        return true;
    }

    private void AllowRetryAfterQuery()
    {
        Publish(_current with
        {
            RetryAllowed = true,
            QueryAfterTimeout = true,
            Code = ResourceCommandCodes.QueryRequired
        });
    }

    private void NoteQueryUnresolved()
    {
        Publish(_current with { QueryAfterTimeout = true, Code = ResourceCommandCodes.QueryRequired });
    }

    private void Succeed(ResourceKey? result)
    {
        Disarm(ref _observeTimer);
        Disarm(ref _rpcTimer);
        Publish(_current with
        {
            State = ResourceOperationState.Succeeded,
            ResultKey = result,
            ErrorKind = ResourceErrorKind.None,
            Code = ResourceCommandCodes.Allowed,
            RetryAllowed = false,
            QueryAfterTimeout = _current.QueryAfterTimeout
        });
        Diagnose("succeeded", DiagnosticOutcome.Success, null);
        ResetDialog();
    }

    private void Fail(string code, ResourceErrorKind kind, ResourceOperationState state, bool retry = false)
    {
        Disarm(ref _observeTimer);
        Disarm(ref _rpcTimer);
        Publish(_current with
        {
            State = state,
            ErrorKind = kind,
            Code = code,
            RetryAllowed = retry,
            MutationSent = _mutationSent
        });
        Diagnose("failed", DiagnosticOutcome.Failure, code);
    }

    private void EnterUnknown(string code, bool query)
    {
        Disarm(ref _observeTimer);
        Disarm(ref _rpcTimer);
        Publish(_current with
        {
            State = ResourceOperationState.UnknownOutcome,
            ErrorKind = ResourceErrorKind.Timeout,
            Code = code,
            QueryAfterTimeout = query,
            RetryAllowed = false
        });
        Diagnose("unknown-outcome", DiagnosticOutcome.Failure, code);
        if (query)
            StartQuery();
    }

    private void StartQuery()
    {
        if (_expectation is null || _current.Target is null)
            return;
        var generation = _generation;
        var correlation = _current.CorrelationId;
        var target = _current.Target;
        var query = _expectation.Kind switch
        {
            ResourceOperationKind.CreateWorkspace when !string.IsNullOrWhiteSpace(_expectation.ExpectedWorkspaceId) =>
                new ResourceQueryRequest(
                    target.Session, ResourceQueryKind.Workspace, _expectation.ExpectedWorkspaceId),
            ResourceOperationKind.CreateWorkspace => new ResourceQueryRequest(
                target.Session, ResourceQueryKind.Snapshot),
            ResourceOperationKind.CreateTerminal when !string.IsNullOrWhiteSpace(_expectation.ExpectedTabId) =>
                new ResourceQueryRequest(
                    target.Session, ResourceQueryKind.Tab, target.WorkspaceId, _expectation.ExpectedTabId),
            ResourceOperationKind.CreateTerminal => new ResourceQueryRequest(
                target.Session, ResourceQueryKind.Snapshot, target.WorkspaceId),
            ResourceOperationKind.CreateAgent => new ResourceQueryRequest(
                target.Session, ResourceQueryKind.Agent, target.WorkspaceId, target.TabId, target.PaneId,
                target.TerminalId),
            ResourceOperationKind.Rename when target.Kind == ResourceKind.Workspace => new ResourceQueryRequest(
                target.Session, ResourceQueryKind.Workspace, target.WorkspaceId),
            ResourceOperationKind.Rename when target.Kind == ResourceKind.Tab => new ResourceQueryRequest(
                target.Session, ResourceQueryKind.Tab, target.WorkspaceId, target.TabId),
            ResourceOperationKind.Rename when target.Kind == ResourceKind.Agent => new ResourceQueryRequest(
                target.Session, ResourceQueryKind.Agent, target.WorkspaceId, target.TabId, target.PaneId,
                target.TerminalId),
            ResourceOperationKind.Close when target.Kind == ResourceKind.Workspace => new ResourceQueryRequest(
                target.Session, ResourceQueryKind.Workspace, target.WorkspaceId),
            ResourceOperationKind.Close when target.Kind == ResourceKind.Tab => new ResourceQueryRequest(
                target.Session, ResourceQueryKind.Tab, target.WorkspaceId, target.TabId),
            _ => new ResourceQueryRequest(
                target.Session, ResourceQueryKind.Pane, target.WorkspaceId, target.TabId, target.PaneId)
        };
        Diagnose("query-after-timeout", DiagnosticOutcome.Success, null);
        StartEffect(async () =>
        {
            ResourceQueryReceipt receipt;
            try
            {
                receipt = await _queries.QueryAsync(query).ConfigureAwait(false);
            }
            catch (Exception)
            {
                receipt = new ResourceQueryReceipt(ResourceTransportKind.Protocol, false);
            }

            Post(new QueryCompletedMessage(correlation, generation, receipt));
        });
    }

    private bool BeginDialog()
    {
        if (_mutationSent && _current.State is ResourceOperationState.Submitting
                or ResourceOperationState.Observing or ResourceOperationState.UnknownOutcome)
        {
            Fail(ResourceCommandCodes.OperationInFlight, ResourceErrorKind.Conflict, _current.State);
            return false;
        }

        _mutationSent = false;
        _expectation = null;
        _generation++;
        CancelRpc();
        Disarm(ref _rpcTimer);
        Disarm(ref _observeTimer);
        return true;
    }

    private void ResetDialog()
    {
        _dialogStale = false;
        _allowGroupClose = false;
        _mutationSent = false;
        _expectation = null;
        CancelRpc();
    }

    private void Publish(ResourceOperation operation)
    {
        _current = operation;
        _states.Writer.TryWrite(operation);
    }

    private void Diagnose(string operation, DiagnosticOutcome outcome, string? code)
    {
        var snapshot = _store.Read();
        _diagnostics?.TryWrite(new DiagnosticEvent(
            _time.GetUtcNow(),
            "resource-command",
            operation,
            outcome,
            code,
            snapshot.Projection.Epoch.Value,
            null,
            null,
            null,
            null));
    }

    private ResourceKey? CreatedFromQuery(ResourceQueryReceipt receipt)
    {
        if (_current.Target is null || _expectation is null)
            return null;
        if (IsNewId(receipt.WorkspaceId, _expectation.BaselineIds, _expectation.Target.WorkspaceId) &&
            receipt.WorkspaceId is { } workspaceId)
            return _current.Target with { Kind = ResourceKind.Workspace, WorkspaceId = workspaceId };
        if (IsNewId(receipt.TabId, _expectation.BaselineIds, _expectation.Target.TabId) &&
            receipt.TabId is { } tabId)
            return _current.Target with { Kind = ResourceKind.Tab, TabId = tabId };
        if (_expectation.Kind == ResourceOperationKind.CreateAgent && receipt.EntityPresent &&
            (_expectation.ExpectedAgentKind is null || receipt.AgentKind == _expectation.ExpectedAgentKind))
            return _current.Target with { Kind = ResourceKind.Agent };
        return null;
    }

    private bool CreatedIdentityObserved(ResourceQueryReceipt receipt)
    {
        if (_expectation is null || receipt.Kind is not ResourceTransportKind.Result || !receipt.EntityPresent)
            return false;
        return _expectation.Kind switch
        {
            ResourceOperationKind.CreateWorkspace =>
                IsNewId(receipt.WorkspaceId, _expectation.BaselineIds, _expectation.Target.WorkspaceId),
            ResourceOperationKind.CreateTerminal =>
                IsNewId(receipt.TabId, _expectation.BaselineIds, _expectation.Target.TabId),
            ResourceOperationKind.CreateAgent =>
                receipt.AgentKind is not null && receipt.AgentKind == _expectation.ExpectedAgentKind,
            _ => false
        };
    }

    private void ApplyCreatedIds(string? workspaceId, string? tabId, string? paneId)
    {
        if (_expectation is null)
            return;
        _expectation = _expectation with
        {
            ExpectedWorkspaceId = IsNewId(workspaceId, _expectation.BaselineIds, _expectation.Target.WorkspaceId)
                ? workspaceId
                : _expectation.ExpectedWorkspaceId,
            ExpectedTabId = IsNewId(tabId, _expectation.BaselineIds, _expectation.Target.TabId)
                ? tabId
                : _expectation.ExpectedTabId,
            ExpectedPaneId = string.IsNullOrWhiteSpace(paneId) ? _expectation.ExpectedPaneId : paneId
        };
    }

    private static bool IsNewId(string? id, IReadOnlyList<string> baseline, string? parent)
    {
        if (string.IsNullOrWhiteSpace(id) || id == parent)
            return false;
        foreach (var prior in baseline)
        {
            if (prior == id)
                return false;
        }

        return true;
    }

    private static string MapApplication(string code) => code switch
    {
        "permission_denied" or "forbidden" or "unauthorized" => ResourceCommandCodes.PermissionDenied,
        "not_found" or "unknown_workspace" or "unknown_tab" or "unknown_pane" or "unknown_agent" =>
            ResourceCommandCodes.NotFound,
        "conflict" or "already_exists" => ResourceCommandCodes.Conflict,
        ResourceCommandCodes.WorkspaceGroupCloseRequired => ResourceCommandCodes.WorkspaceGroupCloseRequired,
        "invalid_params" or "invalid_request" => ResourceCommandCodes.ValidationError,
        _ => code
    };

    private static ResourceErrorKind MapKind(string code) => MapApplication(code) switch
    {
        ResourceCommandCodes.PermissionDenied => ResourceErrorKind.Permission,
        ResourceCommandCodes.NotFound => ResourceErrorKind.NotFound,
        ResourceCommandCodes.Conflict => ResourceErrorKind.Conflict,
        ResourceCommandCodes.WorkspaceGroupCloseRequired => ResourceErrorKind.GroupCloseRequired,
        ResourceCommandCodes.ValidationError => ResourceErrorKind.Validation,
        _ => ResourceErrorKind.Unknown
    };

    private static string MapTransport(ResourceTransportKind kind) => kind switch
    {
        ResourceTransportKind.CancelledAfterWrite => ResourceCommandCodes.CancelledAfterWrite,
        ResourceTransportKind.ConnectionLost => ResourceCommandCodes.Transport,
        ResourceTransportKind.Timeout => ResourceCommandCodes.Timeout,
        _ => ResourceCommandCodes.UnknownOutcome
    };

    private static string[] CaptureBaseline(
        ResourceStoreSnapshot snapshot,
        ResourceOperationKind kind,
        ResourceKey parent)
    {
        var session = ResourceCommandGate.FindSession(snapshot.Projection, parent.Session);
        if (session is null)
            return [];
        if (kind == ResourceOperationKind.CreateWorkspace)
        {
            var ids = new string[session.Workspaces.Count];
            for (var i = 0; i < session.Workspaces.Count; i++)
                ids[i] = session.Workspaces[i].WorkspaceId;
            return ids;
        }

        if (kind == ResourceOperationKind.CreateTerminal)
        {
            var ids = new List<string>();
            foreach (var tab in session.Tabs)
            {
                if (tab.WorkspaceId == parent.WorkspaceId)
                    ids.Add(tab.TabId);
            }

            return [.. ids];
        }

        return [];
    }

    private static bool SameDialogTarget(ResourceKey dialog, ResourceKey? selected) =>
        selected is not null && selected == dialog;

    private static string? Trim(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Trim();
    }

    private static ResourceConfirmationToken NewToken() => new(Guid.NewGuid().ToString("N"));

    private static string NewCorrelation() => Guid.NewGuid().ToString("N");

    private static TaskCompletionSource NewCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private async ValueTask Call(ResourceCommandMessage message, CancellationToken cancellationToken)
    {
        var completion = message switch
        {
            OpenCreateCommand command => command.Completion,
            ValidateDraftCommand command => command.Completion,
            SubmitCreateCommand command => command.Completion,
            OpenRenameCommand command => command.Completion,
            SubmitRenameCommand command => command.Completion,
            OpenCloseCommand command => command.Completion,
            ConfirmCloseCommand command => command.Completion,
            NoteSelectionChangedCommand command => command.Completion,
            CancelDraftCommand command => command.Completion,
            CancelPendingWaitCommand command => command.Completion,
            NotifyProjectionCommand command => command.Completion,
            RefreshOutcomeCommand command => command.Completion,
            RetryAfterAbsentCommand command => command.Completion,
            ResourceStopCommand command => command.Completion,
            _ => throw new ArgumentOutOfRangeException(nameof(message))
        };
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        await _mailbox.Writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
        await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void Post(ResourceCommandMessage message)
    {
        if (!_mailbox.Writer.TryWrite(message))
            _ = _mailbox.Writer.WriteAsync(message);
    }

    private void StartEffect(Func<Task> effect)
    {
        var task = Task.Run(effect);
        _effects.Add(task);
        _ = task.ContinueWith(static (_, state) =>
        {
            if (state is List<Task> list)
                list.RemoveAll(item => item.IsCompleted);
        }, _effects, TaskScheduler.Default);
    }

    private void ArmTimer(ref ITimer? timer, TimeSpan due, Action callback)
    {
        timer ??= _time.CreateTimer(_ => callback(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        timer.Change(due, Timeout.InfiniteTimeSpan);
    }

    private static void Disarm(ref ITimer? timer) =>
        timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

    private void CancelRpc()
    {
        try
        {
            _rpcCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _rpcCts?.Dispose();
        _rpcCts = null;
    }
}
