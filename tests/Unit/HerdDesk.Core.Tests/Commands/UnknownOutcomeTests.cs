using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class UnknownOutcomeTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("timeout queries and does not resend mutation", TimeoutQueriesNoRetry),
        ("snapshot present does not complete create", SnapshotPresentDoesNotCompleteCreate),
        ("agent timeout queries agent get", AgentTimeoutQueriesAgentGet),
        ("late result after new epoch is ignored", LateResultIgnored),
        ("cancel after submit keeps unknown outcome", CancelAfterSubmitUnknown),
        ("store match completes create without a second send", StoreMatchCompletes),
        ("l2 live mutation remains unverified", L2Unverified)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void TimeoutQueriesNoRetry()
    {
        using var env = new ResourceCommandHarness();
        env.Transport.HoldSubmit = new TaskCompletionSource<ResourceTransportReceipt>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        env.Transport.QueryReceipt = new ResourceQueryReceipt(ResourceTransportKind.Result, false);
        env.Coordinator.OpenCreateAsync(
            ResourceOperationKind.CreateWorkspace, env.Workspace(), "dev / w1")
            .AsTask().GetAwaiter().GetResult();
        env.Coordinator.SubmitCreateAsync("lab-2", null, null).AsTask().GetAwaiter().GetResult();
        env.Wait(op => op.State == ResourceOperationState.Submitting);
        env.Time.Advance(TimeSpan.FromMilliseconds(80));
        env.Wait(op => op.State == ResourceOperationState.UnknownOutcome);
        env.Wait(op => env.Transport.Queries.Count >= 1);
        Check(env.Transport.Intents.Count == 1);
        Check(env.Coordinator.Current.QueryAfterTimeout);
        env.Wait(op => op.RetryAllowed);
        env.Coordinator.RetryAfterVerifiedAbsentAsync().AsTask().GetAwaiter().GetResult();
        Check(env.Coordinator.Current.State == ResourceOperationState.Validating);
        Check(env.Transport.Intents.Count == 1);
    }

    static void SnapshotPresentDoesNotCompleteCreate()
    {
        using var env = new ResourceCommandHarness();
        env.Transport.HoldSubmit = new TaskCompletionSource<ResourceTransportReceipt>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        env.Transport.QueryReceipt = new ResourceQueryReceipt(ResourceTransportKind.Result, true);
        env.Coordinator.OpenCreateAsync(
            ResourceOperationKind.CreateWorkspace, env.Workspace(), "dev / w1")
            .AsTask().GetAwaiter().GetResult();
        env.Coordinator.SubmitCreateAsync("lab-2", null, null).AsTask().GetAwaiter().GetResult();
        env.Wait(op => op.State == ResourceOperationState.Submitting);
        env.Time.Advance(TimeSpan.FromMilliseconds(80));
        env.Wait(op => op.State == ResourceOperationState.UnknownOutcome);
        env.Wait(op => env.Transport.Queries.Count >= 1);
        Check(env.Coordinator.Current.State != ResourceOperationState.Succeeded);
        Check(env.Coordinator.Current.RetryAllowed);
        Check(env.Transport.Intents.Count == 1);
        Check(env.Transport.Queries[0].Kind == ResourceQueryKind.Snapshot);
    }

    static void AgentTimeoutQueriesAgentGet()
    {
        using var env = new ResourceCommandHarness();
        env.Transport.HoldSubmit = new TaskCompletionSource<ResourceTransportReceipt>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        env.Coordinator.OpenCreateAsync(
            ResourceOperationKind.CreateAgent, env.AgentPane(), "dev / w1 / p1")
            .AsTask().GetAwaiter().GetResult();
        Check(env.Coordinator.Current.State == ResourceOperationState.Validating);
        env.Coordinator.SubmitCreateAsync("worker", null, KnownAgentKind.Codex)
            .AsTask().GetAwaiter().GetResult();
        env.Wait(op => op.State == ResourceOperationState.Submitting);
        env.Time.Advance(TimeSpan.FromMilliseconds(80));
        env.Wait(op => op.State == ResourceOperationState.UnknownOutcome);
        env.Wait(op => env.Transport.Queries.Count >= 1);
        Check(env.Transport.Queries[0].Kind == ResourceQueryKind.Agent);
        Check(env.Coordinator.Current.State != ResourceOperationState.Succeeded);
        Check(!env.Coordinator.Current.RetryAllowed);
        Check(env.Transport.Intents.Count == 1);
    }

    static void LateResultIgnored()
    {
        using var env = new ResourceCommandHarness();
        var hold = new TaskCompletionSource<ResourceTransportReceipt>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        env.Transport.HoldSubmit = hold;
        env.Coordinator.OpenCreateAsync(
            ResourceOperationKind.CreateWorkspace, env.Workspace(), "dev / w1")
            .AsTask().GetAwaiter().GetResult();
        var first = env.Coordinator.Current.CorrelationId;
        env.Coordinator.SubmitCreateAsync("lab-2", null, null).AsTask().GetAwaiter().GetResult();
        env.Wait(op => op.State == ResourceOperationState.Submitting);
        env.Time.Advance(TimeSpan.FromMilliseconds(80));
        env.Wait(op => op.State == ResourceOperationState.UnknownOutcome);
        Check(env.Coordinator.Current.CorrelationId == first);
        hold.TrySetResult(new ResourceTransportReceipt(
            ResourceTransportKind.Result, CreatedWorkspaceId: "w-late"));
        env.Coordinator.NotifyProjectionAsync().AsTask().GetAwaiter().GetResult();
        Check(env.Coordinator.Current.State == ResourceOperationState.UnknownOutcome);
        Check(env.Coordinator.Current.State is not ResourceOperationState.Succeeded);
    }

    static void CancelAfterSubmitUnknown()
    {
        using var env = new ResourceCommandHarness();
        env.Transport.HoldSubmit = new TaskCompletionSource<ResourceTransportReceipt>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        env.Coordinator.OpenCloseAsync(env.Workspace(), env.Stamp(), "dev / w1")
            .AsTask().GetAwaiter().GetResult();
        var token = env.Coordinator.Current.Confirmation!.Value;
        env.Coordinator.ConfirmCloseAsync(token).AsTask().GetAwaiter().GetResult();
        env.Wait(op => op.MutationSent);
        env.Coordinator.CancelPendingWaitAsync().AsTask().GetAwaiter().GetResult();
        Check(env.Coordinator.Current.State == ResourceOperationState.UnknownOutcome);
        Check(env.Coordinator.Current.Code != ResourceCommandCodes.Cancelled);
        Check(env.Transport.Intents.Count == 1);
    }

    static void StoreMatchCompletes()
    {
        using var env = new ResourceCommandHarness();
        env.Transport.SubmitReceipt = new ResourceTransportReceipt(
            ResourceTransportKind.Result, CreatedWorkspaceId: "w2");
        env.Coordinator.OpenCreateAsync(
            ResourceOperationKind.CreateWorkspace, env.Workspace(), "dev / w1")
            .AsTask().GetAwaiter().GetResult();
        env.Coordinator.SubmitCreateAsync("other", null, null).AsTask().GetAwaiter().GetResult();
        env.Wait(op => op.State == ResourceOperationState.Observing);
        env.Install(DeviceSessionGraphs.AfterCreateCloseStatus(env.Session, 1));
        env.Coordinator.NotifyProjectionAsync().AsTask().GetAwaiter().GetResult();
        env.Wait(op => op.State == ResourceOperationState.Succeeded);
        Check(env.Transport.Intents.Count == 1);
        Check(env.Coordinator.Current.ResultKey?.WorkspaceId == "w2");
    }

    static void L2Unverified()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HerdDesk.slnx")))
            directory = directory.Parent;
        Check(directory is not null);
        var json = File.ReadAllText(Path.Combine(directory!.FullName, "implementation", "hd-017-l2.json"));
        Check(json.Contains("\"l2_live_mutation\": \"UNVERIFIED\"", StringComparison.Ordinal));
        Check(json.Contains("\"ac20_passed\": false", StringComparison.Ordinal));
        Check(json.Contains("\"g0_passed\": false", StringComparison.Ordinal));
        Check(json.Contains("\"live_herdr\": false", StringComparison.Ordinal));
    }
}
