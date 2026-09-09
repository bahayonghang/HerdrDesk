using System.Globalization;
using HerdDesk.Contracts;

namespace HerdDesk.App;

public sealed class FilePaneViewModel
{
    readonly object _gate = new();
    readonly List<FileEntryViewState> _entries = [];
    readonly List<FileBreadcrumbSegment> _breadcrumb = [];
    CancellationTokenSource? _listCts;
    long _generation;

    public FilePaneViewModel(string paneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paneId);
        PaneId = paneId;
        State = FilePaneState.Initial;
        Freshness = DeviceFreshness.Unknown;
        Phase = ConnectionPhase.Offline;
        StatusText = ShellStrings.Empty;
        AutomationName = "file-pane-" + paneId + "-initial";
        RefreshAutomationName = ShellStrings.FileRefresh;
        CancelListAutomationName = ShellStrings.FileCancelList;
    }

    public string PaneId { get; }
    public FileLocation? Location { get; private set; }
    public FilePaneState State { get; private set; }
    public long Generation => _generation;
    public DeviceFreshness Freshness { get; private set; }
    public ConnectionPhase Phase { get; private set; }
    public IReadOnlyList<FileEntryViewState> Entries => _entries;
    public IReadOnlyList<FileBreadcrumbSegment> Breadcrumb => _breadcrumb;
    public bool TransferEnabled { get; private set; }
    public bool StaleBannerVisible { get; private set; }
    public string StatusText { get; private set; }
    public string? ErrorCode { get; private set; }
    public string AutomationName { get; private set; }
    public string RefreshAutomationName { get; }
    public string CancelListAutomationName { get; }
    public DateTimeOffset? LastUpdatedUtc { get; private set; }
    public string HeaderText { get; private set; } = "";

    public IReadOnlyList<FileEntryViewState> Selected
    {
        get
        {
            List<FileEntryViewState> selected = [];
            foreach (var entry in _entries)
            {
                if (entry.Selected)
                    selected.Add(entry);
            }

            return selected;
        }
    }

    public async ValueTask NavigateAsync(FileLocation location, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);
        await LoadAsync(location, refreshing: false, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (Location is null)
            return ValueTask.CompletedTask;
        return LoadAsync(Location, refreshing: true, cancellationToken);
    }

    public ValueTask RefreshFromKeyboardAsync() => RefreshAsync();

    public ValueTask RefreshFromScreenReaderAsync() => RefreshAsync();

    public void CancelList()
    {
        lock (_gate)
        {
            _listCts?.Cancel();
            _generation++;
            if (State is FilePaneState.Loading or FilePaneState.Refreshing)
                SetState(FilePaneState.Cancelled, FileOpCodes.Cancelled);
        }
    }

    public void CancelListFromKeyboard() => CancelList();

    public void CancelListFromScreenReader() => CancelList();

    public void ApplyConnection(DeviceFreshness freshness, ConnectionPhase phase)
    {
        Freshness = freshness;
        Phase = phase;
        if (phase is ConnectionPhase.Incompatible)
        {
            SetState(FilePaneState.Incompatible, ShellStrings.Incompatible);
            return;
        }

        if (phase is ConnectionPhase.Offline)
        {
            StaleBannerVisible = _entries.Count > 0;
            SetState(FilePaneState.Offline, FileOpCodes.OutcomeUnknown);
            return;
        }

        if (phase is ConnectionPhase.Stale || freshness is DeviceFreshness.Stale or DeviceFreshness.Unknown)
        {
            StaleBannerVisible = _entries.Count > 0;
            SetState(FilePaneState.Stale, ShellStrings.Stale);
            return;
        }

        StaleBannerVisible = false;
        if (State is FilePaneState.Stale or FilePaneState.Offline or FilePaneState.Incompatible)
            SetState(_entries.Count == 0 ? FilePaneState.Empty : FilePaneState.Ready, null);
        else
            UpdateTransferEnabled();
        RebuildHeader();
    }

    public void Select(FileComponent name, bool selected)
    {
        ArgumentNullException.ThrowIfNull(name);
        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Name.Equals(name))
                _entries[i] = _entries[i] with { Selected = selected };
        }
    }

    public void FocusEntry(FileComponent name)
    {
        ArgumentNullException.ThrowIfNull(name);
        for (var i = 0; i < _entries.Count; i++)
            _entries[i] = _entries[i] with { Focused = _entries[i].Name.Equals(name) };
    }

    public void SelectByDisplayName(string display) => _ = display;

    public bool NavigateFromText(string display)
    {
        _ = UntrustedText.TryAsPath(display);
        _ = UntrustedText.TryAsCommand(display);
        _ = UntrustedText.TryAsUri(display);
        _ = UntrustedText.TryAsNavigation(display);
        return false;
    }

    public ValueTask OpenEntryAsync(FileComponent name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Location is null)
            return ValueTask.CompletedTask;
        FileEntryViewState? found = null;
        foreach (var entry in _entries)
        {
            if (entry.Name.Equals(name))
            {
                found = entry;
                break;
            }
        }

        if (found is null)
            return ValueTask.CompletedTask;
        if (found.Symlink || found.Kind == FileEntryKind.Symlink)
            return ValueTask.CompletedTask;
        if (found.Kind != FileEntryKind.Directory)
            return ValueTask.CompletedTask;
        return NavigateAsync(Location.WithPath(Location.Path.Append(found.Name)));
    }

    public ValueTask NavigateBreadcrumbAsync(int index)
    {
        if (Location is null || index < 0 || index >= _breadcrumb.Count)
            return ValueTask.CompletedTask;
        return NavigateAsync(Location.WithPath(_breadcrumb[index].Path));
    }

    public ValueTask NavigateBreadcrumbFromKeyboardAsync(int index) => NavigateBreadcrumbAsync(index);

    public ValueTask NavigateBreadcrumbFromScreenReaderAsync(int index) => NavigateBreadcrumbAsync(index);

    async ValueTask LoadAsync(FileLocation location, bool refreshing, CancellationToken cancellationToken)
    {
        long generation;
        CancellationTokenSource cts;
        lock (_gate)
        {
            generation = ++_generation;
            _listCts?.Cancel();
            cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _listCts = cts;
            Location = location;
            if (!refreshing)
                _entries.Clear();
            RebuildBreadcrumb();
            RebuildHeader();
            SetState(refreshing ? FilePaneState.Refreshing : FilePaneState.Loading, null);
        }

        FileOpResult result;
        try
        {
            result = await location.Endpoint.ListAsync(location.Path, 1000, null, cts.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                if (generation == _generation)
                    SetState(FilePaneState.Cancelled, FileOpCodes.Cancelled);
            }

            return;
        }

        lock (_gate)
        {
            if (generation != _generation)
                return;
            ApplyList(result);
        }
    }

    void ApplyList(FileOpResult result)
    {
        ErrorCode = result.Succeeded ? null : result.Code;
        if (!result.Succeeded)
        {
            if (!IsRefreshingState)
                _entries.Clear();
            if (result.Code == FileOpCodes.PermissionDenied)
                SetState(FilePaneState.PermissionDenied, result.Code);
            else if (result.Code == FileOpCodes.Cancelled)
                SetState(FilePaneState.Cancelled, result.Code);
            else if (result.Code is FileOpCodes.Unsupported)
                SetState(FilePaneState.Incompatible, result.Code);
            else
                SetState(FilePaneState.Failed, result.Code);
            return;
        }

        _entries.Clear();
        if (result.Entries is not null)
        {
            foreach (var entry in result.Entries)
                _entries.Add(FileEntryViewState.FromEntry(entry));
        }

        LastUpdatedUtc = DateTimeOffset.UtcNow;
        SetState(_entries.Count == 0 ? FilePaneState.Empty : FilePaneState.Ready, null);
    }

    void SetState(FilePaneState state, string? error)
    {
        State = state;
        if (error is not null)
            ErrorCode = error;
        StatusText = state switch
        {
            FilePaneState.Loading => ShellStrings.FileLoading,
            FilePaneState.Refreshing => ShellStrings.FileRefreshing,
            FilePaneState.Ready => ShellStrings.Ready,
            FilePaneState.Empty => ShellStrings.FileEmptyDirectory,
            FilePaneState.Stale => ShellStrings.FileStaleBanner,
            FilePaneState.Offline => ShellStrings.FileOffline,
            FilePaneState.PermissionDenied => ShellStrings.FilePermissionDenied,
            FilePaneState.Failed => ShellStrings.Error,
            FilePaneState.Incompatible => ShellStrings.Incompatible,
            FilePaneState.Cancelled => ShellStrings.FileCancelled,
            _ => ShellStrings.Empty
        };
        AutomationName = "file-pane-" + PaneId + "-" + state.ToString().ToLowerInvariant();
        UpdateTransferEnabled();
        RebuildHeader();
    }

    bool IsRefreshingState => State is FilePaneState.Refreshing;

    void UpdateTransferEnabled()
    {
        TransferEnabled = (State is FilePaneState.Ready or FilePaneState.Empty)
            && Phase is ConnectionPhase.Ready
            && Freshness is DeviceFreshness.Current
            && !StaleBannerVisible;
    }

    void RebuildBreadcrumb()
    {
        _breadcrumb.Clear();
        if (Location is null)
            return;
        var acc = FileLocator.Root;
        var rootText = Location.Kind == FileLocationKind.Local
            ? ShellStrings.FileLocal
            : Location.Device.Value.ToString("D");
        _breadcrumb.Add(new FileBreadcrumbSegment(acc, UntrustedText.Display(rootText), "breadcrumb-root"));
        for (var i = 0; i < Location.Path.Components.Count; i++)
        {
            acc = acc.Append(Location.Path.Components[i]);
            _breadcrumb.Add(new FileBreadcrumbSegment(
                acc,
                UntrustedText.Display(Location.Path.Components[i].Raw),
                "breadcrumb-" + i.ToString(CultureInfo.InvariantCulture)));
        }
    }

    void RebuildHeader()
    {
        if (Location is null)
        {
            HeaderText = "";
            return;
        }

        var freshness = StaleBannerVisible || State is FilePaneState.Stale or FilePaneState.Offline
            ? ShellStrings.Stale
            : Phase is ConnectionPhase.Ready ? ShellStrings.Ready : StatusText;
        HeaderText = Location.Device.Value.ToString("D")
            + " "
            + UntrustedText.Display(Location.Session.EndpointKey)
            + " "
            + UntrustedText.Display(Location.ProviderId)
            + " "
            + freshness;
    }
}
