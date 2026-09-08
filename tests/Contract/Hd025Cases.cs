using System.Text.Json;
using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.SshTransports;

internal static class Hd025Cases
{
    public static (string Name, Action Run)[] All =>
    [
        ("hd-025 admission queue dirty-set and visibility surface", Surface),
        ("hd-025 l2 live ssh perf and ac27 stay unverified", ResidualJson)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void Surface()
    {
        Check(typeof(ConnectionAdmissionPolicy).IsClass);
        Check(typeof(TerminalQueueBudget).IsClass);
        Check(typeof(DirtySetBudget).IsClass);
        Check(typeof(SshConnectionLease).IsClass);
        Check(typeof(PaneVisibilityCoordinator).IsClass);
        Check(ResourceBudgetCodes.ConnectionBudgetExhausted == "connection_budget_exhausted");
        Check(ResourceBudgetCodes.TerminalQueueLimit == "terminal_queue_limit");
        Check(ResourceBudgetCodes.StaleRenderAck == "stale_render_ack");
        Check(ResourceBudgetCodes.TransportCancelTimeout == "transport_cancel_timeout");
        Check(ResourceBudgetCodes.ProcessStartFailed == "process_start_failed");
        Check(TerminalQueueBudget.MaxQueuedBytes == 24 * 1024 * 1024);
        Check(RendererByteWindow.DefaultMaxInFlightBytes == 256 * 1024);
        Check(TerminalQueueBudget.MaxQueuedBytes != RendererByteWindow.DefaultMaxInFlightBytes);
        Check(!ResourceBudgets.Product.SshSlotsMeasured);
        Check(!ResourceBudgets.Product.FileJobsEnabled);
        Check(Enum.IsDefined(ConnectionPhase.WaitingForCapacity));
        Check(Enum.IsDefined(ConnectionPhase.PausedForCapacity));
        var refs = typeof(InputPolicy).Assembly.GetReferencedAssemblies().Select(item => item.Name!).ToArray();
        foreach (var name in refs)
        {
            var lower = name.ToLowerInvariant();
            Check(!lower.Contains("ssh"));
            Check(!lower.Contains("winui"));
            Check(!lower.Contains("webview2"));
        }

        var root = FindRepoRoot();
        Check(!Directory.Exists(Path.Combine(root, "tests", "Integration.Ssh")));
        Check(!Directory.Exists(Path.Combine(root, "tests", "Integration.Windows")));
        Check(!Directory.EnumerateFiles(Path.Combine(root, "src", "HerdDesk.App"), "*.xaml",
            SearchOption.AllDirectories).Any());
    }

    static void ResidualJson()
    {
        var path = Path.Combine(FindRepoRoot(), "implementation", "hd-025-l2.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Check(root.GetProperty("l2_live_ssh_perf").GetString() == "UNVERIFIED");
        Check(root.GetProperty("ac27_passed").GetBoolean() is false);
        Check(root.GetProperty("g0_passed").GetBoolean() is false);
        Check(root.GetProperty("phase_gate").GetString() != "passed");
        Check(root.GetProperty("b_ssh_measured").GetBoolean() is false);
        Check(root.GetProperty("live_ssh").GetBoolean() is false);
        Check(root.GetProperty("winui_admitted").GetBoolean() is false);
        Check(root.GetProperty("integration_ssh_project").GetBoolean() is false);
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
