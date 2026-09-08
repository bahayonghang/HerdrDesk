namespace HerdDesk.Contracts;

public static class SshCodes
{
    public const string ExecutableUnavailable = "ssh_executable_unavailable";
    public const string ConfigInvalid = "ssh_config_invalid";
    public const string ProfileInvalid = "ssh_profile_invalid";
    public const string HostKeyUnknown = "host_key_unknown";
    public const string HostKeyChanged = "host_key_changed";
    public const string AuthUnsupported = "auth_unsupported";
    public const string AuthFailed = "auth_failed";
    public const string TestTimeout = "ssh_test_timeout";
    public const string TestCancelled = "ssh_test_cancelled";
    public const string TestFailed = "ssh_test_failed";
    public const string PersistenceFailed = "persistence_failed";
    public const string Ok = "ok";
}

public enum SshAuthMode
{
    OpenSshConfig,
    IdentityFile,
    Agent
}

public enum SshConnectionTestPhase
{
    Idle,
    Executable,
    ConfigPreview,
    HostKey,
    AwaitingHostVerification,
    Authenticating,
    Succeeded,
    Failed,
    Cancelled
}

public enum HostKeyStatus
{
    Trusted,
    UnknownCandidate,
    Changed,
    Unavailable
}

public enum SshValueSource
{
    ExplicitField,
    OpenSshConfig
}

public enum SshHopKind
{
    Target,
    ProxyJump
}

public enum SshUnsupportedAuthKind
{
    PasswordArgvOrStdin,
    KeyboardInteractive,
    HiddenPassphrase,
    MfaOrBrowser,
    TailscaleSsh,
    Pkcs11OrSecurityKey
}

public sealed record SshDeviceSettings(
    string HostAlias,
    string? User,
    int? Port,
    string? IdentityFilePath,
    string? IdentityAgent,
    string? ProxyJumpAlias,
    string RemoteHerdrPath,
    string? RemoteHelperPath,
    WireEnum<SshAuthMode> AuthMode,
    long ProfileRevision)
{
    public static WireEnum<SshAuthMode> ParseAuthMode(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        SshAuthMode? known = raw switch
        {
            "open_ssh_config" => SshAuthMode.OpenSshConfig,
            "identity_file" => SshAuthMode.IdentityFile,
            "agent" => SshAuthMode.Agent,
            _ => null
        };
        return new(raw, known);
    }

    public static string AuthModeWire(SshAuthMode mode) => mode switch
    {
        SshAuthMode.OpenSshConfig => "open_ssh_config",
        SshAuthMode.IdentityFile => "identity_file",
        SshAuthMode.Agent => "agent",
        _ => "unknown"
    };

    public static SshDeviceSettings Create(
        string hostAlias,
        SshAuthMode mode,
        string remoteHerdrPath,
        string? user = null,
        int? port = null,
        string? identityFilePath = null,
        string? identityAgent = null,
        string? proxyJumpAlias = null,
        string? remoteHelperPath = null,
        long profileRevision = 0) =>
        new(
            hostAlias,
            user,
            port,
            identityFilePath,
            identityAgent,
            proxyJumpAlias,
            remoteHerdrPath,
            remoteHelperPath,
            new WireEnum<SshAuthMode>(AuthModeWire(mode), mode),
            profileRevision);
}

public sealed record SshResolvedIdentity(string Path, SshValueSource Source);

public sealed record SshResolvedConfiguration(
    string Hostname,
    SshValueSource HostnameSource,
    string User,
    SshValueSource UserSource,
    int Port,
    SshValueSource PortSource,
    IReadOnlyList<SshResolvedIdentity> IdentityFiles,
    string? IdentityAgent,
    SshValueSource? IdentityAgentSource,
    string? ProxyJump,
    SshValueSource? ProxyJumpSource);

public sealed record HostKeyCandidate(
    string Host,
    int Port,
    SshHopKind Hop,
    string KeyType,
    string KeyBlobSha256,
    string FingerprintSha256,
    DateTimeOffset ObservedUtc,
    bool VerifiedOutOfBand = false);

public sealed record HostKeyAssessment(
    HostKeyStatus Status,
    HostKeyCandidate? Candidate,
    long KnownHostRevision);

public sealed record HostKeyTrustWriteResult(long? Revision, string? Code)
{
    public bool Succeeded => Revision is not null && Code is null;
}

public sealed record SshStageMetadatum(string Key, string Value);

public sealed record SshStageResult(
    SshConnectionTestPhase Phase,
    string Code,
    long DurationMs,
    IReadOnlyList<SshStageMetadatum> Metadata);

public sealed record SshConnectionTestResult(
    SshConnectionTestPhase Phase,
    string? Code,
    IReadOnlyList<SshStageResult> Stages,
    SshResolvedConfiguration? Preview,
    HostKeyAssessment? HostKey)
{
    public bool Succeeded => Phase == SshConnectionTestPhase.Succeeded && Code is null;
}

public sealed record SshPreviewResult(SshResolvedConfiguration? Configuration, string? Code)
{
    public bool Succeeded => Configuration is not null && Code is null;
}

public sealed record SshUnsupportedCapability(SshUnsupportedAuthKind Kind, string Id, string Label)
{
    public string Status => "未支持";
}

public interface ISshConfigPreview
{
    ValueTask<SshPreviewResult> PreviewAsync(
        SshDeviceSettings settings, CancellationToken cancellationToken = default);
}

public interface ISshHostKeyStore
{
    ValueTask<HostKeyAssessment> AssessAsync(
        HostKeyCandidate observed, CancellationToken cancellationToken = default);
    ValueTask<HostKeyTrustWriteResult> ConfirmUnknownAsync(
        HostKeyCandidate candidate, CancellationToken cancellationToken = default);
}

public interface ISshConnectionTester
{
    ValueTask<SshConnectionTestResult> TestUntilHostKeyAsync(
        SshDeviceSettings settings, CancellationToken cancellationToken = default);
    ValueTask<SshConnectionTestResult> ConfirmUnknownHostAsync(
        HostKeyCandidate candidate, CancellationToken cancellationToken = default);
    ValueTask<SshConnectionTestResult> AuthenticateAsync(
        SshDeviceSettings settings, CancellationToken cancellationToken = default);
    ValueTask<SshConnectionTestResult> CancelAsync();
}

public static class SshRedaction
{
    public static string Display(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        if (value.Length <= 2)
            return "**";
        var mask = Math.Min(8, value.Length - 1);
        return string.Concat(value.AsSpan(0, 1), new string('*', mask));
    }

    public static IReadOnlyList<SshUnsupportedCapability> UnsupportedMatrix { get; } =
    [
        new(SshUnsupportedAuthKind.PasswordArgvOrStdin, "password_argv_stdin", "密码 argv/stdin"),
        new(SshUnsupportedAuthKind.KeyboardInteractive, "keyboard_interactive", "keyboard-interactive"),
        new(SshUnsupportedAuthKind.HiddenPassphrase, "hidden_passphrase", "隐藏 passphrase 提示"),
        new(SshUnsupportedAuthKind.MfaOrBrowser, "mfa_browser", "MFA/浏览器确认"),
        new(SshUnsupportedAuthKind.TailscaleSsh, "tailscale_ssh", "Tailscale SSH"),
        new(SshUnsupportedAuthKind.Pkcs11OrSecurityKey, "pkcs11_security_key", "PKCS#11/安全密钥")
    ];
}
