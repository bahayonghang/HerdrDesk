using HerdDesk.Contracts;

namespace HerdDesk.Core;

public sealed class ProjectionResourceStore : IResourceProjectionStore
{
    private readonly DeviceProjectionStore _store;

    public ProjectionResourceStore(DeviceProjectionStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public DeviceFreshness Freshness { get; set; } = DeviceFreshness.Current;

    public ResourceStoreSnapshot Read() => new(_store.Read(), Freshness);
}
