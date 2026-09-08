using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.Ssh;

internal enum SshProcessKind
{
    Version,
    ConfigPreview,
    HostKeyScan,
    AuthProbe
}

internal sealed record SshProcessSpec(
    string Executable,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout,
    SshProcessKind Kind);

internal sealed record SshProcessRunResult(
    int ExitCode,
    string Stdout,
    string Stderr,
    bool TimedOut,
    bool Cancelled,
    int? DirectChildProcessId);

internal interface ISshProcessRunner
{
    ValueTask<SshProcessRunResult> RunAsync(
        SshProcessSpec spec, CancellationToken cancellationToken = default);
}

internal sealed class OpenSshLocator
{
    public OpenSshLocator(
        string sshExecutable,
        string? keyScanExecutable = null,
        IReadOnlyList<string>? argumentPrefix = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sshExecutable);
        SshExecutable = Path.GetFullPath(sshExecutable);
        KeyScanExecutable = Path.GetFullPath(
            keyScanExecutable ??
            Path.Combine(
                Path.GetDirectoryName(SshExecutable) ?? SshExecutable,
                OperatingSystem.IsWindows() ? "ssh-keyscan.exe" : "ssh-keyscan"));
        ArgumentPrefix = argumentPrefix ?? [];
    }

    public string SshExecutable { get; }
    public string KeyScanExecutable { get; }
    public IReadOnlyList<string> ArgumentPrefix { get; }

    public static OpenSshLocator SystemDefault()
    {
        if (OperatingSystem.IsWindows())
        {
            var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            return new(Path.Combine(system, "OpenSSH", "ssh.exe"));
        }

        return new("/usr/bin/ssh");
    }

    public bool SshExists => File.Exists(SshExecutable);
    public bool KeyScanExists => File.Exists(KeyScanExecutable);
}
