using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HerdDesk.Infrastructure.Files;

internal static class WindowsNativeIo
{
    public const uint FileFlagOpenReparsePoint = 0x00200000;
    public const uint FileFlagBackupSemantics = 0x02000000;
    public const uint GenericRead = 0x80000000;
    public const uint GenericWrite = 0x40000000;
    public const uint FileShareRead = 0x1;
    public const uint FileShareWrite = 0x2;
    public const uint FileShareDelete = 0x4;
    public const uint OpenExisting = 3;
    public const uint CreateNew = 1;
    public const uint FileAttributeDirectory = 0x10;
    public const uint FileAttributeReparsePoint = 0x400;
    public const uint MoveFileReplaceExisting = 0x1;
    public const uint ErrorFileExists = 80;
    public const uint ErrorAlreadyExists = 183;
    public const uint ErrorAccessDenied = 5;
    public const uint ErrorPathNotFound = 3;
    public const uint ErrorFileNotFound = 2;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        nint lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        nint hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetFileInformationByHandle(SafeFileHandle hFile, out ByHandleFileInformation lpFileInformation);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool MoveFileExW(string lpExistingFileName, string lpNewFileName, uint dwFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool ReplaceFileW(
        string lpReplacedFileName,
        string lpReplacementFileName,
        string? lpBackupFileName,
        uint dwReplaceFlags,
        nint lpExclude,
        nint lpReserved);

    [StructLayout(LayoutKind.Sequential)]
    public struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public ulong CreationTime;
        public ulong LastAccessTime;
        public ulong LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    public static SafeFileHandle OpenNoFollow(string path, bool write)
    {
        var access = write ? GenericRead | GenericWrite : GenericRead;
        var handle = CreateFileW(
            path,
            access,
            FileShareRead | FileShareWrite | FileShareDelete,
            0,
            OpenExisting,
            FileFlagOpenReparsePoint | FileFlagBackupSemantics,
            0);
        if (handle.IsInvalid)
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
        return handle;
    }

    public static SafeFileHandle CreateNewExclusive(string path)
    {
        var handle = CreateFileW(
            path,
            GenericRead | GenericWrite,
            FileShareRead | FileShareDelete,
            0,
            CreateNew,
            FileFlagOpenReparsePoint,
            0);
        if (handle.IsInvalid)
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
        return handle;
    }

    public static bool TryGetInformation(SafeFileHandle handle, out ByHandleFileInformation info) =>
        GetFileInformationByHandle(handle, out info);

    public static bool MoveNoReplace(string from, string to)
    {
        if (MoveFileExW(from, to, 0))
            return true;
        var error = (uint)Marshal.GetLastWin32Error();
        if (error is ErrorFileExists or ErrorAlreadyExists)
            return false;
        Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
        return false;
    }

    public static bool TryReplace(string replaced, string replacement, string backup) =>
        ReplaceFileW(replaced, replacement, backup, 0, 0, 0);

    public static ulong FileIndex(in ByHandleFileInformation info) =>
        ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow;

    public static ulong Size(in ByHandleFileInformation info) =>
        ((ulong)info.FileSizeHigh << 32) | info.FileSizeLow;

    public static ulong MtimeSeconds(in ByHandleFileInformation info)
    {
        // FILETIME is 100ns since 1601. Unix epoch 116444736000000000.
        if (info.LastWriteTime < 116444736000000000UL)
            return 0;
        return (info.LastWriteTime - 116444736000000000UL) / 10_000_000UL;
    }

    public static bool IsReparse(in ByHandleFileInformation info) =>
        (info.FileAttributes & FileAttributeReparsePoint) != 0;

    public static bool IsDirectory(in ByHandleFileInformation info) =>
        (info.FileAttributes & FileAttributeDirectory) != 0;
}
