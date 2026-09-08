using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Text.Json;
using System.Threading.Channels;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal sealed class CountingNotificationSink : ISessionNotificationSink
{
    public int Calls;
    public readonly List<string> Kinds = [];

    public void OnLifecycleNotification(string kind, DeviceSessionState state)
    {
        _ = state;
        lock (Kinds)
            Kinds.Add(kind);
        Interlocked.Increment(ref Calls);
    }
}

internal sealed class RecordingSink : IDiagnosticSink
{
    public List<DiagnosticEvent> Events { get; } = [];
    public long DroppedCount => 0;

    public bool TryWrite(DiagnosticEvent evt)
    {
        lock (Events)
            Events.Add(evt);
        return true;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class ControllableTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
    private readonly List<ManualTimer> _timers = [];

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
            return _now;
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock (_gate)
            _timers.Add(timer);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan delta)
    {
        DateTimeOffset now;
        List<ManualTimer> due;
        lock (_gate)
        {
            _now += delta;
            now = _now;
            due = _timers.Where(item => item.IsDue(now)).ToList();
        }

        foreach (var timer in due)
            timer.Fire();
    }

    internal DateTimeOffset Now
    {
        get
        {
            lock (_gate)
                return _now;
        }
    }

    internal object Gate => _gate;

    internal void Remove(ManualTimer timer)
    {
        lock (_gate)
            _timers.Remove(timer);
    }
}

internal sealed class ManualTimer : ITimer
{
    private readonly ControllableTimeProvider _owner;
    private readonly TimerCallback _callback;
    private readonly object? _state;
    private DateTimeOffset? _due;
    private TimeSpan _period = Timeout.InfiniteTimeSpan;
    private int _disposed;

    public ManualTimer(ControllableTimeProvider owner, TimerCallback callback, object? state)
    {
        _owner = owner;
        _callback = callback;
        _state = state;
    }

    public bool Change(TimeSpan dueTime, TimeSpan period)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return false;
        lock (_owner.Gate)
        {
            _period = period;
            _due = dueTime < TimeSpan.Zero ? null : _owner.Now + dueTime;
        }

        return true;
    }

    public bool IsDue(DateTimeOffset now) => _due is { } due && due <= now;

    public void Fire()
    {
        lock (_owner.Gate)
        {
            if (_due is null)
                return;
            _due = _period > TimeSpan.Zero ? _due.Value + _period : null;
        }

        _callback(_state);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _owner.Remove(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeDecoder : IRpcStateDecoder
{
    public DecodedSessionSnapshot Snapshot = DeviceSessionGraphs.Baseline(
        DeviceSessionGraphs.DefaultSession(), 1);
    public DecodedSessionSnapshot? NextSnapshot;
    public ConcurrentQueue<DecodedRpcEvent> Events { get; } = new();
    public ProjectionEntityChangeSet? NextEntity;
    public string? EntityFailCode;

    public DecodeResult<DecodedSessionSnapshot> DecodeSnapshot(
        JsonElement document,
        SessionKey session,
        ConnectionEpoch epoch,
        SchemaCompatibilityBinding binding)
    {
        _ = (document, binding);
        var value = NextSnapshot ?? Snapshot;
        NextSnapshot = null;
        return DecodeResult<DecodedSessionSnapshot>.Ok(value with { Session = session, Epoch = epoch });
    }

    public DecodeResult<DecodedRpcEvent> DecodeEvent(
        JsonElement document,
        SessionKey session,
        ConnectionEpoch epoch,
        SchemaCompatibilityBinding binding)
    {
        _ = (document, binding);
        if (!Events.TryDequeue(out var evt))
            return DecodeResult<DecodedRpcEvent>.Fail(ProjectionCodes.FullSnapshotRequired);
        return DecodeResult<DecodedRpcEvent>.Ok(evt with { Session = session, Epoch = epoch });
    }

    public DecodeResult<ProjectionEntityChangeSet> DecodeEntityRead(
        string operation,
        JsonElement document,
        SessionKey session,
        ConnectionEpoch epoch,
        SchemaCompatibilityBinding binding)
    {
        _ = (operation, document, epoch, binding);
        if (EntityFailCode is not null)
            return DecodeResult<ProjectionEntityChangeSet>.Fail(EntityFailCode);
        if (NextEntity is not null)
            return DecodeResult<ProjectionEntityChangeSet>.Ok(NextEntity with { Session = session });
        return DecodeEntityFromSnapshot(operation, document, session);
    }

    private DecodeResult<ProjectionEntityChangeSet> DecodeEntityFromSnapshot(
        string operation,
        JsonElement document,
        SessionKey session)
    {
        string? id = null;
        if (document.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "workspace_id", "tab_id", "pane_id", "terminal_id" })
            {
                if (document.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    id = value.GetString();
                    break;
                }

                if (document.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object &&
                    result.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.String)
                {
                    id = value.GetString();
                    break;
                }
            }
        }

        var snap = Snapshot;
        return operation switch
        {
            "workspace.get" => DecodeResult<ProjectionEntityChangeSet>.Ok(new ProjectionEntityChangeSet(
                session, snap.Workspaces.Where(item => item.WorkspaceId == id).ToArray(), [], [], [], [])),
            "tab.get" => DecodeResult<ProjectionEntityChangeSet>.Ok(new ProjectionEntityChangeSet(
                session, [], snap.Tabs.Where(item => item.TabId == id).ToArray(), [], [], [])),
            "pane.get" => DecodeResult<ProjectionEntityChangeSet>.Ok(new ProjectionEntityChangeSet(
                session, [], [], snap.Panes.Where(item => item.PaneId == id).ToArray(), [], [])),
            "agent.get" => DecodeResult<ProjectionEntityChangeSet>.Ok(new ProjectionEntityChangeSet(
                session, [], [], [], snap.Agents.Where(item => item.TerminalId == id).ToArray(), [])),
            "pane.layout" => DecodeResult<ProjectionEntityChangeSet>.Ok(new ProjectionEntityChangeSet(
                session, [], [], [], [], snap.Layouts.ToArray())),
            _ => DecodeResult<ProjectionEntityChangeSet>.Fail(ProjectionCodes.FullSnapshotRequired)
        };
    }
}

internal sealed class FakeRequestConnection : IRpcRequestConnection
{
    private readonly FakeDecoder _decoder;
    private readonly List<string> _methods;
    private readonly TaskCompletionSource _completed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _requested =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _hold;
    private volatile RpcFailure? _failure;
    private int _pending;

    public FakeRequestConnection(ConnectionEpoch epoch, FakeDecoder decoder, List<string> methods)
    {
        Epoch = epoch;
        _decoder = decoder;
        _methods = methods;
    }

    public ConnectionEpoch Epoch { get; }
    public int PendingCount => _pending;
    public int? ChildProcessId => 11;
    public RpcFailure? Failure => _failure;
    public Task WhenCompleted => _completed.Task;
    public Task SnapshotRequested => _requested.Task;
    public RpcFailureKind? NextFailureKind { get; set; }
    public string? NextFailureCode { get; set; }

    public void HoldSnapshot()
    {
        _hold = true;
        _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void ReleaseSnapshot()
    {
        _hold = false;
        _release.TrySetResult();
    }

    public void FailEof() => Complete(new RpcFailure(RpcCodes.RequestLost, RpcFailureKind.ConnectionLost));

    public void FailChildExit() => Complete(new RpcFailure(RpcCodes.ChildExited, RpcFailureKind.ConnectionLost));

    public void FailUnavailable() => Complete(new RpcFailure(RpcCodes.Unavailable, RpcFailureKind.Unavailable));

    public void FailProtocol() => Complete(new RpcFailure(RpcCodes.ProtocolPollution, RpcFailureKind.Protocol));

    private void Complete(RpcFailure failure)
    {
        _failure = failure;
        _completed.TrySetResult();
        _release.TrySetResult();
    }

    public async ValueTask<RpcRequestOutcome> RequestAsync(
        string method,
        JsonElement parameters,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _pending);
        try
        {
            lock (_methods)
                _methods.Add(method);
            if (_failure is not null)
                return new RpcRequestOutcome(_failure);
            if (NextFailureKind is { } kind)
            {
                var code = NextFailureCode ?? RpcCodes.ReconcileFailed;
                NextFailureKind = null;
                return new RpcRequestOutcome(new RpcFailure(code, kind));
            }

            if (method == "session.snapshot")
            {
                _decoder.NextSnapshot = _decoder.Snapshot;
                var requested = _requested;
                _requested = new(TaskCreationOptions.RunContinuationsAsynchronously);
                requested.TrySetResult();
                if (_hold)
                    await _release.Task.ConfigureAwait(false);
                if (_failure is not null)
                    return new RpcRequestOutcome(_failure);
                cancellationToken.ThrowIfCancellationRequested();
                return Ok();
            }

            if (method is "workspace.get" or "tab.get" or "pane.get" or "agent.get" or "pane.layout")
            {
                _decoder.NextEntity = null;
                return Ok(parameters);
            }

            return Ok();
        }
        finally
        {
            Interlocked.Decrement(ref _pending);
        }
    }

    public ValueTask DisposeAsync()
    {
        _failure ??= new RpcFailure(RpcCodes.ConnectionLost, RpcFailureKind.ConnectionLost);
        _completed.TrySetResult();
        _release.TrySetResult();
        return ValueTask.CompletedTask;
    }

    private static RpcRequestOutcome Ok(JsonElement? parameters = null)
    {
        if (parameters is { } value && value.ValueKind == JsonValueKind.Object)
            return new RpcRequestOutcome(JsonDocument.Parse(value.GetRawText()));
        return new RpcRequestOutcome(JsonDocument.Parse("""{"result":{}}"""));
    }
}

internal sealed class FakeSubscriptionConnection : IRpcSubscriptionConnection
{
    private readonly Channel<JsonElement> _events = Channel.CreateBounded<JsonElement>(
        new BoundedChannelOptions(256)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    private readonly TaskCompletionSource<bool> _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _acked;

    public FakeSubscriptionConnection(ConnectionEpoch epoch, bool autoAck)
    {
        Epoch = epoch;
        if (autoAck)
            Acknowledge();
    }

    public ConnectionEpoch Epoch { get; }
    public int? ChildProcessId => 12;
    public RpcFailure? Failure { get; private set; }
    public Task WhenReady => _ready.Task;

    public void Acknowledge()
    {
        _acked = true;
        _ready.TrySetResult(true);
    }

    public void FailAck()
    {
        Failure = new RpcFailure(RpcCodes.SubscribeAckFailed, RpcFailureKind.SubscribeAckFailed);
        _ready.TrySetResult(false);
    }

    public void Emit(JsonElement document)
    {
        if (!_acked)
            return;
        try
        {
            _events.Writer.WriteAsync(document).AsTask().GetAwaiter().GetResult();
        }
        catch (ChannelClosedException)
        {
        }
    }

    public void CompleteEof()
    {
        _events.Writer.TryComplete();
    }

    public void FailOverflow()
    {
        Failure = new RpcFailure(RpcCodes.EventQueueOverflow, RpcFailureKind.EventQueueOverflow);
        _events.Writer.TryComplete();
        _ready.TrySetResult(false);
    }

    public async IAsyncEnumerable<JsonElement> ReadEventsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (Failure is not null)
            yield break;
        await foreach (var item in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return item;
    }

    public ValueTask DisposeAsync()
    {
        Failure ??= new RpcFailure(RpcCodes.ConnectionLost, RpcFailureKind.ConnectionLost);
        _events.Writer.TryComplete();
        _ready.TrySetResult(false);
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeRpcFactory : IRpcConnectionFactory
{
    private readonly FakeDecoder _decoder;
    private readonly object _gate = new();
    private readonly Dictionary<long, (FakeRequestConnection Request, FakeSubscriptionConnection Subscription)>
        _pairs = [];

    public FakeRpcFactory(FakeDecoder decoder) => _decoder = decoder;

    public string Name => "fake-rpc";
    public bool Available => true;
    public bool IsFakeSuccess => false;
    public bool AutoAck { get; set; } = true;
    public bool NextOpenFails { get; set; }
    public List<string> Opens { get; } = [];
    public List<string> Methods { get; } = [];
    public FakeRequestConnection? LastRequest { get; private set; }
    public FakeSubscriptionConnection? LastSubscription { get; private set; }

    public FakeRequestConnection Request(long epoch) => Pair(epoch).Request;

    public FakeSubscriptionConnection Subscription(long epoch) => Pair(epoch).Subscription;

    public ValueTask<IRpcRequestConnection?> OpenRequestAsync(
        SessionKey session,
        ConnectionEpoch epoch,
        string socketPath,
        CancellationToken cancellationToken = default)
    {
        _ = (session, socketPath, cancellationToken);
        lock (_gate)
            Opens.Add("request");
        if (NextOpenFails)
            return ValueTask.FromResult<IRpcRequestConnection?>(null);
        LastRequest = Pair(epoch.Value).Request;
        return ValueTask.FromResult<IRpcRequestConnection?>(LastRequest);
    }

    public ValueTask<IRpcSubscriptionConnection?> OpenSubscriptionAsync(
        SessionKey session,
        ConnectionEpoch epoch,
        string socketPath,
        JsonElement subscribeParameters,
        CancellationToken cancellationToken = default)
    {
        _ = (session, socketPath, subscribeParameters, cancellationToken);
        lock (_gate)
            Opens.Add("subscription");
        if (NextOpenFails)
            return ValueTask.FromResult<IRpcSubscriptionConnection?>(null);
        LastSubscription = Pair(epoch.Value).Subscription;
        return ValueTask.FromResult<IRpcSubscriptionConnection?>(LastSubscription);
    }

    private (FakeRequestConnection Request, FakeSubscriptionConnection Subscription) Pair(long epoch)
    {
        lock (_gate)
        {
            if (_pairs.TryGetValue(epoch, out var pair))
                return pair;
            var request = new FakeRequestConnection(new ConnectionEpoch(epoch), _decoder, Methods);
            var subscription = new FakeSubscriptionConnection(new ConnectionEpoch(epoch), AutoAck);
            pair = (request, subscription);
            _pairs[epoch] = pair;
            return pair;
        }
    }
}

internal sealed class DeviceSessionHarness
{
    public DeviceSessionHarness(SessionKey? session = null)
    {
        Session = session ?? DeviceSessionGraphs.DefaultSession();
        Decoder = new FakeDecoder { Snapshot = DeviceSessionGraphs.Baseline(Session, 1) };
        Factory = new FakeRpcFactory(Decoder);
        Store = new DeviceProjectionStore();
        Time = new ControllableTimeProvider();
        Diagnostics = new RecordingSink();
        Notifications = new CountingNotificationSink();
        Binding = SchemaCompatibilityBinding.PinnedMatchingRuntimeForTests("0.9.0");
        Options = new DeviceSessionOptions
        {
            CoalesceWindow = TimeSpan.FromMilliseconds(250),
            CalibrationPeriod = TimeSpan.FromHours(1),
            SessionAlias = "sess",
            Notifications = Notifications
        };
        Actor = new DeviceSession(Session, Factory, Decoder, Store, Binding, Time, Diagnostics, Options);
    }

    public SessionKey Session { get; }
    public FakeDecoder Decoder { get; }
    public FakeRpcFactory Factory { get; }
    public DeviceProjectionStore Store { get; }
    public ControllableTimeProvider Time { get; }
    public RecordingSink Diagnostics { get; }
    public CountingNotificationSink Notifications { get; }
    public SchemaCompatibilityBinding Binding { get; }
    public DeviceSessionOptions Options { get; }
    public DeviceSession Actor { get; }

    public void Connect() =>
        Actor.ConnectAsync("fake").AsTask().GetAwaiter().GetResult();

    public void Disconnect() =>
        Actor.DisconnectAsync().AsTask().GetAwaiter().GetResult();

    public void RetryNow() =>
        Actor.RetryNowAsync().AsTask().GetAwaiter().GetResult();

    public void NotifyAppStopping() =>
        Actor.NotifyAppStoppingAsync().AsTask().GetAwaiter().GetResult();

    public void DisposeActor() =>
        Actor.DisposeAsync().AsTask().GetAwaiter().GetResult();

    public void Emit(DecodedRpcEvent evt)
    {
        Decoder.Events.Enqueue(evt);
        using var document = JsonDocument.Parse("""{"event":"pane_updated","data":{"pane_id":"p1"}}""");
        Factory.LastSubscription?.Emit(document.RootElement.Clone());
    }

    public DeviceSessionState WaitFor(Func<DeviceSessionState, bool> pred) =>
        DeviceSessionWait.Until(Actor, pred);
}

internal static class DeviceSessionWait
{
    public static void Gate(Task task)
    {
        if (!task.Wait(TimeSpan.FromSeconds(5)))
            throw new Exception("gate_timeout");
    }

    public static DeviceSessionState Until(DeviceSession session, Func<DeviceSessionState, bool> pred)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return UntilAsync(session, pred, cts.Token).GetAwaiter().GetResult();
    }

    static async Task<DeviceSessionState> UntilAsync(
        DeviceSession session,
        Func<DeviceSessionState, bool> pred,
        CancellationToken cancellationToken)
    {
        try
        {
            if (pred(session.Current))
                return session.Current;
            await foreach (var state in session.ReadStatesAsync(cancellationToken).ConfigureAwait(false))
            {
                if (pred(state))
                    return state;
                if (pred(session.Current))
                    return session.Current;
            }
        }
        catch (OperationCanceledException)
        {
        }

        throw new Exception(
            "wait_timeout phase=" + session.Current.Phase +
            " error=" + session.Current.LastErrorCode +
            " cause=" + session.Current.Recovery.Cause +
            " decision=" + session.Current.Recovery.Decision +
            " attempt=" + session.Current.Recovery.Attempt +
            " retryTimer=" + session.Current.Recovery.RetryTimerCount +
            " epoch=" + session.Current.Epoch.Value);
    }
}

internal static class DeviceSessionGraphs
{
    public static SessionKey DefaultSession() =>
        new(new DeviceId(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")), "local-api", "dev");

    public static FrozenDictionary<string, JsonElement> NoJson() => FrozenDictionary<string, JsonElement>.Empty;

    public static WireEnum<AgentStatusKind> Idle() => new("idle", AgentStatusKind.Idle);

    public static DecodedWorkspace Workspace(string id = "w1", string tab = "t1", string label = "lab") =>
        new(id, 1, label, true, 1, 1, tab, Idle(), null, null, NoJson());

    public static DecodedTab Tab(string id = "t1", string workspace = "w1") =>
        new(id, workspace, 1, "main", true, 1, Idle(), NoJson());

    public static DecodedPane Pane(
        string id = "p1",
        string workspace = "w1",
        string tab = "t1",
        string terminal = "term-1",
        string? label = "shell") =>
        new(id, terminal, workspace, tab, true, null, null, label, "claude", KnownAgentKind.Claude,
            null, null, null, Idle(), null, null, null, 1, NoJson());

    public static DecodedLayout Layout(string workspace = "w1", string tab = "t1", string pane = "p1") =>
        new(workspace, tab, false, new DecodedLayoutRect(0, 0, 80, 24), pane,
            [new DecodedLayoutPane(pane, true, new DecodedLayoutRect(0, 0, 80, 24))],
            [], NoJson());

    public static DecodedAgent Agent(
        string terminal = "term-1",
        string workspace = "w1",
        string tab = "t1",
        string pane = "p1") =>
        new(terminal, workspace, tab, pane, null, "claude", KnownAgentKind.Claude, null, null, Idle(),
            true, 1, NoJson());

    public static DecodedSessionSnapshot Baseline(SessionKey session, long epoch, string pane = "p1") =>
        new(session, new ConnectionEpoch(epoch), "0.9.0", 22, "w1", "t1", pane,
            [Workspace()], [Tab()], [Pane(pane)], [Layout(pane: pane)], [Agent(pane: pane)], NoJson());

    public static DecodedSessionSnapshot AfterCreateCloseStatus(SessionKey session, long epoch) =>
        new(session, new ConnectionEpoch(epoch), "0.9.0", 22, "w2", "t2", "p2",
            [Workspace("w1", "t1"), Workspace("w2", "t2", "other")],
            [Tab("t1", "w1"), Tab("t2", "w2")],
            [Pane("p2", "w2", "t2", "term-2", "busy")],
            [Layout("w2", "t2", "p2")],
            [Agent("term-2", "w2", "t2", "p2")],
            NoJson());

    public static DecodedRpcEvent CreateWorkspace(SessionKey session, long epoch) =>
        new(session, new ConnectionEpoch(epoch),
            new WireEnum<RpcEventKind>("workspace.created", RpcEventKind.WorkspaceCreated),
            "w2", "t2", "p2", Workspace("w2", "t2", "other"), Tab("t2", "w2"),
            Pane("p2", "w2", "t2", "term-2"), Layout("w2", "t2", "p2"), null, NoJson());

    public static DecodedRpcEvent ClosePane(SessionKey session, long epoch) =>
        new(session, new ConnectionEpoch(epoch),
            new WireEnum<RpcEventKind>("pane.closed", RpcEventKind.PaneClosed),
            "w1", "t1", "p1", null, null, null, null, null, NoJson());

    public static DecodedRpcEvent StatusPane(SessionKey session, long epoch, string pane = "p1") =>
        new(session, new ConnectionEpoch(epoch),
            new WireEnum<RpcEventKind>("pane.updated", RpcEventKind.PaneUpdated),
            "w1", "t1", pane, null, null, Pane(pane, label: "busy"), null, null, NoJson());

    public static DecodedRpcEvent Unknown(SessionKey session, long epoch) =>
        new(session, new ConnectionEpoch(epoch),
            new WireEnum<RpcEventKind>("future.event", null),
            null, null, null, null, null, null, null, null, NoJson());

    public static void CheckEquivalent(DeviceSessionState state, DecodedSessionSnapshot authority)
    {
        var mapped = ProjectionMapper.MapSnapshot(
            authority with { Session = state.Session, Epoch = state.Epoch },
            state.Capabilities);
        if (!mapped.Succeeded)
            throw new Exception("authority_map_failed " + mapped.Code);
        var actual = state.Projection.Devices.Single().Sessions.Single();
        var expected = mapped.Graph!.SessionState;
        if (!actual.Workspaces.SequenceEqual(expected.Workspaces) ||
            !actual.Tabs.SequenceEqual(expected.Tabs) ||
            !actual.Panes.SequenceEqual(expected.Panes) ||
            !actual.Agents.SequenceEqual(expected.Agents))
            throw new Exception("graph_not_equivalent");
    }
}
