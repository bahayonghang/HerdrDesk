using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HerdDesk.Infrastructure.Files;

internal static class UnixNativeIo
{
    const int AtFdcwd = -100;
    const int AtSymlinkNofollow = 0x100;
    const int AtEmptyPath = 0x1000;
    const uint StatxBasicStats = 0x7ffu;

    [StructLayout(LayoutKind.Sequential)]
    struct StatxTimestamp
    {
        public long TvSec;
        public uint TvNsec;
        public int Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct StatxBuffer
    {
        public uint Mask;
        public uint Blksize;
        public ulong Attributes;
        public uint Nlink;
        public uint Uid;
        public uint Gid;
        public ushort Mode;
        public ushort Spare0;
        public ulong Ino;
        public ulong Size;
        public ulong Blocks;
        public ulong AttributesMask;
        public StatxTimestamp Atime;
        public StatxTimestamp Btime;
        public StatxTimestamp Ctime;
        public StatxTimestamp Mtime;
        public uint RdevMajor;
        public uint RdevMinor;
        public uint DevMajor;
        public uint DevMinor;
        public ulong MntId;
        public uint DioMemAlign;
        public uint DioOffsetAlign;
        public ulong Spare3_0;
        public ulong Spare3_1;
        public ulong Spare3_2;
        public ulong Spare3_3;
        public ulong Spare3_4;
        public ulong Spare3_5;
        public ulong Spare3_6;
        public ulong Spare3_7;
        public ulong Spare3_8;
        public ulong Spare3_9;
        public ulong Spare3_10;
        public ulong Spare3_11;
    }

    [DllImport("libc", SetLastError = true, EntryPoint = "statx")]
    static extern int Statx(int dirfd, string path, int flags, uint mask, out StatxBuffer buf);

    public static bool TryStatNoFollow(string path, out ulong dev, out ulong ino)
    {
        dev = 0;
        ino = 0;
        if (!OperatingSystem.IsLinux())
            return false;
        if (Statx(AtFdcwd, path, AtSymlinkNofollow, StatxBasicStats, out var buf) != 0)
            return false;
        Pack(buf, out dev, out ino);
        return true;
    }

    public static bool TryStatFd(SafeFileHandle handle, out ulong dev, out ulong ino)
    {
        dev = 0;
        ino = 0;
        if (!OperatingSystem.IsLinux() || handle.IsInvalid)
            return false;
        var fd = (int)handle.DangerousGetHandle();
        if (Statx(fd, "", AtEmptyPath, StatxBasicStats, out var buf) != 0)
            return false;
        Pack(buf, out dev, out ino);
        return true;
    }

    static void Pack(in StatxBuffer buf, out ulong dev, out ulong ino)
    {
        dev = ((ulong)buf.DevMajor << 32) | buf.DevMinor;
        ino = buf.Ino;
    }
}
