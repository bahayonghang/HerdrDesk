using System.Text.Json;
using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Ssh;

internal static class Hd020Cases
{
    public static (string Name, Action Run)[] All =>
    [
        ("hd-020 ssh editor ships without winui or live ssh", Surface),
        ("hd-020 l2 isolated openssh and ac22 ac23 stay unverified", ResidualJson)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void Surface()
    {
        Check(typeof(SshConnectionTestService).IsClass);
        Check(typeof(EditDeviceViewModel).IsClass);
        Check(typeof(DeviceProfile).GetProperty("Password") is null);
        Check(typeof(DeviceProfile).GetProperty("PrivateKey") is null);
        Check(typeof(EditDeviceViewModel).GetProperty("Argv") is null);
        Check(typeof(EditDeviceViewModel).GetProperty("ArgumentList") is null);
        Check(typeof(SshConnectionTestResult).GetProperty("Stderr") is null);
        Check(typeof(SshConnectionTestResult).GetProperty("Argv") is null);
        Check(SshRedaction.UnsupportedMatrix.Count == 6);
        Check(SshRedaction.UnsupportedMatrix.All(item => item.Status == "未支持"));
        var root = FindRepoRoot();
        Check(!Directory.Exists(Path.Combine(root, "tests", "Integration.Ssh")));
        Check(!File.Exists(Path.Combine(root, "src", "HerdDesk.App", "Devices", "EditDevicePage.xaml")));
        AppXamlSurface.CheckBlankContainerOnly(root);
        var csproj = File.ReadAllText(Path.Combine(root, "src", "HerdDesk.App", "HerdDesk.App.csproj"));
        Check(!csproj.Contains("SSH.NET", StringComparison.OrdinalIgnoreCase));
    }

    static void ResidualJson()
    {
        var path = Path.Combine(FindRepoRoot(), "implementation", "hd-020-l2.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Check(root.GetProperty("l2_isolated_windows_openssh").GetString() == "UNVERIFIED");
        Check(root.GetProperty("ac22_passed").GetBoolean() is false);
        Check(root.GetProperty("ac23_passed").GetBoolean() is false);
        Check(root.GetProperty("g0_passed").GetBoolean() is false);
        Check(root.GetProperty("phase_gate").GetString() != "passed");
        Check(root.GetProperty("live_ssh").GetBoolean() is false);
        Check(root.GetProperty("herdr_machine_catalog").GetBoolean() is false);
        Check(root.GetProperty("endpoint_generation_1").GetBoolean() is false);
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
