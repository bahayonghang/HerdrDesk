using System.Security.Cryptography;
using System.Text;

namespace HerdDesk.Infrastructure.Diagnostics;

public sealed class DiagnosticAliasProjector
{
    private readonly byte[] _salt;

    public DiagnosticAliasProjector(byte[] salt)
    {
        ArgumentNullException.ThrowIfNull(salt);
        if (salt.Length < 16)
            throw new ArgumentOutOfRangeException(nameof(salt));
        _salt = salt.ToArray();
    }

    public static DiagnosticAliasProjector LoadOrCreate(string saltPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saltPath);
        var directory = Path.GetDirectoryName(saltPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        if (File.Exists(saltPath))
        {
            var existing = File.ReadAllBytes(saltPath);
            if (existing.Length >= 16)
                return new DiagnosticAliasProjector(existing);
        }
        var salt = RandomNumberGenerator.GetBytes(32);
        var temp = saltPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temp, salt);
        File.Move(temp, saltPath, overwrite: true);
        return new DiagnosticAliasProjector(salt);
    }

    public string Alias(string kind, string raw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(raw);
        Span<byte> hash = stackalloc byte[32];
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hasher.AppendData(_salt);
        hasher.AppendData(Encoding.UTF8.GetBytes(kind));
        hasher.AppendData([(byte)0]);
        hasher.AppendData(Encoding.UTF8.GetBytes(raw));
        hasher.GetHashAndReset(hash);
        return kind + "-" + Convert.ToHexString(hash[..6]).ToLowerInvariant();
    }
}
