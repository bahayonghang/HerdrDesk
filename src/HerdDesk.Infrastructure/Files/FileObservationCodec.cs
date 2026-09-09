using System.Buffers.Binary;
using System.Security.Cryptography;
using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.Files;

internal static class FileObservationCodec
{
    static readonly byte[] Magic = "HD028OBS1"u8.ToArray();

    public static FileObservation Compute(
        IReadOnlyList<byte[]> components,
        bool exists,
        FileEntryKind kind,
        FileIdentity? identity,
        ulong size,
        ulong mtime,
        ulong mtimePrecision)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(Magic);
        sha.AppendData([0]);
        Span<byte> len = stackalloc byte[2];
        foreach (var component in components)
        {
            if (component.Length > ushort.MaxValue)
                throw new FileBridgeProtocolException(FileBridgeCodes.InvalidComponent);
            BinaryPrimitives.WriteUInt16LittleEndian(len, (ushort)component.Length);
            sha.AppendData(len);
            sha.AppendData(component);
        }

        sha.AppendData([0, exists ? (byte)1 : (byte)0, KindByte(kind)]);
        var id = identity?.Raw ?? [];
        Span<byte> idLen = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(idLen, (ushort)Math.Min(id.Length, ushort.MaxValue));
        sha.AppendData(idLen);
        if (id.Length > 0)
            sha.AppendData(id);
        Span<byte> nums = stackalloc byte[24];
        BinaryPrimitives.WriteUInt64LittleEndian(nums, size);
        BinaryPrimitives.WriteUInt64LittleEndian(nums[8..], mtime);
        BinaryPrimitives.WriteUInt64LittleEndian(nums[16..], mtimePrecision);
        sha.AppendData(nums);
        Span<byte> digest = stackalloc byte[32];
        sha.TryGetHashAndReset(digest, out _);
        return new FileObservation(Convert.ToHexString(digest).ToLowerInvariant());
    }

    static byte KindByte(FileEntryKind kind) => kind switch
    {
        FileEntryKind.File => 1,
        FileEntryKind.Directory => 2,
        FileEntryKind.Symlink => 3,
        FileEntryKind.Other => 4,
        _ => 0
    };

    public static FileIdentity WindowsIdentity(uint volume, ulong index)
    {
        var raw = new byte[1 + 4 + 8];
        raw[0] = (byte)'W';
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(1), volume);
        BinaryPrimitives.WriteUInt64LittleEndian(raw.AsSpan(5), index);
        return new FileIdentity(raw);
    }

    public static FileIdentity UnixIdentity(ulong dev, ulong ino)
    {
        var raw = new byte[1 + 8 + 8];
        raw[0] = (byte)'U';
        BinaryPrimitives.WriteUInt64LittleEndian(raw.AsSpan(1), dev);
        BinaryPrimitives.WriteUInt64LittleEndian(raw.AsSpan(9), ino);
        return new FileIdentity(raw);
    }

    public static FileIdentity PortableIdentity(string fullPath, ulong size, ulong mtime)
    {
        var path = System.Text.Encoding.UTF8.GetBytes(fullPath);
        var raw = new byte[1 + 8 + 8 + path.Length];
        raw[0] = (byte)'P';
        BinaryPrimitives.WriteUInt64LittleEndian(raw.AsSpan(1), size);
        BinaryPrimitives.WriteUInt64LittleEndian(raw.AsSpan(9), mtime);
        path.CopyTo(raw, 17);
        if (raw.Length > FileBridgeLimits.MaxIdentity)
            return new FileIdentity(raw[..FileBridgeLimits.MaxIdentity]);
        return new FileIdentity(raw);
    }
}
