using HerdDesk.App;
using HerdDesk.Contracts;

internal static class UntrustedNameTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("untrusted display name is not a path command or uri", DisplayIsNotPath),
        ("pane does not navigate from display text", PaneRejectsDisplayNavigation)
    ];

    static void DisplayIsNotPath()
    {
        var raw = "javascript:alert(1); rm -rf / && https://evil.example/a.txt";
        var display = UntrustedText.Display(raw);
        AppTestHost.Check(display.Contains("javascript:alert(1)", StringComparison.Ordinal));
        AppTestHost.Check(UntrustedText.TryAsPath(display) is null);
        AppTestHost.Check(!UntrustedText.TryAsCommand(display));
        AppTestHost.Check(!UntrustedText.TryAsUri(display));
        AppTestHost.Check(!UntrustedText.TryAsNavigation(display));
        AppTestHost.Check(!display.Contains('<') || UntrustedText.TryAsPath("<script>") is null);
        var control = UntrustedText.Display("evil\nname\u0000.bin");
        AppTestHost.Check(!control.Contains('\n'));
        AppTestHost.Check(!control.Contains('\0'));
    }

    static void PaneRejectsDisplayNavigation()
    {
        var fake = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceA, "local"));
        fake.AddFile("safe.txt", "ok"u8.ToArray(), display: "javascript:alert(1)|https://evil.example");
        var pane = new FilePaneViewModel("left");
        pane.ApplyConnection(DeviceFreshness.Current, ConnectionPhase.Ready);
        pane.NavigateAsync(FileLocation.Local(fake)).AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(pane.Entries.Count == 1);
        var entry = pane.Entries[0];
        AppTestHost.Check(entry.DisplayText.Contains("javascript:alert(1)", StringComparison.Ordinal));
        AppTestHost.Check(!entry.AutomationName.Contains("<a ", StringComparison.Ordinal));
        AppTestHost.Check(!entry.AutomationName.Contains("href=", StringComparison.Ordinal));
        pane.SelectByDisplayName(entry.DisplayText);
        AppTestHost.Check(pane.Selected.Count == 0);
        AppTestHost.Check(!pane.NavigateFromText(entry.DisplayText));
        AppTestHost.Check(pane.Location is not null && pane.Location.Path.Equals(FileLocator.Root));
        pane.Select(entry.Name, true);
        AppTestHost.Check(pane.Selected.Count == 1);
        AppTestHost.Check(pane.Selected[0].Name.Equals(entry.Name));
    }
}
