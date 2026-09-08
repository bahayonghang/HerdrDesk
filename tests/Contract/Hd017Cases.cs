using System.Text.Json;
using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Rpc;

internal static class Hd017Cases
{
    public static (string Name, Action Run)[] All =>
    [
        ("resource command public api has no generic exec or argv", PublicSurface),
        ("hd-017 l2 live mutation and ac20 stay unverified", ResidualJson)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void PublicSurface()
    {
        Check(typeof(IResourceCommandCoordinator).IsInterface);
        Check(typeof(ResourceCommandCoordinator).IsClass);
        Check(typeof(ResourceCommandAdapter).IsClass);
        Check(typeof(ResourceCommandViewModel).IsClass);
        Check(typeof(ResourceCommandAdapter).GetMethod("SubmitAsync") is not null);
        foreach (var method in typeof(IResourceCommandCoordinator).GetMethods())
        {
            var name = method.Name.ToLowerInvariant();
            Check(!name.Contains("argv"));
            Check(!name.Contains("exec"));
            Check(!name.Contains("bypass"));
            Check(!name.Contains("serverstop"));
            Check(!name.Contains("commandline"));
            Check(!name.Contains("dynamicmethod"));
        }

        Check(typeof(CreateAgentIntent).GetProperty("Args") is null);
        Check(typeof(CreateTerminalIntent).GetProperty("Command") is null);
        Check(!Enum.GetNames<KnownAgentKind>().Contains("Muse"));
        Check(VerifiedAgentWires.Wire(KnownAgentKind.Claude) == "claude");
        Check(VerifiedAgentWires.Wire(KnownAgentKind.Codex) == "codex");
        Check(VerifiedAgentWires.Wire(KnownAgentKind.OpenCode) == "opencode");
        Check(typeof(ResourceCommandViewModel).GetProperty("AlwaysApproveEnabled") is not null);
        Check(typeof(ResourceCommandViewModel).GetProperty("HasGlobalBypass") is not null);
        Check(typeof(IResourceCommandTransport).GetMethod("SubmitAsync")!.GetParameters()[0].ParameterType
            == typeof(ResourceIntent));
    }

    static void ResidualJson()
    {
        var path = Path.Combine(FindRepoRoot(), "implementation", "hd-017-l2.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Check(root.GetProperty("l2_live_mutation").GetString() == "UNVERIFIED");
        Check(root.GetProperty("ac20_passed").GetBoolean() is false);
        Check(root.GetProperty("g0_passed").GetBoolean() is false);
        Check(root.GetProperty("phase_gate").GetString() != "passed");
        Check(root.GetProperty("live_herdr").GetBoolean() is false);
        Check(root.GetProperty("winui_admitted").GetBoolean() is false);
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
