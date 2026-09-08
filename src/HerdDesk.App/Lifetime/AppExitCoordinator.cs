namespace HerdDesk.App;

public sealed class AppExitCoordinator : IAsyncDisposable
{
    private readonly List<(int Id, IAsyncDisposable Child)> _owned = [];
    private readonly CancellationTokenSource _lifetime = new();
    private int _disposed;

    public bool AcceptingActivation { get; private set; } = true;
    public bool AllowNewConnections { get; private set; } = true;
    public bool Exited { get; private set; }
    public CancellationToken LifetimeToken => _lifetime.Token;
    public IReadOnlyList<int> OwnedIds => _owned.Select(item => item.Id).ToArray();
    public IReadOnlyList<int> ReleasedIds { get; private set; } = [];
    public bool StoppedDaemon { get; private set; }
    public bool StoppedAgent { get; private set; }
    public bool ClosedRemotePane { get; private set; }

    public void RegisterOwned(int id, IAsyncDisposable child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (!AcceptingActivation)
            throw new InvalidOperationException("exit_in_progress");
        _owned.Add((id, child));
    }

    public async ValueTask ExitAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        AcceptingActivation = false;
        AllowNewConnections = false;
        try
        {
            _lifetime.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        var released = new List<int>(_owned.Count);
        foreach (var item in _owned)
        {
            await item.Child.DisposeAsync().ConfigureAwait(false);
            released.Add(item.Id);
        }

        _owned.Clear();
        ReleasedIds = released;
        _lifetime.Dispose();
        Exited = true;
    }

    public ValueTask DisposeAsync() => ExitAsync();
}
