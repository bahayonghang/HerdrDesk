using System.Text;
using System.Text.Json;

namespace HerdDesk.Infrastructure.Quality;

public static class QualityJson
{
    static readonly UTF8Encoding Utf8NoBom = new(false);

    public static string ProbeResultsDirectory(string repoRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        return Path.Combine(repoRoot, "probe-results");
    }

    public static void WriteFile(string path, Action<Utf8JsonWriter> write)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(write);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            write(writer);
        File.WriteAllBytes(path, buffer.ToArray());
    }

    public static string WriteUtf8(Action<Utf8JsonWriter> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            write(writer);
        return Utf8NoBom.GetString(buffer.ToArray());
    }
}
