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
    public static TimeSpan MinDelay { get; } = TimeSpan.FromSeconds(1);
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

    public static TimeSpan EqualJitter(int n, double unitInterval)
    {
        var index = n < 0 ? 0 : n;
        var unit = unitInterval;
        if (double.IsNaN(unit) || double.IsInfinity(unit) || unit < 0)
            unit = 0;
        else if (unit >= 1)
            unit = Math.BitDecrement(1.0);

        var shift = index + 1;
        if (shift > 5)
            shift = 5;
        long capSeconds;
        try
        {
            checked
            {
                capSeconds = 1L << shift;
            }
        }
        catch (OverflowException)
        {
            capSeconds = 30;
        }

        if (capSeconds > 30)
            capSeconds = 30;
        if (capSeconds < 1)
            capSeconds = 1;

        long capTicks;
        try
        {
            checked
            {
                capTicks = capSeconds * TimeSpan.TicksPerSecond;
            }
        }
        catch (OverflowException)
        {
            capTicks = 30 * TimeSpan.TicksPerSecond;
        }

        var half = capTicks / 2.0;
        long delayTicks;
        try
        {
            checked
            {
                delayTicks = (long)(half + (unit * half));
            }
        }
        catch (OverflowException)
        {
            delayTicks = 30 * TimeSpan.TicksPerSecond;
        }

        var minTicks = TimeSpan.TicksPerSecond;
        var maxTicks = 30 * TimeSpan.TicksPerSecond;
        if (delayTicks >= capTicks)
            delayTicks = capTicks - 1;
        if (delayTicks < minTicks)
            delayTicks = minTicks;
        if (delayTicks > maxTicks)
            delayTicks = maxTicks;
        return TimeSpan.FromTicks(delayTicks);
    }
}
