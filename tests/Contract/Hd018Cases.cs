using System.Text.Json;
using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class Hd018Cases
{
    public static (string Name, Action Run)[] All =>
    [
        ("recovery public api has no daemon start or recover-control", PublicSurface),
        ("hd-018 l2 live disconnect and ac13 ac14 ac15 stay unverified", ResidualJson)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void PublicSurface()
    {
        Check(typeof(RecoveryPolicy).IsAbstract);
        Check(typeof(DeviceSession).GetMethod("RetryNowAsync") is not null);
        Check(typeof(IDeviceSession).GetMethod("NotifyAppStoppingAsync") is not null);
        Check(typeof(IControlLeaseCoordinator).GetMethod("NoteRecoverySignalAsync") is not null);
        Check(typeof(RecoveryBindings).GetMethod("StartDaemon") is null);
        Check(typeof(RecoveryBindings).GetMethod("RecoverControl") is null);
        Check(typeof(IDeviceSession).GetMethod("StartDaemon") is null);
        Check(typeof(IControlLeaseCoordinator).GetMethod("RecoverControl") is null);
        foreach (var method in typeof(IDeviceSession).GetMethods())
        {
            var name = method.Name.ToLowerInvariant();
            Check(!name.Contains("startdaemon"));
            Check(!name.Contains("serverstop"));
            Check(!name.Contains("upgrade"));
            Check(!name.Contains("recovercontrol"));
        }

        foreach (var method in typeof(IControlLeaseCoordinator).GetMethods())
        {
            var name = method.Name.ToLowerInvariant();
            Check(!name.Contains("startdaemon"));
            Check(!name.Contains("recovercontrol"));
            Check(!name.Contains("serverstop"));
        }

        Check(Enum.GetNames<LeaseRecoverySignal>().Contains("ProjectionStale"));
        Check(Enum.GetNames<RecoveryCause>().Contains("RequestEof"));
        Check(typeof(AppExitCoordinator).GetMethod("TryRegisterForeign") is not null);
    }

    static void ResidualJson()
    {
        var path = Path.Combine(FindRepoRoot(), "implementation", "hd-018-l2.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Check(root.GetProperty("l2_live_disconnect").GetString() == "UNVERIFIED");
        Check(root.GetProperty("ac13_passed").GetBoolean() is false);
        Check(root.GetProperty("ac14_passed").GetBoolean() is false);
        Check(root.GetProperty("ac15_passed").GetBoolean() is false);
        Check(root.GetProperty("g0_passed").GetBoolean() is false);
        Check(root.GetProperty("phase_gate").GetString() != "passed");
        Check(root.GetProperty("live_herdr").GetBoolean() is false);
        Check(root.GetProperty("winui_admitted").GetBoolean() is false);
        Check(root.GetProperty("auto_start_daemon").GetBoolean() is false);
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
