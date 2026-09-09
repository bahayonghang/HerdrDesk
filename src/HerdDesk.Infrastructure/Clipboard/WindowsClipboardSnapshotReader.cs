using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.Clipboard;

public sealed class WindowsClipboardSnapshotReader : IClipboardSnapshotReader
{
    readonly Func<ClipboardSnapshot> _capture;

    public WindowsClipboardSnapshotReader(Func<ClipboardSnapshot> capture)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
    }

    public static WindowsClipboardSnapshotReader CreateWindows() =>
        new(WindowsClipboardCapture.ReadOnce);

    public bool RegistersWatcher => false;

    public ClipboardSnapshot Read() => _capture();
}

internal static class WindowsClipboardCapture
{
    const uint CfText = 1;
    const uint CfBitmap = 2;
    const uint CfDib = 8;
    const uint CfUnicodeText = 13;
    const uint CfHdrop = 15;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool OpenClipboard(nint hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll")]
    static extern uint EnumClipboardFormats(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    static extern nint GetClipboardData(uint format);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClipboardFormatNameW(uint format, StringBuilder lpszFormatName, int cchMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint RegisterClipboardFormatW(string lpszFormat);

    [DllImport("kernel32.dll")]
    static extern nint GlobalLock(nint hMem);

    [DllImport("kernel32.dll")]
    static extern bool GlobalUnlock(nint hMem);

    [DllImport("kernel32.dll")]
    static extern nuint GlobalSize(nint hMem);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern uint DragQueryFileW(nint hDrop, uint iFile, StringBuilder? lpszFile, uint cch);

    public static ClipboardSnapshot ReadOnce()
    {
        var now = DateTimeOffset.UtcNow;
        if (!OperatingSystem.IsWindows())
        {
            return new ClipboardSnapshot(
                Guid.NewGuid(),
                now,
                false,
                false,
                false,
                [],
                ReadOnlyMemory<byte>.Empty,
                [],
                null,
                0,
                ClipboardCodes.Unsupported);
        }

        return ReadWindows(now);
    }

    [SupportedOSPlatform("windows")]
    static ClipboardSnapshot ReadWindows(DateTimeOffset now)
    {
        if (!OpenClipboard(0))
        {
            var error = Marshal.GetLastWin32Error();
            var code = error is 5 or 170 ? ClipboardCodes.AccessDenied : ClipboardCodes.Busy;
            return new ClipboardSnapshot(
                Guid.NewGuid(),
                now,
                false,
                false,
                false,
                [],
                ReadOnlyMemory<byte>.Empty,
                [],
                null,
                0,
                code);
        }

        try
        {
            var formats = ListFormats();
            var hasText = IsClipboardFormatAvailable(CfUnicodeText) || IsClipboardFormatAvailable(CfText);
            var hasFiles = IsClipboardFormatAvailable(CfHdrop);
            var png = RegisterClipboardFormatW("PNG");
            var hasImage = (png != 0 && IsClipboardFormatAvailable(png))
                || IsClipboardFormatAvailable(CfDib)
                || IsClipboardFormatAvailable(CfBitmap);
            var text = hasText ? ReadText() : ReadOnlyMemory<byte>.Empty;
            var files = hasFiles ? ReadFiles() : Array.Empty<ClipboardFileHandle>();
            var image = hasImage ? ReadImage(png) : null;
            ulong estimated = (ulong)text.Length;
            foreach (var file in files)
                estimated += file.SizeBytes ?? 0;
            if (image is not null)
                estimated += image.SizeBytes;
            string? error = null;
            if (text.Length > 64 * 1024)
                error = ClipboardCodes.Oversize;
            return new ClipboardSnapshot(
                Guid.NewGuid(),
                now,
                hasText,
                hasFiles,
                hasImage,
                formats,
                text,
                files,
                image,
                estimated,
                error);
        }
        finally
        {
            _ = CloseClipboard();
        }
    }

    [SupportedOSPlatform("windows")]
    static IReadOnlyList<string> ListFormats()
    {
        var names = new List<string>();
        uint format = 0;
        while ((format = EnumClipboardFormats(format)) != 0 && names.Count < 32)
        {
            var builder = new StringBuilder(128);
            var written = GetClipboardFormatNameW(format, builder, builder.Capacity);
            names.Add(written > 0 ? "fmt" : "std");
        }

        return names;
    }

    [SupportedOSPlatform("windows")]
    static ReadOnlyMemory<byte> ReadText()
    {
        var unicode = IsClipboardFormatAvailable(CfUnicodeText);
        var handle = GetClipboardData(unicode ? CfUnicodeText : CfText);
        if (handle == 0)
            return ReadOnlyMemory<byte>.Empty;
        var locked = GlobalLock(handle);
        if (locked == 0)
            return ReadOnlyMemory<byte>.Empty;
        try
        {
            var text = unicode ? Marshal.PtrToStringUni(locked) : Marshal.PtrToStringAnsi(locked);
            return text is null ? ReadOnlyMemory<byte>.Empty : Encoding.UTF8.GetBytes(text);
        }
        finally
        {
            _ = GlobalUnlock(handle);
        }
    }

    [SupportedOSPlatform("windows")]
    static IReadOnlyList<ClipboardFileHandle> ReadFiles()
    {
        var handle = GetClipboardData(CfHdrop);
        if (handle == 0)
            return [];
        var locked = GlobalLock(handle);
        var drop = locked == 0 ? handle : locked;
        try
        {
            var count = DragQueryFileW(drop, 0xFFFFFFFF, null, 0);
            var files = new List<ClipboardFileHandle>((int)Math.Min(count, 64));
            for (uint i = 0; i < count && files.Count < 64; i++)
            {
                var chars = DragQueryFileW(drop, i, null, 0);
                var builder = new StringBuilder((int)chars + 1);
                _ = DragQueryFileW(drop, i, builder, (uint)builder.Capacity);
                var path = builder.ToString();
                var name = Path.GetFileName(path);
                if (string.IsNullOrEmpty(name))
                    name = "file";
                files.Add(new ClipboardFileHandle("file-" + i.ToString("D"), name, null, path));
            }

            return files;
        }
        finally
        {
            if (locked != 0)
                _ = GlobalUnlock(handle);
        }
    }

    [SupportedOSPlatform("windows")]
    static ClipboardImageHandle? ReadImage(uint pngFormat)
    {
        nint handle = 0;
        var type = "image/png";
        if (pngFormat != 0 && IsClipboardFormatAvailable(pngFormat))
            handle = GetClipboardData(pngFormat);
        else if (IsClipboardFormatAvailable(CfDib))
        {
            handle = GetClipboardData(CfDib);
            type = "image/bmp";
        }

        if (handle == 0)
            return new ClipboardImageHandle("image", type, 0, ReadOnlyMemory<byte>.Empty);
        var locked = GlobalLock(handle);
        if (locked == 0)
            return new ClipboardImageHandle("image", type, 0, ReadOnlyMemory<byte>.Empty);
        try
        {
            var size = (int)Math.Min(GlobalSize(handle), (nuint)8 * 1024 * 1024);
            if (size <= 0)
                return new ClipboardImageHandle("image", type, 0, ReadOnlyMemory<byte>.Empty);
            var bytes = new byte[size];
            Marshal.Copy(locked, bytes, 0, size);
            return new ClipboardImageHandle("image", type, (ulong)size, bytes);
        }
        finally
        {
            _ = GlobalUnlock(handle);
        }
    }
}
