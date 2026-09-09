using System.Text.Json;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.SshTransports;

internal static class Hd022Cases
{
    public static (string Name, Action Run)[] All =>
    [
        ("hd-022 remote transport ships without winui live ssh or second semantic", Surface),
        ("hd-022 l2 live ssh and ac24 ac26 stay unverified", ResidualJson)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void Surface()
    {
        Check(typeof(RemoteSessionTransportSet).IsClass);
        Check(typeof(RemoteRpcConnectionFactory).IsClass);
        Check(typeof(RemoteTerminalTransportFactory).IsClass);
        Check(typeof(SshProcessChannel).IsClass);
        Check(typeof(SshTransportOutcome).GetProperty("Stderr") is null);
        Check(typeof(SshTransportOutcome).GetProperty("Argv") is null);
        Check(typeof(SshTransportOutcome).GetProperty("Host") is null);
        Check(SshTransportCodes.StdoutProtocolPollution == "remote_stdout_protocol_pollution");
        var root = FindRepoRoot();
        Check(!Directory.Exists(Path.Combine(root, "tests", "Integration.Ssh")));
        AppXamlSurface.CheckBlankContainerOnly(root);
    }

    static void ResidualJson()
    {
        var path = Path.Combine(FindRepoRoot(), "implementation", "hd-022-l2.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Check(root.GetProperty("l2_live_ssh").GetString() == "UNVERIFIED");
        Check(root.GetProperty("ac24_passed").GetBoolean() is false);
        Check(root.GetProperty("ac26_passed").GetBoolean() is false);
        Check(root.GetProperty("g0_passed").GetBoolean() is false);
        Check(root.GetProperty("phase_gate").GetString() != "passed");
        Check(root.GetProperty("live_ssh").GetBoolean() is false);
        Check(root.GetProperty("herdr_machine_catalog").GetBoolean() is false);
        Check(root.GetProperty("endpoint_generation_1").GetBoolean() is false);
        Check(root.GetProperty("winui_admitted").GetBoolean() is false);
        Check(root.GetProperty("integration_ssh_project").GetBoolean() is false);
        Check(root.GetProperty("tt_forced").GetBoolean() is false);
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
