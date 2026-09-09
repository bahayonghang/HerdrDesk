using System.Security.Cryptography;
using HerdDesk.App;
using HerdDesk.Contracts;

internal static class TransferQueueProjectionTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("zero byte job has no fake speed", ZeroByteNoSpeed),
        ("unknown total has no percent or eta", UnknownTotal),
        ("failure stays in queue after cleanup", FailureCleanupStays),
        ("completed requires hash and rename projection", CompletedNeedsHash),
        ("cancel does not drop the job before cleanup", CancelStaysUntilCleaned),
        ("close with active jobs asks then cancel-and-exit", ExitAsksThenCancels),
        ("completed hash result is not overwritten by cancel", CompletedNotOverwrittenByCancel)
    ];

    static void ZeroByteNoSpeed()
    {
        var left = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceA, "local"));
        var right = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceB, "ssh"));
        left.AddFile("empty.bin", []);
        var workspace = FileWorkspaceHarness.Workspace(left, right);
        workspace.Left.Select(FileWorkspaceHarness.Comp("empty.bin"), true);
        workspace.CreateDraft(TransferDirection.LeftToRight);
        workspace.ConfirmTransferAsync().AsTask().GetAwaiter().GetResult();
        var job = workspace.Queue.Jobs[0];
        AppTestHost.Check(job.Phase == TransferJobPhase.Completed);
        AppTestHost.Check(job.BytesAccepted == 0);
        AppTestHost.Check(job.BytesTotal == 0);
        AppTestHost.Check(job.SpeedBytesPerSecond is null);
        AppTestHost.Check(job.Eta is null);
        AppTestHost.Check(job.Percent == 100);
        AppTestHost.Check(job.HashVerified);
        AppTestHost.Check(job.RenameCommitted);
        AppTestHost.Check(job.Sha256 == Convert.ToHexString(SHA256.HashData([])).ToLowerInvariant());
        AppTestHost.Check(right.Has("empty.bin"));
        AppTestHost.Check(right.ReadAll("empty.bin").Length == 0);
    }

    static void UnknownTotal()
    {
        var queue = new TransferQueueViewModel();
        var source = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceA, "local"));
        var dest = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceB, "ssh"));
        var entry = FileEntryViewState.FromEntry(new FileEntry(
            FileWorkspaceHarness.Comp("blob.bin"),
            "blob.bin",
            FileEntryKind.File,
            0,
            0,
            1,
            new FileIdentity("blob"u8.ToArray()),
            false,
            new FileObservation(new string('b', 64))));
        var draft = TransferDraft.Create(
            FileLocation.Local(source),
            FileLocation.Remote(AppTestHost.DeviceB, dest),
            [entry],
            1,
            DateTimeOffset.UtcNow,
            totalKnown: false,
            knownTotalBytes: 50);
        var job = queue.Enqueue(draft, entry);
        queue.ApplyProgress(job.JobId, 10, null);
        AppTestHost.Check(!job.TotalKnown);
        AppTestHost.Check(job.BytesTotal is null);
        AppTestHost.Check(job.Percent is null);
        AppTestHost.Check(job.Eta is null);
        AppTestHost.Check(job.BytesAccepted == 10);
        AppTestHost.Check(job.Phase == TransferJobPhase.Transferring);
    }

    static void FailureCleanupStays()
    {
        var left = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceA, "local"));
        var right = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceB, "ssh"));
        left.AddFile("payload.bin", "hello"u8.ToArray());
        right.FailNextWrite = true;
        var workspace = FileWorkspaceHarness.Workspace(left, right);
        workspace.Left.Select(FileWorkspaceHarness.Comp("payload.bin"), true);
        workspace.CreateDraft(TransferDirection.LeftToRight);
        workspace.ConfirmTransferAsync().AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(workspace.Queue.Jobs.Count == 1);
        var job = workspace.Queue.Jobs[0];
        AppTestHost.Check(job.Phase == TransferJobPhase.Failed);
        AppTestHost.Check(job.CleanupState == TransferCleanupState.Cleaned);
        AppTestHost.Check(!right.Has("payload.bin"));
        AppTestHost.Check(!right.HasTemp);
        AppTestHost.Check(job.ErrorCode == FileOpCodes.PermissionDenied);
        AppTestHost.Check(!job.HashVerified);
    }

    static void CompletedNeedsHash()
    {
        var queue = new TransferQueueViewModel();
        var source = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceA, "local"));
        var dest = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceB, "ssh"));
        var entry = FileEntryViewState.FromEntry(new FileEntry(
            FileWorkspaceHarness.Comp("a.txt"),
            "a.txt",
            FileEntryKind.File,
            1,
            0,
            1,
            new FileIdentity("a"u8.ToArray()),
            false,
            new FileObservation(new string('c', 64))));
        var draft = TransferDraft.Create(
            FileLocation.Local(source),
            FileLocation.Remote(AppTestHost.DeviceB, dest),
            [entry],
            1,
            DateTimeOffset.UtcNow,
            true,
            1);
        var job = queue.Enqueue(draft, entry);
        queue.ApplyResult(new TransferResult(
            true, FileOpCodes.Ok, TransferStage.Completed, job.JobId, null, null, 1, null));
        AppTestHost.Check(job.Phase == TransferJobPhase.Unknown);
        AppTestHost.Check(!job.HashVerified);
        AppTestHost.Check(!job.RenameCommitted);
        queue.ApplyResult(new TransferResult(
            true, FileOpCodes.Ok, TransferStage.Completed, job.JobId, FileLocator.Root.Append(entry.Name),
            new string('d', 64), 1, null));
        AppTestHost.Check(job.Phase == TransferJobPhase.Completed);
        AppTestHost.Check(job.HashVerified);
        AppTestHost.Check(job.RenameCommitted);
    }

    static void CancelStaysUntilCleaned()
    {
        var left = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceA, "local"));
        var right = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceB, "ssh"));
        var payload = new byte[64];
        payload.AsSpan().Fill(7);
        left.AddFile("slow.bin", payload);
        var workspace = FileWorkspaceHarness.Workspace(left, right);
        workspace.Left.Select(FileWorkspaceHarness.Comp("slow.bin"), true);
        workspace.CreateDraft(TransferDirection.LeftToRight);
        var block = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        right.BlockWrite = block;
        var confirm = workspace.ConfirmTransferAsync().AsTask();
        FileWorkspaceHarness.WaitUntil(() => workspace.Queue.Jobs.Count == 1
            && workspace.Queue.Jobs[0].Phase is TransferJobPhase.Transferring or TransferJobPhase.Preparing
                or TransferJobPhase.Queued);
        var job = workspace.Queue.Jobs[0];
        workspace.CancelJobFromKeyboardAsync(job.JobId).AsTask().GetAwaiter().GetResult();
        block.TrySetCanceled();
        confirm.GetAwaiter().GetResult();
        AppTestHost.Check(workspace.Queue.Jobs.Count == 1);
        AppTestHost.Check(workspace.Queue.Jobs[0].Phase is TransferJobPhase.Cancelled or TransferJobPhase.Cancelling);
        AppTestHost.Check(workspace.Queue.Jobs[0].CleanupState is TransferCleanupState.Cleaned
            or TransferCleanupState.Cleaning);
        AppTestHost.Check(!right.Has("slow.bin") || workspace.Queue.Jobs[0].Phase != TransferJobPhase.Completed);
        AppTestHost.Check(!workspace.Queue.Jobs[0].HashVerified || workspace.Queue.Jobs[0].Phase == TransferJobPhase.Completed);
    }

    static void ExitAsksThenCancels()
    {
        var queue = new TransferQueueViewModel();
        var source = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceA, "local"));
        var dest = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceB, "ssh"));
        var entry = FileEntryViewState.FromEntry(new FileEntry(
            FileWorkspaceHarness.Comp("a.txt"),
            "a.txt",
            FileEntryKind.File,
            1,
            0,
            1,
            new FileIdentity("a"u8.ToArray()),
            false,
            new FileObservation(new string('e', 64))));
        var draft = TransferDraft.Create(
            FileLocation.Local(source),
            FileLocation.Remote(AppTestHost.DeviceB, dest),
            [entry],
            1,
            DateTimeOffset.UtcNow,
            true,
            1);
        var workspace = new FileWorkspaceViewModel(FileWorkspaceHarness.Coordinator());
        workspace.CreateDraft(FileLocation.Local(source), FileLocation.Remote(AppTestHost.DeviceB, dest), [entry], true, 1);
        queue.Enqueue(draft, entry);
        var job = workspace.Queue.Enqueue(draft, entry);
        workspace.Queue.SetPhase(job.JobId, TransferJobPhase.Transferring);
        AppTestHost.Check(workspace.RequestClose() == ExitDecision.Ask);
        AppTestHost.Check(workspace.ExitPrompt is not null);
        AppTestHost.Check(!workspace.HasInvisibleBackgroundJobs);
        workspace.ConfirmExitAsync(ExitWithJobsAction.CancelAndExit).AsTask().GetAwaiter().GetResult();
        AppTestHost.Check(workspace.Closed);
        AppTestHost.Check(!workspace.Queue.HasActiveJobs);
        AppTestHost.Check(!workspace.HasInvisibleBackgroundJobs);
    }

    static void CompletedNotOverwrittenByCancel()
    {
        var left = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceA, "local"));
        var right = new FakeFileEndpoint(FileWorkspaceHarness.Key(AppTestHost.DeviceB, "ssh"));
        left.AddFile("done.bin", "xyz"u8.ToArray());
        var workspace = FileWorkspaceHarness.Workspace(left, right);
        workspace.Left.Select(FileWorkspaceHarness.Comp("done.bin"), true);
        workspace.CreateDraft(TransferDirection.LeftToRight);
        workspace.ConfirmTransferAsync().AsTask().GetAwaiter().GetResult();
        var job = workspace.Queue.Jobs[0];
        AppTestHost.Check(job.Phase == TransferJobPhase.Completed);
        AppTestHost.Check(job.HashVerified);
        AppTestHost.Check(job.RenameCommitted);
        workspace.CancelJobAsync(job.JobId).AsTask().GetAwaiter().GetResult();
        workspace.Queue.ApplyResult(new TransferResult(
            false, FileOpCodes.Cancelled, TransferStage.Cancelled, job.JobId, null, null, 0, null));
        AppTestHost.Check(job.Phase == TransferJobPhase.Completed);
        AppTestHost.Check(job.HashVerified);
        AppTestHost.Check(job.RenameCommitted);
        AppTestHost.Check(job.CleanupState != TransferCleanupState.Cleaning);
        AppTestHost.Check(right.Has("done.bin"));
    }
}
