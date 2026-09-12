using System.Text.Json;
using System.Threading.Channels;
using HerdDesk.App;
using HerdDesk.App.Composition;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class ObserveConnectionOrchestratorTests
{
    public static IReadOnlyList<(string Name, Action Run)> All =>
    [
        ("observe orchestration fails closed when rpc is unavailable", Unavailable),
        ("observe transport binds and releases app child", TransportLifecycle)
    ];

    private static void Unavailable()
    {
        var catalog = new ProjectionCatalog();
        var orchestrator = new ObserveConnectionOrchestrator(
            new FakeRpc(false), new ThrowingDecoder(), new RecordingDiagnosticSink(), catalog);
        var profile = new DeviceProfile(AppTestHost.DeviceA, "a", ConnectionKinds.Local, "", []);
        var session = SessionProfile.Explicit(@"C:\herdr.sock", EndpointKind.FilesystemPath);
        AppTestHost.Check(!orchestrator.ConnectAsync(profile, session).AsTask().GetAwaiter().GetResult());
        orchestrator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(!catalog.DaemonAvailable);
    }

    private static void TransportLifecycle()
    {
        var exit = new AppExitCoordinator();
        var factory = new FakeTerminalFactory();
        var orchestrator = new ObserveTransportOrchestrator(factory, exit);
        var pane = new PaneKey(AppTestHost.SessionOf(AppTestHost.DeviceA), "w", "p");
        var host = new TerminalHostSession();
        var result = orchestrator.OpenAsync(pane, new ConnectionEpoch(1), "herdr", "target", host)
            .AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(result.Succeeded && host.Pane == pane && host.Epoch == new ConnectionEpoch(1));
        while (host.DequeueOutbound() is not null) { }
        factory.Transport.Emit(new TerminalFrameArrived(
            1,
            pane,
            new ConnectionEpoch(1),
            new TerminalOwnedFrame(pane, new ConnectionEpoch(1), 1, 80, 24, true, [1, 2, 3])));
        byte[]? outbound = null;
        SpinWait.SpinUntil(() => (outbound = host.DequeueOutbound()) is not null, TimeSpan.FromSeconds(1));
        AppTestHost.Check(outbound is not null);
        exit.ExitAsync().AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(factory.Transport.Disposed);
        host.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private sealed class FakeRpc(bool available) : IRpcConnectionFactory
    {
        public string Name => "fake-rpc";
        public bool Available => available;
        public bool IsFakeSuccess => false;
        public ValueTask<IRpcRequestConnection?> OpenRequestAsync(SessionKey s, ConnectionEpoch e, string p, CancellationToken c = default) => ValueTask.FromResult<IRpcRequestConnection?>(null);
        public ValueTask<IRpcSubscriptionConnection?> OpenSubscriptionAsync(SessionKey s, ConnectionEpoch e, string p, JsonElement x, CancellationToken c = default) => ValueTask.FromResult<IRpcSubscriptionConnection?>(null);
    }

    private sealed class ThrowingDecoder : IRpcStateDecoder
    {
        public DecodeResult<DecodedSessionSnapshot> DecodeSnapshot(JsonElement d, SessionKey s, ConnectionEpoch e, SchemaCompatibilityBinding b) => throw new NotSupportedException();
        public DecodeResult<DecodedRpcEvent> DecodeEvent(JsonElement d, SessionKey s, ConnectionEpoch e, SchemaCompatibilityBinding b) => throw new NotSupportedException();
        public DecodeResult<ProjectionEntityChangeSet> DecodeEntityRead(string o, JsonElement d, SessionKey s, ConnectionEpoch e, SchemaCompatibilityBinding b) => throw new NotSupportedException();
    }

    private sealed class FakeTerminalFactory : ITerminalTransportFactory
    {
        public FakeTransport Transport { get; } = new();
        public string Name => "fake-terminal";
        public bool Available => true;
        public bool IsFakeSuccess => true;
        public ValueTask<ITerminalTransport?> OpenAsync(TerminalOpenRequest request, CancellationToken c = default)
        {
            Transport.SetIdentity(request);
            return ValueTask.FromResult<ITerminalTransport?>(Transport);
        }
    }

    private sealed class FakeTransport : ITerminalTransport, IChildProcessIdentity
    {
        private readonly Channel<TerminalTransportEvent> events = Channel.CreateUnbounded<TerminalTransportEvent>();
        private PaneKey pane;
        private ConnectionEpoch epoch;
        public bool Disposed { get; private set; }
        public int ChildProcessId => 4242;
        public PaneKey Pane => pane;
        public ConnectionEpoch Epoch => epoch;
        public TerminalMode Mode => TerminalMode.Observe;
        public async IAsyncEnumerable<TerminalTransportEvent> ReadEventsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken c = default)
        {
            await foreach (var item in events.Reader.ReadAllAsync(c).ConfigureAwait(false))
                yield return item;
        }
        public void SetIdentity(TerminalOpenRequest request) { pane = request.Pane; epoch = request.Epoch; }
        public void Emit(TerminalTransportEvent item) => events.Writer.TryWrite(item);
        public ValueTask<TerminalWriteReceipt> SendInputAsync(TerminalInputCommand i, CancellationToken c = default) => throw new NotSupportedException();
        public ValueTask<TerminalWriteReceipt> ResizeAsync(TerminalResizeCommand i, CancellationToken c = default) => throw new NotSupportedException();
        public ValueTask<TerminalWriteReceipt> ScrollAsync(TerminalScrollCommand i, CancellationToken c = default) => throw new NotSupportedException();
        public ValueTask<TerminalWriteReceipt> ReleaseAsync(CancellationToken c = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() { Disposed = true; events.Writer.TryComplete(); return ValueTask.CompletedTask; }
    }
}
