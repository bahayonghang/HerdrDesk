using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using OsProcess = System.Diagnostics.Process;

namespace HerdDesk.Infrastructure.Quality;

public sealed record ProcessResourceSnapshot(
    int ProcessId,
    DateTime StartTimeUtc,
    double UserProcessorSeconds,
    double PrivilegedProcessorSeconds,
    double TotalProcessorSeconds,
    int? HandleCount,
    long? WorkingSetBytes,
    long? PrivateBytes,
    int OwnedProcessCount);

public sealed record IdleCpuSample(
    ProcessResourceSnapshot Before,
    ProcessResourceSnapshot After,
    double WallSeconds,
    int LogicalCpus,
    double NormalizedPercent,
    bool EightHourSeries);

public static class ProcessResourceSampler
{
    static readonly string[] OwnedNames =
    [
        "HerdDesk.App",
        "herddesk-bridge",
        "herddesk-filebridge"
    ];

    public static ProcessResourceSnapshot CaptureCurrent() =>
        Capture(OsProcess.GetCurrentProcess());

    public static ProcessResourceSnapshot Capture(OsProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);
        process.Refresh();
        int? handles = OperatingSystem.IsWindows() ? ReadWindowsHandleCount(process) : null;
        long? working = null;
        long? privateBytes = OperatingSystem.IsWindows() ? ReadWindowsPrivateBytes(process) : null;
        try
        {
            working = process.WorkingSet64;
        }
        catch (PlatformNotSupportedException)
        {
        }

        DateTime start;
        try
        {
            start = process.StartTime.ToUniversalTime();
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException)
        {
            start = DateTime.UtcNow;
        }

        return new ProcessResourceSnapshot(
            process.Id,
            start,
            process.UserProcessorTime.TotalSeconds,
            process.PrivilegedProcessorTime.TotalSeconds,
            process.TotalProcessorTime.TotalSeconds,
            handles,
            working,
            privateBytes,
            CountOwnedProcesses());
    }

    public static IdleCpuSample MeasureIdle(TimeSpan window)
    {
        if (window <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(window));
        var process = OsProcess.GetCurrentProcess();
        var before = Capture(process);
        var wall = Stopwatch.StartNew();
        Thread.Sleep(window);
        process.Refresh();
        var after = Capture(process);
        wall.Stop();
        var logical = Math.Max(1, Environment.ProcessorCount);
        var deltaProcessor = Math.Max(0, after.TotalProcessorSeconds - before.TotalProcessorSeconds);
        var wallSeconds = Math.Max(wall.Elapsed.TotalSeconds, 0.0001);
        var normalized = deltaProcessor / (wallSeconds * logical) * 100;
        return new IdleCpuSample(before, after, wallSeconds, logical, normalized, false);
    }

    public static int CountOwnedProcesses()
    {
        var count = 0;
        foreach (var name in OwnedNames)
        {
            OsProcess[] found;
            try
            {
                found = OsProcess.GetProcessesByName(name);
            }
            catch (PlatformNotSupportedException)
            {
                continue;
            }

            count += found.Length;
            foreach (var item in found)
                item.Dispose();
        }

        return count;
    }

    public static void Write(IdleCpuSample sample, string path)
    {
        ArgumentNullException.ThrowIfNull(sample);
        QualityJson.WriteFile(path, writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("document_kind", "hd033_idle_cpu_sample");
            writer.WriteBoolean("eight_hour_series", sample.EightHourSeries);
            writer.WriteBoolean("eight_hour_soak_executed", false);
            writer.WriteBoolean("herdr_executed", false);
            writer.WriteBoolean("invented_timings", false);
            writer.WriteNumber("mib_bytes", 1_048_576);
            writer.WriteNumber("wall_seconds", sample.WallSeconds);
            writer.WriteNumber("logical_cpus", sample.LogicalCpus);
            writer.WriteNumber("normalized_percent", sample.NormalizedPercent);
            WriteSnapshot(writer, "before", sample.Before);
            WriteSnapshot(writer, "after", sample.After);
            writer.WriteEndObject();
        });
    }

    public static void WriteSnapshot(ProcessResourceSnapshot snapshot, string path)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        QualityJson.WriteFile(path, writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("document_kind", "hd033_process_resource_snapshot");
            writer.WriteBoolean("herdr_executed", false);
            writer.WriteBoolean("live_working_set", false);
            writer.WriteBoolean("derive_process_memory_from_q_p", false);
            writer.WriteNumber("mib_bytes", 1_048_576);
            WriteSnapshotFields(writer, snapshot);
            writer.WriteEndObject();
        });
    }

    static void WriteSnapshot(Utf8JsonWriter writer, string name, ProcessResourceSnapshot snapshot)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        WriteSnapshotFields(writer, snapshot);
        writer.WriteEndObject();
    }

    [SupportedOSPlatform("windows")]
    static int ReadWindowsHandleCount(OsProcess process) => process.HandleCount;

    [SupportedOSPlatform("windows")]
    static long ReadWindowsPrivateBytes(OsProcess process) => process.PrivateMemorySize64;

    static void WriteSnapshotFields(Utf8JsonWriter writer, ProcessResourceSnapshot snapshot)
    {
        writer.WriteNumber("process_id", snapshot.ProcessId);
        writer.WriteString("start_time_utc", snapshot.StartTimeUtc.ToString("O"));
        writer.WriteNumber("user_processor_seconds", snapshot.UserProcessorSeconds);
        writer.WriteNumber("privileged_processor_seconds", snapshot.PrivilegedProcessorSeconds);
        writer.WriteNumber("total_processor_seconds", snapshot.TotalProcessorSeconds);
        if (snapshot.HandleCount is { } handles)
            writer.WriteNumber("handle_count", handles);
        else
            writer.WriteNull("handle_count");
        if (snapshot.WorkingSetBytes is { } working)
            writer.WriteNumber("working_set_bytes", working);
        else
            writer.WriteNull("working_set_bytes");
        if (snapshot.PrivateBytes is { } privateBytes)
            writer.WriteNumber("private_bytes", privateBytes);
        else
            writer.WriteNull("private_bytes");
        writer.WriteNumber("owned_process_count", snapshot.OwnedProcessCount);
    }
}
