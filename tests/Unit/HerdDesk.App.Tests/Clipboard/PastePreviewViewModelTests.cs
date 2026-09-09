using System.Text;
using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Clipboard;
using HerdDesk.Infrastructure.Configuration;

internal static class PastePreviewViewModelTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("paste preview names target multiline size and default cancel", AutomationNames),
        ("keyboard cancel sends zero bytes", KeyboardCancel),
        ("file paste does not advertise agent attachment", FileDoesNotAdvertise),
        ("cache full is not a fake success", CacheFull),
        ("stale file paste does not continue to attachment", StaleFileDoesNotContinue),
        ("image cache store does not hold a leaked lease", ImageCacheNotLeased)
    ];

    static void AutomationNames()
    {
        using var env = new PasteEnv("hello\nworld");
        env.Vm.BeginPasteFromKeyboard(env.Confirm());
        AppTestHost.Check(env.Vm.State == PasteUiState.NeedsMultilineConfirm);
        AppTestHost.Check(env.Vm.DefaultIsCancel);
        AppTestHost.Check(env.Vm.DefaultButton == "cancel");
        AppTestHost.Check(env.Vm.AutomationName.Contains(env.Pane.PaneId, StringComparison.Ordinal));
        AppTestHost.Check(env.Vm.AutomationName.Contains(ShellStrings.PasteMultiline, StringComparison.Ordinal));
        AppTestHost.Check(env.Vm.AutomationName.Contains("字节", StringComparison.Ordinal));
        AppTestHost.Check(env.Vm.AutomationName.Contains(ShellStrings.PasteDefaultCancel, StringComparison.Ordinal));
        AppTestHost.Check(env.Vm.CancelAutomationName == ShellStrings.PasteCancel);
        AppTestHost.Check(env.Vm.SendAutomationName == ShellStrings.PasteSend);
        AppTestHost.Check(env.Vm.LineCountText.Contains("行", StringComparison.Ordinal));
        AppTestHost.Check(!env.Vm.ClaimsAgentAccepted);
        AppTestHost.Check(!env.Vm.WatcherEnabled);
    }

    static void KeyboardCancel()
    {
        using var env = new PasteEnv("hello\nworld");
        env.Vm.BeginPasteFromScreenReader(env.Confirm());
        var cancelled = env.Vm.DismissEsc();
        AppTestHost.Check(cancelled.SentBytes.Length == 0);
        AppTestHost.Check(env.Input.Submitted.Count == 0);
        AppTestHost.Check(env.Vm.State == PasteUiState.Cancelled);
    }

    static void FileDoesNotAdvertise()
    {
        using var env = new PasteEnv(files: true);
        env.Vm.BeginPaste(env.Confirm());
        AppTestHost.Check(env.Vm.Intent!.Kind == ClipboardIntentKind.FileList);
        var continued = env.Vm.ContinueToAttachment();
        AppTestHost.Check(continued.Code == ClipboardCodes.ContinueToAttachment);
        AppTestHost.Check(!env.Vm.ClaimsAgentAccepted);
        AppTestHost.Check(!env.Vm.PreviewImpliesReceipt);
        AppTestHost.Check(env.Attachments.Evidence.AdvertisesAttachmentSupport is false);
        AppTestHost.Check(env.Input.Submitted.Count == 0);
    }

    static void CacheFull()
    {
        using var env = new PasteEnv(image: true, cacheBytes: 16);
        var filler = env.Cache.Store("0123456789ab"u8.ToArray());
        AppTestHost.Check(filler.Succeeded);
        env.Vm.BeginPaste(env.Confirm());
        var continued = env.Vm.ContinueToAttachment();
        AppTestHost.Check(!continued.Succeeded);
        AppTestHost.Check(continued.Code == ClipboardCodes.CacheFull);
        AppTestHost.Check(!env.Vm.ClaimsAgentAccepted);
    }

    static void StaleFileDoesNotContinue()
    {
        using var env = new PasteEnv(files: true);
        env.Vm.BeginPaste(env.Confirm());
        var stale = env.Vm.NoteLiveTarget(new PasteLiveTarget(
            new PaneKey(env.Pane.Session, env.Pane.WorkspaceId, "other"),
            new ConnectionEpoch(1),
            "lease-1",
            1,
            true,
            TerminalAccess.Controlling,
            new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
        AppTestHost.Check(!stale.Succeeded);
        AppTestHost.Check(env.Vm.State == PasteUiState.Stale);
        var continued = env.Vm.ContinueToAttachment();
        AppTestHost.Check(!continued.Succeeded);
        AppTestHost.Check(continued.Code is ClipboardCodes.TargetStale or ClipboardCodes.StaleEpoch);
        AppTestHost.Check(env.Input.Submitted.Count == 0);
        AppTestHost.Check(!env.Vm.ClaimsAgentAccepted);
    }

    static void ImageCacheNotLeased()
    {
        using var env = new PasteEnv(image: true, cacheBytes: 1024);
        env.Vm.BeginPaste(env.Confirm());
        var continued = env.Vm.ContinueToAttachment();
        AppTestHost.Check(continued.Succeeded);
        AppTestHost.Check(env.Vm.LastCacheObjectId is Guid);
        AppTestHost.Check(!env.Cache.IsHeld(env.Vm.LastCacheObjectId!.Value));
        AppTestHost.Check(!env.Vm.ClaimsAgentAccepted);
        AppTestHost.Check(!env.Vm.PreviewImpliesReceipt);
    }
}

internal sealed class PasteEnv : IDisposable
{
    readonly string _root;

    public PasteEnv(string? text = null, bool files = false, bool image = false, long cacheBytes = 1024)
    {
        _root = Path.Combine(Path.GetTempPath(), "herddesk-hd031-paste-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        Pane = new PaneKey(AppTestHost.SessionOf(AppTestHost.DeviceA), "ws", "p1");
        Input = new RecordingPasteAppInput();
        ClipboardSnapshot snapshot;
        if (files)
        {
            snapshot = new ClipboardSnapshot(
                Guid.NewGuid(),
                DateTimeOffset.UnixEpoch,
                false,
                true,
                false,
                ["std"],
                ReadOnlyMemory<byte>.Empty,
                [new ClipboardFileHandle("f1", "note.txt", 4)],
                null,
                4);
        }
        else if (image)
        {
            snapshot = new ClipboardSnapshot(
                Guid.NewGuid(),
                DateTimeOffset.UnixEpoch,
                false,
                false,
                true,
                ["std"],
                ReadOnlyMemory<byte>.Empty,
                [],
                new ClipboardImageHandle("img", "image/png", 12, "0123456789ab"u8.ToArray()),
                12);
        }
        else
        {
            snapshot = new ClipboardSnapshot(
                Guid.NewGuid(),
                DateTimeOffset.UnixEpoch,
                true,
                false,
                false,
                ["std"],
                Encoding.UTF8.GetBytes(text ?? "hello"),
                [],
                null,
                (ulong)(text ?? "hello").Length);
        }

        Reader = new CountingAppClipboardReader(snapshot);
        var paste = new PasteCoordinator(Reader, Input);
        Attachments = new AttachmentCoordinator(
            AttachmentCapabilityCatalog.Empty,
            new TransferCoordinator(new ConnectionAdmissionPolicy(ResourceBudgets.ForTests(8))),
            new AttachmentLeaseRegistry(),
            Input);
        var paths = AppDataPaths.FromRoot(_root);
        Directory.CreateDirectory(paths.CacheDirectory);
        Cache = new AttachmentCache(
            paths,
            TimeProvider.System,
            new AttachmentCacheOptions { MaxBytes = cacheBytes, DefaultTtl = TimeSpan.FromHours(1) });
        Vm = new PastePreviewViewModel(paste, Attachments, Cache);
    }

    public PaneKey Pane { get; }
    public RecordingPasteAppInput Input { get; }
    public CountingAppClipboardReader Reader { get; }
    public AttachmentCoordinator Attachments { get; }
    public AttachmentCache Cache { get; }
    public PastePreviewViewModel Vm { get; }

    public PasteConfirmRequest Confirm() =>
        new(Pane, new ConnectionEpoch(1), "lease-1", 1, true, TerminalAccess.Controlling,
            new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }
}

internal sealed class CountingAppClipboardReader : IClipboardSnapshotReader
{
    public int Invocations { get; private set; }
    readonly ClipboardSnapshot _next;

    public CountingAppClipboardReader(ClipboardSnapshot next) => _next = next;

    public ClipboardSnapshot Read()
    {
        Invocations++;
        return _next;
    }
}

internal sealed class RecordingPasteAppInput : IAttachmentInputSink
{
    public List<RendererInput> Submitted { get; } = [];

    public ValueTask<AttachmentInputReceipt> SubmitAsync(
        RendererInput input,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        Submitted.Add(input);
        return ValueTask.FromResult(new AttachmentInputReceipt(true, ClipboardCodes.Allowed, input.Bytes));
    }
}
