namespace HerdDesk.App.Quality;

public static class SoakLaunchPolicy
{
    public const string MinimizedEnvironmentVariable = "HERDDESK_SOAK_MINIMIZED";
    public const string MinimizedEnabledValue = "1";

    public static bool SuppressWindowClose()
    {
        var value = Environment.GetEnvironmentVariable(MinimizedEnvironmentVariable);
        return string.Equals(value, MinimizedEnabledValue, StringComparison.Ordinal);
    }
}
