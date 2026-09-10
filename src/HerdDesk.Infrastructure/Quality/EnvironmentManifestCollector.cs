using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Win32;
using OsProcess = System.Diagnostics.Process;

namespace HerdDesk.Infrastructure.Quality;

public sealed record EnvironmentManifest(
    DateTimeOffset CapturedAtUtc,
    string OsDescription,
    string OsPlatform,
    string OsVersion,
    bool IsWindows,
    bool Is64BitProcess,
    bool Is64BitOperatingSystem,
    int LogicalCpus,
    long? RamBytes,
    string? ProcessorName,
    int? AcLineStatus,
    string? SdkPinVersion,
    string? SdkPinRollForward,
    bool WebView2Present,
    string? WebView2Version,
    string? WebView2Source,
    int? SystemDpi,
    int? ScreenWidth,
    int? ScreenHeight,
    bool NarratorExePresent,
    bool NarratorLaunched,
    bool NarratorStartedByCollector);

public static class EnvironmentManifestCollector
{
    const string WebView2Client = @"{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

    public static EnvironmentManifest Collect(string? repoRoot = null)
    {
        string? sdkVersion = null;
        string? sdkRollForward = null;
        if (!string.IsNullOrWhiteSpace(repoRoot))
            ReadSdkPin(repoRoot, out sdkVersion, out sdkRollForward);

        var webView2 = ReadWebView2Version();
        var narratorExe = NarratorExePath();
        var narratorPresent = File.Exists(narratorExe);
        var narratorLaunched = NarratorProcessRunning();
        return new EnvironmentManifest(
            DateTimeOffset.UtcNow,
            RuntimeInformation.OSDescription,
            Environment.OSVersion.Platform.ToString(),
            Environment.OSVersion.Version.ToString(),
            OperatingSystem.IsWindows(),
            Environment.Is64BitProcess,
            Environment.Is64BitOperatingSystem,
            Environment.ProcessorCount,
            ReadRamBytes(),
            ReadProcessorName(),
            ReadAcLineStatus(),
            sdkVersion,
            sdkRollForward,
            webView2 is not null,
            webView2?.Version,
            webView2?.Source,
            ReadSystemDpi(),
            ReadScreenMetric(0),
            ReadScreenMetric(1),
            narratorPresent,
            narratorLaunched,
            false);
    }

    public static void Write(EnvironmentManifest manifest, string path)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        QualityJson.WriteFile(path, writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("document_kind", "hd033_environment_manifest");
            writer.WriteString("captured_at_utc", manifest.CapturedAtUtc.ToString("O"));
            writer.WriteBoolean("herdr_executed", false);
            writer.WriteBoolean("live_dpi", false);
            writer.WriteBoolean("live_narrator", false);
            writer.WriteBoolean("eight_hour_soak_executed", false);
            writer.WriteBoolean("invented_timings", false);
            writer.WriteBoolean("host_fingerprint_redacted", true);
            writer.WriteNumber("mib_bytes", 1_048_576);
            writer.WriteString("os_description", manifest.OsDescription);
            writer.WriteString("os_platform", manifest.OsPlatform);
            writer.WriteString("os_version", manifest.OsVersion);
            writer.WriteBoolean("is_windows", manifest.IsWindows);
            writer.WriteBoolean("is_64bit_process", manifest.Is64BitProcess);
            writer.WriteBoolean("is_64bit_operating_system", manifest.Is64BitOperatingSystem);
            writer.WriteNumber("logical_cpus", manifest.LogicalCpus);
            if (manifest.RamBytes is { } ram)
                writer.WriteNumber("ram_bytes", ram);
            else
                writer.WriteNull("ram_bytes");
            if (manifest.ProcessorName is { } cpu)
                writer.WriteString("processor_name", cpu);
            else
                writer.WriteNull("processor_name");
            if (manifest.AcLineStatus is { } ac)
                writer.WriteNumber("ac_line_status", ac);
            else
                writer.WriteNull("ac_line_status");
            if (manifest.SdkPinVersion is { } sdk)
                writer.WriteString("sdk_pin_version", sdk);
            else
                writer.WriteNull("sdk_pin_version");
            if (manifest.SdkPinRollForward is { } roll)
                writer.WriteString("sdk_pin_roll_forward", roll);
            else
                writer.WriteNull("sdk_pin_roll_forward");
            writer.WriteBoolean("webview2_present", manifest.WebView2Present);
            if (manifest.WebView2Version is { } wv)
                writer.WriteString("webview2_version", wv);
            else
                writer.WriteNull("webview2_version");
            if (manifest.WebView2Source is { } src)
                writer.WriteString("webview2_source", src);
            else
                writer.WriteNull("webview2_source");
            if (manifest.SystemDpi is { } dpi)
                writer.WriteNumber("system_dpi", dpi);
            else
                writer.WriteNull("system_dpi");
            if (manifest.ScreenWidth is { } width)
                writer.WriteNumber("screen_width", width);
            else
                writer.WriteNull("screen_width");
            if (manifest.ScreenHeight is { } height)
                writer.WriteNumber("screen_height", height);
            else
                writer.WriteNull("screen_height");
            writer.WriteBoolean("narrator_exe_present", manifest.NarratorExePresent);
            writer.WriteBoolean("narrator_launched", manifest.NarratorLaunched);
            writer.WriteBoolean("narrator_started_by_collector", manifest.NarratorStartedByCollector);
            writer.WriteStartObject("redaction");
            writer.WriteString("host", "omitted");
            writer.WriteString("user", "omitted");
            writer.WriteString("path", "omitted");
            writer.WriteString("credential", "omitted");
            writer.WriteEndObject();
            writer.WriteEndObject();
        });
    }

    static void ReadSdkPin(string repoRoot, out string? version, out string? rollForward)
    {
        version = null;
        rollForward = null;
        var path = Path.Combine(repoRoot, "global.json");
        if (!File.Exists(path))
            return;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("sdk", out var sdk))
            return;
        if (sdk.TryGetProperty("version", out var versionElement))
            version = versionElement.GetString();
        if (sdk.TryGetProperty("rollForward", out var rollElement))
            rollForward = rollElement.GetString();
    }

    static long? ReadRamBytes()
    {
        if (OperatingSystem.IsWindows())
            return ReadWindowsRamBytes();

        var memInfo = "/proc/meminfo";
        if (!File.Exists(memInfo))
            return null;
        foreach (var line in File.ReadLines(memInfo))
        {
            if (!line.StartsWith("MemTotal:", StringComparison.Ordinal))
                continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && long.TryParse(parts[1], out var kib))
                return kib * 1024;
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    static long? ReadWindowsRamBytes()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (GlobalMemoryStatusEx(ref status) && status.TotalPhys > 0)
            return (long)status.TotalPhys;
        return null;
    }

    static string? ReadProcessorName() =>
        OperatingSystem.IsWindows() ? ReadWindowsProcessorName() : null;

    [SupportedOSPlatform("windows")]
    static string? ReadWindowsProcessorName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return key?.GetValue("ProcessorNameString") as string;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static int? ReadAcLineStatus() =>
        OperatingSystem.IsWindows() ? ReadWindowsAcLineStatus() : null;

    [SupportedOSPlatform("windows")]
    static int? ReadWindowsAcLineStatus()
    {
        if (!GetSystemPowerStatus(out var status))
            return null;
        return status.ACLineStatus;
    }

    static (string Version, string Source)? ReadWebView2Version() =>
        OperatingSystem.IsWindows() ? ReadWindowsWebView2Version() : null;

    [SupportedOSPlatform("windows")]
    static (string Version, string Source)? ReadWindowsWebView2Version()
    {
        string[] paths =
        [
            @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\" + WebView2Client,
            @"SOFTWARE\Microsoft\EdgeUpdate\Clients\" + WebView2Client
        ];
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    foreach (var path in paths)
                    {
                        using var key = baseKey.OpenSubKey(path);
                        if (key?.GetValue("pv") is string pv &&
                            !string.IsNullOrWhiteSpace(pv) &&
                            pv != "0.0.0.0")
                            return (pv, hive == RegistryHive.LocalMachine ? "hklm" : "hkcu");
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        return null;
    }

    static int? ReadSystemDpi() =>
        OperatingSystem.IsWindows() ? ReadWindowsSystemDpi() : null;

    [SupportedOSPlatform("windows")]
    static int? ReadWindowsSystemDpi()
    {
        try
        {
            var dpi = GetDpiForSystem();
            if (dpi > 0)
                return (int)dpi;
        }
        catch (EntryPointNotFoundException)
        {
        }

        var dc = GetDC(0);
        if (dc == 0)
            return null;
        try
        {
            var dpi = GetDeviceCaps(dc, 88);
            return dpi > 0 ? dpi : null;
        }
        finally
        {
            _ = ReleaseDC(0, dc);
        }
    }

    static int? ReadScreenMetric(int index) =>
        OperatingSystem.IsWindows() ? ReadWindowsScreenMetric(index) : null;

    [SupportedOSPlatform("windows")]
    static int? ReadWindowsScreenMetric(int index)
    {
        var value = GetSystemMetrics(index);
        return value > 0 ? value : null;
    }

    static string NarratorExePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Narrator.exe");

    static bool NarratorProcessRunning() =>
        OperatingSystem.IsWindows() && WindowsNarratorProcessRunning();

    [SupportedOSPlatform("windows")]
    static bool WindowsNarratorProcessRunning()
    {
        OsProcess[] processes;
        try
        {
            processes = OsProcess.GetProcessesByName("Narrator");
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }

        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll")]
    static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    static extern uint GetDpiForSystem();

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    static extern nint GetDC(nint hwnd);

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    static extern int ReleaseDC(nint hwnd, nint hdc);

    [SupportedOSPlatform("windows")]
    [DllImport("gdi32.dll")]
    static extern int GetDeviceCaps(nint hdc, int index);

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int index);
}
