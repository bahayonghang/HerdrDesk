using System.Text.Json;
using HerdDesk.App;
using HerdDesk.Contracts;

internal static class Hd011Cases
{
    public static (string Name, Action Run)[] All =>
    [
        ("app assembly stays bcl without winui packages", AppHasNoWinui),
        ("muse is not a known tui kind", MuseNotKnownKind),
        ("activation intent has no command or input fields", ActivationIntentShape),
        ("hd-011 l2 l3 and ac19 stay unverified", ResidualJson)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void AppHasNoWinui()
    {
        var assembly = typeof(ShellViewModel).Assembly;
        Check(assembly.GetName().Name == "HerdDesk.App");
        foreach (var name in assembly.GetReferencedAssemblies().Select(item => item.Name!))
        {
            var lower = name.ToLowerInvariant();
            Check(!lower.Contains("windowsappsdk"));
            Check(!lower.Contains("webview2"));
            Check(!lower.Contains("winui"));
            Check(!lower.Contains("ssh"));
        }

        foreach (var type in assembly.GetTypes())
        {
            Check(type.Namespace is null ||
                  !type.Namespace.Contains("Microsoft.UI", StringComparison.Ordinal));
            Check(!type.Name.Contains("Xaml", StringComparison.OrdinalIgnoreCase));
        }

        var root = FindRepoRoot();
        Check(!Directory.EnumerateFiles(Path.Combine(root, "src", "HerdDesk.App"), "*.xaml",
            SearchOption.AllDirectories).Any());
        var csproj = File.ReadAllText(Path.Combine(root, "src", "HerdDesk.App", "HerdDesk.App.csproj"));
        Check(!csproj.Contains("PackageReference", StringComparison.OrdinalIgnoreCase));
        Check(!csproj.Contains("net10.0-windows", StringComparison.Ordinal));
    }

    static void MuseNotKnownKind()
    {
        var names = Enum.GetNames<KnownAgentKind>();
        Check(!names.Contains("Muse"));
        Check(!names.Contains("Qwen"));
        Check(names.Contains("Claude"));
    }

    static void ActivationIntentShape()
    {
        var type = typeof(ActivationIntent);
        Check(type.GetProperty("Command") is null);
        Check(type.GetProperty("Input") is null);
        Check(type.GetProperty("Takeover") is null);
        Check(type.GetProperty("Password") is null);
        Check(typeof(ShellViewModel).GetProperty("HiddenTerminalBridgeCount") is not null);
    }

    static void ResidualJson()
    {
        var path = Path.Combine(FindRepoRoot(), "implementation", "hd-011-l2.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Check(root.GetProperty("l2_windows_visual_activation").GetString() == "UNVERIFIED");
        Check(root.GetProperty("l3_ime_screen_reader_dpi").GetString() == "UNVERIFIED");
        Check(root.GetProperty("ac19_passed").GetBoolean() is false);
        Check(root.GetProperty("g0_passed").GetBoolean() is false);
        Check(root.GetProperty("phase_gate").GetString() != "passed");
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
