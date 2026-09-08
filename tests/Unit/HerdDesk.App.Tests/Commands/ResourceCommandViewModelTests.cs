using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class ResourceCommandViewModelTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("form disables writes when schema is unknown", UnknownSchemaDisables),
        ("unknown freshness disables writes", UnknownFreshnessDisables),
        ("shell and agent dialogs stay split without bypass", ShellAgentSplitNoBypass),
        ("keyboard and screen reader share close confirm", KeyboardScreenReader),
        ("cancel draft does not submit", CancelDraft)
    ];

    static void UnknownSchemaDisables()
    {
        using var env = new AppResourceEnv(incompatible: true);
        AppTestHost.Check(!env.Vm.WritesEnabled);
        AppTestHost.Check(env.Vm.WritesDisabledReason == ShellStrings.SchemaIncompatible);
        AppTestHost.Check(!env.Vm.CreateWorkspaceEnabled);
        AppTestHost.Check(!env.Vm.CreateTerminalEnabled);
        AppTestHost.Check(!env.Vm.CreateAgentEnabled);
    }

    static void UnknownFreshnessDisables()
    {
        using var env = new AppResourceEnv(unknownFreshness: true);
        AppTestHost.Check(!env.Vm.WritesEnabled);
        AppTestHost.Check(env.Vm.WritesDisabledReason == ShellStrings.StaleWritesDisabled);
        AppTestHost.Check(!env.Vm.CreateWorkspaceEnabled);
    }

    static void ShellAgentSplitNoBypass()
    {
        using var env = new AppResourceEnv();
        env.Vm.OpenCreateTerminal(env.Workspace, "dev / w1");
        AppTestHost.Check(env.Vm.ShellProfileVisible);
        AppTestHost.Check(!env.Vm.AgentProfileVisible);
        AppTestHost.Check(!env.Vm.CommandLineVisible);
        AppTestHost.Check(!env.Vm.ArgvVisible);
        AppTestHost.Check(!env.Vm.AlwaysApproveEnabled);
        AppTestHost.Check(!env.Vm.HasGlobalBypass);
        AppTestHost.Check(!env.Vm.HasDoNotAskAgain);
        env.Vm.OpenCreateAgent(env.Agent, "dev / w1 / p1");
        AppTestHost.Check(env.Vm.AgentProfileVisible);
        AppTestHost.Check(!env.Vm.ShellProfileVisible);
        AppTestHost.Check(env.Vm.AgentKinds.Length == 3);
        AppTestHost.Check(env.Vm.AgentKinds.Contains(KnownAgentKind.Claude));
        AppTestHost.Check(env.Vm.AgentKinds.Contains(KnownAgentKind.Codex));
        AppTestHost.Check(env.Vm.AgentKinds.Contains(KnownAgentKind.OpenCode));
        AppTestHost.Check(!env.Vm.AgentKinds.Select(item => item.ToString()).Contains("Muse"));
        env.Vm.ValidateDraft("worker", "/tmp/agent", KnownAgentKind.Claude);
        AppTestHost.Check(env.Vm.WorkingDirectoryDisplay == "/tmp/agent");
        AppTestHost.Check(env.Vm.Breadcrumb == "dev / w1 / p1");
    }

    static void KeyboardScreenReader()
    {
        using var env = new AppResourceEnv();
        env.Vm.OpenClose(env.Workspace, env.Stamp, "dev / w1");
        AppTestHost.Check(env.Vm.ConfirmEnabled);
        AppTestHost.Check(env.Vm.ConfirmCloseAutomationName == ShellStrings.ConfirmClose);
        env.Vm.ConfirmCloseFromKeyboard();
        env.Wait(op => op.MutationSent || op.State is ResourceOperationState.Observing);
        AppTestHost.Check(env.Transport.Intents.Count == 1);
        AppTestHost.Check(!((CloseResourceIntent)env.Transport.Intents[0]).CloseGroup);
        AppTestHost.Check(env.Vm.CreateWorkspaceAutomationName == ShellStrings.CreateWorkspace);
        AppTestHost.Check(env.Vm.CreateTerminalAutomationName == ShellStrings.CreateTerminal);
        AppTestHost.Check(env.Vm.CreateAgentAutomationName == ShellStrings.CreateAgent);
    }

    static void CancelDraft()
    {
        using var env = new AppResourceEnv();
        env.Vm.OpenCreateWorkspaceFromScreenReader(env.Workspace, "dev / w1");
        env.Vm.CancelDraft();
        AppTestHost.Check(env.Vm.State == ResourceOperationState.Cancelled);
        AppTestHost.Check(env.Transport.Intents.Count == 0);
        AppTestHost.Check(!env.Vm.HasDoNotAskAgain);
    }
}

file sealed class AppResourceStore : IResourceProjectionStore
{
    public ResourceStoreSnapshot Snapshot { get; set; } = new(DeviceProjectionSnapshot.Empty, DeviceFreshness.Unknown);

    public ResourceStoreSnapshot Read() => Snapshot;
}

file sealed class AppResourceTransport : IResourceCommandTransport, IResourceQueryTransport
{
    public List<ResourceIntent> Intents { get; } = [];

    public ValueTask<ResourceTransportReceipt> SubmitAsync(
        ResourceIntent intent,
        CancellationToken cancellationToken = default)
    {
        Intents.Add(intent);
        return ValueTask.FromResult(new ResourceTransportReceipt(ResourceTransportKind.Result));
    }

    public ValueTask<ResourceQueryReceipt> QueryAsync(
        ResourceQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        _ = (query, cancellationToken);
        return ValueTask.FromResult(new ResourceQueryReceipt(ResourceTransportKind.Result, false));
    }
}

file sealed class AppResourceEnv : IDisposable
{
    public AppResourceStore Store { get; } = new();
    public AppResourceTransport Transport { get; } = new();
    public ResourceCommandCoordinator Coordinator { get; }
    public ResourceCommandViewModel Vm { get; }
    public SessionKey Session { get; } = AppTestHost.SessionOf(AppTestHost.DeviceA);
    public ResourceKey Workspace { get; }
    public ResourceKey Agent { get; }
    public ResourceProjectionStamp Stamp { get; }

    public AppResourceEnv(bool incompatible = false, bool unknownFreshness = false)
    {
        var capabilities = incompatible ? AppTestHost.Incompatible() : AppTestHost.Compatible();
        var session = Session;
        Workspace = new ResourceKey(session, ResourceKind.Workspace, "w1", "t1", "p1", "term-1");
        Agent = new ResourceKey(session, ResourceKind.Agent, "w1", "t1", "p1", "term-1");
        var pane = AppTestHost.Pane(new PaneKey(session, "w1", "p1"));
        var workspace = AppTestHost.Workspace(session, "w1", "lab", 1);
        var projected = new ProjectedDevice(
            session.Device, capabilities,
            [AppTestHost.SessionState(session, [workspace], [pane])]);
        var snapshot = new DeviceProjectionSnapshot(
            new ConnectionEpoch(1), 1,
            incompatible ? ConnectionPhase.Incompatible : ConnectionPhase.Ready,
            [projected]);
        var freshness = incompatible || unknownFreshness ? DeviceFreshness.Unknown : DeviceFreshness.Current;
        Store.Snapshot = new ResourceStoreSnapshot(snapshot, freshness);
        Stamp = new ResourceProjectionStamp(snapshot.Epoch, snapshot.Revision);
        Coordinator = new ResourceCommandCoordinator(Store, Transport, Transport);
        var catalog = new ProjectionCatalog
        {
            Snapshot = snapshot,
            Freshness = freshness,
            DaemonAvailable = true
        };
        Vm = new ResourceCommandViewModel(Coordinator, catalog);
        Vm.HandleSelectionChanged(Workspace);
    }

    public void Wait(Func<ResourceOperation, bool> pred)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        WaitAsync(pred, cts.Token).GetAwaiter().GetResult();
    }

    async Task WaitAsync(Func<ResourceOperation, bool> pred, CancellationToken cancellationToken)
    {
        if (pred(Coordinator.Current))
            return;
        await foreach (var _ in Coordinator.ReadStatesAsync(cancellationToken).ConfigureAwait(false))
        {
            if (pred(Coordinator.Current))
                return;
        }

        throw new Exception("wait_timeout");
    }

    public void Dispose() => Coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
}
