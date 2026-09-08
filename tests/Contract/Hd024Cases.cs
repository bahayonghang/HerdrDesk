using System.Text.Json;
using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Ssh;

internal static class Hd024Cases
{
    public static (string Name, Action Run)[] All =>
    [
        ("hd-024 recovery stays on devicesession without a second manager", Surface),
        ("hd-024 l2 live auth and ac22 ac26 stay unverified", ResidualJson)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void Surface()
    {
        Check(typeof(RecoveryPolicy).GetMethod(nameof(RecoveryPolicy.DecideRemote)) is not null);
        Check(typeof(DeviceSession).GetMethod(nameof(DeviceSession.NoteFailureAsync)) is not null);
        Check(typeof(DeviceSession).GetMethod(nameof(DeviceSession.CancelRetryAsync)) is not null);
        Check(typeof(IDeviceSession).GetMethod(nameof(IDeviceSession.CancelRetryAsync)) is not null);
        Check(typeof(SshFailureClassifier).IsAbstract);
        Check(typeof(SshRecoveryBlockStore).IsClass);
        Check(typeof(DeviceConnectionStatusViewModel).IsClass);
        Check(typeof(DeviceSession).Assembly.GetType("HerdDesk.Core.RecoveryManager") is null);
        Check(typeof(DeviceSession).Assembly.GetType("HerdDesk.Core.ReconnectCoordinator") is null);
        var refs = typeof(InputPolicy).Assembly.GetReferencedAssemblies().Select(item => item.Name!).ToArray();
        foreach (var name in refs)
        {
            var lower = name.ToLowerInvariant();
            Check(!lower.Contains("ssh"));
            Check(!lower.Contains("winui"));
        }

        var root = FindRepoRoot();
        Check(!Directory.Exists(Path.Combine(root, "tests", "Integration.Ssh")));
        Check(!Directory.EnumerateFiles(Path.Combine(root, "src", "HerdDesk.App"), "*.xaml",
            SearchOption.AllDirectories).Any());
    }

    static void ResidualJson()
    {
        var path = Path.Combine(FindRepoRoot(), "implementation", "hd-024-l2.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Check(root.GetProperty("l2_live_auth").GetString() == "UNVERIFIED");
        Check(root.GetProperty("ac22_passed").GetBoolean() is false);
        Check(root.GetProperty("ac26_passed").GetBoolean() is false);
        Check(root.GetProperty("g0_passed").GetBoolean() is false);
        Check(root.GetProperty("phase_gate").GetString() != "passed");
        Check(root.GetProperty("live_ssh").GetBoolean() is false);
        Check(root.GetProperty("winui_admitted").GetBoolean() is false);
        Check(root.GetProperty("integration_ssh_project").GetBoolean() is false);
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
