using System.Globalization;
using System.Text;
using HerdDesk.Contracts;

namespace HerdDesk.App;

public enum FileLocationKind
{
    Local,
    Remote
}

public enum FilePaneState
{
    Initial,
    Loading,
    Ready,
    Empty,
    Refreshing,
    Stale,
    Offline,
    PermissionDenied,
    Failed,
    Incompatible,
    Cancelled
}

public enum TransferDirection
{
    LeftToRight,
    RightToLeft
}

public enum TransferJobPhase
{
    Queued,
    Preparing,
    Transferring,
    Verifying,
    Renaming,
    Completed,
    Cancelling,
    Cancelled,
    Failed,
    Unknown
}

public enum ConflictIntent
{
    Replace,
    KeepBoth,
    Cancel,
    TokenMismatch
}

public enum ExitDecision
{
    Closed,
    Ask
}

public enum ExitWithJobsAction
{
    Return,
    Wait,
    CancelAndExit
}

public static class FileWorkspaceCodes
{
    public const string ConflictTokenMismatch = "conflict_token_mismatch";
    public const string SilentOverwriteDenied = "silent_overwrite_denied";
    public const string TargetOffline = "target_offline";
    public const string DisplayIsNotPath = "display_is_not_path";
    public const string ActiveJobsOpen = "active_jobs_open";
}

public static class TransferCleanupState
{
    public const string None = "none";
    public const string Cleaning = "cleaning";
    public const string Cleaned = "cleaned";
    public const string FailedCleanup = "failed_cleanup";
}

public sealed class FileLocation : IEquatable<FileLocation>
{
    FileLocation(FileLocationKind kind, TransferEndpointKey key, FileLocator path, IFileEndpoint endpoint)
    {
        Kind = kind;
        Key = key;
        Path = path;
        Endpoint = endpoint;
    }

    public FileLocationKind Kind { get; }
    public TransferEndpointKey Key { get; }
    public DeviceId Device => Key.Device;
    public SessionKey Session => Key.Session;
    public ConnectionEpoch Epoch => Key.Epoch;
    public string ProviderId => Key.EndpointId;
    public FileLocator Path { get; }
    public IFileEndpoint Endpoint { get; }

    public FileLocation WithPath(FileLocator path) => new(Kind, Key, path, Endpoint);

    public static FileLocation Local(IFileEndpoint endpoint, FileLocator? path = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return new(FileLocationKind.Local, endpoint.Key, path ?? FileLocator.Root, endpoint);
    }

    public static FileLocation Remote(DeviceId device, IFileEndpoint endpoint, FileLocator? path = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (device.Value == Guid.Empty)
            throw new ArgumentException(FileOpCodes.InvalidPath, nameof(device));
        if (endpoint.Key.Device != device)
            throw new ArgumentException(FileOpCodes.InvalidPath, nameof(device));
        return new(FileLocationKind.Remote, endpoint.Key, path ?? FileLocator.Root, endpoint);
    }

    public bool Equals(FileLocation? other)
    {
        if (other is null)
            return false;
        return Kind == other.Kind
            && Device.Equals(other.Device)
            && ProviderId == other.ProviderId
            && Path.Equals(other.Path);
    }

    public override bool Equals(object? obj) => Equals(obj as FileLocation);

    public override int GetHashCode() => HashCode.Combine(Kind, Device, ProviderId, Path);
}

public sealed record FileBreadcrumbSegment(
    FileLocator Path,
    string DisplayText,
    string AutomationName);

public sealed record FileEntryViewState(
    FileComponent Name,
    string DisplayName,
    FileEntryKind Kind,
    ulong Size,
    ulong Mtime,
    bool Symlink,
    FileObservation Observation,
    FileIdentity Identity,
    bool Selected = false,
    bool Focused = false)
{
    public string DisplayText => UntrustedText.Display(DisplayName);

    public string AutomationName
    {
        get
        {
            var kind = Symlink || Kind == FileEntryKind.Symlink
                ? "symlink"
                : Kind.ToString().ToLowerInvariant();
            var selected = Selected ? " selected" : "";
            var focused = Focused ? " focused" : "";
            return DisplayText + " " + kind + " " + Size.ToString(CultureInfo.InvariantCulture) + selected + focused;
        }
    }

    public static FileEntryViewState FromEntry(FileEntry entry, bool selected = false, bool focused = false) =>
        new(
            entry.Name,
            entry.DisplayName,
            entry.Kind,
            entry.Size,
            entry.Mtime,
            entry.Symlink,
            entry.Observation,
            entry.Identity,
            selected,
            focused);
}

public sealed record TransferDraft(
    Guid Id,
    DateTimeOffset CreatedAt,
    long Generation,
    FileLocation Source,
    FileLocation Destination,
    IReadOnlyList<FileEntryViewState> Entries,
    bool TotalKnown,
    ulong? KnownTotalBytes)
{
    public int EntryCount => Entries.Count;
    public DeviceId SourceDevice => Source.Device;
    public DeviceId DestinationDevice => Destination.Device;

    public static TransferDraft Create(
        FileLocation source,
        FileLocation destination,
        IReadOnlyList<FileEntryViewState> entries,
        long generation,
        DateTimeOffset createdAt,
        bool totalKnown,
        ulong? knownTotalBytes)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(entries);
        return new(
            Guid.NewGuid(),
            createdAt,
            generation,
            source,
            destination,
            [.. entries],
            totalKnown,
            totalKnown ? knownTotalBytes : null);
    }
}

public sealed record TransferLease(
    Guid JobId,
    FileLocation Source,
    FileLocation Destination,
    DeviceId SourceDevice,
    DeviceId DestinationDevice,
    FileLocator DestinationParent,
    FileComponent Name,
    ConnectionEpoch SourceEpoch,
    ConnectionEpoch DestinationEpoch);

public sealed record ConflictPrompt(
    Guid JobId,
    Guid DraftId,
    FileLocation Source,
    FileLocation Destination,
    FileComponent Name,
    string SourceDisplay,
    string DestinationDisplay,
    string ExactPathDisplay,
    string ScopeText,
    FileObservation Token);

public sealed record ExitWithJobsPrompt(int ActiveCount, string Message);

public sealed class TransferJobViewState
{
    DateTimeOffset _sampleAt;
    ulong _sampleBytes;

    public TransferJobViewState(
        Guid jobId,
        TransferDraft draft,
        FileEntryViewState entry,
        DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(entry);
        JobId = jobId;
        DraftId = draft.Id;
        Source = draft.Source;
        Destination = draft.Destination;
        Name = entry.Name;
        DisplayName = entry.DisplayName;
        TotalKnown = draft.TotalKnown;
        BytesTotal = draft.TotalKnown ? entry.Size : null;
        BytesAccepted = 0;
        Phase = TransferJobPhase.Queued;
        StartedAt = startedAt;
        _sampleAt = startedAt;
        CleanupState = TransferCleanupState.None;
        Lease = new TransferLease(
            jobId,
            draft.Source,
            draft.Destination,
            draft.SourceDevice,
            draft.DestinationDevice,
            draft.Destination.Path,
            entry.Name,
            draft.Source.Epoch,
            draft.Destination.Epoch);
    }

    public Guid JobId { get; }
    public Guid DraftId { get; }
    public FileLocation Source { get; }
    public FileLocation Destination { get; }
    public FileComponent Name { get; }
    public string DisplayName { get; }
    public TransferLease Lease { get; }
    public TransferJobPhase Phase { get; private set; }
    public ulong BytesAccepted { get; private set; }
    public ulong? BytesTotal { get; private set; }
    public bool TotalKnown { get; private set; }
    public double? SpeedBytesPerSecond { get; private set; }
    public TimeSpan? Eta { get; private set; }
    public string? ErrorCode { get; private set; }
    public string CleanupState { get; private set; }
    public bool HashVerified { get; private set; }
    public bool RenameCommitted { get; private set; }
    public FileLocator? FinalPath { get; private set; }
    public string? Sha256 { get; private set; }
    public DateTimeOffset StartedAt { get; }

    public string DisplayText => UntrustedText.Display(DisplayName);

    public string SourceBreadcrumb => FormatRoute(Source);
    public string DestinationBreadcrumb => FormatRoute(Destination);

    public bool IsTerminal =>
        Phase is TransferJobPhase.Completed or TransferJobPhase.Cancelled or TransferJobPhase.Failed;

    public bool IsActive => !IsTerminal;

    public bool CommitProjected =>
        Phase == TransferJobPhase.Completed && HashVerified && RenameCommitted;

    public double? Percent
    {
        get
        {
            if (!TotalKnown || BytesTotal is not ulong total)
                return null;
            if (total == 0)
                return Phase == TransferJobPhase.Completed ? 100 : 0;
            return (double)BytesAccepted / total * 100;
        }
    }

    internal void SetPhase(TransferJobPhase phase)
    {
        if (CommitProjected)
            return;
        Phase = phase;
    }

    internal void SetCleanup(string state)
    {
        if (CommitProjected)
            return;
        CleanupState = state;
    }

    internal void SetError(string? code)
    {
        if (CommitProjected)
            return;
        ErrorCode = code;
    }

    internal void ApplyProgress(ulong accepted, ulong? total, DateTimeOffset now)
    {
        if (CommitProjected)
            return;
        BytesAccepted = accepted;
        if (total is ulong known)
        {
            TotalKnown = true;
            BytesTotal = known;
        }
        else
        {
            TotalKnown = false;
            BytesTotal = null;
        }

        var elapsed = (now - _sampleAt).TotalSeconds;
        var delta = accepted >= _sampleBytes ? accepted - _sampleBytes : 0;
        if (accepted == 0 || elapsed <= 0 || delta == 0)
        {
            SpeedBytesPerSecond = null;
            Eta = null;
        }
        else
        {
            SpeedBytesPerSecond = delta / elapsed;
            if (TotalKnown && BytesTotal is ulong t && t > accepted && SpeedBytesPerSecond > 0)
                Eta = TimeSpan.FromSeconds((t - accepted) / SpeedBytesPerSecond.Value);
            else
                Eta = null;
        }

        _sampleAt = now;
        _sampleBytes = accepted;
        if (Phase is TransferJobPhase.Queued)
            Phase = TransferJobPhase.Transferring;
    }

    internal void ApplyResult(TransferResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (CommitProjected)
            return;
        ErrorCode = result.Succeeded ? null : result.Code;
        BytesAccepted = result.Length;
        if (result.Progress is { } progress)
        {
            BytesAccepted = progress.BytesAccepted;
            BytesTotal = progress.BytesTotal;
            TotalKnown = true;
        }

        if (result.Succeeded
            && result.Stage == TransferStage.Completed
            && !string.IsNullOrEmpty(result.Sha256))
        {
            HashVerified = true;
            RenameCommitted = true;
            Phase = TransferJobPhase.Completed;
            FinalPath = result.FinalPath;
            Sha256 = result.Sha256;
            CleanupState = TransferCleanupState.None;
            if (BytesAccepted == 0 || (BytesTotal is ulong t && t == 0))
            {
                SpeedBytesPerSecond = null;
                Eta = null;
            }

            return;
        }

        if (result.Stage == TransferStage.Completed)
        {
            HashVerified = false;
            RenameCommitted = false;
            Phase = TransferJobPhase.Unknown;
            ErrorCode = result.Code ?? FileOpCodes.OutcomeUnknown;
            return;
        }

        Phase = Project(result.Stage);
        if (Phase == TransferJobPhase.Cancelled)
            CleanupState = TransferCleanupState.Cleaned;
        else if (result.Stage == TransferStage.FailedCleanup)
            CleanupState = TransferCleanupState.FailedCleanup;
        else if (Phase == TransferJobPhase.Failed)
            CleanupState = TransferCleanupState.Cleaned;
        else if (Phase == TransferJobPhase.Cancelling)
            CleanupState = TransferCleanupState.Cleaning;
    }

    public static TransferJobPhase Project(TransferStage stage) => stage switch
    {
        TransferStage.Queued => TransferJobPhase.Queued,
        TransferStage.Admitted or TransferStage.Starting or TransferStage.Staging => TransferJobPhase.Preparing,
        TransferStage.Transferring => TransferJobPhase.Transferring,
        TransferStage.Verifying => TransferJobPhase.Verifying,
        TransferStage.Committing => TransferJobPhase.Renaming,
        TransferStage.Completed => TransferJobPhase.Completed,
        TransferStage.CancelRequested or TransferStage.Cleaning => TransferJobPhase.Cancelling,
        TransferStage.Cancelled => TransferJobPhase.Cancelled,
        TransferStage.Failed or TransferStage.FailedCleanup => TransferJobPhase.Failed,
        _ => TransferJobPhase.Unknown
    };

    static string FormatRoute(FileLocation location)
    {
        var owner = location.Kind == FileLocationKind.Local
            ? ShellStrings.FileLocal
            : location.Device.Value.ToString("D");
        var builder = new StringBuilder();
        builder.Append(owner);
        builder.Append(' ');
        builder.Append(UntrustedText.Display(location.ProviderId));
        foreach (var component in location.Path.Components)
        {
            builder.Append(" / ");
            builder.Append(UntrustedText.Display(component.Raw));
        }

        return builder.ToString();
    }
}
