using HerdDesk.App;
using HerdDesk.Contracts;

internal static class FilePaneGenerationTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("late list does not overwrite a newer generation", LateListDropped),
        ("cancel keeps cancelled when a late list arrives", CancelDropsLateList),
        ("offline keeps cached entries as stale", OfflineKeepsCache),
        ("permission denied uses structured code", PermissionDenied),
        ("empty directory is a distinct ready-empty state", EmptyDirectory),
        ("incompatible disables transfer", IncompatibleDisables),
        ("breadcrumb keyboard uses locator not display text", BreadcrumbKeyboard),
        ("symlink is not opened as a directory", SymlinkNotDirectory),
        ("permission denied after navigate does not keep prior entries", PermissionDeniedClearsPriorEntries),
        ("unicode and space names stay structured locators", UnicodeSpaceNames)
    ];

    static void LateListDropped()
    {
        var fake = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceA, "local"));
        var pane = new FilePaneViewModel("left");
        pane.ApplyConnection(DeviceFreshness.Current, ConnectionPhase.Ready);
        var locA = FileLocation.Local(fake, FileLocator.Root);
        var locB = FileLocation.Local(fake, FileLocator.Root.Append(FileWorkspaceHarness.Comp("sub")));
        fake.AddDirectory("sub");
        fake.AddFile("old.txt", "old"u8.ToArray());
        fake.AddFile(FileLocator.Root.Append(FileWorkspaceHarness.Comp("sub")), "new.txt", "new"u8.ToArray());
        var first = new TaskCompletionSource<FileOpResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.BlockList = first;
        var t1 = pane.NavigateAsync(locA).AsTask();
        FileWorkspaceHarness.WaitUntil(() => pane.State == FilePaneState.Loading && pane.Generation == 1);
        var second = new TaskCompletionSource<FileOpResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.BlockList = second;
        var t2 = pane.NavigateAsync(locB).AsTask();
        FileWorkspaceHarness.WaitUntil(() => pane.Generation == 2);
        first.TrySetResult(ListResult(File("old.txt", "old"u8.ToArray())));
        t1.GetAwaiter().GetResult();
        AppTestHost.Check(pane.Generation == 2);
        AppTestHost.Check(pane.Location is not null && pane.Location.Path.Equals(locB.Path));
        foreach (var entry in pane.Entries)
            AppTestHost.Check(entry.DisplayName != "old.txt");
        second.SetResult(ListResult(File("new.txt", "new"u8.ToArray())));
        t2.GetAwaiter().GetResult();
        AppTestHost.Check(pane.State == FilePaneState.Ready);
        AppTestHost.Check(pane.Entries.Count == 1);
        AppTestHost.Check(pane.Entries[0].DisplayName == "new.txt");
    }

    static void CancelDropsLateList()
    {
        var fake = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceA, "local"));
        fake.AddFile("late.txt", "late"u8.ToArray());
        var pane = new FilePaneViewModel("left");
        pane.ApplyConnection(DeviceFreshness.Current, ConnectionPhase.Ready);
        var block = new TaskCompletionSource<FileOpResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.BlockList = block;
        var load = pane.NavigateAsync(FileLocation.Local(fake)).AsTask();
        FileWorkspaceHarness.WaitUntil(() => pane.State == FilePaneState.Loading);
        pane.CancelListFromKeyboard();
        AppTestHost.Check(pane.State == FilePaneState.Cancelled);
        block.TrySetResult(ListResult(File("late.txt", "late"u8.ToArray())));
        load.GetAwaiter().GetResult();
        AppTestHost.Check(pane.State == FilePaneState.Cancelled);
        AppTestHost.Check(pane.Entries.Count == 0);
    }

    static void OfflineKeepsCache()
    {
        var fake = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceA, "local"));
        fake.AddFile("cached.txt", "keep"u8.ToArray());
        var pane = new FilePaneViewModel("left");
        pane.ApplyConnection(DeviceFreshness.Current, ConnectionPhase.Ready);
        pane.NavigateAsync(FileLocation.Local(fake)).AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(pane.State == FilePaneState.Ready);
        AppTestHost.Check(pane.TransferEnabled);
        pane.ApplyConnection(DeviceFreshness.Stale, ConnectionPhase.Offline);
        AppTestHost.Check(pane.State == FilePaneState.Offline);
        AppTestHost.Check(pane.StaleBannerVisible);
        AppTestHost.Check(!pane.TransferEnabled);
        AppTestHost.Check(pane.Entries.Count == 1);
        AppTestHost.Check(pane.Entries[0].DisplayName == "cached.txt");
        AppTestHost.Check(pane.HeaderText.Contains(AppTestHost.DeviceA.Value.ToString("D"), StringComparison.Ordinal));
    }

    static void PermissionDenied()
    {
        var fake = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceA, "local"));
        fake.NextListCode = FileOpCodes.PermissionDenied;
        var pane = new FilePaneViewModel("left");
        pane.ApplyConnection(DeviceFreshness.Current, ConnectionPhase.Ready);
        pane.NavigateAsync(FileLocation.Local(fake)).AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(pane.State == FilePaneState.PermissionDenied);
        AppTestHost.Check(pane.ErrorCode == FileOpCodes.PermissionDenied);
        AppTestHost.Check(!pane.TransferEnabled);
        AppTestHost.Check(pane.StatusText == ShellStrings.FilePermissionDenied);
    }

    static void PermissionDeniedClearsPriorEntries()
    {
        var fake = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceA, "local"));
        fake.AddFile("cached.txt", "keep"u8.ToArray());
        fake.AddDirectory("secret");
        var pane = new FilePaneViewModel("left");
        pane.ApplyConnection(DeviceFreshness.Current, ConnectionPhase.Ready);
        pane.NavigateAsync(FileLocation.Local(fake)).AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(pane.Entries.Count == 2);
        fake.NextListCode = FileOpCodes.PermissionDenied;
        var secret = FileLocation.Local(fake, FileLocator.Root.Append(FileWorkspaceHarness.Comp("secret")));
        pane.NavigateAsync(secret).AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(pane.State == FilePaneState.PermissionDenied);
        AppTestHost.Check(pane.Location is not null && pane.Location.Path.Equals(secret.Path));
        AppTestHost.Check(pane.Entries.Count == 0);
        foreach (var entry in pane.Entries)
            AppTestHost.Check(entry.DisplayName != "cached.txt");
    }

    static void UnicodeSpaceNames()
    {
        var fake = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceA, "local"));
        var longName = new string('名', 80) + " file.txt";
        fake.AddFile("你好 world.txt", "ok"u8.ToArray());
        fake.AddFile(longName, "long"u8.ToArray());
        var pane = new FilePaneViewModel("left");
        pane.ApplyConnection(DeviceFreshness.Current, ConnectionPhase.Ready);
        pane.NavigateAsync(FileLocation.Local(fake)).AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(pane.State == FilePaneState.Ready);
        AppTestHost.Check(pane.Entries.Count == 2);
        var unicode = FileWorkspaceHarness.Comp("你好 world.txt");
        FileEntryViewState? found = null;
        foreach (var entry in pane.Entries)
        {
            if (entry.Name.Equals(unicode))
                found = entry;
        }

        AppTestHost.Check(found is not null);
        AppTestHost.Check(found!.DisplayText.Contains("你好 world.txt", StringComparison.Ordinal));
        AppTestHost.Check(!pane.NavigateFromText(found.DisplayText));
        AppTestHost.Check(UntrustedText.TryAsPath(found.DisplayText) is null);
        pane.SelectByDisplayName(found.DisplayText);
        AppTestHost.Check(pane.Selected.Count == 0);
        pane.Select(unicode, true);
        AppTestHost.Check(pane.Selected.Count == 1);
        AppTestHost.Check(pane.Selected[0].Name.Equals(unicode));
        var longComp = FileWorkspaceHarness.Comp(longName);
        var sawLong = false;
        foreach (var entry in pane.Entries)
        {
            if (entry.Name.Equals(longComp))
                sawLong = true;
        }

        AppTestHost.Check(sawLong);
        AppTestHost.Check(pane.Location is not null && pane.Location.Path.Equals(FileLocator.Root));
    }

    static void EmptyDirectory()
    {
        var fake = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceA, "local"));
        var pane = new FilePaneViewModel("left");
        pane.ApplyConnection(DeviceFreshness.Current, ConnectionPhase.Ready);
        pane.NavigateAsync(FileLocation.Local(fake)).AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(pane.State == FilePaneState.Empty);
        AppTestHost.Check(pane.StatusText == ShellStrings.FileEmptyDirectory);
        AppTestHost.Check(pane.TransferEnabled);
    }

    static void IncompatibleDisables()
    {
        var fake = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceA, "local"));
        fake.AddFile("a.txt", "x"u8.ToArray());
        var pane = new FilePaneViewModel("left");
        pane.ApplyConnection(DeviceFreshness.Current, ConnectionPhase.Ready);
        pane.NavigateAsync(FileLocation.Local(fake)).AsTask().GetAwaiter().GetResult();
        pane.ApplyConnection(DeviceFreshness.Current, ConnectionPhase.Incompatible);
        AppTestHost.Check(pane.State == FilePaneState.Incompatible);
        AppTestHost.Check(!pane.TransferEnabled);
    }

    static void BreadcrumbKeyboard()
    {
        var fake = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceA, "local"));
        fake.AddDirectory("sub");
        fake.AddFile(FileLocator.Root.Append(FileWorkspaceHarness.Comp("sub")), "inner.txt", "i"u8.ToArray());
        var pane = new FilePaneViewModel("left");
        pane.ApplyConnection(DeviceFreshness.Current, ConnectionPhase.Ready);
        var sub = FileLocation.Local(fake, FileLocator.Root.Append(FileWorkspaceHarness.Comp("sub")));
        pane.NavigateAsync(sub).AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(pane.Breadcrumb.Count == 2);
        AppTestHost.Check(pane.Breadcrumb[0].Path.Equals(FileLocator.Root));
        pane.NavigateBreadcrumbFromKeyboardAsync(0).AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(pane.Location is not null && pane.Location.Path.Equals(FileLocator.Root));
        AppTestHost.Check(!pane.NavigateFromText(pane.Breadcrumb[0].DisplayText));
    }

    static void SymlinkNotDirectory()
    {
        var fake = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceA, "local"));
        fake.AddSymlink("link-dir");
        fake.AddDirectory("real");
        var pane = new FilePaneViewModel("left");
        pane.ApplyConnection(DeviceFreshness.Current, ConnectionPhase.Ready);
        pane.NavigateAsync(FileLocation.Local(fake)).AsTask().GetAwaiter().GetResult();
        var link = FileWorkspaceHarness.Comp("link-dir");
        pane.OpenEntryAsync(link).AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(pane.Location is not null && pane.Location.Path.Equals(FileLocator.Root));
        FileEntryViewState? found = null;
        foreach (var entry in pane.Entries)
        {
            if (entry.Name.Equals(link))
                found = entry;
        }

        AppTestHost.Check(found is not null);
        AppTestHost.Check(found!.Symlink);
        AppTestHost.Check(found.AutomationName.Contains("symlink", StringComparison.Ordinal));
    }

    static FileEntry File(string name, byte[] data) =>
        new(
            FileWorkspaceHarness.Comp(name),
            name,
            FileEntryKind.File,
            (ulong)data.Length,
            0,
            1,
            new FileIdentity(data),
            false,
            new FileObservation(new string('a', 64)));

    static FileOpResult ListResult(params FileEntry[] entries) =>
        new(true, FileOpCodes.Ok, Entries: entries);
}
