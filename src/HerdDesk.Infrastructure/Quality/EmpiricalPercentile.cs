namespace HerdDesk.Infrastructure.Quality;

public static class EmpiricalPercentile
{
    public static double NearestRank(IReadOnlyList<double> sortedAscending, double percentile)
    {
        ArgumentNullException.ThrowIfNull(sortedAscending);
        if (sortedAscending.Count == 0)
            throw new ArgumentException("empty_samples", nameof(sortedAscending));
        if (percentile <= 0 || percentile > 1)
            throw new ArgumentOutOfRangeException(nameof(percentile));
        var rank = (int)Math.Ceiling(sortedAscending.Count * percentile);
        return sortedAscending[rank - 1];
    }

    public static (double P50, double P95, double Max) Summarize(IReadOnlyList<double> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
            throw new ArgumentException("empty_samples", nameof(samples));
        var sorted = samples.ToArray();
        Array.Sort(sorted);
        return (NearestRank(sorted, 0.50), NearestRank(sorted, 0.95), sorted[^1]);
    }
}
