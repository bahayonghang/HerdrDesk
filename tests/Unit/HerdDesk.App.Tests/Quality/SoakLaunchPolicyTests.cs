using HerdDesk.App.Quality;

internal static class SoakLaunchPolicyTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("soak launch policy reads HERDDESK_SOAK_MINIMIZED", EnvVarMatrix)
    ];

    static void EnvVarMatrix()
    {
        const string name = SoakLaunchPolicy.MinimizedEnvironmentVariable;
        var previous = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, null);
            AppTestHost.Check(!SoakLaunchPolicy.SuppressWindowClose());

            Environment.SetEnvironmentVariable(name, SoakLaunchPolicy.MinimizedEnabledValue);
            AppTestHost.Check(SoakLaunchPolicy.SuppressWindowClose());

            Environment.SetEnvironmentVariable(name, "0");
            AppTestHost.Check(!SoakLaunchPolicy.SuppressWindowClose());

            Environment.SetEnvironmentVariable(name, "true");
            AppTestHost.Check(!SoakLaunchPolicy.SuppressWindowClose());

            Environment.SetEnvironmentVariable(name, "2");
            AppTestHost.Check(!SoakLaunchPolicy.SuppressWindowClose());
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, previous);
        }
    }
}
