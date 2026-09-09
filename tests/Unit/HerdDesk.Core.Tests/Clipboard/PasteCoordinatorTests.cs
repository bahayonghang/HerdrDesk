using System.Text;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class PasteCoordinatorTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("multiline paste requires confirm with default cancel", MultilineConfirm),
        ("cancel sends zero bytes", CancelSendsNothing),
        ("confirm sends original bytes with no extra enter", ConfirmNoExtraEnter),
        ("trailing newline is not doubled", TrailingNewlineNotDoubled),
        ("stale epoch does not send to the wrong pane", StaleEpoch),
        ("stale does not resurrect the original target", StaleDoesNotResurrect),
        ("lease revoke does not send", LeaseRevoke),
        ("mixed formats do not auto-pick", MixedRequiresChoice),
        ("observe paste does not read the clipboard", ObserveDoesNotRead),
        ("diagnostics omit clipboard content", DiagnosticsOmitContent)
    ];

    static void MultilineConfirm()
    {
        var reader = new CountingClipboardReader(ClipboardHarness.Snapshot(text: true, body: "hello\nworld"));
        var input = new RecordingPasteInput();
        var coordinator = new PasteCoordinator(reader, input);
        var began = coordinator.BeginPaste(ClipboardHarness.Confirm());
        ClipboardHarness.Check(began.Succeeded);
        ClipboardHarness.Check(coordinator.State == PasteUiState.NeedsMultilineConfirm);
        ClipboardHarness.Check(coordinator.DefaultActionIsCancel);
        ClipboardHarness.Check(began.Code == ClipboardCodes.MultilineConfirmRequired);
        ClipboardHarness.Check(reader.Invocations == 1);
        ClipboardHarness.Check(input.Submitted.Count == 0);
        ClipboardHarness.Check(coordinator.BytesSent == 0);
    }

    static void CancelSendsNothing()
    {
        var reader = new CountingClipboardReader(ClipboardHarness.Snapshot(text: true, body: "hello\nworld"));
        var input = new RecordingPasteInput();
        var coordinator = new PasteCoordinator(reader, input);
        coordinator.BeginPaste(ClipboardHarness.Confirm());
        var cancelled = coordinator.Cancel();
        ClipboardHarness.Check(cancelled.Succeeded);
        ClipboardHarness.Check(cancelled.SentBytes.Length == 0);
        ClipboardHarness.Check(coordinator.BytesSent == 0);
        ClipboardHarness.Check(input.Submitted.Count == 0);
        ClipboardHarness.Check(coordinator.State == PasteUiState.Cancelled);
        ClipboardHarness.Check(!coordinator.ClaimsAgentAccepted);
    }

    static void ConfirmNoExtraEnter()
    {
        var original = "hello\nworld";
        var expected = ClipboardHarness.Utf8(original);
        var reader = new CountingClipboardReader(ClipboardHarness.Snapshot(text: true, body: original));
        var input = new RecordingPasteInput();
        var coordinator = new PasteCoordinator(reader, input);
        coordinator.BeginPaste(ClipboardHarness.Confirm());
        var sent = coordinator.ConfirmAsync(ClipboardHarness.Live()).AsTask().GetAwaiter().GetResult();
        ClipboardHarness.Check(sent.Succeeded);
        ClipboardHarness.Check(input.Submitted.Count == 1);
        var bytes = input.Submitted[0].Bytes.ToArray();
        ClipboardHarness.Check(bytes.AsSpan().SequenceEqual(expected));
        ClipboardHarness.Check(!bytes.AsSpan().SequenceEqual(ClipboardHarness.Utf8(original + "\n")));
        ClipboardHarness.Check(!bytes.AsSpan().SequenceEqual(ClipboardHarness.Utf8(original + "\r")));
        ClipboardHarness.Check(!bytes.AsSpan().SequenceEqual(ClipboardHarness.Utf8(original + "\r\n")));
        ClipboardHarness.Check(!ClipboardHarness.HasExtraEnter(bytes, expected));
        ClipboardHarness.Check(input.Submitted[0].Origin == InputOrigin.ExplicitPaste);
        var decision = InputPolicy.Evaluate(
            new InputContext(ClipboardHarness.Pane(), new ConnectionEpoch(1), TerminalAccess.Controlling, true),
            input.Submitted[0]);
        ClipboardHarness.Check(decision.Allowed);
        ClipboardHarness.Check(reader.Invocations == 1);
        ClipboardHarness.Check(!coordinator.ClaimsAgentAccepted);
    }

    static void TrailingNewlineNotDoubled()
    {
        var original = "hello\n";
        var expected = ClipboardHarness.Utf8(original);
        var reader = new CountingClipboardReader(ClipboardHarness.Snapshot(text: true, body: original));
        var input = new RecordingPasteInput();
        var coordinator = new PasteCoordinator(reader, input);
        coordinator.BeginPaste(ClipboardHarness.Confirm());
        var sent = coordinator.ConfirmAsync(ClipboardHarness.Live()).AsTask().GetAwaiter().GetResult();
        ClipboardHarness.Check(sent.Succeeded);
        var bytes = input.Submitted[0].Bytes.ToArray();
        ClipboardHarness.Check(bytes.AsSpan().SequenceEqual(expected));
        ClipboardHarness.Check(!ClipboardHarness.HasExtraEnter(bytes, expected));
        ClipboardHarness.Check(bytes.Length == expected.Length);
    }

    static void StaleEpoch()
    {
        var reader = new CountingClipboardReader(ClipboardHarness.Snapshot(text: true, body: "hello\nworld"));
        var input = new RecordingPasteInput();
        var coordinator = new PasteCoordinator(reader, input);
        coordinator.BeginPaste(ClipboardHarness.Confirm(epoch: 1));
        var stale = coordinator.NoteLiveTarget(ClipboardHarness.Live(epoch: 2));
        ClipboardHarness.Check(!stale.Succeeded);
        ClipboardHarness.Check(stale.Code == ClipboardCodes.StaleEpoch);
        ClipboardHarness.Check(coordinator.State == PasteUiState.Stale);
        var sent = coordinator.ConfirmAsync(ClipboardHarness.Live(epoch: 2)).AsTask().GetAwaiter().GetResult();
        ClipboardHarness.Check(!sent.Succeeded);
        ClipboardHarness.Check(input.Submitted.Count == 0);
        ClipboardHarness.Check(sent.SentBytes.Length == 0);
        var switched = coordinator.ConfirmAsync(ClipboardHarness.Live(pane: "other", epoch: 1))
            .AsTask().GetAwaiter().GetResult();
        ClipboardHarness.Check(!switched.Succeeded);
        ClipboardHarness.Check(input.Submitted.Count == 0);
    }

    static void StaleDoesNotResurrect()
    {
        var reader = new CountingClipboardReader(ClipboardHarness.Snapshot(text: true, body: "hello\nworld"));
        var input = new RecordingPasteInput();
        var coordinator = new PasteCoordinator(reader, input);
        coordinator.BeginPaste(ClipboardHarness.Confirm(pane: "p1", epoch: 1));
        var switched = coordinator.NoteLiveTarget(ClipboardHarness.Live(pane: "p2", epoch: 1));
        ClipboardHarness.Check(!switched.Succeeded);
        ClipboardHarness.Check(coordinator.State == PasteUiState.Stale);
        var original = coordinator.ConfirmAsync(ClipboardHarness.Live(pane: "p1", epoch: 1))
            .AsTask().GetAwaiter().GetResult();
        ClipboardHarness.Check(!original.Succeeded);
        ClipboardHarness.Check(original.SentBytes.Length == 0);
        ClipboardHarness.Check(input.Submitted.Count == 0);
        var newer = coordinator.ConfirmAsync(ClipboardHarness.Live(pane: "p2", epoch: 1))
            .AsTask().GetAwaiter().GetResult();
        ClipboardHarness.Check(!newer.Succeeded);
        ClipboardHarness.Check(input.Submitted.Count == 0);
    }

    static void LeaseRevoke()
    {
        var reader = new CountingClipboardReader(ClipboardHarness.Snapshot(text: true, body: "hello\nworld"));
        var input = new RecordingPasteInput();
        var coordinator = new PasteCoordinator(reader, input);
        coordinator.BeginPaste(ClipboardHarness.Confirm());
        var revoked = coordinator.NoteLiveTarget(ClipboardHarness.Live(
            verified: false, access: TerminalAccess.Observing));
        ClipboardHarness.Check(!revoked.Succeeded);
        ClipboardHarness.Check(revoked.Code == ClipboardCodes.ControlRevoked);
        ClipboardHarness.Check(coordinator.State == PasteUiState.Stale);
        var original = coordinator.ConfirmAsync(ClipboardHarness.Live()).AsTask().GetAwaiter().GetResult();
        ClipboardHarness.Check(!original.Succeeded);
        ClipboardHarness.Check(original.SentBytes.Length == 0);
        ClipboardHarness.Check(input.Submitted.Count == 0);
        var stillRevoked = coordinator.ConfirmAsync(ClipboardHarness.Live(
            verified: false, access: TerminalAccess.Observing)).AsTask().GetAwaiter().GetResult();
        ClipboardHarness.Check(!stillRevoked.Succeeded);
        ClipboardHarness.Check(input.Submitted.Count == 0);
    }

    static void MixedRequiresChoice()
    {
        var reader = new CountingClipboardReader(
            ClipboardHarness.Snapshot(text: true, files: true, body: "hello"));
        var input = new RecordingPasteInput();
        var coordinator = new PasteCoordinator(reader, input);
        var began = coordinator.BeginPaste(ClipboardHarness.Confirm());
        ClipboardHarness.Check(began.Code == ClipboardCodes.MixedIntentRequired);
        ClipboardHarness.Check(coordinator.State == PasteUiState.NeedsIntentChoice);
        var auto = coordinator.ConfirmAsync(ClipboardHarness.Live()).AsTask().GetAwaiter().GetResult();
        ClipboardHarness.Check(!auto.Succeeded);
        ClipboardHarness.Check(auto.Code == ClipboardCodes.MixedIntentRequired);
        ClipboardHarness.Check(input.Submitted.Count == 0);
        var chosen = coordinator.ChooseIntent(ClipboardIntentKind.Text);
        ClipboardHarness.Check(chosen.Succeeded);
        ClipboardHarness.Check(coordinator.Intent!.Kind == ClipboardIntentKind.Text);
    }

    static void ObserveDoesNotRead()
    {
        var reader = new CountingClipboardReader(ClipboardHarness.Snapshot(text: true, body: "secret"));
        var input = new RecordingPasteInput();
        var diagnostics = new RecordingPasteDiagnostics();
        var coordinator = new PasteCoordinator(reader, input, diagnostics);
        var denied = coordinator.BeginPaste(ClipboardHarness.Confirm(
            verified: false, access: TerminalAccess.Observing));
        ClipboardHarness.Check(!denied.Succeeded);
        ClipboardHarness.Check(denied.Code == ClipboardCodes.ControlNotVerified);
        ClipboardHarness.Check(reader.Invocations == 0);
        ClipboardHarness.Check(input.Submitted.Count == 0);
        foreach (var evt in diagnostics.Events)
        {
            var component = evt.Component ?? "";
            var operation = evt.Operation ?? "";
            ClipboardHarness.Check(component is "clipboard" or "cache");
            ClipboardHarness.Check(!component.Contains("secret", StringComparison.Ordinal));
            ClipboardHarness.Check(!operation.Contains("secret", StringComparison.Ordinal));
        }
        ClipboardHarness.Check(!coordinator.WatcherEnabled);
    }

    static void DiagnosticsOmitContent()
    {
        const string canary = "hd031-canary-paste-body";
        var reader = new CountingClipboardReader(ClipboardHarness.Snapshot(text: true, body: canary + "\nline"));
        var input = new RecordingPasteInput();
        var diagnostics = new RecordingPasteDiagnostics();
        var coordinator = new PasteCoordinator(reader, input, diagnostics);
        coordinator.BeginPaste(ClipboardHarness.Confirm());
        var sent = coordinator.ConfirmAsync(ClipboardHarness.Live()).AsTask().GetAwaiter().GetResult();
        ClipboardHarness.Check(sent.Succeeded);
        ClipboardHarness.Check(diagnostics.Events.Count > 0);
        foreach (var evt in diagnostics.Events)
        {
            var dumped = evt.ToString();
            ClipboardHarness.Check(!dumped.Contains(canary, StringComparison.Ordinal));
            ClipboardHarness.Check(evt.Component == "clipboard");
            ClipboardHarness.Check(evt.DeviceAlias is null);
            ClipboardHarness.Check(evt.SessionAlias is null);
            ClipboardHarness.Check(evt.ErrorCode is null
                || (!evt.ErrorCode.Contains(canary, StringComparison.Ordinal)
                    && evt.ErrorCode.Length <= 64));
        }
    }
}
