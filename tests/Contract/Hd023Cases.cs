using System.Text.Json;
using HerdDesk.App;
using HerdDesk.Core;

internal static class Hd023Cases
{
    public static (string Name, Action Run)[] All =>
    [
        ("hd-023 aggregation ships without winui network or second identity map", Surface),
        ("hd-023 l2 live three-device p95 and ac19 ac21 stay unverified", ResidualJson)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void Surface()
    {
        Check(typeof(GlobalProjectionStore).IsClass);
        Check(typeof(GlobalTargetResolver).IsClass);
        Check(typeof(GlobalSearchIndex).IsClass);
        Check(typeof(GlobalEntityRef).IsClass);
        Check(typeof(WriteIntentGuard).IsClass);
        Check(typeof(MultiDeviceNavigationViewModel).IsClass);
        Check(typeof(GlobalSearchViewModel).IsClass);
        Check(typeof(GlobalEntityRef).GetProperty("Command") is null);
        Check(typeof(GlobalEntityRef).GetProperty("Input") is null);
        Check(typeof(GlobalEntityRef).GetProperty("Takeover") is null);
        Check(typeof(GlobalEntityRef).GetProperty("Password") is null);
        Check(typeof(SearchDocument).GetProperty("Bytes") is null);
        var root = FindRepoRoot();
        Check(!Directory.Exists(Path.Combine(root, "tests", "Integration.Windows")));
        Check(!Directory.EnumerateFiles(Path.Combine(root, "src", "HerdDesk.App"), "*.xaml",
            SearchOption.AllDirectories).Any());
        Check(!Directory.EnumerateFiles(Path.Combine(root, "src", "HerdDesk.App"),
            "DeviceConnectionSummary.xaml", SearchOption.AllDirectories).Any());
    }

    static void ResidualJson()
    {
        var path = Path.Combine(FindRepoRoot(), "implementation", "hd-023-l2.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Check(root.GetProperty("l2_live_three_device_search_p95").GetString() == "UNVERIFIED");
        Check(root.GetProperty("ac19_passed").GetBoolean() is false);
        Check(root.GetProperty("ac21_passed").GetBoolean() is false);
        Check(root.GetProperty("g0_passed").GetBoolean() is false);
        Check(root.GetProperty("phase_gate").GetString() != "passed");
        Check(root.GetProperty("live_three_device_ssh").GetBoolean() is false);
        Check(root.GetProperty("live_three_device_p95").GetBoolean() is false);
        Check(root.GetProperty("winui_admitted").GetBoolean() is false);
        Check(root.GetProperty("integration_windows_project").GetBoolean() is false);
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
