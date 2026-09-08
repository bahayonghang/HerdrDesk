using System.Text.Json;
using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Terminal.Web;

internal static class Hd015Cases
{
    public static (string Name, Action Run)[] All =>
    [
        ("input coordinators ship without webview2 or npm", CoordinatorShape),
        ("hd-015 l3 ime and ac09 ac10 stay unverified", ResidualJson)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void CoordinatorShape()
    {
        Check(typeof(TerminalInputController).IsClass);
        Check(typeof(ImeCompositionBridge).IsClass);
        Check(typeof(KeySequenceTranslator).IsClass);
        Check(typeof(SelectionAndMousePolicy).IsClass);
        Check(typeof(TerminalFocusCoordinator).IsClass);
        Check(typeof(TerminalInputViewModel).IsClass);
        Check(AgentInputProfiles.Resolve("muse").IsUnknown);
        Check(!Enum.GetNames<KnownAgentKind>().Contains("Muse"));
        var assembly = typeof(TerminalInputController).Assembly;
        foreach (var name in assembly.GetReferencedAssemblies().Select(item => item.Name!))
        {
            var lower = name.ToLowerInvariant();
            Check(!lower.Contains("windowsappsdk"));
            Check(!lower.Contains("webview2"));
            Check(!lower.Contains("winui"));
        }

        var root = FindRepoRoot();
        Check(!Directory.Exists(Path.Combine(root, "web", "terminal")));
        Check(!Directory.Exists(Path.Combine(root, "tests", "Integration.Windows")));
        var csproj = File.ReadAllText(Path.Combine(root, "src", "HerdDesk.Terminal.Web",
            "HerdDesk.Terminal.Web.csproj"));
        Check(!csproj.Contains("PackageReference", StringComparison.OrdinalIgnoreCase));
        Check(TerminalInputHost.L3ImeDesktop == "UNVERIFIED");
        var controller = new TerminalInputController(
            new InputContext(
                new PaneKey(
                    new SessionKey(new DeviceId(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")),
                        "endpoint", "dev"),
                    "ws", "p1"),
                new ConnectionEpoch(1), TerminalAccess.Observing, false),
            CompositionPolicy.Evaluate, InputPolicy.Evaluate);
        Check(!controller.RequestControl().Allowed);
        Check(!controller.ControlVerified);
    }

    static void ResidualJson()
    {
        var path = Path.Combine(FindRepoRoot(), "implementation", "hd-015-l3.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Check(root.GetProperty("l3_ime_desktop").GetString() == "UNVERIFIED");
        Check(root.GetProperty("l2_webview_ime").GetString() == "UNVERIFIED");
        Check(root.GetProperty("webview2_admitted").GetBoolean() is false);
        Check(root.GetProperty("npm_xterm_admitted").GetBoolean() is false);
        Check(root.GetProperty("ac09_passed").GetBoolean() is false);
        Check(root.GetProperty("ac10_passed").GetBoolean() is false);
        Check(root.GetProperty("g0_passed").GetBoolean() is false);
        Check(root.GetProperty("phase_gate").GetString() != "passed");
        Check(root.GetProperty("windows_desktop_restore").GetString() == "not_admitted");
        Check(root.GetProperty("live_herdr").GetBoolean() is false);
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
