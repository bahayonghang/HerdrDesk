using System.Text.Json;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Rpc;

internal static class ResourceCommandSchemaTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("adapter maps verified create rename close fields", MapsVerifiedFields),
        ("adapter never writes env args or close_group by default", OmitsDangerousFields),
        ("adapter sends close_group only after explicit confirm", CloseGroupExplicit),
        ("adapter rejects unverified agent kind", RejectsUnknownAgent),
        ("snapshot query does not claim entity present", SnapshotQueryNotEntity)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static SessionKey Session() =>
        new(new DeviceId(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")), "local-api", "dev");

    static ResourceKey Workspace() => new(Session(), ResourceKind.Workspace, "w1", "t1", "p1", "term-1");

    static ResourceKey Pane() => new(Session(), ResourceKind.Pane, "w1", "t1", "p1", "term-1");

    static ResourceKey Agent() => new(Session(), ResourceKind.Agent, "w1", "t1", "p1", "term-1");

    static void MapsVerifiedFields()
    {
        var rpc = new RecordingRpc();
        var adapter = new ResourceCommandAdapter(rpc);
        rpc.ResultJson = """{"id":1,"result":{"workspace_id":"w2"}}""";
        var created = adapter.SubmitAsync(new CreateWorkspaceIntent(Session(), "lab", "/tmp/ws"))
            .AsTask().GetAwaiter().GetResult();
        Check(rpc.LastMethod == SchemaOperations.WorkspaceCreate);
        Check(rpc.LastParams!.Contains("\"label\":\"lab\"", StringComparison.Ordinal));
        Check(rpc.LastParams.Contains("\"cwd\":\"/tmp/ws\"", StringComparison.Ordinal));
        Check(!rpc.LastParams.Contains("env", StringComparison.Ordinal));
        Check(created.CreatedWorkspaceId == "w2");

        rpc.ResultJson = """{"id":2,"result":{"tab_id":"t2"}}""";
        adapter.SubmitAsync(new CreateTerminalIntent(Workspace(), "/tmp/sh", null))
            .AsTask().GetAwaiter().GetResult();
        Check(rpc.LastMethod == SchemaOperations.TabCreate);
        Check(rpc.LastParams!.Contains("\"workspace_id\":\"w1\"", StringComparison.Ordinal));
        Check(rpc.LastParams.Contains("\"cwd\":\"/tmp/sh\"", StringComparison.Ordinal));

        rpc.ResultJson = """{"id":3,"result":{"pane_id":"p1"}}""";
        adapter.SubmitAsync(new CreateAgentIntent(Agent(), KnownAgentKind.OpenCode, "worker"))
            .AsTask().GetAwaiter().GetResult();
        Check(rpc.LastMethod == SchemaOperations.AgentStart);
        Check(rpc.LastParams!.Contains("\"kind\":\"opencode\"", StringComparison.Ordinal));
        Check(rpc.LastParams.Contains("\"name\":\"worker\"", StringComparison.Ordinal));
        Check(rpc.LastParams.Contains("\"pane_id\":\"p1\"", StringComparison.Ordinal));
        Check(!rpc.LastParams.Contains("args", StringComparison.Ordinal));

        adapter.SubmitAsync(new RenameResourceIntent(Workspace(), "new-lab"))
            .AsTask().GetAwaiter().GetResult();
        Check(rpc.LastMethod == SchemaOperations.WorkspaceRename);
        Check(rpc.LastParams!.Contains("\"workspace_id\":\"w1\"", StringComparison.Ordinal));
        Check(rpc.LastParams.Contains("\"label\":\"new-lab\"", StringComparison.Ordinal));
    }

    static void OmitsDangerousFields()
    {
        var rpc = new RecordingRpc();
        var adapter = new ResourceCommandAdapter(rpc);
        adapter.SubmitAsync(new CloseResourceIntent(Workspace(), false))
            .AsTask().GetAwaiter().GetResult();
        Check(rpc.LastMethod == SchemaOperations.WorkspaceClose);
        Check(rpc.LastParams!.Contains("\"workspace_id\":\"w1\"", StringComparison.Ordinal));
        Check(!rpc.LastParams.Contains("close_group", StringComparison.Ordinal));
        Check(!rpc.LastParams.Contains("env", StringComparison.Ordinal));
        Check(!rpc.LastParams.Contains("args", StringComparison.Ordinal));
        adapter.SubmitAsync(new CloseResourceIntent(Pane(), false))
            .AsTask().GetAwaiter().GetResult();
        Check(rpc.LastMethod == SchemaOperations.PaneClose);
        Check(rpc.LastParams!.Contains("\"pane_id\":\"p1\"", StringComparison.Ordinal));
    }

    static void CloseGroupExplicit()
    {
        var rpc = new RecordingRpc();
        var adapter = new ResourceCommandAdapter(rpc);
        adapter.SubmitAsync(new CloseResourceIntent(Workspace(), true))
            .AsTask().GetAwaiter().GetResult();
        Check(rpc.LastMethod == SchemaOperations.WorkspaceClose);
        Check(rpc.LastParams!.Contains("\"close_group\":true", StringComparison.Ordinal));
    }

    static void RejectsUnknownAgent()
    {
        var rpc = new RecordingRpc();
        var adapter = new ResourceCommandAdapter(rpc);
        var receipt = adapter.SubmitAsync(new CreateAgentIntent(Agent(), (KnownAgentKind)99, "x"))
            .AsTask().GetAwaiter().GetResult();
        Check(receipt.Kind != ResourceTransportKind.Result);
        Check(rpc.LastMethod is null);
    }

    static void SnapshotQueryNotEntity()
    {
        var rpc = new RecordingRpc();
        var adapter = new ResourceCommandAdapter(rpc);
        rpc.ResultJson = """{"id":4,"result":{"focused_workspace_id":"w1","workspace_id":"w1"}}""";
        var queried = adapter.QueryAsync(new ResourceQueryRequest(Session(), ResourceQueryKind.Snapshot))
            .AsTask().GetAwaiter().GetResult();
        Check(rpc.LastMethod == SchemaOperations.SessionSnapshot);
        Check(!queried.EntityPresent);
        adapter.QueryAsync(new ResourceQueryRequest(Session(), ResourceQueryKind.Agent, "w1", "t1", "p1", "term-1"))
            .AsTask().GetAwaiter().GetResult();
        Check(rpc.LastMethod == SchemaOperations.AgentGet);
        Check(rpc.LastParams!.Contains("\"target\":\"term-1\"", StringComparison.Ordinal));
        Check(!rpc.LastParams.Contains("terminal_id", StringComparison.Ordinal));
    }
}

file sealed class RecordingRpc : IRpcRequestConnection
{
    public string? LastMethod { get; private set; }
    public string? LastParams { get; private set; }
    public string ResultJson { get; set; } = """{"id":1,"result":{}}""";
    public ConnectionEpoch Epoch { get; } = new(1);
    public int PendingCount => 0;
    public int? ChildProcessId => 1;
    public RpcFailure? Failure => null;
    public Task WhenCompleted => Task.CompletedTask;

    public ValueTask<RpcRequestOutcome> RequestAsync(
        string method,
        JsonElement parameters,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        LastMethod = method;
        LastParams = parameters.GetRawText();
        return ValueTask.FromResult(new RpcRequestOutcome(JsonDocument.Parse(ResultJson)));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
