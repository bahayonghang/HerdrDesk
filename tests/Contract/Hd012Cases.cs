using System.Text.Json;
using HerdDesk.App;
using HerdDesk.Core;

internal static class Hd012Cases
{
    public static (string Name, Action Run)[] All =>
    [
        ("notification target has no command fields", TargetShape),
        ("windows toast sink stays unavailable", ToastSinkUnavailable),
        ("hd-012 l2 and ac17 ac18 stay unverified", ResidualJson),
        ("integration windows project is not admitted", NoIntegrationProject)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void TargetShape()
    {
        var type = typeof(NotificationTarget);
        Check(type.GetProperty("Command") is null);
        Check(type.GetProperty("Input") is null);
        Check(type.GetProperty("Takeover") is null);
        Check(type.GetProperty("Password") is null);
        Check(typeof(WindowsNotificationSink).GetProperty("Available") is not null);
    }

    static void ToastSinkUnavailable()
    {
        var sink = new WindowsNotificationSink();
        Check(!sink.Available);
        AppXamlSurface.CheckBlankContainerOnly(FindRepoRoot());
    }

    static void ResidualJson()
    {
        var path = Path.Combine(FindRepoRoot(), "implementation", "hd-012-l2.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Check(root.GetProperty("l2_windows_toast_activation").GetString() == "UNVERIFIED");
        Check(root.GetProperty("ac17_passed").GetBoolean() is false);
        Check(root.GetProperty("ac18_passed").GetBoolean() is false);
        Check(root.GetProperty("g0_passed").GetBoolean() is false);
        Check(root.GetProperty("phase_gate").GetString() != "passed");
        Check(root.GetProperty("winui_admitted").GetBoolean() is false);
    }

    static void NoIntegrationProject()
    {
        var root = FindRepoRoot();
        Check(!Directory.Exists(Path.Combine(root, "tests", "Integration.Windows")));
        Check(!File.Exists(Path.Combine(root, "tests", "Integration.Windows",
            "HerdDesk.Integration.Windows.csproj")));
    }

    static string FindRepoRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "HerdDesk.slnx")))
                return directory;
            directory = Directory.GetParent(directory)?.FullName ?? "";
        }

        throw new Exception("repo_root_missing");
    }
}
