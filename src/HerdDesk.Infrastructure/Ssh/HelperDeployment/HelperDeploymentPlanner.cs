using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.Ssh;

public sealed class HelperDeploymentPlanner : IHelperDeploymentPlanner
{
    private static readonly string[] Operations =
    [
        "probe-os-arch",
        "write-private-staging",
        "verify-length-hash",
        "hardlink-noclobber",
        "selftest-version",
        "switch-current-receipt"
    ];

    private readonly ITrustedHelperManifest _manifest;
    private readonly IRemotePlatformProbe _probe;

    public HelperDeploymentPlanner(ITrustedHelperManifest manifest, IRemotePlatformProbe probe)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(probe);
        _manifest = manifest;
        _probe = probe;
    }

    public async ValueTask<HelperPlanResult> PlanAsync(
        DeviceId device,
        SessionKey session,
        SshDeviceSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (device.Value == Guid.Empty || session.Device != device)
            return new(null, null, null, HelperCodes.ManifestInvalid);
        if (_manifest.LoadCode is not null)
            return new(null, null, null, _manifest.LoadCode);
        if (_manifest.Snapshot is null)
            return new(null, null, null, HelperCodes.ManifestUntrusted);

        var probed = await _probe.ProbeAsync(settings, cancellationToken).ConfigureAwait(false);
        if (!probed.Succeeded)
            return new(null, null, null, probed.Code ?? HelperCodes.PlatformUnsupported);

        if (!_manifest.TryGetArtifact(probed.Target!.Triple, out var artifact, out var payload, out var code))
            return new(null, null, null, code);
        if (HelperRemoteScripts.PayloadSha256(payload) != artifact.Sha256 ||
            payload.Length != artifact.Length)
            return new(null, null, null, HelperCodes.HashMismatch);

        var version = _manifest.Snapshot.BridgeVersion;
        var plan = new DeploymentPlan(
            device,
            session,
            probed.Target,
            version,
            artifact.Sha256,
            artifact.Length,
            HelperRemoteScripts.PrivateDirectory(probed.Home!, version, probed.Target.Triple),
            probed.HomeSha256!,
            HelperRemoteScripts.HashCommandFor(probed.Target.Os),
            Operations,
            SshRedaction.Display(device.Value.ToString("D")),
            HelperRemoteScripts.HashPrefix(artifact.Sha256));
        return new(plan, artifact, payload, null);
    }
}
