using System.Text.Json;
using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Clipboard;
using HerdDesk.Terminal.Web;

internal static class Hd031Cases
{
    public static (string Name, Action Run)[] All =>
    [
        ("hd-031 clipboard coordinators are compiled", Surface),
        ("hd-031 l2 live clipboard ime and ac36 stay unverified", ResidualJson)
    ];

    static readonly string[] PassKeys = ["ac36_passed", "g0_passed"];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void Surface()
    {
        Check(typeof(ClipboardIntentResolver).IsClass);
        Check(typeof(PasteCoordinator).IsClass);
        Check(typeof(OscClipboardPolicy).IsClass);
        Check(typeof(AttachmentCache).IsClass);
        Check(typeof(WindowsClipboardSnapshotReader).IsClass);
        Check(typeof(PastePreviewViewModel).IsClass);
        Check(typeof(IClipboardSnapshotReader).IsInterface);
        Check(Enum.GetNames<ClipboardIntentKind>().Contains("Mixed"));
        Check(typeof(InputPolicy).IsClass);
        var reader = typeof(WindowsClipboardSnapshotReader);
        Check(reader.GetMethod("AddClipboardFormatListener") is null);
        Check(reader.GetMethod("SetClipboardViewer") is null);
        var root = FindRepoRoot();
        AppXamlSurface.CheckIntegrationWindowsProject(root);
        AppXamlSurface.CheckBlankContainerOnly(root);
        Check(File.Exists(Path.Combine(root, "src", "HerdDesk.Core", "Clipboard", "PasteCoordinator.cs")));
        Check(File.Exists(Path.Combine(root, "src", "HerdDesk.Terminal.Web", "Input", "OscClipboardPolicy.cs")));
        Check(File.Exists(Path.Combine(root, "src", "HerdDesk.Infrastructure", "Clipboard", "AttachmentCache.cs")));
        var readerSrc = File.ReadAllText(Path.Combine(root, "src", "HerdDesk.Infrastructure", "Clipboard",
            "WindowsClipboardSnapshotReader.cs"));
        Check(!readerSrc.Contains("AddClipboardFormatListener", StringComparison.Ordinal));
        Check(!readerSrc.Contains("SetClipboardViewer", StringComparison.Ordinal));
        Check(!readerSrc.Contains("WM_CLIPBOARDUPDATE", StringComparison.Ordinal));
        var oscSrc = File.ReadAllText(Path.Combine(root, "src", "HerdDesk.Terminal.Web", "Input",
            "OscClipboardPolicy.cs"));
        Check(!oscSrc.Contains("OpenClipboard", StringComparison.Ordinal));
        Check(!oscSrc.Contains("GetClipboardData", StringComparison.Ordinal));
        var cacheSrc = File.ReadAllText(Path.Combine(root, "src", "HerdDesk.Infrastructure", "Clipboard",
            "AttachmentCache.cs"));
        Check(!cacheSrc.Contains("Directory.GetFiles", StringComparison.Ordinal));
        Check(!cacheSrc.Contains("EnumerateFiles", StringComparison.Ordinal));
        var refs = typeof(PasteCoordinator).Assembly.GetReferencedAssemblies()
            .Select(item => item.Name!).ToArray();
        foreach (var name in refs)
        {
            var lower = name.ToLowerInvariant();
            Check(!lower.Contains("windowsappsdk"));
            Check(!lower.Contains("winui"));
            Check(!lower.Contains("webview2"));
            Check(!lower.Contains("ssh"));
            Check(!lower.Contains("herddesk.infrastructure"));
        }
    }

    static void ResidualJson()
    {
        var path = Path.Combine(FindRepoRoot(), "implementation", "hd-031-l2.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Check(root.GetProperty("document_kind").GetString() == "hd031_l2_status");
        Check(root.GetProperty("l2_live_clipboard").GetString() == "UNVERIFIED");
        Check(root.GetProperty("l2_live_ime").GetString() == "UNVERIFIED");
        Check(root.GetProperty("live_clipboard").GetBoolean() is false);
        Check(root.GetProperty("winui_admitted").GetBoolean() is false);
        Check(root.GetProperty("integration_windows").GetBoolean() is false);
        Check(root.GetProperty("integration_windows_project").GetBoolean() is false);
        Check(root.GetProperty("clipboard_watcher").GetBoolean() is false);
        Check(root.GetProperty("osc52_read_default").GetString() == "deny");
        Check(root.GetProperty("osc52_write_default").GetString() == "deny");
        foreach (var key in PassKeys)
            Check(root.GetProperty(key).GetBoolean() is false);
        Check(root.GetProperty("phase_gate").GetString() != "passed");
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
