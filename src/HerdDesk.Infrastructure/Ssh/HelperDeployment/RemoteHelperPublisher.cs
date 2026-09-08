using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Configuration;

namespace HerdDesk.Infrastructure.Ssh;

public sealed class RemoteHelperPublisher : IHelperPublisher
{
    private readonly OpenSshLocator _locator;
    private readonly ISshProcessRunner _runner;
    private readonly string _knownHostsFile;

    public RemoteHelperPublisher(AppDataPaths paths)
        : this(OpenSshLocator.SystemDefault(), new SshOwnedProcessRunner(), paths.KnownHostsFile)
    {
    }

    internal RemoteHelperPublisher(
        OpenSshLocator locator,
        ISshProcessRunner runner,
        string knownHostsFile)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentException.ThrowIfNullOrWhiteSpace(knownHostsFile);
        _locator = locator;
        _runner = runner;
        _knownHostsFile = knownHostsFile;
    }

    public async ValueTask<HelperPublishResult> PublishAsync(
        DeploymentPlan plan,
        SshDeviceSettings settings,
        byte[] payload,
        string stagingId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingId);
        if (ContainsForbiddenArgv(plan, payload, stagingId))
            return new(HelperCodes.ManifestInvalid, false);
        if (payload.Length != plan.Length ||
            HelperRemoteScripts.PayloadSha256(payload) != plan.Sha256)
            return new(HelperCodes.HashMismatch, false);
        if (!_locator.SshExists)
            return new(HelperCodes.PlatformUnsupported, false);
        if (!SshProcessSpecFactory.TryHelperBootstrap(
                _locator,
                settings,
                _knownHostsFile,
                plan.Version,
                plan.Target.Triple,
                plan.Sha256,
                plan.Length,
                stagingId,
                plan.HashCommand,
                payload,
                out var spec,
                out var specCode))
        {
            return new(
                specCode is SshCodes.ExecutableUnavailable or SshCodes.ProfileInvalid or SshCodes.AuthUnsupported
                    ? HelperCodes.PlatformUnsupported
                    : specCode,
                false);
        }

        if (spec.Arguments.Any(ForbiddenArgument))
            return new(HelperCodes.ManifestInvalid, false);

        SshProcessRunResult ran;
        try
        {
            ran = await _runner.RunAsync(spec, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new(HelperCodes.Cancelled, false);
        }
        catch (Exception)
        {
            return new(HelperCodes.UploadFailed, false);
        }

        if (ran.Cancelled)
            return new(HelperCodes.Cancelled, false);
        if (ran.TimedOut)
            return new(HelperCodes.UploadFailed, false);
        if (!string.IsNullOrEmpty(ran.Stderr))
            return new(HelperCodes.UploadFailed, false);
        if (!HelperRemoteScripts.TryMapRemoteCode(ran.Stdout, out var remote))
            return new(HelperCodes.UploadFailed, false);
        if (remote == HelperCodes.Ok)
            return new(null, true);
        return new(remote, false);
    }

    public async ValueTask CleanupAsync(
        DeploymentPlan plan,
        SshDeviceSettings settings,
        string stagingId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(settings);
        if (!SshProcessSpecFactory.TryHelperCleanup(
                _locator,
                settings,
                _knownHostsFile,
                plan.Version,
                plan.Target.Triple,
                stagingId,
                out var spec,
                out _))
            return;
        try
        {
            await _runner.RunAsync(spec, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private static bool ContainsForbiddenArgv(DeploymentPlan plan, byte[] payload, string stagingId)
    {
        _ = payload;
        if (HelperRemoteScripts.IsForbiddenInstallHome(plan.PrivateDirectory))
            return true;
        return !HelperRemoteScripts.IsStagingId(stagingId);
    }

    internal static bool ForbiddenArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument))
            return true;
        if (argument.Contains("sudo", StringComparison.OrdinalIgnoreCase))
            return true;
        if (argument.StartsWith("/usr/", StringComparison.Ordinal) ||
            argument.StartsWith("/opt/", StringComparison.Ordinal))
            return true;
        return false;
    }
}
