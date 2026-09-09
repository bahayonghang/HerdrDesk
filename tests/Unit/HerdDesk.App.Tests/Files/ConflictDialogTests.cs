using System.Text;
using HerdDesk.App;
using HerdDesk.Contracts;

internal static class ConflictDialogTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("confirm does not silently overwrite", NoSilentOverwrite),
        ("replace uses the conflict token", ReplaceWithToken),
        ("keep both writes a new name", KeepBoth),
        ("cancel leaves the destination unchanged", CancelConflict),
        ("token mismatch rejects replace", TokenMismatch),
        ("apply all stays on the current draft", ApplyAllCurrentDraftOnly),
        ("dismiss equals cancel", DismissIsCancel),
        ("single resolve continues remaining jobs of the same draft", RemainingJobsAfterSingleResolve),
        ("single resolve still prompts the next conflict", NextConflictStillPrompts)
    ];

    static void NoSilentOverwrite()
    {
        var left = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceA, "local"));
        var right = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceB, "ssh"));
        left.AddFile("a.txt", "new"u8.ToArray());
        right.AddFile("a.txt", "old"u8.ToArray());
        var workspace = FileWorkspaceHarness.Workspace(left, right);
        workspace.Left.Select(FileWorkspaceHarness.Comp("a.txt"), true);
        workspace.CreateDraft(TransferDirection.LeftToRight);
        workspace.ConfirmTransferAsync().AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(workspace.Conflict.IsOpen);
        AppTestHost.Check(Encoding.UTF8.GetString(right.ReadAll("a.txt")) == "old");
        AppTestHost.Check(workspace.Queue.Jobs[0].Phase != TransferJobPhase.Completed);
        AppTestHost.Check(workspace.LastError == FileWorkspaceCodes.SilentOverwriteDenied);
        AppTestHost.Check(workspace.Conflict.ExactPathDisplay.Length > 0);
        AppTestHost.Check(workspace.Conflict.ScopeText == ShellStrings.FileConflictScopeDraft);
    }

    static void ReplaceWithToken()
    {
        var left = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceA, "local"));
        var right = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceB, "ssh"), replaceSupported: true);
        left.AddFile("a.txt", "new"u8.ToArray());
        right.AddFile("a.txt", "old"u8.ToArray());
        var workspace = FileWorkspaceHarness.Workspace(left, right);
        workspace.Left.Select(FileWorkspaceHarness.Comp("a.txt"), true);
        workspace.CreateDraft(TransferDirection.LeftToRight);
        workspace.ConfirmTransferAsync().AsTask().GetAwaiter().GetResult();
        var token = workspace.Conflict.Current!.Token;
        workspace.ResolveConflictFromKeyboardAsync(ConflictIntent.Replace, token)
            .AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(!workspace.Conflict.IsOpen);
        AppTestHost.Check(Encoding.UTF8.GetString(right.ReadAll("a.txt")) == "new");
        AppTestHost.Check(workspace.Queue.Jobs[0].Phase == TransferJobPhase.Completed);
        AppTestHost.Check(workspace.Queue.Jobs[0].HashVerified);
    }

    static void KeepBoth()
    {
        var left = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceA, "local"));
        var right = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceB, "ssh"));
        left.AddFile("a.txt", "new"u8.ToArray());
        right.AddFile("a.txt", "old"u8.ToArray());
        var workspace = FileWorkspaceHarness.Workspace(left, right);
        workspace.Left.Select(FileWorkspaceHarness.Comp("a.txt"), true);
        workspace.CreateDraft(TransferDirection.LeftToRight);
        workspace.ConfirmTransferAsync().AsTask().GetAwaiter().GetResult();
        var token = workspace.Conflict.Current!.Token;
        AppTestHost.Check(workspace.Conflict.KeepBothCandidateDisplay.Contains("a (1).txt", StringComparison.Ordinal));
        workspace.ResolveConflictAsync(ConflictIntent.KeepBoth, token).AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(Encoding.UTF8.GetString(right.ReadAll("a.txt")) == "old");
        AppTestHost.Check(right.Has("a (1).txt"));
        AppTestHost.Check(Encoding.UTF8.GetString(right.ReadAll("a (1).txt")) == "new");
        AppTestHost.Check(workspace.Queue.Jobs[0].Phase == TransferJobPhase.Completed);
    }

    static void CancelConflict()
    {
        var left = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceA, "local"));
        var right = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceB, "ssh"));
        left.AddFile("a.txt", "new"u8.ToArray());
        right.AddFile("a.txt", "old"u8.ToArray());
        var workspace = FileWorkspaceHarness.Workspace(left, right);
        workspace.Left.Select(FileWorkspaceHarness.Comp("a.txt"), true);
        workspace.CreateDraft(TransferDirection.LeftToRight);
        workspace.ConfirmTransferAsync().AsTask().GetAwaiter().GetResult();
        workspace.ResolveConflictAsync(ConflictIntent.Cancel, null).AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(Encoding.UTF8.GetString(right.ReadAll("a.txt")) == "old");
        AppTestHost.Check(!right.Has("a (1).txt"));
        AppTestHost.Check(workspace.Queue.Jobs[0].Phase == TransferJobPhase.Cancelled);
        AppTestHost.Check(!workspace.Conflict.IsOpen);
    }

    static void TokenMismatch()
    {
        var left = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceA, "local"));
        var right = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceB, "ssh"), replaceSupported: true);
        left.AddFile("a.txt", "new"u8.ToArray());
        right.AddFile("a.txt", "old"u8.ToArray());
        var workspace = FileWorkspaceHarness.Workspace(left, right);
        workspace.Left.Select(FileWorkspaceHarness.Comp("a.txt"), true);
        workspace.CreateDraft(TransferDirection.LeftToRight);
        workspace.ConfirmTransferAsync().AsTask().GetAwaiter().GetResult();
        workspace.ResolveConflictAsync(ConflictIntent.Replace, new FileObservation(new string('f', 64)))
            .AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(workspace.Conflict.IsOpen);
        AppTestHost.Check(workspace.LastError == FileOpCodes.StaleTarget);
        AppTestHost.Check(Encoding.UTF8.GetString(right.ReadAll("a.txt")) == "old");
        AppTestHost.Check(workspace.Queue.Jobs[0].Phase != TransferJobPhase.Completed);
    }

    static void ApplyAllCurrentDraftOnly()
    {
        var left = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceA, "local"));
        var right = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceB, "ssh"), replaceSupported: true);
        left.AddFile("a.txt", "n1"u8.ToArray());
        left.AddFile("b.txt", "n2"u8.ToArray());
        left.AddFile("c.txt", "n3"u8.ToArray());
        right.AddFile("a.txt", "o1"u8.ToArray());
        right.AddFile("b.txt", "o2"u8.ToArray());
        var other = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceC, "other"), replaceSupported: true);
        other.AddFile("c.txt", "keep"u8.ToArray());
        var workspace = FileWorkspaceHarness.Workspace(left, right);
        workspace.Left.Select(FileWorkspaceHarness.Comp("a.txt"), true);
        workspace.Left.Select(FileWorkspaceHarness.Comp("b.txt"), true);
        var draft = workspace.CreateDraft(TransferDirection.LeftToRight);
        AppTestHost.Check(draft is not null);
        workspace.ConfirmTransferAsync().AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(workspace.Conflict.IsOpen);
        var firstDraft = workspace.Conflict.Current!.DraftId;
        var otherEntry = FileEntryViewState.FromEntry(new FileEntry(
            FileWorkspaceHarness.Comp("c.txt"),
            "c.txt",
            FileEntryKind.File,
            2,
            0,
            1,
            new FileIdentity("n3"u8.ToArray()),
            false,
            new FileObservation(new string('9', 64))));
        var foreign = TransferDraft.Create(
            FileLocation.Local(left),
            FileLocation.Remote(AppTestHost.DeviceC, other),
            [otherEntry],
            0,
            DateTimeOffset.UtcNow,
            true,
            2);
        var foreignJob = workspace.Queue.Enqueue(foreign, otherEntry);
        AppTestHost.Check(foreignJob.DraftId != firstDraft);
        var token = workspace.Conflict.Current.Token;
        workspace.ResolveConflictAsync(ConflictIntent.Replace, token, applyAll: true)
            .AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(Encoding.UTF8.GetString(right.ReadAll("a.txt")) == "n1");
        AppTestHost.Check(Encoding.UTF8.GetString(right.ReadAll("b.txt")) == "n2");
        AppTestHost.Check(Encoding.UTF8.GetString(other.ReadAll("c.txt")) == "keep");
        AppTestHost.Check(foreignJob.Phase == TransferJobPhase.Queued);
        foreach (var job in workspace.Queue.Jobs)
        {
            if (job.JobId == foreignJob.JobId)
                continue;
            AppTestHost.Check(job.DraftId == firstDraft);
        }
    }

    static void DismissIsCancel()
    {
        var dialog = new ConflictDialogViewModel();
        var source = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceA, "local"));
        var dest = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceB, "ssh"));
        dialog.Open(new ConflictPrompt(
            Guid.NewGuid(),
            Guid.NewGuid(),
            FileLocation.Local(source),
            FileLocation.Remote(AppTestHost.DeviceB, dest),
            FileWorkspaceHarness.Comp("a.txt"),
            "src",
            "dst",
            "local / a.txt",
            ShellStrings.FileConflictScopeDraft,
            new FileObservation(new string('1', 64))));
        AppTestHost.Check(dialog.IsOpen);
        var intent = dialog.CancelFromScreenReader();
        AppTestHost.Check(intent == ConflictIntent.Cancel);
        AppTestHost.Check(!dialog.IsOpen);
        AppTestHost.Check(dialog.RestoreFocusTarget == "copy-action");
        AppTestHost.Check(dialog.ReplaceAutomationName == ShellStrings.FileConflictReplace);
        AppTestHost.Check(dialog.KeepBothAutomationName == ShellStrings.FileConflictKeepBoth);
        AppTestHost.Check(dialog.CancelAutomationName == ShellStrings.FileConflictCancel);
    }

    static void RemainingJobsAfterSingleResolve()
    {
        var left = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceA, "local"));
        var right = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceB, "ssh"), replaceSupported: true);
        left.AddFile("a.txt", "n1"u8.ToArray());
        left.AddFile("b.txt", "n2"u8.ToArray());
        right.AddFile("a.txt", "o1"u8.ToArray());
        var workspace = FileWorkspaceHarness.Workspace(left, right);
        workspace.Left.Select(FileWorkspaceHarness.Comp("a.txt"), true);
        workspace.Left.Select(FileWorkspaceHarness.Comp("b.txt"), true);
        workspace.CreateDraft(TransferDirection.LeftToRight);
        workspace.ConfirmTransferAsync().AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(workspace.Conflict.IsOpen);
        var token = workspace.Conflict.Current!.Token;
        workspace.ResolveConflictAsync(ConflictIntent.Replace, token, applyAll: false)
            .AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(!workspace.Conflict.IsOpen);
        AppTestHost.Check(Encoding.UTF8.GetString(right.ReadAll("a.txt")) == "n1");
        AppTestHost.Check(right.Has("b.txt"));
        AppTestHost.Check(Encoding.UTF8.GetString(right.ReadAll("b.txt")) == "n2");
        AppTestHost.Check(workspace.Queue.Jobs.Count == 2);
        foreach (var job in workspace.Queue.Jobs)
            AppTestHost.Check(job.Phase == TransferJobPhase.Completed);
        AppTestHost.Check(workspace.Queue.Jobs[0].DraftId == workspace.Queue.Jobs[1].DraftId);
    }

    static void NextConflictStillPrompts()
    {
        var left = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceA, "local"));
        var right = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceB, "ssh"), replaceSupported: true);
        left.AddFile("a.txt", "n1"u8.ToArray());
        left.AddFile("b.txt", "n2"u8.ToArray());
        right.AddFile("a.txt", "o1"u8.ToArray());
        right.AddFile("b.txt", "o2"u8.ToArray());
        var workspace = FileWorkspaceHarness.Workspace(left, right);
        workspace.Left.Select(FileWorkspaceHarness.Comp("a.txt"), true);
        workspace.Left.Select(FileWorkspaceHarness.Comp("b.txt"), true);
        workspace.CreateDraft(TransferDirection.LeftToRight);
        workspace.ConfirmTransferAsync().AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(workspace.Conflict.IsOpen);
        var firstName = workspace.Conflict.Current!.Name;
        var token = workspace.Conflict.Current.Token;
        workspace.ResolveConflictAsync(ConflictIntent.Replace, token, applyAll: false)
            .AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(workspace.Conflict.IsOpen);
        AppTestHost.Check(!workspace.Conflict.Current!.Name.Equals(firstName));
        var a = Encoding.UTF8.GetString(right.ReadAll("a.txt"));
        var b = Encoding.UTF8.GetString(right.ReadAll("b.txt"));
        AppTestHost.Check((a == "n1" && b == "o2") || (a == "o1" && b == "n2"));
        var completed = 0;
        foreach (var job in workspace.Queue.Jobs)
        {
            if (job.Phase == TransferJobPhase.Completed)
                completed++;
        }

        AppTestHost.Check(completed == 1);
        AppTestHost.Check(workspace.LastError == FileWorkspaceCodes.SilentOverwriteDenied);
    }
}
