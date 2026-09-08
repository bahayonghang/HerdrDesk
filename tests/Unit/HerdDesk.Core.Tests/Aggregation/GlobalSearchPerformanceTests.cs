using System.Diagnostics;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class GlobalSearchPerformanceTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("three devices and one hundred projections search under p95 budget", P95Budget),
        ("cancelled query does not commit late results", CancelDropsLate),
        ("scope limits results to one device", ScopeFilters)
    ];

    static void P95Budget()
    {
        var store = new GlobalProjectionStore();
        var documents = AggregationHarness.MixedHundred(store);
        AggregationHarness.Check(documents.Count >= 100);
        AggregationHarness.Check(store.PartitionCount == 3);
        AggregationHarness.Check(store.TerminalProcessDelta == 0);
        var samples = new List<double>(40);
        for (var i = 0; i < 40; i++)
        {
            var watch = Stopwatch.StartNew();
            var result = store.Search(new GlobalSearchQuery("pane-", null, i + 1));
            watch.Stop();
            AggregationHarness.Check(!result.Cancelled);
            AggregationHarness.Check(result.TerminalProcessDelta == 0);
            if (i >= 10)
                samples.Add(watch.Elapsed.TotalMilliseconds);
        }

        samples.Sort();
        var p95 = samples[(int)Math.Ceiling(samples.Count * 0.95) - 1];
        AggregationHarness.Check(p95 < 100);
        var ordered = store.Search(new GlobalSearchQuery("", null, 99)).Documents
            .Where(item => item.Kind == GlobalEntityKind.Device).ToArray();
        AggregationHarness.Check(ordered.Length == 3);
        AggregationHarness.Check(ordered[0].UserOrder <= ordered[1].UserOrder);
        AggregationHarness.Check(ordered[1].UserOrder <= ordered[2].UserOrder);
        AggregationHarness.Check(ordered[0].Ref.Device == AggregationHarness.DeviceA);
    }

    static void CancelDropsLate()
    {
        var store = new GlobalProjectionStore();
        AggregationHarness.MixedHundred(store);
        var late = store.Search(new GlobalSearchQuery("pane-", null, 1));
        var cancelled = store.Search(new GlobalSearchQuery("pane-", null, 2, true));
        AggregationHarness.Check(cancelled.Cancelled);
        AggregationHarness.Check(cancelled.Documents.Count == 0);
        AggregationHarness.Check(late.Generation != cancelled.Generation);
    }

    static void ScopeFilters()
    {
        var store = new GlobalProjectionStore();
        AggregationHarness.MixedHundred(store);
        var scoped = store.Search(new GlobalSearchQuery("", AggregationHarness.DeviceB, 3));
        AggregationHarness.Check(scoped.Documents.Count > 0);
        AggregationHarness.Check(scoped.Documents.All(item => item.Ref.Device == AggregationHarness.DeviceB));
        var loading = AggregationHarness.DeviceState(
            AggregationHarness.DeviceA, 1, ConnectionPhase.Synchronizing, DeviceFreshness.Refreshing,
            AggregationHarness.NamesakeSession(AggregationHarness.DeviceA));
        store.ApplySession(loading, 0, "lab-0");
        var partial = store.Search(new GlobalSearchQuery("", null, 4));
        AggregationHarness.Check(partial.Partial);
        AggregationHarness.Check(store.FindSession(AggregationHarness.SessionOf(AggregationHarness.DeviceB)) is not null);
        AggregationHarness.Check(store.Search(new GlobalSearchQuery("", AggregationHarness.DeviceB, 5))
            .Documents.Any(item => item.Kind == GlobalEntityKind.Pane));
    }
}
