using System.Diagnostics;
using HerdDesk.Contracts;
using HerdDesk.Core;

namespace HerdDesk.App;

public sealed class GlobalSearchViewModel
{
    private readonly GlobalProjectionStore _store;
    private readonly RecentAccessStore _recents;
    private readonly GlobalTargetResolver _resolver;
    private IReadOnlyList<SearchDocument> _results = [];
    private long _generation;
    private DeviceId? _scope;

    public GlobalSearchViewModel(
        GlobalProjectionStore store,
        RecentAccessStore recents,
        GlobalTargetResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(recents);
        ArgumentNullException.ThrowIfNull(resolver);
        _store = store;
        _recents = recents;
        _resolver = resolver;
    }

    public string Query { get; private set; } = "";
    public IReadOnlyList<SearchDocument> Results => _results;
    public long Generation => _generation;
    public DeviceId? Scope => _scope;
    public bool Partial { get; private set; }
    public double LatencyMs { get; private set; }
    public int HiddenTerminalBridgeCount { get; } = 0;
    public int TerminalProcessDelta => _store.TerminalProcessDelta;

    public void SetSearchScope(DeviceId? device)
    {
        _scope = device;
        UpdateQuery(Query);
    }

    public void UpdateQuery(string query)
    {
        Query = query ?? "";
        var generation = Interlocked.Increment(ref _generation);
        var watch = Stopwatch.StartNew();
        var recents = SearchPaletteViewModel.ToRecents(_recents, _store);
        var result = _store.Search(new GlobalSearchQuery(Query, _scope, generation), recents);
        watch.Stop();
        if (result.Cancelled || result.Generation != generation || generation != _generation)
            return;
        _results = result.Documents;
        Partial = result.Partial;
        LatencyMs = watch.Elapsed.TotalMilliseconds;
    }

    public void CancelSearch() => Interlocked.Increment(ref _generation);

    public bool TryCommit(long generation, IReadOnlyList<SearchDocument> documents, bool partial = false)
    {
        ArgumentNullException.ThrowIfNull(documents);
        if (generation != _generation)
            return false;
        _results = documents;
        Partial = partial;
        return true;
    }

    public ResolveResult Activate(SearchDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return _resolver.Resolve(document.Ref);
    }

    public ResolveResult ActivateSelected(int index)
    {
        if (index < 0 || index >= _results.Count)
            return ResolveResult.Expired();
        return Activate(_results[index]);
    }
}
