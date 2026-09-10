using System.Diagnostics;
using HerdDesk.Infrastructure.Quality;
using OsProcess = System.Diagnostics.Process;

namespace HerdDesk.App.Quality;

public sealed record ColdStartSample(
    int Index,
    string Kind,
    double WallMs,
    int ExitCode,
    bool TimedOut,
    int ProcessId);

public sealed record ColdStartReport(
    string Kind,
    string Executable,
    IReadOnlyList<string> Arguments,
    int SampleCount,
    IReadOnlyList<ColdStartSample> Samples,
    double P50Ms,
    double P95Ms,
    double MaxMs,
    bool FirstInteractivePixel,
    bool ParserConsumedIsNotPresentation,
    bool LaunchedUi);

public static class ColdStartSampler
{
    public const int RequiredSamples = 30;

    public static ColdStartReport Run(
        string executable,
        IReadOnlyList<string> arguments,
        int count,
        TimeSpan timeout,
        string kind,
        Func<int, IReadOnlyList<string>>? argumentsForIndex = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
            throw new ArgumentException("executable_missing", nameof(executable));

        var samples = new List<ColdStartSample>(count);
        var walls = new List<double>(count);
        for (var i = 0; i < count; i++)
        {
            var args = argumentsForIndex is null ? arguments : argumentsForIndex(i);
            var sample = Launch(i, kind, executable, args, timeout);
            samples.Add(sample);
            walls.Add(sample.WallMs);
        }

        var summary = EmpiricalPercentile.Summarize(walls);
        return new ColdStartReport(
            kind,
            executable,
            arguments,
            samples.Count,
            samples,
            summary.P50,
            summary.P95,
            summary.Max,
            false,
            true,
            string.Equals(kind, "ui", StringComparison.Ordinal));
    }

    public static string? FindDotnetHost()
    {
        var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(root))
        {
            var pinned = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(pinned))
                return pinned;
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return null;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        return null;
    }

    public static string? FindAppHost(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var exe = Path.Combine(directory, OperatingSystem.IsWindows() ? "HerdDesk.App.exe" : "HerdDesk.App");
        if (File.Exists(exe))
            return Path.GetFullPath(exe);
        return null;
    }

    public static string? FindAppDll(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var dll = Path.Combine(directory, "HerdDesk.App.dll");
        return File.Exists(dll) ? Path.GetFullPath(dll) : null;
    }

    public static bool TryResolveComposeOnly(
        string directory,
        out string executable,
        out List<string> arguments)
    {
        executable = "";
        arguments = [];
        var host = FindAppHost(directory);
        if (host is not null)
        {
            executable = host;
            arguments = ["--compose-only"];
            return true;
        }

        var dll = FindAppDll(directory);
        var dotnet = FindDotnetHost();
        if (dll is null || dotnet is null)
            return false;
        executable = dotnet;
        arguments = [dll, "--compose-only"];
        return true;
    }

    public static bool TryResolveShellSmoke(
        string directory,
        out string executable,
        out List<string> arguments)
    {
        executable = "";
        arguments = [];
        var host = FindAppHost(directory);
        if (host is null)
            return false;
        executable = host;
        arguments = ["--shell-smoke"];
        return true;
    }

    public static void Write(ColdStartReport report, string path)
    {
        ArgumentNullException.ThrowIfNull(report);
        QualityJson.WriteFile(path, writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("document_kind", "hd033_cold_start_process_samples");
            writer.WriteString("kind", report.Kind);
            writer.WriteString("executable_name", Path.GetFileName(report.Executable));
            writer.WriteBoolean("first_interactive_pixel", report.FirstInteractivePixel);
            writer.WriteBoolean("parser_consumed_is_not_presentation", report.ParserConsumedIsNotPresentation);
            writer.WriteBoolean("launched_ui", report.LaunchedUi);
            writer.WriteBoolean("herdr_executed", false);
            writer.WriteBoolean("invented_timings", false);
            writer.WriteNumber("sample_count", report.SampleCount);
            writer.WriteNumber("p50_ms", report.P50Ms);
            writer.WriteNumber("p95_ms", report.P95Ms);
            writer.WriteNumber("max_ms", report.MaxMs);
            writer.WritePropertyName("samples");
            writer.WriteStartArray();
            foreach (var sample in report.Samples)
            {
                writer.WriteStartObject();
                writer.WriteNumber("index", sample.Index);
                writer.WriteString("kind", sample.Kind);
                writer.WriteNumber("wall_ms", sample.WallMs);
                writer.WriteNumber("exit_code", sample.ExitCode);
                writer.WriteBoolean("timed_out", sample.TimedOut);
                writer.WriteNumber("process_id", sample.ProcessId);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    static ColdStartSample Launch(
        int index,
        string kind,
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeout)
    {
        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        var watch = Stopwatch.StartNew();
        using var process = OsProcess.Start(start);
        if (process is null)
            throw new InvalidOperationException("cold_start_process_missing");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var exited = process.WaitForExit(timeout);
        if (!exited)
        {
            try
            {
                process.Kill(entireProcessTree: string.Equals(kind, "ui", StringComparison.Ordinal));
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
            }

            process.WaitForExit(3000);
        }

        watch.Stop();
        var exit = 0;
        try
        {
            exit = exited ? process.ExitCode : -1;
        }
        catch (InvalidOperationException)
        {
            exit = -1;
        }

        return new ColdStartSample(index, kind, watch.Elapsed.TotalMilliseconds, exit, !exited, process.Id);
    }
}
