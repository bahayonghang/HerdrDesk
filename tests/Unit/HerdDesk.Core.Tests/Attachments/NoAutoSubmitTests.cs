using System.Text;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class NoAutoSubmitTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("path insert bytes have no extra enter", PathHasNoEnter),
        ("paste text bytes match source without extra enter", PasteHasNoEnter),
        ("windows path encoding has no submit key", WindowsPathHasNoEnter)
    ];

    static void PathHasNoEnter()
    {
        var key = AttachmentHarness.Key();
        var catalog = AttachmentHarness.Catalog(new AttachmentCapabilityRecord(
            key, AttachmentHarness.Verified(AttachmentOperations.PathInsert)));
        var input = new RecordingAttachmentInput();
        var coordinator = AttachmentHarness.Coordinator(catalog, input: input);
        var expected = AttachmentHarness.Utf8("/tmp/payload.bin");
        coordinator.SetIntent(AttachmentIntent.InsertFilePath);
        coordinator.ChooseSource(AttachmentHarness.Source(display: "payload.bin"), expected);
        coordinator.ConfirmTarget(AttachmentHarness.Confirm(key: key));
        var live = AttachmentHarness.Live(key: key);
        var result = coordinator.InsertAsync(live).AsTask().GetAwaiter().GetResult();
        AttachmentHarness.Check(result.Succeeded);
        AttachmentHarness.Check(input.Submitted.Count == 1);
        var bytes = input.Submitted[0].Bytes.ToArray();
        AttachmentHarness.Check(bytes.AsSpan().SequenceEqual(expected));
        AttachmentHarness.Check(!AttachmentHarness.HasSubmitKey(bytes));
        AttachmentHarness.Check(bytes[^1] is not 0x0A and not 0x0D and not 0x20);
        AttachmentHarness.Check(input.Submitted[0].Origin == InputOrigin.ExplicitPaste);
        var decision = InputPolicy.Evaluate(
            new InputContext(live.Pane, live.Epoch, TerminalAccess.Controlling, true),
            input.Submitted[0]);
        AttachmentHarness.Check(decision.Allowed);
        AttachmentHarness.Check(!coordinator.ClaimsAgentAccepted);
    }

    static void PasteHasNoEnter()
    {
        var key = AttachmentHarness.Key();
        var catalog = AttachmentHarness.Catalog(new AttachmentCapabilityRecord(
            key, AttachmentHarness.Verified(AttachmentOperations.PathInsert)));
        var input = new RecordingAttachmentInput();
        var coordinator = AttachmentHarness.Coordinator(catalog, input: input);
        var expected = AttachmentHarness.Utf8("hello");
        coordinator.SetIntent(AttachmentIntent.PasteText);
        coordinator.ChooseSource(AttachmentHarness.Source(kind: AttachmentSourceKind.ClipboardText), expected);
        coordinator.ConfirmTarget(AttachmentHarness.Confirm(key: key));
        var result = coordinator.InsertAsync(AttachmentHarness.Live(key: key)).AsTask().GetAwaiter().GetResult();
        AttachmentHarness.Check(result.Succeeded);
        var bytes = coordinator.LastInsertedBytes.ToArray();
        AttachmentHarness.Check(bytes.AsSpan().SequenceEqual(expected));
        AttachmentHarness.Check(Encoding.UTF8.GetString(bytes) == "hello");
        AttachmentHarness.Check(!bytes.AsSpan().SequenceEqual("hello\n"u8));
        AttachmentHarness.Check(!bytes.AsSpan().SequenceEqual("hello\r"u8));
        AttachmentHarness.Check(!bytes.AsSpan().SequenceEqual("hello\r\n"u8));
        AttachmentHarness.Check(!AttachmentHarness.HasSubmitKey(bytes));
        AttachmentHarness.Check(input.Submitted[0].Origin == InputOrigin.ExplicitPaste);
    }

    static void WindowsPathHasNoEnter()
    {
        var locator = FileLocator.Root
            .Append(AttachmentHarness.Comp("tmp"))
            .Append(AttachmentHarness.Comp("payload.bin"));
        var posix = AttachmentPathCodec.EncodeLocator(locator, "linux");
        var windows = AttachmentPathCodec.EncodeLocator(locator, "windows-11");
        AttachmentHarness.Check(Encoding.UTF8.GetString(posix) == "/tmp/payload.bin");
        AttachmentHarness.Check(Encoding.UTF8.GetString(windows) == "tmp\\payload.bin");
        AttachmentHarness.Check(!AttachmentHarness.HasSubmitKey(posix));
        AttachmentHarness.Check(!AttachmentHarness.HasSubmitKey(windows));
        AttachmentHarness.Check(posix[^1] is not 0x0A and not 0x0D);
        AttachmentHarness.Check(windows[^1] is not 0x0A and not 0x0D);
    }
}
