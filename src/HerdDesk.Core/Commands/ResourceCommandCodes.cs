namespace HerdDesk.Core;

public static class ResourceCommandCodes
{
    public const string Allowed = "allowed";
    public const string SchemaIncompatible = "rpc_schema_incompatible";
    public const string CapabilityUnknown = "capability_unknown";
    public const string OperationDisabled = "operation_disabled";
    public const string InvalidIdentity = "invalid_identity";
    public const string StaleTarget = "stale_target";
    public const string StaleConfirmation = "stale_confirmation";
    public const string ValidationError = "validation_error";
    public const string AgentKindUnknown = "agent_kind_unknown";
    public const string AgentKindUnverified = "agent_kind_unverified";
    public const string CommandLineRejected = "command_line_rejected";
    public const string ArgvRejected = "argv_rejected";
    public const string ApprovalBypassRejected = "approval_bypass_rejected";
    public const string CloseGroupUnconfirmed = "close_group_unconfirmed";
    public const string WorkspaceGroupCloseRequired = "workspace_group_close_required";
    public const string PermissionDenied = "permission_denied";
    public const string Conflict = "conflict";
    public const string NotFound = "not_found";
    public const string Timeout = "timeout";
    public const string Transport = "transport";
    public const string Protocol = "protocol";
    public const string NotSent = "rpc_not_sent";
    public const string CancelledAfterWrite = "rpc_cancelled_after_write";
    public const string Unavailable = "rpc_bridge_unavailable";
    public const string OperationInFlight = "operation_in_flight";
    public const string MutationAlreadySent = "mutation_already_sent";
    public const string RetryWithoutConfirm = "retry_without_confirm";
    public const string UnknownOutcome = "unknown_outcome";
    public const string QueryRequired = "query_required";
    public const string Cancelled = "cancelled";
    public const string Offline = "offline";
    public const string Stale = "stale";
    public const string DynamicMethodRejected = "dynamic_method_rejected";
}
