using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class ClipboardIntentResolverTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("text format is text intent", TextIntent),
        ("file list format is file intent", FileIntent),
        ("image format is image intent", ImageIntent),
        ("mixed formats require a choice", MixedIntent),
        ("empty snapshot is empty", EmptyIntent),
        ("unknown formats are unsupported", UnsupportedIntent),
        ("access denied is a distinct intent", AccessDeniedIntent)
    ];

    static void TextIntent()
    {
        var snapshot = ClipboardHarness.Snapshot(text: true, body: "hello");
        var intent = ClipboardIntentResolver.Resolve(snapshot);
        ClipboardHarness.Check(intent.Kind == ClipboardIntentKind.Text);
        ClipboardHarness.Check(intent.LineCount == 1);
        ClipboardHarness.Check(intent.Utf8ByteCount == 5);
        ClipboardHarness.Check(intent.CharacterCount == 5);
        ClipboardHarness.Check(!intent.RequiresMultilineConfirm);
        ClipboardHarness.Check(intent.Preview == "hello");
        ClipboardHarness.Check(intent.MixedOptions.Count == 0);
    }

    static void FileIntent()
    {
        var snapshot = ClipboardHarness.Snapshot(
            files: true,
            fileHandles: [new ClipboardFileHandle("f1", "note.txt", 4)]);
        var intent = ClipboardIntentResolver.Resolve(snapshot);
        ClipboardHarness.Check(intent.Kind == ClipboardIntentKind.FileList);
        ClipboardHarness.Check(intent.Code == ClipboardCodes.ContinueToAttachment);
        ClipboardHarness.Check(!intent.RequiresMultilineConfirm);
    }

    static void ImageIntent()
    {
        var snapshot = ClipboardHarness.Snapshot(
            image: true,
            imageHandle: new ClipboardImageHandle("img", "image/png", 12, "PNG"u8.ToArray()));
        var intent = ClipboardIntentResolver.Resolve(snapshot);
        ClipboardHarness.Check(intent.Kind == ClipboardIntentKind.Image);
        ClipboardHarness.Check(intent.Code == ClipboardCodes.ContinueToAttachment);
    }

    static void MixedIntent()
    {
        var snapshot = ClipboardHarness.Snapshot(text: true, files: true, body: "a\nb");
        var intent = ClipboardIntentResolver.Resolve(snapshot);
        ClipboardHarness.Check(intent.Kind == ClipboardIntentKind.Mixed);
        ClipboardHarness.Check(intent.MixedOptions.Contains(ClipboardIntentKind.Text));
        ClipboardHarness.Check(intent.MixedOptions.Contains(ClipboardIntentKind.FileList));
        ClipboardHarness.Check(!intent.MixedOptions.Contains(ClipboardIntentKind.Image));
        ClipboardHarness.Check(intent.Code == ClipboardCodes.MixedIntentRequired);
        ClipboardHarness.Check(intent.RequiresMultilineConfirm);
    }

    static void EmptyIntent()
    {
        var snapshot = ClipboardHarness.Snapshot();
        var intent = ClipboardIntentResolver.Resolve(snapshot);
        ClipboardHarness.Check(intent.Kind == ClipboardIntentKind.Empty);
        ClipboardHarness.Check(intent.Code == ClipboardCodes.Empty);
        ClipboardHarness.Check(intent.LineCount == 0);
    }

    static void UnsupportedIntent()
    {
        var snapshot = ClipboardHarness.Snapshot(formats: ["std"]);
        var intent = ClipboardIntentResolver.Resolve(snapshot);
        ClipboardHarness.Check(intent.Kind == ClipboardIntentKind.Unsupported);
        ClipboardHarness.Check(intent.Code == ClipboardCodes.Unsupported);
    }

    static void AccessDeniedIntent()
    {
        var snapshot = ClipboardHarness.Snapshot(error: ClipboardCodes.AccessDenied);
        var intent = ClipboardIntentResolver.Resolve(snapshot);
        ClipboardHarness.Check(intent.Kind == ClipboardIntentKind.AccessDenied);
        ClipboardHarness.Check(intent.Code == ClipboardCodes.AccessDenied);
    }
}
