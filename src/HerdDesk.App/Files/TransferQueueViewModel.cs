using HerdDesk.Contracts;

namespace HerdDesk.App;

public sealed class TransferQueueViewModel
{
    readonly List<TransferJobViewState> _jobs = [];
    readonly TimeProvider _time;

    public TransferQueueViewModel(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        CancelJobAutomationName = ShellStrings.FileCancelJob;
        RetryAutomationName = ShellStrings.Retry;
    }

    public IReadOnlyList<TransferJobViewState> Jobs => _jobs;
    public string CancelJobAutomationName { get; }
    public string RetryAutomationName { get; }

    public int ActiveCount
    {
        get
        {
            var count = 0;
            foreach (var job in _jobs)
            {
                if (job.IsActive)
                    count++;
            }

            return count;
        }
    }

    public bool HasActiveJobs => ActiveCount > 0;

    public TransferJobViewState? Find(Guid jobId)
    {
        foreach (var job in _jobs)
        {
            if (job.JobId == jobId)
                return job;
        }

        return null;
    }

    public TransferJobViewState Enqueue(TransferDraft draft, FileEntryViewState entry)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(entry);
        var job = new TransferJobViewState(Guid.NewGuid(), draft, entry, _time.GetUtcNow());
        _jobs.Add(job);
        return job;
    }

    public void ApplyProgress(Guid jobId, ulong accepted, ulong? total, DateTimeOffset? now = null)
    {
        var job = Find(jobId);
        job?.ApplyProgress(accepted, total, now ?? _time.GetUtcNow());
    }

    public void ApplyResult(TransferResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var job = Find(result.JobId);
        job?.ApplyResult(result);
    }

    public void SetPhase(Guid jobId, TransferJobPhase phase) => Find(jobId)?.SetPhase(phase);

    public void SetCleanup(Guid jobId, string state) => Find(jobId)?.SetCleanup(state);

    public void SetError(Guid jobId, string? code) => Find(jobId)?.SetError(code);

    public TransferDraft RetryDraft(Guid jobId, DateTimeOffset? now = null)
    {
        var job = Find(jobId) ?? throw new InvalidOperationException(FileOpCodes.NotFound);
        var entry = new FileEntryViewState(
            job.Name,
            job.DisplayName,
            FileEntryKind.File,
            job.BytesTotal ?? 0,
            0,
            false,
            new FileObservation(""),
            new FileIdentity(job.Name.Raw));
        return TransferDraft.Create(
            job.Source,
            job.Destination,
            [entry],
            0,
            now ?? _time.GetUtcNow(),
            job.TotalKnown,
            job.BytesTotal);
    }
}
