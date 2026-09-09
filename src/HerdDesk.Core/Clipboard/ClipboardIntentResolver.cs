using System.Text;
using HerdDesk.Contracts;

namespace HerdDesk.Core;

public static class ClipboardIntentResolver
{
    public const int PreviewMaxChars = 80;

    public static ClipboardIntent Resolve(ClipboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.ErrorCode is ClipboardCodes.AccessDenied or ClipboardCodes.Busy)
        {
            return new ClipboardIntent(
                ClipboardIntentKind.AccessDenied,
                [],
                0,
                0,
                0,
                "",
                false,
                snapshot.ErrorCode);
        }

        if (snapshot.ErrorCode is ClipboardCodes.Unsupported)
        {
            return new ClipboardIntent(
                ClipboardIntentKind.Unsupported,
                [],
                0,
                0,
                0,
                "",
                false,
                ClipboardCodes.Unsupported);
        }

        var options = new List<ClipboardIntentKind>(3);
        if (snapshot.HasText)
            options.Add(ClipboardIntentKind.Text);
        if (snapshot.HasFileList)
            options.Add(ClipboardIntentKind.FileList);
        if (snapshot.HasImage)
            options.Add(ClipboardIntentKind.Image);

        if (options.Count == 0)
        {
            var empty = snapshot.FormatNames.Count == 0 && snapshot.EstimatedBytes == 0
                && snapshot.TextBytes.Length == 0;
            return new ClipboardIntent(
                empty ? ClipboardIntentKind.Empty : ClipboardIntentKind.Unsupported,
                [],
                0,
                0,
                0,
                "",
                false,
                empty ? ClipboardCodes.Empty : ClipboardCodes.Unsupported);
        }

        if (options.Count > 1)
        {
            var (lines, chars, bytes, preview, multiline) = MeasureText(snapshot.TextBytes);
            return new ClipboardIntent(
                ClipboardIntentKind.Mixed,
                options,
                lines,
                chars,
                bytes,
                preview,
                multiline,
                ClipboardCodes.MixedIntentRequired);
        }

        if (snapshot.HasText)
        {
            var (lines, chars, bytes, preview, multiline) = MeasureText(snapshot.TextBytes);
            var code = bytes > InputPolicy.MaxInputBytes
                ? ClipboardCodes.Oversize
                : bytes == 0
                    ? ClipboardCodes.Empty
                    : ClipboardCodes.Allowed;
            var kind = bytes == 0 ? ClipboardIntentKind.Empty : ClipboardIntentKind.Text;
            return new ClipboardIntent(kind, [], lines, chars, bytes, preview, multiline, code);
        }

        if (snapshot.HasFileList)
        {
            return new ClipboardIntent(
                ClipboardIntentKind.FileList,
                [],
                0,
                snapshot.Files.Count,
                0,
                "",
                false,
                ClipboardCodes.ContinueToAttachment);
        }

        return new ClipboardIntent(
            ClipboardIntentKind.Image,
            [],
            0,
            0,
            snapshot.Image is { } image ? (int)Math.Min(image.SizeBytes, int.MaxValue) : 0,
            "",
            false,
            ClipboardCodes.ContinueToAttachment);
    }

    public static (int Lines, int Characters, int Utf8Bytes, string Preview, bool Multiline) MeasureText(
        ReadOnlyMemory<byte> utf8)
    {
        var span = utf8.Span;
        var multiline = ContainsNewline(span);
        var lines = CountLines(span);
        var text = Encoding.UTF8.GetString(span);
        var characters = 0;
        foreach (var _ in text.EnumerateRunes())
            characters++;
        return (lines, characters, span.Length, TruncatePreview(text), multiline);
    }

    public static bool ContainsNewline(ReadOnlySpan<byte> bytes)
    {
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] is 0x0A or 0x0D)
                return true;
        }

        return false;
    }

    public static int CountLines(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
            return 0;
        var lines = 1;
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == 0x0D)
            {
                lines++;
                if (i + 1 < bytes.Length && bytes[i + 1] == 0x0A)
                    i++;
            }
            else if (bytes[i] == 0x0A)
            {
                lines++;
            }
        }

        return lines;
    }

    public static string TruncatePreview(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var builder = new StringBuilder();
        var count = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (count >= PreviewMaxChars)
            {
                builder.Append('…');
                break;
            }

            var value = rune.Value;
            if (value < 0x20 && value != '\t')
                builder.Append('\uFFFD');
            else
                builder.Append(rune.ToString());
            count++;
        }

        return builder.ToString();
    }
}
