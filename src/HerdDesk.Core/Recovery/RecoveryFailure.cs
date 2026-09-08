using HerdDesk.Contracts;

namespace HerdDesk.Core;

public enum RecoveryScope
{
    Rpc,
    Terminal,
    Renderer,
    Application
}

public enum RecoveryCause
{
    ManualDisconnect,
    AppStopping,
    RequestEof,
    SubscriptionEof,
    RpcBridgeExit,
    DaemonUnreachable,
    SchemaIncompatible,
    ProtocolError,
    TerminalClosed,
    TerminalStdoutEof,
    TerminalClientExit,
    RendererFailure,
    Cancellation,
    Authentication,
    HostKeyChanged
}

public enum RecoveryRetryClass
{
    Stop,
    AwaitUser,
    Transient
}

public static class RecoveryCodes
{
    public const string ManualDisconnect = "manual_disconnect";
    public const string AppStopping = "app_stopping";
    public const string RequestEof = "request_eof";
    public const string SubscriptionEof = "subscription_eof";
    public const string RpcBridgeExit = "rpc_bridge_exit";
    public const string DaemonUnreachable = "daemon_unreachable";
    public const string SchemaIncompatible = "schema_incompatible";
    public const string ProtocolError = "protocol_incompatible";
    public const string TerminalClosed = "terminal_closed";
    public const string TerminalStdoutEof = "terminal_stdout_eof";
    public const string TerminalClientExit = "terminal_client_exit";
    public const string RendererFailure = "renderer_failure";
    public const string Cancellation = "cancellation";
    public const string Authentication = "authentication";
    public const string HostKeyChanged = "host_key_changed";
    public const string RetryAfter = "retry_after";
    public const string RetryNow = "retry_now";
    public const string AwaitUser = "await_user";
    public const string Stop = "stop";
    public const string RetryExhausted = "retry_exhausted";
}

public sealed record RecoveryFailure(
    RecoveryScope Scope,
    RecoveryCause Cause,
    SessionKey Session,
    PaneKey? Pane,
    ConnectionEpoch Epoch,
    bool WasUserInitiated,
    RecoveryRetryClass RetryClass,
    string DiagnosticId);
