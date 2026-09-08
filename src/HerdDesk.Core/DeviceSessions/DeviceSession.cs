using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using HerdDesk.Contracts;

namespace HerdDesk.Core;

public sealed class DeviceSession : IDeviceSession
{
    private static readonly JsonElement EmptyObject = CreateEmptyObject();

    private readonly SessionKey _session;
    private readonly IRpcConnectionFactory _factory;
    private readonly IRpcStateDecoder _decoder;
    private readonly DeviceProjectionStore _store;
    private readonly SchemaCompatibilityBinding _binding;
    private readonly TimeProvider _time;
    private readonly IDiagnosticSink _diagnostics;
    private readonly DeviceSessionOptions _options;
    private readonly Channel<ActorMessage> _mailbox;
    private readonly Channel<DeviceSessionState> _states;
    private readonly HashSet<ulong> _inflightOps = [];
    private readonly List<Task> _effects = [];
    private readonly Task _loop;

    private volatile DeviceSessionState _current;
    private ConnectionEpoch _epoch;
    private ConnectionPhase _phase = ConnectionPhase.Offline;
    private DeviceFreshness _freshness = DeviceFreshness.Unknown;
    private CapabilityProfile _capabilities;
    private DirtyTracker _dirty = new();
    private CancellationTokenSource? _epochCts;
    private IRpcRequestConnection? _request;
    private IRpcSubscriptionConnection? _subscription;
    private ITimer? _coalesce;
    private ITimer? _calibration;
    private TaskCompletionSource? _connectWait;
    private TaskCompletionSource? _stopWait;
    private string? _lastError;
    private long _nextOperation;
    private int _acceptedInvalidations;
    private int _missedReconcile;
    private bool _acked;
    private bool _subscriptionActive;
    private bool _baselineInstalled;
    private bool _readInFlight;
    private bool _reconcileAfterRead;
    private bool _coalesceArmed;
    private bool _calibrationArmed;
    private bool _intentionalClose;
    private bool _stopping;
    private ulong _coalesceOp;
    private ulong _calibrationOp;
    private ConnectionEpoch _coalesceEpoch;
    private ConnectionEpoch _calibrationEpoch;

    public DeviceSession(
        SessionKey session,
        IRpcConnectionFactory connections,
        IRpcStateDecoder decoder,
        DeviceProjectionStore store,
        SchemaCompatibilityBinding binding,
        TimeProvider time,
        IDiagnosticSink diagnostics,
        DeviceSessionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(decoder);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (session.Device.Value == Guid.Empty || string.IsNullOrWhiteSpace(session.EndpointKey))
            throw new ArgumentException(ProjectionCodes.InvalidIdentity, nameof(session));
        _session = session;
        _factory = connections;
        _decoder = decoder;
        _store = store;
        _binding = binding;
        _time = time;
        _diagnostics = diagnostics;
        _options = options ?? DeviceSessionOptions.Default;
        if (_options.MailboxCapacity < 1)
            throw new ArgumentOutOfRangeException(nameof(options));
        _capabilities = EmptyCapabilities(binding);
        _mailbox = Channel.CreateBounded<ActorMessage>(new BoundedChannelOptions(_options.MailboxCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        _states = Channel.CreateBounded<DeviceSessionState>(new BoundedChannelOptions(1)
        {
            SingleReader = false,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        _current = SnapshotState();
        _states.Writer.TryWrite(_current);
        _loop = Task.Run(RunAsync);
    }

    public SessionKey Session => _session;
    public ConnectionEpoch Epoch => _current.Epoch;
    public DeviceSessionState Current => _current;

    public async ValueTask ConnectAsync(string socketPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(socketPath);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        await _mailbox.Writer.WriteAsync(new ConnectCommand(socketPath, completion), cancellationToken)
            .ConfigureAwait(false);
        await completion.Task.ConfigureAwait(false);
    }

    public async ValueTask DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _mailbox.Writer.WriteAsync(new DisconnectCommand(completion), cancellationToken)
            .ConfigureAwait(false);
        await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<DeviceSessionState> ReadStatesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return _current;
        await foreach (var state in _states.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return state;
    }

    public async ValueTask DisposeAsync()
    {
        if (_stopping && _loop.IsCompleted)
            return;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await _mailbox.Writer.WriteAsync(new StopCommand(completion)).ConfigureAwait(false);
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
            _stopWait?.TrySetResult();
        }
    }

    private void Handle(ActorMessage message)
    {
        switch (message)
        {
            case ConnectCommand command:
                HandleConnect(command);
                break;
            case DisconnectCommand command:
                HandleDisconnect(command);
                break;
            case StopCommand command:
                HandleStop(command);
                break;
            case ConnectionsOpenedMessage opened:
                HandleOpened(opened);
                break;
            case SubscriptionAckMessage ack:
                HandleAck(ack);
                break;
            case InvalidatedMessage invalidated:
                HandleInvalidated(invalidated);
                break;
            case SnapshotCompletedMessage snapshot:
                HandleSnapshotCompleted(snapshot);
                break;
            case EntityReadCompletedMessage entity:
                HandleEntityCompleted(entity);
                break;
            case ReconcileDueMessage reconcile:
                if (reconcile.Epoch == _epoch)
                    HandleReconcile();
                break;
            case CalibrationDueMessage calibration:
                if (calibration.Epoch == _epoch)
                    HandleCalibration();
                break;
            case ConnectionEndedMessage ended:
                HandleEnded(ended);
                break;
        }

        DrainMissedReconcile();
    }

    private void HandleConnect(ConnectCommand command)
    {
        if (_stopping)
        {
            command.Completion.TrySetResult();
            return;
        }

        _connectWait?.TrySetResult();
        _connectWait = command.Completion;
        BeginEpoch(command.SocketPath);
    }

    private void HandleDisconnect(DisconnectCommand command)
    {
        EnterOffline();
        command.Completion.TrySetResult();
    }

    private void HandleStop(StopCommand command)
    {
        _stopping = true;
        _stopWait = command.Completion;
        EnterOffline();
        _mailbox.Writer.TryComplete();
    }

    private void BeginEpoch(string socketPath)
    {
        _intentionalClose = true;
        CancelEpochEffects();
        var nextValue = _epoch.Value + 1;
        if (nextValue <= 0)
            nextValue = 1;
        _epoch = new ConnectionEpoch(nextValue);
        _intentionalClose = false;
        _epochCts?.Dispose();
        _epochCts = new CancellationTokenSource();
        _acked = false;
        _subscriptionActive = false;
        _baselineInstalled = false;
        _readInFlight = false;
        _reconcileAfterRead = false;
        _coalesceArmed = false;
        _calibrationArmed = false;
        _dirty = new DirtyTracker();
        _inflightOps.Clear();
        _capabilities = EmptyCapabilities(_binding);
        _phase = ConnectionPhase.Connecting;
        _freshness = DeviceFreshness.Refreshing;
        _lastError = null;
        _acceptedInvalidations = 0;
        var cleared = _store.ClearForNewEpoch(_epoch);
        if (!cleared.Succeeded)
        {
            EnterStale(cleared.Code ?? ProjectionCodes.StaleEpoch);
            return;
        }

        Publish();
        var epoch = _epoch;
        var ct = _epochCts.Token;
        StartEffect(() => OpenConnectionsAsync(socketPath, epoch, ct));
    }

    private async Task OpenConnectionsAsync(string socketPath, ConnectionEpoch epoch, CancellationToken ct)
    {
        IRpcRequestConnection? request = null;
        IRpcSubscriptionConnection? subscription = null;
        try
        {
            var subscriptionTask = _factory
                .OpenSubscriptionAsync(_session, epoch, socketPath, EmptyObject, ct).AsTask();
            var requestTask = _factory.OpenRequestAsync(_session, epoch, socketPath, ct).AsTask();
            await Task.WhenAll(subscriptionTask, requestTask).ConfigureAwait(false);
            subscription = subscriptionTask.Result;
            request = requestTask.Result;
            if (request is null || subscription is null)
            {
                if (request is not null)
                    await request.DisposeAsync().ConfigureAwait(false);
                if (subscription is not null)
                    await subscription.DisposeAsync().ConfigureAwait(false);
                Post(new ConnectionEndedMessage(
                    epoch, NextOp(), RpcCodes.Unavailable, RpcFailureKind.Unavailable, false, false));
                return;
            }

            Post(new ConnectionsOpenedMessage(epoch, request, subscription));
        }
        catch (OperationCanceledException)
        {
            if (request is not null)
                await request.DisposeAsync().ConfigureAwait(false);
            if (subscription is not null)
                await subscription.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            if (request is not null)
                await request.DisposeAsync().ConfigureAwait(false);
            if (subscription is not null)
                await subscription.DisposeAsync().ConfigureAwait(false);
            Post(new ConnectionEndedMessage(
                epoch, NextOp(), RpcCodes.Unavailable, RpcFailureKind.Unavailable, false, false));
        }
    }

    private void HandleOpened(ConnectionsOpenedMessage message)
    {
        if (message.Epoch != _epoch || _stopping || _intentionalClose ||
            _phase != ConnectionPhase.Connecting || _request is not null)
        {
            StartEffect(async () =>
            {
                await message.Request.DisposeAsync().ConfigureAwait(false);
                await message.Subscription.DisposeAsync().ConfigureAwait(false);
            });
            return;
        }

        _request = message.Request;
        _subscription = message.Subscription;
        var ct = _epochCts?.Token ?? CancellationToken.None;
        StartPump(message.Subscription, message.Epoch, ct);
        StartRequestWatch(message.Request, ct);
        CompleteConnect();
    }

    private void StartPump(
        IRpcSubscriptionConnection subscription,
        ConnectionEpoch epoch,
        CancellationToken ct)
    {
        StartEffect(async () =>
        {
            try
            {
                await subscription.WhenReady.WaitAsync(ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested || subscription.Failure is not null)
                {
                    if (subscription.Failure is not null)
                        Post(ToEnded(epoch, subscription.Failure, false, true));
                    return;
                }

                Post(new SubscriptionAckMessage(epoch, NextOp()));
                await foreach (var document in subscription.ReadEventsAsync(ct).ConfigureAwait(false))
                {
                    await _mailbox.Writer
                        .WriteAsync(new InvalidatedMessage(epoch, NextOp(), document.Clone()), ct)
                        .ConfigureAwait(false);
                }

                if (subscription.Failure is not null)
                    Post(ToEnded(epoch, subscription.Failure, false, true));
                else
                    Post(new ConnectionEndedMessage(
                        epoch, NextOp(), RpcCodes.SubscriptionLost, RpcFailureKind.ConnectionLost, false, true));
            }
            catch (OperationCanceledException)
            {
            }
            catch (ChannelClosedException)
            {
            }
            catch (Exception)
            {
                Post(new ConnectionEndedMessage(
                    epoch, NextOp(), RpcCodes.SubscriptionLost, RpcFailureKind.ConnectionLost, false, true));
            }
        });
    }

    private void StartRequestWatch(IRpcRequestConnection request, CancellationToken ct)
    {
        StartEffect(async () =>
        {
            var epoch = request.Epoch;
            try
            {
                await request.WhenCompleted.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var failure = request.Failure ??
                          new RpcFailure(RpcCodes.RequestLost, RpcFailureKind.ConnectionLost);
            Post(ToEnded(epoch, failure, true, false));
        });
    }

    private void HandleAck(SubscriptionAckMessage message)
    {
        if (message.Epoch != _epoch || _stopping || _acked || _intentionalClose ||
            _phase != ConnectionPhase.Connecting)
            return;
        _acked = true;
        _subscriptionActive = true;
        _phase = ConnectionPhase.Synchronizing;
        _freshness = DeviceFreshness.Refreshing;
        Publish();
        RearmCalibration();
        StartSnapshot(_dirty.Generation);
    }

    private void HandleInvalidated(InvalidatedMessage message)
    {
        if (message.Epoch != _epoch || !_acked || _stopping ||
            _phase is ConnectionPhase.Offline or ConnectionPhase.Stale or ConnectionPhase.Incompatible)
            return;
        _acceptedInvalidations++;
        var decoded = _decoder.DecodeEvent(message.Document, _session, _epoch, _binding);
        if (!decoded.Succeeded)
        {
            if (IsProtocolFailure(decoded.Code))
            {
                EnterStale(decoded.Code ?? RpcCodes.ProtocolPollution);
                return;
            }

            _dirty.Add(DirtyScope.Whole());
        }
        else
        {
            _dirty.Add(ReconcilePlanner.Plan(decoded.Value!, _capabilities, _options.DegradedFullSnapshotOnly));
        }

        if (_phase == ConnectionPhase.Ready)
            _freshness = DeviceFreshness.Refreshing;
        Publish();
        ArmCoalesce();
    }

    private void HandleSnapshotCompleted(SnapshotCompletedMessage message)
    {
        using (message.Outcome)
        {
            if (!AcceptCompletion(message.Epoch, message.OperationId))
                return;
            _readInFlight = false;
            if (_phase is ConnectionPhase.Stale or ConnectionPhase.Offline or ConnectionPhase.Incompatible)
                return;
            if (message.Outcome?.Failure is not null)
            {
                ApplyRequestFailure(message.Outcome.Failure);
                return;
            }

            if (message.Outcome?.Document is null)
            {
                EnterStale(RpcCodes.ReconcileFailed);
                return;
            }

            var decoded = _decoder.DecodeSnapshot(
                message.Outcome.Document.RootElement, _session, _epoch, _binding);
            if (!decoded.Succeeded)
            {
                if (decoded.Code == ProjectionCodes.SchemaIncompatible)
                    EnterIncompatible(decoded.Code);
                else
                    EnterStale(RpcCodes.ReconcileFailed);
                return;
            }

            ApplyDecodedSnapshot(decoded.Value!, message.DirtyGenerationAtRequest);
        }
    }

    private void ApplyDecodedSnapshot(DecodedSessionSnapshot decoded, long generation)
    {
        var capabilities = CapabilityGate.Evaluate(_binding, decoded.Protocol, decoded.Version);
        _capabilities = capabilities;
        var mapped = ProjectionMapper.MapSnapshot(decoded, capabilities);
        if (!mapped.Succeeded)
        {
            EnterStale(mapped.Code ?? RpcCodes.ReconcileFailed);
            return;
        }

        if (mapped.Graph!.Phase == ConnectionPhase.Incompatible || capabilities.VerifiedOperations.Count == 0)
        {
            _ = _store.InstallSnapshot(_epoch, mapped.Graph);
            EnterIncompatible(ProjectionCodes.SchemaIncompatible);
            return;
        }

        var installed = _store.InstallSnapshot(_epoch, mapped.Graph);
        if (!installed.Succeeded)
        {
            var code = installed.Code ?? RpcCodes.ReconcileFailed;
            if (_phase == ConnectionPhase.Ready)
            {
                EnterStale(code);
                return;
            }

            _lastError = code;
            _freshness = DeviceFreshness.Refreshing;
            Diagnose("snapshot-install", DiagnosticOutcome.Failure, code);
            Publish();
            return;
        }

        MarkBaseline();
        _dirty.ClearUpTo(generation);
        FinishReadCycle();
    }

    private void HandleEntityCompleted(EntityReadCompletedMessage message)
    {
        if (!AcceptCompletion(message.Epoch, message.OperationId))
            return;
        _readInFlight = false;
        if (_phase is ConnectionPhase.Stale or ConnectionPhase.Offline or ConnectionPhase.Incompatible)
            return;
        if (message.FailureCode is not null || message.ChangeSet is null)
        {
            StartSnapshot(_dirty.Generation);
            return;
        }

        var installed = _store.InstallEntityRead(_epoch, message.ChangeSet);
        if (!installed.Succeeded)
        {
            Diagnose("entity-install", DiagnosticOutcome.Failure, installed.Code);
            StartSnapshot(_dirty.Generation);
            return;
        }

        _dirty.ClearUpTo(message.DirtyGenerationAtRequest);
        FinishReadCycle();
    }

    private void FinishReadCycle()
    {
        if (_dirty.Count == 0)
            TryReady();
        else
        {
            if (_phase == ConnectionPhase.Ready)
                _freshness = DeviceFreshness.Refreshing;
            Publish();
            ArmCoalesce();
        }

        if (_reconcileAfterRead)
        {
            _reconcileAfterRead = false;
            HandleReconcile();
        }

        RearmCalibration();
    }

    private void HandleReconcile()
    {
        if (_stopping || !_acked || _epoch.Value <= 0)
            return;
        _coalesceArmed = false;
        if (_readInFlight)
        {
            _reconcileAfterRead = true;
            return;
        }

        if (_dirty.Count == 0)
        {
            TryReady();
            return;
        }

        var generation = _dirty.Generation;
        if (_options.DegradedFullSnapshotOnly || _dirty.RequiresFullSnapshot)
            StartSnapshot(generation);
        else
            StartEntityReads(generation, _dirty.Items());
    }

    private void HandleCalibration()
    {
        _calibrationArmed = false;
        if (_stopping || !_acked)
            return;
        if (_readInFlight)
        {
            _reconcileAfterRead = true;
            return;
        }

        StartSnapshot(_dirty.Generation);
    }

    private void HandleEnded(ConnectionEndedMessage message)
    {
        if (message.Epoch != _epoch || _stopping || _intentionalClose)
            return;
        if (_phase is ConnectionPhase.Offline or ConnectionPhase.Stale or ConnectionPhase.Incompatible)
            return;
        if (message.Code == ProjectionCodes.SchemaIncompatible)
        {
            EnterIncompatible(message.Code);
            return;
        }

        var code = message.FromRequest && message.Kind == RpcFailureKind.ConnectionLost
            ? RpcCodes.RequestLost
            : message.FromSubscription && message.Kind == RpcFailureKind.ConnectionLost
                ? RpcCodes.SubscriptionLost
                : message.Code;
        EnterStale(code);
    }

    private void ApplyRequestFailure(RpcFailure failure)
    {
        if (failure.Kind is RpcFailureKind.ConnectionLost or RpcFailureKind.Protocol
            or RpcFailureKind.Unavailable)
        {
            HandleEnded(new ConnectionEndedMessage(
                _epoch, NextOp(), failure.Code, failure.Kind, true, false));
            return;
        }

        EnterStale(RpcCodes.ReconcileFailed);
    }

    private void StartSnapshot(long generation)
    {
        if (_readInFlight)
        {
            _reconcileAfterRead = true;
            return;
        }

        var request = _request;
        if (request is null)
        {
            EnterStale(RpcCodes.RequestLost);
            return;
        }

        _readInFlight = true;
        var epoch = _epoch;
        var op = NextOp();
        _inflightOps.Add(op);
        var ct = _epochCts?.Token ?? CancellationToken.None;
        Diagnose("snapshot", DiagnosticOutcome.Success, null);
        StartEffect(async () =>
        {
            RpcRequestOutcome? outcome = null;
            try
            {
                outcome = await request.RequestAsync("session.snapshot", EmptyObject, ct)
                    .ConfigureAwait(false);
                var owned = outcome;
                outcome = null;
                Post(new SnapshotCompletedMessage(epoch, op, generation, owned));
            }
            catch (OperationCanceledException)
            {
                outcome?.Dispose();
            }
            catch (Exception)
            {
                outcome?.Dispose();
                Post(new SnapshotCompletedMessage(
                    epoch, op, generation,
                    new RpcRequestOutcome(new RpcFailure(RpcCodes.ReconcileFailed, RpcFailureKind.Protocol))));
            }
        });
    }

    private void StartEntityReads(long generation, DirtyScope[] items)
    {
        if (_readInFlight)
        {
            _reconcileAfterRead = true;
            return;
        }

        var request = _request;
        if (request is null)
        {
            EnterStale(RpcCodes.RequestLost);
            return;
        }

        _readInFlight = true;
        var epoch = _epoch;
        var op = NextOp();
        _inflightOps.Add(op);
        var ct = _epochCts?.Token ?? CancellationToken.None;
        Diagnose("entity-read", DiagnosticOutcome.Success, null);
        StartEffect(async () =>
        {
            try
            {
                var change = await ReadEntitiesAsync(request, items, epoch, ct).ConfigureAwait(false);
                Post(new EntityReadCompletedMessage(epoch, op, generation, change.ChangeSet, change.Code));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                Post(new EntityReadCompletedMessage(
                    epoch, op, generation, null, RpcCodes.ReconcileFailed));
            }
        });
    }

    private async Task<(ProjectionEntityChangeSet? ChangeSet, string? Code)> ReadEntitiesAsync(
        IRpcRequestConnection request,
        DirtyScope[] items,
        ConnectionEpoch epoch,
        CancellationToken cancellationToken)
    {
        var workspaces = new List<DecodedWorkspace>();
        var tabs = new List<DecodedTab>();
        var panes = new List<DecodedPane>();
        var agents = new List<DecodedAgent>();
        var layouts = new List<DecodedLayout>();
        foreach (var item in items)
        {
            if (item.WholeSession)
                return (null, ProjectionCodes.FullSnapshotRequired);
            var planned = BuildGetter(item);
            if (planned is null)
                return (null, ProjectionCodes.FullSnapshotRequired);
            using var parameters = planned.Value.Parameters;
            using var outcome = await request
                .RequestAsync(planned.Value.Method, parameters.RootElement, cancellationToken)
                .ConfigureAwait(false);
            if (!outcome.Succeeded || outcome.Document is null)
                return (null, outcome.Failure?.Code ?? RpcCodes.ReconcileFailed);
            var decoded = _decoder.DecodeEntityRead(
                planned.Value.Method, outcome.Document.RootElement, _session, epoch, _binding);
            if (!decoded.Succeeded || decoded.Value is null)
                return (null, decoded.Code ?? ProjectionCodes.FullSnapshotRequired);
            workspaces.AddRange(decoded.Value.Workspaces);
            tabs.AddRange(decoded.Value.Tabs);
            panes.AddRange(decoded.Value.Panes);
            agents.AddRange(decoded.Value.Agents);
            layouts.AddRange(decoded.Value.Layouts);
        }

        return (new ProjectionEntityChangeSet(_session, workspaces, tabs, panes, agents, layouts), null);
    }

    private static (string Method, JsonDocument Parameters)? BuildGetter(DirtyScope scope)
    {
        if (scope.Layout && !string.IsNullOrWhiteSpace(scope.WorkspaceId) &&
            !string.IsNullOrWhiteSpace(scope.TabId))
            return ("pane.layout", Params(("workspace_id", scope.WorkspaceId!), ("tab_id", scope.TabId!)));
        if (!string.IsNullOrWhiteSpace(scope.WorkspaceId))
            return ("workspace.get", Params(("workspace_id", scope.WorkspaceId!)));
        if (!string.IsNullOrWhiteSpace(scope.TabId))
            return ("tab.get", Params(("tab_id", scope.TabId!)));
        if (!string.IsNullOrWhiteSpace(scope.PaneId))
            return ("pane.get", Params(("pane_id", scope.PaneId!)));
        if (!string.IsNullOrWhiteSpace(scope.TerminalId))
            return ("agent.get", Params(("terminal_id", scope.TerminalId!)));
        return null;
    }

    private void TryReady()
    {
        if (_stopping || !_subscriptionActive || !_acked || _dirty.Count != 0 || _readInFlight)
        {
            Publish();
            return;
        }

        if (_phase is ConnectionPhase.Synchronizing or ConnectionPhase.Ready)
        {
            _phase = ConnectionPhase.Ready;
            _freshness = DeviceFreshness.Current;
            _lastError = null;
        }

        Publish();
    }

    private void MarkBaseline()
    {
        if (_baselineInstalled)
            return;
        _baselineInstalled = true;
    }

    private void EnterStale(string code)
    {
        if (_phase == ConnectionPhase.Offline)
            return;
        _lastError = code;
        _phase = ConnectionPhase.Stale;
        _freshness = DeviceFreshness.Stale;
        _subscriptionActive = false;
        _acked = false;
        _readInFlight = false;
        _reconcileAfterRead = false;
        _capabilities = WithoutMutations(_capabilities);
        _ = _store.MarkStale(_epoch, code);
        Diagnose("stale", DiagnosticOutcome.Failure, code);
        Publish();
        CompleteConnect();
        CancelEpochEffects();
    }

    private void EnterIncompatible(string code)
    {
        _lastError = code;
        _phase = ConnectionPhase.Incompatible;
        _freshness = DeviceFreshness.Stale;
        _subscriptionActive = false;
        _acked = false;
        _readInFlight = false;
        _reconcileAfterRead = false;
        _capabilities = _capabilities with { VerifiedOperations = FrozenSet<string>.Empty };
        Diagnose("incompatible", DiagnosticOutcome.Failure, code);
        Publish();
        CompleteConnect();
        CancelEpochEffects();
    }

    private void EnterOffline()
    {
        _intentionalClose = true;
        _phase = ConnectionPhase.Offline;
        _freshness = DeviceFreshness.Unknown;
        _subscriptionActive = false;
        _acked = false;
        _readInFlight = false;
        _reconcileAfterRead = false;
        _capabilities = WithoutMutations(_capabilities);
        if (_epoch.Value > 0)
            _ = _store.MarkStale(_epoch, "offline");
        Publish();
        CompleteConnect();
        CancelEpochEffects();
    }

    private void CompleteConnect()
    {
        _connectWait?.TrySetResult();
        _connectWait = null;
    }

    private void CancelEpochEffects()
    {
        try
        {
            _epochCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _coalesce?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _calibration?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _coalesceArmed = false;
        _calibrationArmed = false;
        var request = _request;
        var subscription = _subscription;
        _request = null;
        _subscription = null;
        if (request is null && subscription is null)
            return;
        StartEffect(async () =>
        {
            if (request is not null)
                await request.DisposeAsync().ConfigureAwait(false);
            if (subscription is not null)
                await subscription.DisposeAsync().ConfigureAwait(false);
        });
    }

    private void ArmCoalesce()
    {
        if (_readInFlight)
        {
            _reconcileAfterRead = true;
            return;
        }

        _coalesceOp = NextOp();
        _coalesceEpoch = _epoch;
        _coalesce ??= _time.CreateTimer(
            _ =>
            {
                if (!TryPost(new ReconcileDueMessage(_coalesceEpoch, _coalesceOp)))
                    Interlocked.Exchange(ref _missedReconcile, 1);
            },
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        _coalesce.Change(_options.CoalesceWindow, Timeout.InfiniteTimeSpan);
        _coalesceArmed = true;
    }

    private void RearmCalibration()
    {
        if (_stopping || !_acked)
            return;
        _calibrationOp = NextOp();
        _calibrationEpoch = _epoch;
        _calibration ??= _time.CreateTimer(
            _ =>
            {
                if (!TryPost(new CalibrationDueMessage(_calibrationEpoch, _calibrationOp)))
                    Interlocked.Exchange(ref _missedReconcile, 1);
            },
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        _calibration.Change(_options.CalibrationPeriod, Timeout.InfiniteTimeSpan);
        _calibrationArmed = true;
    }

    private void DrainMissedReconcile()
    {
        if (Interlocked.Exchange(ref _missedReconcile, 0) != 0)
            HandleReconcile();
    }

    private bool AcceptCompletion(ConnectionEpoch epoch, ulong operationId)
    {
        if (epoch != _epoch)
            return false;
        if (!_inflightOps.Remove(operationId))
        {
            Diagnose("duplicate-completion", DiagnosticOutcome.Dropped, null);
            return false;
        }

        return true;
    }

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

    private void Post(ActorMessage message)
    {
        if (_mailbox.Writer.TryWrite(message))
            return;
        _ = WritePosted(message);
    }

    private async Task WritePosted(ActorMessage message)
    {
        try
        {
            await _mailbox.Writer.WriteAsync(message).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
        }
    }

    private bool TryPost(ActorMessage message) => _mailbox.Writer.TryWrite(message);

    private ulong NextOp() => (ulong)Interlocked.Increment(ref _nextOperation);

    private void Publish()
    {
        PruneEffects();
        var state = SnapshotState();
        _current = state;
        _states.Writer.TryWrite(state);
    }

    private DeviceSessionState SnapshotState() =>
        new(
            _session,
            _epoch,
            _phase,
            _freshness,
            _capabilities,
            _store.Read(),
            _dirty.Generation,
            _dirty.Count,
            _lastError,
            _baselineInstalled,
            _acceptedInvalidations,
            (_coalesceArmed ? 1 : 0) + (_calibrationArmed ? 1 : 0),
            _readInFlight ? 1 : 0,
            _effects.Count(item => !item.IsCompleted));

    private void PruneEffects()
    {
        if (_effects.Count > 8)
            _effects.RemoveAll(item => item.IsCompleted);
    }

    private void Diagnose(string operation, DiagnosticOutcome outcome, string? code)
    {
        var name = operation.Length <= DiagnosticEvent.MaxTokenLength
            ? operation
            : operation[..DiagnosticEvent.MaxTokenLength];
        var alias = _options.SessionAlias;
        if (alias.Length > DiagnosticEvent.MaxTokenLength)
            alias = alias[..DiagnosticEvent.MaxTokenLength];
        _diagnostics.TryWrite(new DiagnosticEvent(
            _time.GetUtcNow(),
            "device-session",
            name,
            outcome,
            code,
            _epoch.Value,
            null,
            null,
            null,
            alias));
    }

    private async Task TeardownAsync()
    {
        _intentionalClose = true;
        try
        {
            _epochCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _coalesce?.Dispose();
        _calibration?.Dispose();
        _coalesce = null;
        _calibration = null;
        var request = _request;
        var subscription = _subscription;
        _request = null;
        _subscription = null;
        if (request is not null)
            await request.DisposeAsync().ConfigureAwait(false);
        if (subscription is not null)
            await subscription.DisposeAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(_effects).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }

        _effects.Clear();
        _readInFlight = false;
        _coalesceArmed = false;
        _calibrationArmed = false;
        Publish();
        _epochCts?.Dispose();
        _states.Writer.TryComplete();
    }

    private static ConnectionEndedMessage ToEnded(
        ConnectionEpoch epoch,
        RpcFailure failure,
        bool fromRequest,
        bool fromSubscription) =>
        new(epoch, 0, failure.Code, failure.Kind, fromRequest, fromSubscription);

    private static bool IsProtocolFailure(string? code) =>
        code is RpcCodes.ProtocolPollution or RpcCodes.EnvelopeInvalid
            or ProjectionCodes.DuplicateJsonKey or ProjectionCodes.FieldTypeInvalid
            or ProjectionCodes.ErrorEnvelope;

    private static CapabilityProfile EmptyCapabilities(SchemaCompatibilityBinding binding) =>
        new(
            binding.CliVersion,
            null,
            binding.Protocol,
            binding.SchemaVersion,
            binding.SchemaDocumentSha256,
            binding.RuntimeSchemaSha256Status,
            FrozenSet<string>.Empty);

    private static CapabilityProfile WithoutMutations(CapabilityProfile profile)
    {
        if (!profile.HasMutationControl)
            return profile;
        var kept = profile.VerifiedOperations
            .Where(item => !SchemaOperations.MutationControl.Contains(item))
            .ToFrozenSet(StringComparer.Ordinal);
        return profile with { VerifiedOperations = kept };
    }

    private static JsonElement CreateEmptyObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private static JsonDocument Params(params (string Name, string Value)[] pairs)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var (name, value) in pairs)
                writer.WriteString(name, value);
            writer.WriteEndObject();
        }

        return JsonDocument.Parse(buffer.ToArray());
    }
}
