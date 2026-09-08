namespace HerdDesk.Infrastructure.Configuration;

public sealed class AppDataPaths
{
    public AppDataPaths(
        string root,
        string settingsDirectory,
        string cacheDirectory,
        string logDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        Root = Path.GetFullPath(root);
        SettingsDirectory = Path.GetFullPath(settingsDirectory);
        CacheDirectory = Path.GetFullPath(cacheDirectory);
        LogDirectory = Path.GetFullPath(logDirectory);
        ConfigurationFile = Path.Combine(SettingsDirectory, "device-profiles.json");
        ConfigurationBackupFile = ConfigurationFile + ".bak";
        KnownHostsFile = Path.Combine(SettingsDirectory, "ssh-known-hosts.json");
        KnownHostsBackupFile = KnownHostsFile + ".bak";
        HelperReceiptsFile = Path.Combine(SettingsDirectory, "helper-receipts.json");
        HelperReceiptsBackupFile = HelperReceiptsFile + ".bak";
        HelperReceiptsLockFile = Path.Combine(SettingsDirectory, "helper-receipts.lock");
        DiagnosticSaltFile = Path.Combine(SettingsDirectory, "diagnostic-alias.salt");
        DiagnosticLogFile = Path.Combine(LogDirectory, "diagnostics.jsonl");
    }

    public string Root { get; }
    public string SettingsDirectory { get; }
    public string CacheDirectory { get; }
    public string LogDirectory { get; }
    public string ConfigurationFile { get; }
    public string ConfigurationBackupFile { get; }
    public string KnownHostsFile { get; }
    public string KnownHostsBackupFile { get; }
    public string HelperReceiptsFile { get; }
    public string HelperReceiptsBackupFile { get; }
    public string HelperReceiptsLockFile { get; }
    public string DiagnosticSaltFile { get; }
    public string DiagnosticLogFile { get; }

    public static AppDataPaths FromRoot(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var full = Path.GetFullPath(root);
        return new(
            full,
            Path.Combine(full, "settings"),
            Path.Combine(full, "cache"),
            Path.Combine(full, "logs"));
    }

    public static AppDataPaths ForCurrentUser()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local))
            throw new InvalidOperationException("application_data_unavailable");
        return FromRoot(Path.Combine(local, "HerdDesk"));
    }
}
