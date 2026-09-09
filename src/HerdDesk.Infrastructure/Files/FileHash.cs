using System.Security.Cryptography;

namespace HerdDesk.Infrastructure.Files;

internal static class FileHash
{
    public static string Hex(ReadOnlySpan<byte> data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string Empty { get; } = Hex([]);

    public static IncrementalHash Create() => IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    public static string Finish(IncrementalHash hash)
    {
        Span<byte> digest = stackalloc byte[32];
        if (!hash.TryGetHashAndReset(digest, out var written) || written != 32)
            throw new InvalidOperationException("hash_mismatch");
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}
