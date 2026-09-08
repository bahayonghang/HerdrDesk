using HerdDesk.Contracts;
using HerdDesk.Core;

internal sealed class FakeResourceStore : IResourceProjectionStore
{
    public ResourceStoreSnapshot Snapshot { get; set; } = new(DeviceProjectionSnapshot.Empty, DeviceFreshness.Unknown);

    public ResourceStoreSnapshot Read() => Snapshot;
}

internal sealed class FakeResourceTransport : IResourceCommandTransport, IResourceQueryTransport
{
    public readonly List<ResourceIntent> Intents = [];
    public readonly List<ResourceQueryRequest> Queries = [];
    public ResourceTransportReceipt SubmitReceipt { get; set; } = new(ResourceTransportKind.Result);
    public ResourceQueryReceipt QueryReceipt { get; set; } = new(ResourceTransportKind.Result, false);
    public TaskCompletionSource<ResourceTransportReceipt>? HoldSubmit { get; set; }

    public async ValueTask<ResourceTransportReceipt> SubmitAsync(
        ResourceIntent intent,
        CancellationToken cancellationToken = default)
    {
        Intents.Add(intent);
        if (HoldSubmit is not null)
            return await HoldSubmit.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return SubmitReceipt;
    }

    public ValueTask<ResourceQueryReceipt> QueryAsync(
        ResourceQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        Queries.Add(query);
        return ValueTask.FromResult(QueryReceipt);
    }
}

internal sealed class ResourceCommandHarness : IDisposable
{
    public SessionKey Session { get; } = DeviceSessionGraphs.DefaultSession();
    public FakeResourceStore Store { get; } = new();
    public FakeResourceTransport Transport { get; } = new();
    public ControllableTimeProvider Time { get; } = new();
    public ResourceCommandCoordinator Coordinator { get; }

    public ResourceCommandHarness(CapabilityProfile? capabilities = null, ResourceCommandOptions? options = null)
    {
        Install(DeviceSessionGraphs.Baseline(Session, 1), capabilities ?? Compatible());
        Coordinator = new ResourceCommandCoordinator(
            Store, Transport, Transport, null, Time,
            options ?? new ResourceCommandOptions
            {
                RpcTimeout = TimeSpan.FromMilliseconds(50),
                ObserveTimeout = TimeSpan.FromMilliseconds(50)
            });
    }

    public static CapabilityProfile Compatible() =>
        CapabilityGate.Evaluate(SchemaCompatibilityBinding.PinnedMatchingRuntimeForTests("0.9.0"), 22, "0.9.0");

    public static CapabilityProfile Incompatible() =>
        CapabilityGate.Evaluate(SchemaCompatibilityBinding.PinnedUnverified("0.9.0"), 22, "0.9.0");

    public ResourceKey Workspace() => new(Session, ResourceKind.Workspace, "w1", "t1", "p1", "term-1");

    public ResourceKey Pane() => new(Session, ResourceKind.Pane, "w1", "t1", "p1", "term-1");

    public ResourceKey AgentPane() => new(Session, ResourceKind.Agent, "w1", "t1", "p1", "term-1");

    public ResourceProjectionStamp Stamp() =>
        new(Store.Snapshot.Projection.Epoch, Store.Snapshot.Projection.Revision);

    public void Install(DecodedSessionSnapshot decoded, CapabilityProfile? capabilities = null)
    {
        var mapped = ProjectionMapper.MapSnapshot(decoded, capabilities ?? Compatible());
        if (!mapped.Succeeded)
            throw new Exception(mapped.Code);
        var store = new DeviceProjectionStore();
        var installed = store.InstallSnapshot(decoded.Epoch, mapped.Graph!);
        if (!installed.Succeeded)
            throw new Exception(installed.Code);
        Store.Snapshot = new ResourceStoreSnapshot(store.Read(), DeviceFreshness.Current);
    }

    public void Wait(Func<ResourceOperation, bool> pred)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        WaitAsync(pred, cts.Token).GetAwaiter().GetResult();
    }

    async Task WaitAsync(Func<ResourceOperation, bool> pred, CancellationToken cancellationToken)
    {
        if (pred(Coordinator.Current))
            return;
        await foreach (var _ in Coordinator.ReadStatesAsync(cancellationToken).ConfigureAwait(false))
        {
            if (pred(Coordinator.Current))
                return;
        }

        throw new Exception("wait_timeout state=" + Coordinator.Current.State + " code=" + Coordinator.Current.Code);
    }

    public void Dispose() => Coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
}
