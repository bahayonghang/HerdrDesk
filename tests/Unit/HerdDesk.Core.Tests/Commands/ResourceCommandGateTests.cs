using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class ResourceCommandGateTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("unknown protocol disables resource writes", UnknownProtocolDisablesWrites),
        ("open create agent stays validating before name", OpenAgentValidating),
        ("create workspace shell and agent stay distinct without argv", DistinctCreates),
        ("muse agent kind is rejected", MuseRejected),
        ("rename and close reject stale confirmation after target switch", StaleDialogRejected),
        ("close omits group flag until explicit confirm", CloseGroupNotDefault),
        ("group close required does not auto resend", GroupCloseRequiredNoAutoRetry),
        ("cancel draft does not send", CancelDraftNoSend),
        ("double submit is rejected", DoubleSubmitRejected)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void UnknownProtocolDisablesWrites()
    {
        using var env = new ResourceCommandHarness(ResourceCommandHarness.Incompatible());
        env.Coordinator.OpenCreateAsync(
            ResourceOperationKind.CreateWorkspace, env.Workspace(), "dev / w1")
            .AsTask().GetAwaiter().GetResult();
        Check(env.Coordinator.Current.State == ResourceOperationState.Failed);
        Check(env.Coordinator.Current.Code == ResourceCommandCodes.SchemaIncompatible);
        Check(env.Transport.Intents.Count == 0);
        var gate = ResourceCommandGate.EvaluateWrite(
            env.Store.Snapshot, env.Session, SchemaOperations.WorkspaceCreate);
        Check(!gate.Allowed);
        Check(gate.Code == ResourceCommandCodes.SchemaIncompatible);
    }

    static void OpenAgentValidating()
    {
        using var env = new ResourceCommandHarness();
        env.Coordinator.OpenCreateAsync(
            ResourceOperationKind.CreateAgent, env.AgentPane(), "dev / w1 / p1")
            .AsTask().GetAwaiter().GetResult();
        Check(env.Coordinator.Current.State == ResourceOperationState.Validating);
        Check(env.Coordinator.Current.Code is null);
        Check(env.Transport.Intents.Count == 0);
    }

    static void DistinctCreates()
    {
        using var env = new ResourceCommandHarness();
        env.Coordinator.OpenCreateAsync(
            ResourceOperationKind.CreateWorkspace, env.Workspace(), "dev / w1")
            .AsTask().GetAwaiter().GetResult();
        env.Coordinator.SubmitCreateAsync("lab-2", "/tmp/ws", null).AsTask().GetAwaiter().GetResult();
        env.Wait(op => op.State is ResourceOperationState.Observing or ResourceOperationState.Succeeded);
        Check(env.Transport.Intents.Count == 1);
        Check(env.Transport.Intents[0] is CreateWorkspaceIntent);
        Check(((CreateWorkspaceIntent)env.Transport.Intents[0]).WorkingDirectory == "/tmp/ws");
        Check(((CreateWorkspaceIntent)env.Transport.Intents[0]).Name == "lab-2");

        using var shell = new ResourceCommandHarness();
        shell.Coordinator.OpenCreateAsync(
            ResourceOperationKind.CreateTerminal, shell.Workspace(), "dev / w1")
            .AsTask().GetAwaiter().GetResult();
        shell.Coordinator.SubmitCreateAsync(null, "/tmp/shell", null).AsTask().GetAwaiter().GetResult();
        shell.Wait(op => op.State is ResourceOperationState.Observing or ResourceOperationState.Succeeded);
        Check(shell.Transport.Intents[0] is CreateTerminalIntent);
        Check(((CreateTerminalIntent)shell.Transport.Intents[0]).WorkingDirectory == "/tmp/shell");

        using var agent = new ResourceCommandHarness();
        agent.Coordinator.OpenCreateAsync(
            ResourceOperationKind.CreateAgent, agent.AgentPane(), "dev / w1 / p1")
            .AsTask().GetAwaiter().GetResult();
        agent.Coordinator.SubmitCreateAsync("worker", "/tmp/agent", KnownAgentKind.Codex)
            .AsTask().GetAwaiter().GetResult();
        agent.Wait(op => op.State is ResourceOperationState.Observing or ResourceOperationState.Succeeded);
        var intent = (CreateAgentIntent)agent.Transport.Intents[0];
        Check(intent.AgentKind == KnownAgentKind.Codex);
        Check(intent.Name == "worker");
        Check(intent.GetType().GetProperty("Args") is null);
        Check(intent.GetType().GetProperty("Command") is null);
        Check(!Enum.GetNames<KnownAgentKind>().Contains("Muse"));
    }

    static void MuseRejected()
    {
        using var env = new ResourceCommandHarness();
        env.Coordinator.OpenCreateAsync(
            ResourceOperationKind.CreateAgent, env.AgentPane(), "dev / w1 / p1")
            .AsTask().GetAwaiter().GetResult();
        env.Coordinator.SubmitCreateAsync("muse", null, (KnownAgentKind)42)
            .AsTask().GetAwaiter().GetResult();
        Check(env.Coordinator.Current.State == ResourceOperationState.Failed);
        Check(env.Coordinator.Current.Code == ResourceCommandCodes.AgentKindUnverified);
        Check(env.Transport.Intents.Count == 0);
    }

    static void StaleDialogRejected()
    {
        using var env = new ResourceCommandHarness();
        env.Coordinator.OpenRenameAsync(
            env.Workspace(), env.Stamp(), "dev / w1", "lab")
            .AsTask().GetAwaiter().GetResult();
        var token = env.Coordinator.Current.Confirmation!.Value;
        env.Coordinator.NoteSelectionChangedAsync(env.Pane()).AsTask().GetAwaiter().GetResult();
        Check(env.Coordinator.Current.State == ResourceOperationState.StaleTarget);
        env.Coordinator.SubmitRenameAsync(token, "renamed").AsTask().GetAwaiter().GetResult();
        Check(env.Transport.Intents.Count == 0);
        Check(env.Coordinator.Current.Code is ResourceCommandCodes.StaleTarget
            or ResourceCommandCodes.StaleConfirmation);
    }

    static void CloseGroupNotDefault()
    {
        using var env = new ResourceCommandHarness();
        env.Coordinator.OpenCloseAsync(env.Workspace(), env.Stamp(), "dev / w1")
            .AsTask().GetAwaiter().GetResult();
        var token = env.Coordinator.Current.Confirmation!.Value;
        Check(!env.Coordinator.Current.CloseGroup);
        env.Coordinator.ConfirmCloseAsync(token).AsTask().GetAwaiter().GetResult();
        env.Wait(op => op.MutationSent || op.State is ResourceOperationState.Observing
            or ResourceOperationState.Succeeded or ResourceOperationState.Failed);
        Check(env.Transport.Intents.Count == 1);
        var close = (CloseResourceIntent)env.Transport.Intents[0];
        Check(!close.CloseGroup);
    }

    static void GroupCloseRequiredNoAutoRetry()
    {
        using var env = new ResourceCommandHarness();
        env.Transport.SubmitReceipt = new ResourceTransportReceipt(
            ResourceTransportKind.ApplicationError, ResourceCommandCodes.WorkspaceGroupCloseRequired);
        env.Coordinator.OpenCloseAsync(env.Workspace(), env.Stamp(), "dev / w1")
            .AsTask().GetAwaiter().GetResult();
        var token = env.Coordinator.Current.Confirmation!.Value;
        env.Coordinator.ConfirmCloseAsync(token).AsTask().GetAwaiter().GetResult();
        env.Wait(op => op.NeedsGroupClose);
        Check(env.Transport.Intents.Count == 1);
        Check(!((CloseResourceIntent)env.Transport.Intents[0]).CloseGroup);
        env.Coordinator.ConfirmCloseAsync(token).AsTask().GetAwaiter().GetResult();
        Check(env.Transport.Intents.Count == 1);
        Check(env.Coordinator.Current.Code == ResourceCommandCodes.CloseGroupUnconfirmed);
        env.Coordinator.ConfirmCloseAsync(token, true).AsTask().GetAwaiter().GetResult();
        env.Wait(op => env.Transport.Intents.Count >= 2 ||
            op.State is ResourceOperationState.Observing or ResourceOperationState.Submitting);
        Check(env.Transport.Intents.Count == 2);
        Check(((CloseResourceIntent)env.Transport.Intents[1]).CloseGroup);
    }

    static void CancelDraftNoSend()
    {
        using var env = new ResourceCommandHarness();
        env.Coordinator.OpenCreateAsync(
            ResourceOperationKind.CreateWorkspace, env.Workspace(), "dev / w1")
            .AsTask().GetAwaiter().GetResult();
        env.Coordinator.CancelDraftAsync().AsTask().GetAwaiter().GetResult();
        Check(env.Coordinator.Current.State == ResourceOperationState.Cancelled);
        Check(env.Transport.Intents.Count == 0);
    }

    static void DoubleSubmitRejected()
    {
        using var env = new ResourceCommandHarness();
        env.Transport.HoldSubmit = new TaskCompletionSource<ResourceTransportReceipt>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        env.Coordinator.OpenCreateAsync(
            ResourceOperationKind.CreateWorkspace, env.Workspace(), "dev / w1")
            .AsTask().GetAwaiter().GetResult();
        env.Coordinator.SubmitCreateAsync("one", null, null).AsTask().GetAwaiter().GetResult();
        env.Wait(op => op.State == ResourceOperationState.Submitting);
        env.Coordinator.SubmitCreateAsync("two", null, null).AsTask().GetAwaiter().GetResult();
        Check(env.Coordinator.Current.Code == ResourceCommandCodes.MutationAlreadySent);
        Check(env.Transport.Intents.Count == 1);
        env.Transport.HoldSubmit.TrySetResult(new ResourceTransportReceipt(ResourceTransportKind.Result));
    }
}
