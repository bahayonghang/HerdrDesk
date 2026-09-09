using HerdDesk.Contracts;
using HerdDesk.Core;

namespace HerdDesk.App;

public sealed class FileWorkspaceViewModel
{
    readonly TransferCoordinator _coordinator;
    readonly TimeProvider _time;
    readonly Dictionary<Guid, CancellationTokenSource> _jobTokens = [];
    TransferJobViewState? _conflictJob;
    TransferDirection _direction = TransferDirection.LeftToRight;

    public FileWorkspaceViewModel(TransferCoordinator coordinator, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        _coordinator = coordinator;
        _time = time ?? TimeProvider.System;
        Left = new FilePaneViewModel("left");
        Right = new FilePaneViewModel("right");
        Queue = new TransferQueueViewModel(_time);
        Conflict = new ConflictDialogViewModel();
        FocusedPaneId = "left";
        ActiveTab = "left";
        Layout = LayoutBreakpoint.Wide;
        CopyToRightAutomationName = ShellStrings.FileCopyToRight;
        CopyToLeftAutomationName = ShellStrings.FileCopyToLeft;
        ConfirmTransferAutomationName = ShellStrings.FileConfirmTransfer;
        CancelJobAutomationName = ShellStrings.FileCancelJob;
        Left.ApplyConnection(DeviceFreshness.Current, ConnectionPhase.Ready);
        Right.ApplyConnection(DeviceFreshness.Current, ConnectionPhase.Ready);
    }

    public FilePaneViewModel Left { get; }
    public FilePaneViewModel Right { get; }
    public TransferQueueViewModel Queue { get; }
    public ConflictDialogViewModel Conflict { get; }
    public TransferDraft? Draft { get; private set; }
    public string FocusedPaneId { get; private set; }
    public string ActiveTab { get; private set; }
    public LayoutBreakpoint Layout { get; private set; }
    public bool NarrowLayout => Layout == LayoutBreakpoint.Narrow;
    public ExitWithJobsPrompt? ExitPrompt { get; private set; }
    public bool Closed { get; private set; }
    public bool WaitingForJobs { get; private set; }
    public string? LastError { get; private set; }
    public string CopyToRightAutomationName { get; }
    public string CopyToLeftAutomationName { get; }
    public string ConfirmTransferAutomationName { get; }
    public string CancelJobAutomationName { get; }

    public FilePaneViewModel FocusedPane => FocusedPaneId == Right.PaneId ? Right : Left;

    public FilePaneViewModel SourcePane =>
        _direction == TransferDirection.LeftToRight ? Left : Right;

    public FilePaneViewModel DestinationPane =>
        _direction == TransferDirection.LeftToRight ? Right : Left;

    public TransferJobViewState? ActiveJob
    {
        get
        {
            foreach (var job in Queue.Jobs)
            {
                if (job.IsActive)
                    return job;
            }

            foreach (var job in Queue.Jobs)
            {
                if (job.Phase == TransferJobPhase.Completed)
                    return job;
            }

            return Queue.Jobs.Count > 0 ? Queue.Jobs[^1] : null;
        }
    }

    public string RouteSummary
    {
        get
        {
            var job = ActiveJob;
            if (job is not null)
                return job.SourceBreadcrumb + " → " + job.DestinationBreadcrumb;
            if (Draft is not null)
                return FormatLocation(Draft.Source) + " → " + FormatLocation(Draft.Destination);
            if (SourcePane.Location is null || DestinationPane.Location is null)
                return "";
            return FormatLocation(SourcePane.Location) + " → " + FormatLocation(DestinationPane.Location);
        }
    }

    public bool HasInvisibleBackgroundJobs => false;

    public void SwitchFocus(string paneId)
    {
        if (paneId == Right.PaneId)
            FocusedPaneId = Right.PaneId;
        else
            FocusedPaneId = Left.PaneId;
        ActiveTab = FocusedPaneId;
    }

    public void SetLayout(LayoutBreakpoint layout)
    {
        Layout = layout;
        if (layout == LayoutBreakpoint.Narrow)
            ActiveTab = FocusedPaneId;
    }

    public void SwitchNarrowTab(string paneId) => SwitchFocus(paneId);

    public ValueTask SwitchDeviceAsync(FileLocation location) => FocusedPane.NavigateAsync(location);

    public void SwitchDevice(FileLocation location) =>
        SwitchDeviceAsync(location).AsTask().GetAwaiter().GetResult();

    public TransferDraft? CreateDraft(TransferDirection direction)
    {
        _direction = direction;
        var source = SourcePane;
        var dest = DestinationPane;
        if (source.Location is null || dest.Location is null)
            return null;
        if (!source.TransferEnabled || !dest.TransferEnabled)
            return null;
        var selected = source.Selected;
        if (selected.Count == 0)
            return null;
        ulong total = 0;
        var known = true;
        foreach (var entry in selected)
        {
            if (entry.Kind != FileEntryKind.File)
                known = false;
            else
                total += entry.Size;
        }

        Draft = TransferDraft.Create(
            source.Location,
            dest.Location,
            selected,
            source.Generation,
            _time.GetUtcNow(),
            known,
            known ? total : null);
        return Draft;
    }

    public TransferDraft CreateDraft(
        FileLocation source,
        FileLocation destination,
        IReadOnlyList<FileEntryViewState> entries,
        bool totalKnown,
        ulong? knownTotalBytes = null)
    {
        Draft = TransferDraft.Create(
            source,
            destination,
            entries,
            0,
            _time.GetUtcNow(),
            totalKnown,
            knownTotalBytes);
        return Draft;
    }

    public ValueTask CreateDraftFromKeyboardAsync(TransferDirection direction)
    {
        CreateDraft(direction);
        return ValueTask.CompletedTask;
    }

    public async ValueTask ConfirmTransferAsync()
    {
        var draft = Draft;
        if (draft is null || draft.Entries.Count == 0 || Conflict.IsOpen)
            return;
        List<TransferJobViewState> jobs = [];
        foreach (var entry in draft.Entries)
        {
            if (entry.Kind != FileEntryKind.File)
                continue;
            var job = Queue.Enqueue(draft, entry);
            _jobTokens[job.JobId] = new CancellationTokenSource();
            jobs.Add(job);
        }

        foreach (var job in jobs)
        {
            await TryStartJobAsync(job).ConfigureAwait(false);
            if (Conflict.IsOpen)
                return;
        }
    }

    public ValueTask ConfirmTransferFromKeyboardAsync() => ConfirmTransferAsync();

    public ValueTask ConfirmTransferFromScreenReaderAsync() => ConfirmTransferAsync();

    public async ValueTask ResolveConflictAsync(ConflictIntent intent, FileObservation? token, bool applyAll = false)
    {
        if (_conflictJob is null)
        {
            Conflict.Submit(intent, token, applyAll);
            return;
        }

        var job = _conflictJob;
        var decision = Conflict.Submit(intent, token, applyAll);
        if (decision == ConflictIntent.TokenMismatch)
        {
            LastError = FileOpCodes.StaleTarget;
            return;
        }

        _conflictJob = null;
        if (decision == ConflictIntent.Cancel)
        {
            Queue.SetPhase(job.JobId, TransferJobPhase.Cancelled);
            Queue.SetCleanup(job.JobId, TransferCleanupState.Cleaned);
            Queue.SetError(job.JobId, FileOpCodes.Cancelled);
            if (applyAll)
                CancelDraftJobs(job.DraftId);
            return;
        }

        var mode = decision == ConflictIntent.Replace ? FileConflictMode.Replace : FileConflictMode.KeepBoth;
        var observation = mode == FileConflictMode.Replace ? token : null;
        await CopyJobAsync(job, mode, observation).ConfigureAwait(false);
        await ContinueDraftAsync(job.DraftId, applyAll ? mode : FileConflictMode.Fail)
            .ConfigureAwait(false);
    }

    public ValueTask ResolveConflictFromKeyboardAsync(ConflictIntent intent, FileObservation? token, bool applyAll = false) =>
        ResolveConflictAsync(intent, token, applyAll);

    public async ValueTask CancelJobAsync(Guid jobId)
    {
        var job = Queue.Find(jobId);
        if (job is null || job.IsTerminal || job.CommitProjected)
            return;
        if (_jobTokens.TryGetValue(jobId, out var cts))
            cts.Cancel();
        if (job.IsTerminal || job.CommitProjected)
            return;
        var queued = job.Phase is TransferJobPhase.Queued or TransferJobPhase.Preparing
            || !_jobTokens.ContainsKey(jobId);
        Queue.SetPhase(jobId, TransferJobPhase.Cancelling);
        Queue.SetCleanup(jobId, TransferCleanupState.Cleaning);
        if (queued)
        {
            Queue.ApplyResult(new TransferResult(
                false, FileOpCodes.Cancelled, TransferStage.Cancelled, jobId, null, null, 0, null));
        }

        await ValueTask.CompletedTask;
    }

    public ValueTask CancelJobFromKeyboardAsync(Guid jobId) => CancelJobAsync(jobId);

    public TransferDraft Retry(Guid jobId)
    {
        var draft = Queue.RetryDraft(jobId, _time.GetUtcNow());
        Draft = draft;
        return draft;
    }

    public FileLocation? OpenCompletedTarget(Guid jobId)
    {
        var job = Queue.Find(jobId);
        if (job is null || job.Phase != TransferJobPhase.Completed)
            return null;
        FilePaneViewModel? owner = null;
        if (Left.Location is { } left && left.Device.Equals(job.Destination.Device)
            && left.Path.Equals(job.Destination.Path))
            owner = Left;
        else if (Right.Location is { } right && right.Device.Equals(job.Destination.Device)
            && right.Path.Equals(job.Destination.Path))
            owner = Right;
        if (owner is { State: FilePaneState.Offline or FilePaneState.Stale })
            LastError = FileWorkspaceCodes.TargetOffline;
        return job.Destination;
    }

    public ExitDecision RequestClose()
    {
        if (!Queue.HasActiveJobs)
        {
            Closed = true;
            ExitPrompt = null;
            return ExitDecision.Closed;
        }

        ExitPrompt = new ExitWithJobsPrompt(Queue.ActiveCount, ShellStrings.FileExitWithJobs);
        LastError = FileWorkspaceCodes.ActiveJobsOpen;
        return ExitDecision.Ask;
    }

    public async ValueTask ConfirmExitAsync(ExitWithJobsAction action)
    {
        if (action == ExitWithJobsAction.Return)
        {
            ExitPrompt = null;
            return;
        }

        if (action == ExitWithJobsAction.Wait)
        {
            WaitingForJobs = true;
            ExitPrompt = null;
            return;
        }

        foreach (var job in Queue.Jobs)
        {
            if (job.IsActive)
                await CancelJobAsync(job.JobId).ConfigureAwait(false);
        }

        Closed = true;
        ExitPrompt = null;
        WaitingForJobs = false;
    }

    async ValueTask TryStartJobAsync(TransferJobViewState job)
    {
        var destPath = job.Destination.Path.Append(job.Name);
        var stat = await job.Destination.Endpoint.StatAsync(destPath, null).ConfigureAwait(false);
        if (stat.Succeeded && stat.Stat is not null)
        {
            LastError = FileWorkspaceCodes.SilentOverwriteDenied;
            Queue.SetPhase(job.JobId, TransferJobPhase.Queued);
            _conflictJob = job;
            Conflict.Open(new ConflictPrompt(
                job.JobId,
                job.DraftId,
                job.Source,
                job.Destination,
                job.Name,
                job.SourceBreadcrumb,
                job.DestinationBreadcrumb,
                ConflictDialogViewModel.FormatExactPath(job.Destination, job.Name),
                ShellStrings.FileConflictScopeDraft,
                stat.Stat.Observation));
            return;
        }

        await CopyJobAsync(job, FileConflictMode.Fail, null).ConfigureAwait(false);
    }

    async ValueTask CopyJobAsync(TransferJobViewState job, FileConflictMode mode, FileObservation? observation)
    {
        Queue.SetPhase(job.JobId, TransferJobPhase.Preparing);
        if (!_jobTokens.TryGetValue(job.JobId, out var cts))
        {
            cts = new CancellationTokenSource();
            _jobTokens[job.JobId] = cts;
        }

        Queue.SetPhase(job.JobId, TransferJobPhase.Transferring);
        TransferResult result;
        try
        {
            result = await _coordinator.CopyAsync(
                new TransferRequest(
                    job.JobId,
                    job.Source.Endpoint,
                    job.Source.Path.Append(job.Name),
                    job.Destination.Endpoint,
                    job.Destination.Path,
                    job.Name,
                    mode,
                    observation),
                cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            result = new TransferResult(
                false, FileOpCodes.Cancelled, TransferStage.Cancelled, job.JobId, null, null, 0, null);
        }

        if (result.Succeeded
            && result.Stage == TransferStage.Completed
            && !string.IsNullOrEmpty(result.Sha256))
        {
            Queue.ApplyResult(result);
            return;
        }

        if (cts.IsCancellationRequested || result.Code == FileOpCodes.Cancelled)
        {
            Queue.ApplyResult(new TransferResult(
                false, FileOpCodes.Cancelled, TransferStage.Cancelled, job.JobId, null, null, 0, null));
            return;
        }

        if (result.Code == FileOpCodes.NameExists)
        {
            LastError = FileWorkspaceCodes.SilentOverwriteDenied;
            var stat = await job.Destination.Endpoint.StatAsync(job.Destination.Path.Append(job.Name), null)
                .ConfigureAwait(false);
            if (stat.Succeeded && stat.Stat is not null)
            {
                Queue.SetPhase(job.JobId, TransferJobPhase.Queued);
                _conflictJob = job;
                Conflict.Open(new ConflictPrompt(
                    job.JobId,
                    job.DraftId,
                    job.Source,
                    job.Destination,
                    job.Name,
                    job.SourceBreadcrumb,
                    job.DestinationBreadcrumb,
                    ConflictDialogViewModel.FormatExactPath(job.Destination, job.Name),
                    ShellStrings.FileConflictScopeDraft,
                    stat.Stat.Observation));
                return;
            }
        }

        Queue.ApplyResult(result);
    }

    async ValueTask ContinueDraftAsync(Guid draftId, FileConflictMode mode)
    {
        List<TransferJobViewState> queued = [];
        foreach (var job in Queue.Jobs)
        {
            if (job.DraftId == draftId && job.Phase == TransferJobPhase.Queued)
                queued.Add(job);
        }

        foreach (var job in queued)
        {
            if (Conflict.IsOpen)
                return;
            var destPath = job.Destination.Path.Append(job.Name);
            var stat = await job.Destination.Endpoint.StatAsync(destPath, null).ConfigureAwait(false);
            FileObservation? observation = null;
            var copyMode = mode;
            if (stat.Succeeded && stat.Stat is not null)
            {
                if (mode == FileConflictMode.Replace)
                    observation = stat.Stat.Observation;
            }
            else
                copyMode = FileConflictMode.Fail;
            await CopyJobAsync(job, copyMode, observation).ConfigureAwait(false);
        }
    }

    void CancelDraftJobs(Guid draftId)
    {
        foreach (var job in Queue.Jobs)
        {
            if (job.DraftId == draftId && job.IsActive)
            {
                Queue.SetPhase(job.JobId, TransferJobPhase.Cancelled);
                Queue.SetCleanup(job.JobId, TransferCleanupState.Cleaned);
                Queue.SetError(job.JobId, FileOpCodes.Cancelled);
            }
        }
    }

    static string FormatLocation(FileLocation location)
    {
        var owner = location.Kind == FileLocationKind.Local
            ? ShellStrings.FileLocal
            : location.Device.Value.ToString("D");
        return owner + " " + UntrustedText.Display(location.ProviderId);
    }
}
