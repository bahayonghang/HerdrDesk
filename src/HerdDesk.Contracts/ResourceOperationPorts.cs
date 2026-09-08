namespace HerdDesk.Contracts;

public enum ResourceKind
{
    Workspace,
    Tab,
    Terminal,
    Pane,
    Agent
}

public enum ResourceOperationKind
{
    CreateWorkspace,
    CreateTerminal,
    CreateAgent,
    Rename,
    Close
}

public enum ResourceOperationState
{
    Idle,
    Validating,
    AwaitingConfirmation,
    Submitting,
    Observing,
    Succeeded,
    Failed,
    Cancelled,
    UnknownOutcome,
    StaleTarget
}

public enum ResourceErrorKind
{
    None,
    Validation,
    Capability,
    Permission,
    Conflict,
    NotFound,
    Timeout,
    Transport,
    Protocol,
    Unknown,
    StaleTarget,
    GroupCloseRequired
}

public enum ResourceTransportKind
{
    Result,
    ApplicationError,
    NotSent,
    CancelledAfterWrite,
    ConnectionLost,
    Protocol,
    Unavailable,
    Timeout
}

public readonly record struct ResourceConfirmationToken(string Value);

public sealed record ResourceKey(
    SessionKey Session,
    ResourceKind Kind,
    string WorkspaceId,
    string? TabId = null,
    string? PaneId = null,
    string? TerminalId = null)
{
    public PaneKey? AsPane() =>
        string.IsNullOrWhiteSpace(PaneId) ? null : new PaneKey(Session, WorkspaceId, PaneId);
}

public sealed record ResourceProjectionStamp(ConnectionEpoch Epoch, long Revision);

public sealed record VerifiedAgentOptions
{
    public static VerifiedAgentOptions None { get; } = new();
}

public sealed record CreateWorkspaceRequest(
    SessionKey Session,
    string? Name,
    string? WorkingDirectory);

public sealed record CreateTerminalRequest(
    ResourceKey Parent,
    string? WorkingDirectory,
    string? Name);

public sealed record CreateAgentRequest(
    ResourceKey Parent,
    KnownAgentKind Kind,
    string Name,
    string? WorkingDirectory,
    VerifiedAgentOptions Options);

public sealed record RenameResourceRequest(
    ResourceKey Target,
    ResourceProjectionStamp Expected,
    ResourceConfirmationToken Confirmation,
    string NewName);

public sealed record CloseResourceRequest(
    ResourceKey Target,
    ResourceProjectionStamp Expected,
    ResourceConfirmationToken Confirmation,
    bool CloseGroup = false);

public abstract record ResourceIntent
{
    public abstract ResourceOperationKind Kind { get; }
}

public sealed record CreateWorkspaceIntent(
    SessionKey Session,
    string? Name,
    string? WorkingDirectory) : ResourceIntent
{
    public override ResourceOperationKind Kind => ResourceOperationKind.CreateWorkspace;
}

public sealed record CreateTerminalIntent(
    ResourceKey Parent,
    string? WorkingDirectory,
    string? Name) : ResourceIntent
{
    public override ResourceOperationKind Kind => ResourceOperationKind.CreateTerminal;
}

public sealed record CreateAgentIntent(
    ResourceKey Parent,
    KnownAgentKind AgentKind,
    string Name) : ResourceIntent
{
    public override ResourceOperationKind Kind => ResourceOperationKind.CreateAgent;
}

public sealed record RenameResourceIntent(
    ResourceKey Target,
    string NewName) : ResourceIntent
{
    public override ResourceOperationKind Kind => ResourceOperationKind.Rename;
}

public sealed record CloseResourceIntent(
    ResourceKey Target,
    bool CloseGroup) : ResourceIntent
{
    public override ResourceOperationKind Kind => ResourceOperationKind.Close;
}

public sealed record ResourceTransportReceipt(
    ResourceTransportKind Kind,
    string? ApplicationCode = null,
    string? CreatedWorkspaceId = null,
    string? CreatedTabId = null,
    string? CreatedPaneId = null,
    string? CreatedTerminalId = null,
    string? ObservedName = null);

public enum ResourceQueryKind
{
    Snapshot,
    Workspace,
    Tab,
    Pane,
    Agent
}

public sealed record ResourceQueryRequest(
    SessionKey Session,
    ResourceQueryKind Kind,
    string? WorkspaceId = null,
    string? TabId = null,
    string? PaneId = null,
    string? TerminalId = null);

public sealed record ResourceQueryReceipt(
    ResourceTransportKind Kind,
    bool EntityPresent,
    string? ApplicationCode = null,
    string? Name = null,
    string? WorkspaceId = null,
    string? TabId = null,
    string? PaneId = null,
    string? TerminalId = null,
    KnownAgentKind? AgentKind = null);

public sealed record ResourceStoreSnapshot(
    DeviceProjectionSnapshot Projection,
    DeviceFreshness Freshness);

public sealed record ResourceOperation(
    string CorrelationId,
    ResourceOperationKind Kind,
    ResourceOperationState State,
    ResourceKey? Target,
    string Breadcrumb,
    string? WorkingDirectoryDisplay,
    KnownAgentKind? AgentKind,
    string? DraftName,
    bool CloseGroup,
    bool NeedsGroupClose,
    bool MutationSent,
    bool QueryAfterTimeout,
    bool RetryAllowed,
    ResourceErrorKind ErrorKind,
    string? Code,
    ResourceKey? ResultKey,
    ConnectionEpoch Epoch,
    long ProjectionRevision,
    ResourceConfirmationToken? Confirmation)
{
    public static ResourceOperation Idle { get; } = new(
        "",
        ResourceOperationKind.CreateWorkspace,
        ResourceOperationState.Idle,
        null,
        "",
        null,
        null,
        null,
        false,
        false,
        false,
        false,
        false,
        ResourceErrorKind.None,
        null,
        null,
        new ConnectionEpoch(0),
        0,
        null);
}

public sealed record ResourceGateDecision(bool Allowed, string Code, ResourceErrorKind ErrorKind)
{
    public static ResourceGateDecision Ok() => new(true, "allowed", ResourceErrorKind.None);

    public static ResourceGateDecision Deny(string code, ResourceErrorKind kind) =>
        new(false, code, kind);
}

public interface IResourceProjectionStore
{
    ResourceStoreSnapshot Read();
}

public interface IResourceCommandTransport
{
    ValueTask<ResourceTransportReceipt> SubmitAsync(
        ResourceIntent intent,
        CancellationToken cancellationToken = default);
}

public interface IResourceQueryTransport
{
    ValueTask<ResourceQueryReceipt> QueryAsync(
        ResourceQueryRequest query,
        CancellationToken cancellationToken = default);
}

public interface IResourceCommandCoordinator : IAsyncDisposable
{
    ResourceOperation Current { get; }

    IAsyncEnumerable<ResourceOperation> ReadStatesAsync(CancellationToken cancellationToken = default);

    ValueTask OpenCreateAsync(
        ResourceOperationKind kind,
        ResourceKey parent,
        string breadcrumb,
        CancellationToken cancellationToken = default);

    ValueTask ValidateDraftAsync(
        string? name,
        string? workingDirectory,
        KnownAgentKind? agentKind,
        CancellationToken cancellationToken = default);

    ValueTask SubmitCreateAsync(
        string? name,
        string? workingDirectory,
        KnownAgentKind? agentKind,
        CancellationToken cancellationToken = default);

    ValueTask OpenRenameAsync(
        ResourceKey target,
        ResourceProjectionStamp expected,
        string breadcrumb,
        string currentName,
        CancellationToken cancellationToken = default);

    ValueTask SubmitRenameAsync(
        ResourceConfirmationToken confirmation,
        string newName,
        CancellationToken cancellationToken = default);

    ValueTask OpenCloseAsync(
        ResourceKey target,
        ResourceProjectionStamp expected,
        string breadcrumb,
        CancellationToken cancellationToken = default);

    ValueTask ConfirmCloseAsync(
        ResourceConfirmationToken confirmation,
        bool closeGroup = false,
        CancellationToken cancellationToken = default);

    ValueTask NoteSelectionChangedAsync(
        ResourceKey? selected,
        CancellationToken cancellationToken = default);

    ValueTask CancelDraftAsync(CancellationToken cancellationToken = default);

    ValueTask CancelPendingWaitAsync(CancellationToken cancellationToken = default);

    ValueTask NotifyProjectionAsync(CancellationToken cancellationToken = default);

    ValueTask RefreshOutcomeAsync(CancellationToken cancellationToken = default);

    ValueTask RetryAfterVerifiedAbsentAsync(CancellationToken cancellationToken = default);
}

public static class VerifiedAgentWires
{
    public static string? Wire(KnownAgentKind kind) => kind switch
    {
        KnownAgentKind.Claude => "claude",
        KnownAgentKind.Codex => "codex",
        KnownAgentKind.OpenCode => "opencode",
        _ => null
    };

    public static bool IsVerified(KnownAgentKind kind) => Wire(kind) is not null;
}
