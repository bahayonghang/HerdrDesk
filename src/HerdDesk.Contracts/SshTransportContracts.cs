namespace HerdDesk.Contracts;

public static class SshTransportCodes
{
    public const string StdoutProtocolPollution = "remote_stdout_protocol_pollution";
    public const string RecordTooLarge = "remote_record_too_large";
    public const string StreamTruncated = "remote_stream_truncated";
    public const string BridgeIncompatible = "remote_bridge_incompatible";
    public const string DaemonUnavailable = "remote_daemon_unavailable";
    public const string ChildExited = "remote_child_exited";
    public const string TransportCancelled = "remote_transport_cancelled";
    public const string TerminalClosed = "remote_terminal_closed";
    public const string SchemaIncompatible = "remote_schema_incompatible";
    public const string TrustRequired = "remote_trust_required";
    public const string UnclassifiedExit = "remote_unclassified_exit";
}

public enum SshChannelKind
{
    RequestRpc,
    EventRpc,
    Terminal,
    CompatibilityProbe
}

public enum SshTransportStage
{
    Starting,
    Ready,
    Failed,
    Teardown
}

public enum SshRetryDisposition
{
    RetryAfter,
    RetryNow,
    AwaitUser,
    Stop,
    UnknownBlocked,
    HostKeyBlocked,
    AuthenticationBlocked
}

public sealed record SshTransportOutcome(
    SshChannelKind Channel,
    SshTransportStage Stage,
    ConnectionEpoch Epoch,
    string Code,
    SshRetryDisposition Disposition,
    long DurationMs,
    long StdoutBytes,
    long StderrBytes,
    int? ExitCode,
    int? DirectChildProcessId);
