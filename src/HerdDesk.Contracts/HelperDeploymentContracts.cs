namespace HerdDesk.Contracts;

public static class HelperCodes
{
    public const string ManifestUntrusted = "helper_manifest_untrusted";
    public const string ManifestInvalid = "helper_manifest_invalid";
    public const string PlatformUnsupported = "helper_platform_unsupported";
    public const string ConsentRequired = "helper_consent_required";
    public const string ConsentStale = "helper_consent_stale";
    public const string UploadFailed = "helper_upload_failed";
    public const string HashMismatch = "helper_hash_mismatch";
    public const string VersionCollision = "helper_version_collision";
    public const string SelftestFailed = "helper_selftest_failed";
    public const string ActivationFailed = "helper_activation_failed";
    public const string Cancelled = "helper_cancelled";
    public const string Ok = "ok";
}

public enum HelperDeploymentPhase
{
    Idle,
    Planning,
    AwaitingConsent,
    Uploading,
    Verifying,
    Activating,
    Succeeded,
    Cancelled,
    Failed,
    RolledBack
}

public sealed record HelperTarget(
    string Triple,
    string Os,
    string Arch,
    string UnameS,
    string UnameM,
    string ArtifactFileName,
    bool RemoteDeploy,
    string AtomicPublish);

public sealed record HelperArtifactRecord(
    string Triple,
    string FileName,
    long Length,
    string Sha256,
    string Os,
    string Arch,
    string BuildStatus);

public sealed record HelperManifestSnapshot(
    int ManifestVersion,
    string BridgeVersion,
    string ProtocolVersion,
    string SourceCommit,
    string ApplicationVersion,
    IReadOnlyList<HelperArtifactRecord> Artifacts);

public sealed record HelperReceiptKey(
    DeviceId Device,
    string EndpointKey,
    string? SessionName,
    string TargetTriple,
    string RemoteHomeSha256);

public sealed record DeploymentPlan(
    DeviceId Device,
    SessionKey Session,
    HelperTarget Target,
    string Version,
    string Sha256,
    long Length,
    string PrivateDirectory,
    string RemoteHomeSha256,
    string HashCommand,
    IReadOnlyList<string> Operations,
    string DisplayDevice,
    string HashPrefix);

public sealed record HelperConsentValues(DeviceId Device, string Version, string Sha256);

public sealed record DeploymentReceipt(
    HelperReceiptKey Key,
    string Version,
    string Sha256,
    string? PreviousVersion,
    string? PreviousSha256,
    long Revision);

public sealed record HelperStageResult(
    HelperDeploymentPhase Phase,
    string Code,
    long DurationMs);

public sealed record HelperDeploymentResult(
    HelperDeploymentPhase Phase,
    string? Code,
    DeploymentPlan? Plan,
    DeploymentReceipt? Receipt,
    IReadOnlyList<HelperStageResult> Stages)
{
    public bool Succeeded => Phase == HelperDeploymentPhase.Succeeded && Code is null;
}

public sealed record HelperPlanResult(
    DeploymentPlan? Plan,
    HelperArtifactRecord? Artifact,
    byte[]? Payload,
    string? Code)
{
    public bool Succeeded => Plan is not null && Artifact is not null && Payload is not null && Code is null;
}

public sealed record HelperPublishResult(string? Code, bool Published)
{
    public bool Succeeded => Published && Code is null;
}

public sealed record RemotePlatformProbeResult(
    HelperTarget? Target,
    string? Home,
    string? HomeSha256,
    string? Os,
    string? Arch,
    string? Code)
{
    public bool Succeeded => Target is not null && Home is not null && HomeSha256 is not null && Code is null;
}

public interface ITrustedHelperManifest
{
    string ApplicationVersion { get; }
    string? LoadCode { get; }
    HelperManifestSnapshot? Snapshot { get; }
    IReadOnlyList<HelperTarget> Targets { get; }
    bool TryGetArtifact(string triple, out HelperArtifactRecord artifact, out byte[] payload, out string code);
}

public interface IRemotePlatformProbe
{
    ValueTask<RemotePlatformProbeResult> ProbeAsync(
        SshDeviceSettings settings, CancellationToken cancellationToken = default);
}

public interface IHelperDeploymentPlanner
{
    ValueTask<HelperPlanResult> PlanAsync(
        DeviceId device,
        SessionKey session,
        SshDeviceSettings settings,
        CancellationToken cancellationToken = default);
}

public interface IHelperPublisher
{
    ValueTask<HelperPublishResult> PublishAsync(
        DeploymentPlan plan,
        SshDeviceSettings settings,
        byte[] payload,
        string stagingId,
        CancellationToken cancellationToken = default);
    ValueTask CleanupAsync(
        DeploymentPlan plan,
        SshDeviceSettings settings,
        string stagingId,
        CancellationToken cancellationToken = default);
}

public interface IHelperDeploymentService
{
    HelperDeploymentPhase Phase { get; }
    DeploymentPlan? Plan { get; }
    ValueTask<HelperDeploymentResult> PlanAsync(
        DeviceId device,
        SessionKey session,
        SshDeviceSettings settings,
        CancellationToken cancellationToken = default);
    ValueTask<HelperDeploymentResult> ConfirmAsync(
        HelperConsentValues consent, CancellationToken cancellationToken = default);
    ValueTask<HelperDeploymentResult> CancelAsync();
    ValueTask<HelperDeploymentResult> RollbackAsync(
        HelperReceiptKey key, CancellationToken cancellationToken = default);
}
