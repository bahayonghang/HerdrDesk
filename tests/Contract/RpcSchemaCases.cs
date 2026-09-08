using System.Text;
using System.Text.Json;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Rpc.SchemaV1;

internal static class RpcSchemaCases
{
    public static (string Name, Action Run)[] All =>
    [
        ("rpc schema valid snapshot maps and survives document dispose", ValidSnapshotOwnedClone),
        ("rpc schema unknown field and enum remain after dispose", UnknownFieldAndEnumOwned),
        ("rpc schema required missing wrong type duplicate and error fail closed", FailClosedCases),
        ("rpc schema duplicate identity and dangling parent reject the graph", IdentityFailures),
        ("rpc schema hash mismatch leaves mutation operations empty", HashMismatchNoMutation),
        ("rpc schema event unknown kind keeps raw value", UnknownEventKind),
        ("rpc schema errors omit raw json titles cwd and endpoint", ErrorsAreRedacted),
        ("rpc schema entity getter unwraps workspace payload", EntityGetterWorkspace)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static SessionKey Session() =>
        new(new DeviceId(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")), "local-api", "dev");

    static SchemaCompatibilityBinding Unverified() =>
        SchemaCompatibilityBinding.PinnedUnverified("0.9.0");

    static SchemaCompatibilityBinding Matching() =>
        SchemaCompatibilityBinding.PinnedMatchingRuntimeForTests("0.9.0");

    static RpcStateDecoder Decoder() => new();

    static string FixtureDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "tests", "fixtures", "rpc-schema-v1");
            if (Directory.Exists(path))
                return path;
        }
        throw new Exception("rpc_schema_fixture_missing");
    }

    static byte[] ReadFixture(string name) =>
        File.ReadAllBytes(Path.Combine(FixtureDir(), name));

    static void ValidSnapshotOwnedClone()
    {
        var decoder = Decoder();
        using var document = JsonDocument.Parse(ReadFixture("snapshot-valid.json"));
        var decoded = decoder.DecodeSnapshot(document.RootElement, Session(), new ConnectionEpoch(1), Unverified());
        Check(decoded.Succeeded);
        document.Dispose();
        Check(decoded.Value!.Version == "0.9.0");
        Check(decoded.Value.Protocol == 22);
        Check(decoded.Value.Workspaces[0].WorkspaceId == "w1");
        Check(decoded.Value.Panes[0].AgentKind == KnownAgentKind.Claude);
        var capabilities = CapabilityGate.Evaluate(Unverified(), decoded.Value.Protocol, decoded.Value.Version);
        Check(!capabilities.HasMutationControl);
        var mapped = ProjectionMapper.MapSnapshot(decoded.Value, capabilities);
        Check(mapped.Succeeded);
        Check(mapped.Graph!.Phase == ConnectionPhase.Incompatible);
        var store = new DeviceProjectionStore();
        Check(store.InstallSnapshot(new ConnectionEpoch(1), mapped.Graph).Succeeded);
        Check(store.Read().Devices[0].Sessions[0].Panes[0].Key.PaneId == "w1:p1");
    }

    static void UnknownFieldAndEnumOwned()
    {
        var decoder = Decoder();
        using var document = JsonDocument.Parse(ReadFixture("snapshot-unknown.json"));
        var decoded = decoder.DecodeSnapshot(document.RootElement, Session(), new ConnectionEpoch(1), Matching());
        Check(decoded.Succeeded);
        document.Dispose();
        Check(decoded.Value!.Extensions["future_snapshot_field"].GetProperty("keep").GetBoolean());
        Check(decoded.Value.Workspaces[0].Extensions["future_workspace_field"].GetString() == "owned-clone");
        Check(decoded.Value.Workspaces[0].AgentStatus.Raw == "dreaming");
        Check(decoded.Value.Workspaces[0].AgentStatus.Known is null);
        Check(decoded.Value.Panes[0].AgentRaw == "muse");
        Check(decoded.Value.Panes[0].AgentKind is null);
        Check(decoded.Value.Agents[0].AgentRaw == "qwen");
        Check(decoded.Value.Agents[0].AgentKind is null);
        var capabilities = CapabilityGate.Evaluate(Matching(), decoded.Value.Protocol, decoded.Value.Version);
        Check(capabilities.HasMutationControl);
        Check(!capabilities.HasOperation("pane.graphics.set"));
        var mapped = ProjectionMapper.MapSnapshot(decoded.Value, capabilities);
        Check(mapped.Succeeded);
        Check(mapped.Graph!.SessionState.Panes[0].AgentKind is null);
        Check(mapped.Graph.SessionState.Workspaces[0].AgentStatus.Raw == "dreaming");
    }

    static void FailClosedCases()
    {
        using var cases = JsonDocument.Parse(ReadFixture("cases.json"));
        var decoder = Decoder();
        foreach (var item in cases.RootElement.GetProperty("cases").EnumerateArray())
        {
            var name = item.GetProperty("name").GetString()!;
            if (name == "unknown_event")
                continue;
            var expect = item.GetProperty("expect").GetString()!;
            var raw = Encoding.UTF8.GetBytes(item.GetProperty("raw").GetString()!);
            var decoded = decoder.DecodeSnapshotBytes(raw, Session(), new ConnectionEpoch(1), Unverified());
            Check(!decoded.Succeeded);
            Check(decoded.Code == expect);
            Check(decoded.Value is null);
        }
    }

    static void IdentityFailures()
    {
        var session = Session();
        var decoder = Decoder();
        var valid = decoder.DecodeSnapshotBytes(
            ReadFixture("snapshot-valid.json"), session, new ConnectionEpoch(1), Matching());
        Check(valid.Succeeded);
        var duplicate = valid.Value! with
        {
            Panes = [valid.Value.Panes[0], valid.Value.Panes[0]]
        };
        var mappedDuplicate = ProjectionMapper.MapSnapshot(duplicate, CapabilityGate.Evaluate(
            Matching(), 22, "0.9.0"));
        Check(!mappedDuplicate.Succeeded);
        Check(mappedDuplicate.Code == ProjectionCodes.DuplicateIdentity);

        var dangling = valid.Value with
        {
            Panes =
            [
                valid.Value.Panes[0] with { WorkspaceId = "missing" }
            ]
        };
        var mappedDangling = ProjectionMapper.MapSnapshot(dangling, CapabilityGate.Evaluate(
            Matching(), 22, "0.9.0"));
        Check(!mappedDangling.Succeeded);
        Check(mappedDangling.Code == ProjectionCodes.ParentMissing);
    }

    static void HashMismatchNoMutation()
    {
        var binding = new SchemaCompatibilityBinding(
            SchemaCompatibilityBinding.PinnedProtocol,
            SchemaCompatibilityBinding.PinnedSchemaVersion,
            SchemaCompatibilityBinding.PinnedDocumentSha256,
            SchemaCompatibilityBinding.PinnedProvenance,
            "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff",
            "0.9.0");
        var decoder = Decoder();
        var decoded = decoder.DecodeSnapshotBytes(
            ReadFixture("snapshot-valid.json"), Session(), new ConnectionEpoch(1), binding);
        Check(decoded.Succeeded);
        var profile = CapabilityGate.Evaluate(binding, decoded.Value!.Protocol, decoded.Value.Version);
        Check(profile.VerifiedOperations.Count == 0);
        Check(!profile.HasMutationControl);
        var mapped = ProjectionMapper.MapSnapshot(decoded.Value, profile);
        Check(mapped.Graph!.Phase == ConnectionPhase.Incompatible);
    }

    static void UnknownEventKind()
    {
        using var cases = JsonDocument.Parse(ReadFixture("cases.json"));
        var raw = cases.RootElement.GetProperty("cases").EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == "unknown_event")
            .GetProperty("raw").GetString()!;
        var decoder = Decoder();
        using var document = JsonDocument.Parse(raw);
        var decoded = decoder.DecodeEvent(document.RootElement, Session(), new ConnectionEpoch(1), Unverified());
        Check(decoded.Succeeded);
        document.Dispose();
        Check(decoded.Value!.Event.Raw == "pane_future_signal");
        Check(decoded.Value.Event.Known is null);
        Check(decoded.Value.Extensions["future_flag"].GetBoolean());

        var created = decoder.DecodeEventBytes(
            ReadFixture("event-workspace-created.json"), Session(), new ConnectionEpoch(1), Unverified());
        Check(created.Succeeded);
        Check(created.Value!.Event.Known == RpcEventKind.WorkspaceCreated);
        Check(created.Value.Workspace!.WorkspaceId == "w2");
        Check(created.Value.Workspace.Extensions["close_group"].GetBoolean());
    }

    static void ErrorsAreRedacted()
    {
        var decoder = Decoder();
        var raw = """{"id":"1","error":{"code":"denied","message":"/secret/cwd title endpoint"}}"""u8.ToArray();
        var decoded = decoder.DecodeSnapshotBytes(raw, Session(), new ConnectionEpoch(1), Unverified());
        Check(!decoded.Succeeded);
        Check(decoded.Code == ProjectionCodes.ErrorEnvelope);
        Check(!decoded.Code!.Contains("/secret", StringComparison.Ordinal));
        Check(!decoded.Code.Contains("title", StringComparison.Ordinal));
        Check(!decoded.Code.Contains('{'));
        Check(!decoded.Code.Contains("local-api", StringComparison.Ordinal));
    }

    static void EntityGetterWorkspace()
    {
        using var snapshot = JsonDocument.Parse(ReadFixture("snapshot-valid.json"));
        var workspace = snapshot.RootElement.GetProperty("result").GetProperty("snapshot").GetProperty("workspaces")[0];
        using var envelope = JsonDocument.Parse(
            """{"result":{"workspace":""" + workspace.GetRawText() + "}}");
        var decoded = Decoder().DecodeEntityRead(
            "workspace.get", envelope.RootElement, Session(), new ConnectionEpoch(1), Matching());
        Check(decoded.Succeeded);
        Check(decoded.Value!.Workspaces[0].WorkspaceId == "w1");
        Check(decoded.Value.Tabs.Count == 0);
        var unknown = Decoder().DecodeEntityRead(
            "future.get", envelope.RootElement, Session(), new ConnectionEpoch(1), Matching());
        Check(!unknown.Succeeded);
        Check(unknown.Code == ProjectionCodes.FullSnapshotRequired);
    }
}
