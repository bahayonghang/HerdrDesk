using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Configuration;

namespace HerdDesk.Infrastructure.Ssh;

public sealed class RemotePlatformProbe : IRemotePlatformProbe
{
    private readonly OpenSshLocator _locator;
    private readonly ISshProcessRunner _runner;
    private readonly string _knownHostsFile;
    private readonly IReadOnlyList<HelperTarget> _targets;

    public RemotePlatformProbe(AppDataPaths paths, ITrustedHelperManifest manifest)
        : this(
            OpenSshLocator.SystemDefault(),
            new SshOwnedProcessRunner(),
            paths.KnownHostsFile,
            manifest.Targets)
    {
    }

    internal RemotePlatformProbe(
        OpenSshLocator locator,
        ISshProcessRunner runner,
        string knownHostsFile,
        IReadOnlyList<HelperTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentException.ThrowIfNullOrWhiteSpace(knownHostsFile);
        ArgumentNullException.ThrowIfNull(targets);
        _locator = locator;
        _runner = runner;
        _knownHostsFile = knownHostsFile;
        _targets = targets;
    }

    public async ValueTask<RemotePlatformProbeResult> ProbeAsync(
        SshDeviceSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!_locator.SshExists ||
            !SshProcessSpecFactory.TryPlatformProbe(_locator, settings, _knownHostsFile, out var spec, out _))
        {
            return Fail(HelperCodes.PlatformUnsupported);
        }

        SshProcessRunResult ran;
        try
        {
            ran = await _runner.RunAsync(spec, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Fail(HelperCodes.Cancelled);
        }
        catch (Exception)
        {
            return Fail(HelperCodes.PlatformUnsupported);
        }

        if (ran.Cancelled)
            return Fail(HelperCodes.Cancelled);
        if (ran.TimedOut || ran.ExitCode != 0)
            return Fail(HelperCodes.PlatformUnsupported);
        if (!string.IsNullOrEmpty(ran.Stderr))
            return Fail(HelperCodes.PlatformUnsupported);
        if (!TryParse(ran.Stdout, _targets, out var target, out var home))
            return Fail(HelperCodes.PlatformUnsupported);
        if (!target.RemoteDeploy || target.AtomicPublish != "hardlink_noclobber")
            return Fail(HelperCodes.PlatformUnsupported);
        return new(target, home, HelperRemoteScripts.HomeSha256(home), target.Os, target.Arch, null);
    }

    internal static bool TryParse(
        string stdout,
        IReadOnlyList<HelperTarget> targets,
        out HelperTarget target,
        out string home)
    {
        target = null!;
        home = "";
        if (string.IsNullOrEmpty(stdout) || stdout.Length > HelperRemoteScripts.MaxProbeBytes)
            return false;
        if (stdout.Contains('\r', StringComparison.Ordinal) || stdout.Contains('\0', StringComparison.Ordinal))
            return false;
        foreach (var ch in stdout)
        {
            if (char.IsControl(ch) && ch != '\n')
                return false;
        }

        if (!stdout.EndsWith('\n'))
            return false;
        var trimmed = stdout.TrimEnd('\n');
        var parts = trimmed.Split('\n');
        if (parts.Length != 3)
            return false;
        var os = parts[0];
        var arch = parts[1];
        home = parts[2];
        if (os is not "linux" and not "darwin")
            return false;
        if (os == "linux" && arch is not "x86_64" and not "aarch64")
            return false;
        if (os == "darwin" && arch is not "x86_64" and not "arm64")
            return false;
        if (!IsSafeHome(home))
            return false;

        var matches = targets.Where(item => item.UnameS == os && item.UnameM == arch).ToArray();
        if (matches.Length != 1)
            return false;
        target = matches[0];
        return true;
    }

    private static bool IsSafeHome(string home)
    {
        if (string.IsNullOrEmpty(home) || home.Length > 512)
            return false;
        if (home[0] != '/' || home.StartsWith("//", StringComparison.Ordinal))
            return false;
        if (HelperRemoteScripts.IsForbiddenInstallHome(home))
            return false;
        if (home.Contains("..", StringComparison.Ordinal) || home.Contains('\\', StringComparison.Ordinal))
            return false;
        if (home.Contains("//", StringComparison.Ordinal))
            return false;
        foreach (var ch in home)
        {
            if (char.IsControl(ch))
                return false;
            if (ch is not '/' and not '.' and not '-' and not '_' && !char.IsAsciiLetterOrDigit(ch))
                return false;
        }

        return true;
    }

    private static RemotePlatformProbeResult Fail(string code) =>
        new(null, null, null, null, null, code);
}
