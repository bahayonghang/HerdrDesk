using HerdDesk.Contracts;
using HerdDesk.Core;

namespace HerdDesk.App;

public sealed class ResourceCommandViewModel
{
    private readonly ResourceCommandCoordinator _coordinator;
    private readonly ProjectionCatalog _catalog;
    private ResourceKey? _selection;

    public ResourceCommandViewModel(ResourceCommandCoordinator coordinator, ProjectionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(catalog);
        _coordinator = coordinator;
        _catalog = catalog;
    }

    public ResourceCommandCoordinator Coordinator => _coordinator;
    public ResourceOperation Operation => _coordinator.Current;
    public ResourceOperationState State => Operation.State;
    public bool AlwaysApproveEnabled => false;
    public bool HasGlobalBypass => false;
    public bool HasDoNotAskAgain => false;
    public bool CommandLineVisible => false;
    public bool ArgvVisible => false;
    public string Breadcrumb => Operation.Breadcrumb;
    public string? WorkingDirectoryDisplay => Operation.WorkingDirectoryDisplay;
    public KnownAgentKind[] AgentKinds { get; } =
        [KnownAgentKind.Claude, KnownAgentKind.Codex, KnownAgentKind.OpenCode];
    public string CreateWorkspaceAutomationName => ShellStrings.CreateWorkspace;
    public string CreateTerminalAutomationName => ShellStrings.CreateTerminal;
    public string CreateAgentAutomationName => ShellStrings.CreateAgent;
    public string RenameAutomationName => ShellStrings.RenameResource;
    public string CloseAutomationName => ShellStrings.CloseResource;
    public string ConfirmCloseAutomationName =>
        Operation.NeedsGroupClose ? ShellStrings.ConfirmGroupClose : ShellStrings.ConfirmClose;
    public string CancelAutomationName => ShellStrings.CancelEdit;
    public string SubmitAutomationName => ShellStrings.SubmitCreate;

    public bool WritesEnabled
    {
        get
        {
            var snapshot = StoreSnapshot();
            if (_selection is null)
                return false;
            return ResourceCommandGate.EvaluateWrite(
                snapshot, _selection.Session, SchemaOperations.WorkspaceCreate).Allowed;
        }
    }

    public string? WritesDisabledReason
    {
        get
        {
            if (WritesEnabled)
                return null;
            var snapshot = StoreSnapshot();
            if (snapshot.Projection.Phase is ConnectionPhase.Incompatible ||
                snapshot.Projection.Devices.Count > 0 &&
                snapshot.Projection.Devices[0].Capabilities.VerifiedOperations.Count == 0)
                return ShellStrings.SchemaIncompatible;
            if (snapshot.Projection.Phase is ConnectionPhase.Offline)
                return ShellStrings.OfflineWritesDisabled;
            if (snapshot.Projection.Phase is ConnectionPhase.Stale ||
                snapshot.Freshness is DeviceFreshness.Stale or DeviceFreshness.Unknown)
                return ShellStrings.StaleWritesDisabled;
            return ShellStrings.CapabilityUnknown;
        }
    }

    public bool CreateWorkspaceEnabled => Can(SchemaOperations.WorkspaceCreate);
    public bool CreateTerminalEnabled => Can(SchemaOperations.TabCreate);
    public bool CreateAgentEnabled => Can(SchemaOperations.AgentStart);
    public bool RenameEnabled => _selection is not null && Can(ResourceCommandGate.RenameOperation(_selection.Kind));
    public bool CloseEnabled => _selection is not null && Can(ResourceCommandGate.CloseOperation(_selection.Kind));
    public bool SubmitEnabled =>
        State is ResourceOperationState.Validating && Operation.Code is null && !CommandLineVisible;
    public bool ConfirmEnabled =>
        State is ResourceOperationState.AwaitingConfirmation && Operation.Confirmation is not null;
    public bool ShellProfileVisible => Operation.Kind == ResourceOperationKind.CreateTerminal;
    public bool AgentProfileVisible => Operation.Kind == ResourceOperationKind.CreateAgent;
    public string StatusLabel => State switch
    {
        ResourceOperationState.Succeeded => ShellStrings.Ready,
        ResourceOperationState.Failed => ShellStrings.Failed,
        ResourceOperationState.Cancelled => ShellStrings.CancelEdit,
        ResourceOperationState.UnknownOutcome => ShellStrings.UnknownOutcome,
        ResourceOperationState.StaleTarget => ShellStrings.StaleDialog,
        ResourceOperationState.Submitting or ResourceOperationState.Observing or ResourceOperationState.Validating
            or ResourceOperationState.AwaitingConfirmation => ShellStrings.Loading,
        _ => ShellStrings.Ready
    };

    public string? ErrorText => Operation.Code switch
    {
        ResourceCommandCodes.WorkspaceGroupCloseRequired
            or ResourceCommandCodes.CloseGroupUnconfirmed => ShellStrings.GroupCloseRequired,
        ResourceCommandCodes.StaleTarget or ResourceCommandCodes.StaleConfirmation => ShellStrings.StaleDialog,
        ResourceCommandCodes.SchemaIncompatible => ShellStrings.SchemaIncompatible,
        ResourceCommandCodes.CapabilityUnknown => ShellStrings.CapabilityUnknown,
        ResourceCommandCodes.UnknownOutcome or ResourceCommandCodes.Timeout => ShellStrings.UnknownOutcome,
        null => null,
        _ => Operation.Code
    };

    public void HandleSelectionChanged(ResourceKey? key)
    {
        _selection = key;
        _coordinator.NoteSelectionChangedAsync(key).AsTask().GetAwaiter().GetResult();
        _coordinator.NotifyProjectionAsync().AsTask().GetAwaiter().GetResult();
    }

    public void OpenCreateWorkspace(ResourceKey parent, string breadcrumb) =>
        OpenCreate(ResourceOperationKind.CreateWorkspace, parent, breadcrumb);

    public void OpenCreateTerminal(ResourceKey parent, string breadcrumb) =>
        OpenCreate(ResourceOperationKind.CreateTerminal, parent, breadcrumb);

    public void OpenCreateAgent(ResourceKey parent, string breadcrumb) =>
        OpenCreate(ResourceOperationKind.CreateAgent, parent, breadcrumb);

    public void OpenCreateWorkspaceFromKeyboard(ResourceKey parent, string breadcrumb) =>
        OpenCreateWorkspace(parent, breadcrumb);

    public void OpenCreateWorkspaceFromScreenReader(ResourceKey parent, string breadcrumb) =>
        OpenCreateWorkspace(parent, breadcrumb);

    public void OpenCreateTerminalFromKeyboard(ResourceKey parent, string breadcrumb) =>
        OpenCreateTerminal(parent, breadcrumb);

    public void OpenCreateAgentFromScreenReader(ResourceKey parent, string breadcrumb) =>
        OpenCreateAgent(parent, breadcrumb);

    public void ValidateDraft(string? name, string? workingDirectory, KnownAgentKind? agentKind) =>
        _coordinator.ValidateDraftAsync(name, workingDirectory, agentKind).AsTask().GetAwaiter().GetResult();

    public void SubmitCreate(string? name, string? workingDirectory, KnownAgentKind? agentKind) =>
        _coordinator.SubmitCreateAsync(name, workingDirectory, agentKind).AsTask().GetAwaiter().GetResult();

    public void SubmitCreateFromKeyboard(string? name, string? workingDirectory, KnownAgentKind? agentKind) =>
        SubmitCreate(name, workingDirectory, agentKind);

    public void SubmitCreateFromScreenReader(string? name, string? workingDirectory, KnownAgentKind? agentKind) =>
        SubmitCreate(name, workingDirectory, agentKind);

    public void OpenRename(ResourceKey target, ResourceProjectionStamp expected, string breadcrumb, string currentName) =>
        _coordinator.OpenRenameAsync(target, expected, breadcrumb, currentName).AsTask().GetAwaiter().GetResult();

    public void SubmitRename(string newName)
    {
        if (Operation.Confirmation is not { } token)
            return;
        _coordinator.SubmitRenameAsync(token, newName).AsTask().GetAwaiter().GetResult();
    }

    public void SubmitRenameFromKeyboard(string newName) => SubmitRename(newName);

    public void OpenClose(ResourceKey target, ResourceProjectionStamp expected, string breadcrumb) =>
        _coordinator.OpenCloseAsync(target, expected, breadcrumb).AsTask().GetAwaiter().GetResult();

    public void ConfirmClose(bool closeGroup = false)
    {
        if (Operation.Confirmation is not { } token)
            return;
        if (closeGroup && !Operation.NeedsGroupClose)
            return;
        _coordinator.ConfirmCloseAsync(token, closeGroup).AsTask().GetAwaiter().GetResult();
    }

    public void ConfirmCloseFromKeyboard() => ConfirmClose();

    public void ConfirmCloseFromScreenReader() => ConfirmClose();

    public void ConfirmGroupCloseFromKeyboard() => ConfirmClose(true);

    public void ConfirmGroupCloseFromScreenReader() => ConfirmClose(true);

    public void CancelDraft() =>
        _coordinator.CancelDraftAsync().AsTask().GetAwaiter().GetResult();

    public void CancelPendingWait() =>
        _coordinator.CancelPendingWaitAsync().AsTask().GetAwaiter().GetResult();

    public void RefreshOutcome() =>
        _coordinator.RefreshOutcomeAsync().AsTask().GetAwaiter().GetResult();

    public void RetryAfterVerifiedAbsent() =>
        _coordinator.RetryAfterVerifiedAbsentAsync().AsTask().GetAwaiter().GetResult();

    public ResourceProjectionStamp CurrentStamp()
    {
        var snapshot = _catalog.Snapshot;
        return new ResourceProjectionStamp(snapshot.Epoch, snapshot.Revision);
    }

    void OpenCreate(ResourceOperationKind kind, ResourceKey parent, string breadcrumb) =>
        _coordinator.OpenCreateAsync(kind, parent, breadcrumb).AsTask().GetAwaiter().GetResult();

    bool Can(string operation)
    {
        if (_selection is null)
            return false;
        return ResourceCommandGate.EvaluateWrite(StoreSnapshot(), _selection.Session, operation).Allowed;
    }

    ResourceStoreSnapshot StoreSnapshot() =>
        new(_catalog.Snapshot, _catalog.Freshness);
}
