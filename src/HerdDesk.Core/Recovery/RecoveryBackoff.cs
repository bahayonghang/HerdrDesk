namespace HerdDesk.Core;

public interface IRecoveryEntropy
{
    TimeSpan NextJitter(TimeSpan maxInclusive);
}

public sealed class ZeroRecoveryEntropy : IRecoveryEntropy
{
    public static ZeroRecoveryEntropy Instance { get; } = new();

    public TimeSpan NextJitter(TimeSpan maxInclusive)
    {
        _ = maxInclusive;
        return TimeSpan.Zero;
    }
}

public static class RecoveryBackoff
{
    public static readonly TimeSpan[] Bases =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(16),
        TimeSpan.FromSeconds(30)
    ];

    public static TimeSpan Cap { get; } = TimeSpan.FromSeconds(30);
    public static int AutomaticRetryLimit { get; } = 6;

    public static TimeSpan Delay(int attempt, IRecoveryEntropy entropy)
    {
        ArgumentNullException.ThrowIfNull(entropy);
        var index = Math.Clamp(attempt, 1, Bases.Length) - 1;
        var basis = Bases[index];
        var slack = Cap - basis;
        if (slack < TimeSpan.Zero)
            slack = TimeSpan.Zero;
        var jitter = entropy.NextJitter(slack);
        if (jitter < TimeSpan.Zero)
            jitter = TimeSpan.Zero;
        if (jitter > slack)
            jitter = slack;
        var total = basis + jitter;
        return total > Cap ? Cap : total;
    }
}
