using System.Text.Json;
using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class Hd016Cases
{
    public static (string Name, Action Run)[] All =>
    [
        ("control lease public api has no argv nonce or resource command", PublicSurface),
        ("hd-016 l2 live lease and ac07 ac14 ac16 stay unverified", ResidualJson)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void PublicSurface()
    {
        Check(typeof(IControlLeaseCoordinator).IsInterface);
        Check(typeof(ControlLeaseCoordinator).IsClass);
        Check(typeof(TerminalControlViewModel).IsClass);
        Check(typeof(ControlAttemptOutcome).GetEnumNames().Contains("Busy"));
        Check(!typeof(TerminalAccess).GetEnumNames().Contains("Busy"));
        Check(!typeof(TerminalAccess).GetEnumNames().Contains("Rejected"));
        Check(!typeof(TerminalAccess).GetEnumNames().Contains("Cancelled"));
        Check(typeof(TakeoverChallengeView).GetProperty("Handle") is not null);
        Check(typeof(TakeoverChallengeView).GetProperty("Nonce") is null);
        Check(typeof(IControlLeaseCoordinator).GetMethod("ConfirmTakeoverAsync") is not null);
        Check(typeof(IControlLeaseCoordinator).GetProperty("AlwaysTakeover") is null);
        foreach (var method in typeof(IControlLeaseCoordinator).GetMethods())
        {
            var name = method.Name.ToLowerInvariant();
            Check(!name.Contains("argv"));
            Check(!name.Contains("nonce"));
            Check(!name.Contains("createworkspace"));
            Check(!name.Contains("rename"));
            Check(!name.Contains("closeworkspace"));
            Check(!name.Contains("exec"));
            Check(!name.Contains("approval"));
            Check(!name.Contains("bypass"));
            Check(!name.Contains("serverstop"));
            Check(!name.Contains("recovercontrol"));
        }

        var type = typeof(ControlLeaseCoordinator);
        Check(type.GetProperty("Nonce") is null);
        Check(typeof(TerminalControlViewModel).GetProperty("AlwaysTakeoverEnabled") is not null);
        var vmFlags = typeof(TerminalControlViewModel).GetProperty("AlwaysTakeoverEnabled");
        Check(vmFlags is not null);
    }

    static void ResidualJson()
    {
        var path = Path.Combine(FindRepoRoot(), "implementation", "hd-016-l2.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Check(root.GetProperty("l2_live_lease").GetString() == "UNVERIFIED");
        Check(root.GetProperty("ac07_passed").GetBoolean() is false);
        Check(root.GetProperty("ac14_passed").GetBoolean() is false);
        Check(root.GetProperty("ac16_passed").GetBoolean() is false);
        Check(root.GetProperty("g0_passed").GetBoolean() is false);
        Check(root.GetProperty("phase_gate").GetString() != "passed");
        Check(root.GetProperty("live_herdr").GetBoolean() is false);
        Check(root.GetProperty("resource_crud").GetString() == "hd-017");
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
