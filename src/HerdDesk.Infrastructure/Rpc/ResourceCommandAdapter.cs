using System.Text.Json;
using HerdDesk.Contracts;
using HerdDesk.Core;

namespace HerdDesk.Infrastructure.Rpc;

public sealed class ResourceCommandAdapter : IResourceCommandTransport, IResourceQueryTransport
{
    private static readonly HashSet<string> AllowedMethods = new(StringComparer.Ordinal)
    {
        SchemaOperations.WorkspaceCreate,
        SchemaOperations.WorkspaceRename,
        SchemaOperations.WorkspaceClose,
        SchemaOperations.TabCreate,
        SchemaOperations.TabRename,
        SchemaOperations.TabClose,
        SchemaOperations.PaneRename,
        SchemaOperations.PaneClose,
        SchemaOperations.AgentStart,
        SchemaOperations.AgentRename,
        SchemaOperations.WorkspaceGet,
        SchemaOperations.TabGet,
        SchemaOperations.PaneGet,
        SchemaOperations.AgentGet,
        SchemaOperations.SessionSnapshot
    };

    private readonly IRpcRequestConnection _connection;

    public ResourceCommandAdapter(IRpcRequestConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connection = connection;
    }

    public async ValueTask<ResourceTransportReceipt> SubmitAsync(
        ResourceIntent intent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        if (!TryMap(intent, out var method, out var parameters, out var reject))
        {
            parameters?.Dispose();
            return reject;
        }

        if (parameters is null)
            return reject;
        using (parameters)
            return await SendAsync(method, parameters.RootElement, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ResourceQueryReceipt> QueryAsync(
        ResourceQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        string method;
        JsonDocument parameters;
        switch (query.Kind)
        {
            case ResourceQueryKind.Workspace:
                if (string.IsNullOrWhiteSpace(query.WorkspaceId))
                    return new ResourceQueryReceipt(ResourceTransportKind.Protocol, false, ResourceCommandCodes.ValidationError);
                method = SchemaOperations.WorkspaceGet;
                parameters = WriteObject(("workspace_id", query.WorkspaceId!));
                break;
            case ResourceQueryKind.Tab:
                if (string.IsNullOrWhiteSpace(query.TabId))
                    return new ResourceQueryReceipt(ResourceTransportKind.Protocol, false, ResourceCommandCodes.ValidationError);
                method = SchemaOperations.TabGet;
                parameters = WriteObject(("tab_id", query.TabId!));
                break;
            case ResourceQueryKind.Pane:
                if (string.IsNullOrWhiteSpace(query.PaneId))
                    return new ResourceQueryReceipt(ResourceTransportKind.Protocol, false, ResourceCommandCodes.ValidationError);
                method = SchemaOperations.PaneGet;
                parameters = WriteObject(("pane_id", query.PaneId!));
                break;
            case ResourceQueryKind.Agent:
                if (string.IsNullOrWhiteSpace(query.TerminalId) && string.IsNullOrWhiteSpace(query.PaneId))
                    return new ResourceQueryReceipt(ResourceTransportKind.Protocol, false, ResourceCommandCodes.ValidationError);
                method = SchemaOperations.AgentGet;
                parameters = WriteObject(("target", query.TerminalId ?? query.PaneId!));
                break;
            default:
                method = SchemaOperations.SessionSnapshot;
                parameters = WriteObject();
                break;
        }

        using (parameters)
        {
            var sent = await SendAsync(method, parameters.RootElement, cancellationToken).ConfigureAwait(false);
            if (sent.Kind is ResourceTransportKind.ApplicationError)
            {
                var missing = sent.ApplicationCode is ResourceCommandCodes.NotFound
                    or "not_found" or "unknown_workspace" or "unknown_tab" or "unknown_pane"
                    or "unknown_agent";
                return new ResourceQueryReceipt(
                    sent.Kind,
                    false,
                    missing ? ResourceCommandCodes.NotFound : sent.ApplicationCode);
            }

            if (sent.Kind is not ResourceTransportKind.Result)
                return new ResourceQueryReceipt(sent.Kind, false, sent.ApplicationCode);

            if (query.Kind is ResourceQueryKind.Snapshot)
                return new ResourceQueryReceipt(ResourceTransportKind.Result, false);

            return new ResourceQueryReceipt(
                ResourceTransportKind.Result,
                true,
                null,
                sent.ObservedName,
                sent.CreatedWorkspaceId ?? query.WorkspaceId,
                sent.CreatedTabId ?? query.TabId,
                sent.CreatedPaneId ?? query.PaneId,
                sent.CreatedTerminalId ?? query.TerminalId);
        }
    }

    private async ValueTask<ResourceTransportReceipt> SendAsync(
        string method,
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        if (!AllowedMethods.Contains(method))
            return new ResourceTransportReceipt(
                ResourceTransportKind.Protocol, ResourceCommandCodes.DynamicMethodRejected);

        RpcRequestOutcome outcome;
        try
        {
            outcome = await _connection.RequestAsync(method, parameters, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new ResourceTransportReceipt(ResourceTransportKind.Timeout);
        }

        using (outcome)
        {
            if (outcome.Failure is { } failure)
                return MapFailure(failure);
            if (outcome.Document is null)
                return new ResourceTransportReceipt(ResourceTransportKind.Protocol, ResourceCommandCodes.Protocol);
            return ReadEnvelope(outcome.Document);
        }
    }

    private static bool TryMap(
        ResourceIntent intent,
        out string method,
        out JsonDocument? parameters,
        out ResourceTransportReceipt reject)
    {
        method = "";
        parameters = null;
        reject = new ResourceTransportReceipt(ResourceTransportKind.Protocol, ResourceCommandCodes.ValidationError);
        switch (intent)
        {
            case CreateWorkspaceIntent create:
                method = SchemaOperations.WorkspaceCreate;
                parameters = WriteOptional(
                    ("label", create.Name),
                    ("cwd", create.WorkingDirectory));
                return true;
            case CreateTerminalIntent terminal:
                if (string.IsNullOrWhiteSpace(terminal.Parent.WorkspaceId))
                    return false;
                method = SchemaOperations.TabCreate;
                parameters = WriteOptional(
                    ("workspace_id", terminal.Parent.WorkspaceId),
                    ("cwd", terminal.WorkingDirectory),
                    ("label", terminal.Name));
                return true;
            case CreateAgentIntent agent:
                var wire = VerifiedAgentWires.Wire(agent.AgentKind);
                if (wire is null || string.IsNullOrWhiteSpace(agent.Name) ||
                    string.IsNullOrWhiteSpace(agent.Parent.PaneId))
                {
                    reject = new ResourceTransportReceipt(
                        ResourceTransportKind.Protocol, ResourceCommandCodes.AgentKindUnverified);
                    return false;
                }

                method = SchemaOperations.AgentStart;
                parameters = WriteRequired(
                    ("name", agent.Name),
                    ("kind", wire),
                    ("pane_id", agent.Parent.PaneId!));
                return true;
            case RenameResourceIntent rename:
                method = ResourceCommandGate.RenameOperation(rename.Target.Kind);
                parameters = rename.Target.Kind switch
                {
                    ResourceKind.Workspace => WriteRequired(
                        ("workspace_id", rename.Target.WorkspaceId),
                        ("label", rename.NewName)),
                    ResourceKind.Tab => WriteRequired(
                        ("tab_id", rename.Target.TabId ?? ""),
                        ("label", rename.NewName)),
                    ResourceKind.Agent => WriteObject(
                        ("target", rename.Target.PaneId ?? rename.Target.TerminalId ?? ""),
                        ("name", rename.NewName)),
                    _ => WriteObject(
                        ("pane_id", rename.Target.PaneId ?? ""),
                        ("label", rename.NewName))
                };
                return true;
            case CloseResourceIntent close:
                method = ResourceCommandGate.CloseOperation(close.Target.Kind);
                if (close.Target.Kind == ResourceKind.Workspace)
                {
                    parameters = close.CloseGroup
                        ? WriteCloseGroup(close.Target.WorkspaceId)
                        : WriteRequired(("workspace_id", close.Target.WorkspaceId));
                    return true;
                }

                if (close.Target.Kind == ResourceKind.Tab)
                {
                    parameters = WriteRequired(("tab_id", close.Target.TabId ?? ""));
                    return true;
                }

                parameters = WriteRequired(("pane_id", close.Target.PaneId ?? ""));
                return true;
            default:
                reject = new ResourceTransportReceipt(
                    ResourceTransportKind.Protocol, ResourceCommandCodes.DynamicMethodRejected);
                return false;
        }
    }

    private static ResourceTransportReceipt MapFailure(RpcFailure failure) => failure.Kind switch
    {
        RpcFailureKind.NotSent => new ResourceTransportReceipt(ResourceTransportKind.NotSent, failure.Code),
        RpcFailureKind.CancelledAfterWrite =>
            new ResourceTransportReceipt(ResourceTransportKind.CancelledAfterWrite, failure.Code),
        RpcFailureKind.ConnectionLost =>
            new ResourceTransportReceipt(ResourceTransportKind.ConnectionLost, failure.Code),
        RpcFailureKind.Unavailable =>
            new ResourceTransportReceipt(ResourceTransportKind.Unavailable, failure.Code),
        _ => new ResourceTransportReceipt(ResourceTransportKind.Protocol, failure.Code)
    };

    private static ResourceTransportReceipt ReadEnvelope(JsonDocument document)
    {
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var error) &&
            error.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
        {
            var code = ReadCode(error);
            return new ResourceTransportReceipt(ResourceTransportKind.ApplicationError, code);
        }

        JsonElement result = default;
        var hasResult = root.TryGetProperty("result", out result);
        if (!hasResult)
            result = root;
        return new ResourceTransportReceipt(
            ResourceTransportKind.Result,
            null,
            ReadNestedId(result, "workspace_id", "workspace"),
            ReadNestedId(result, "tab_id", "tab"),
            ReadNestedId(result, "pane_id", "pane"),
            ReadNestedId(result, "terminal_id", "agent"),
            ReadName(result));
    }

    private static string ReadCode(JsonElement error)
    {
        if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var code))
        {
            try
            {
                if (code.ValueKind == JsonValueKind.String)
                    return code.GetString() ?? ResourceCommandCodes.Protocol;
                if (code.ValueKind == JsonValueKind.Number && code.TryGetInt64(out var number))
                    return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (InvalidOperationException)
            {
                return ResourceCommandCodes.Protocol;
            }
        }

        return ResourceCommandCodes.Protocol;
    }

    private static string? ReadNestedId(JsonElement result, string field, string objectName)
    {
        var direct = ReadString(result, field);
        if (direct is not null)
            return direct;
        if (result.ValueKind == JsonValueKind.Object &&
            result.TryGetProperty(objectName, out var nested) &&
            nested.ValueKind == JsonValueKind.Object)
            return ReadString(nested, field);
        return null;
    }

    private static string? ReadName(JsonElement result)
    {
        return ReadString(result, "label") ??
               ReadString(result, "name") ??
               ReadNestedId(result, "label", "workspace") ??
               ReadNestedId(result, "label", "tab") ??
               ReadNestedId(result, "name", "agent");
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return null;
        try
        {
            return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static JsonDocument WriteRequired(params (string Name, string Value)[] pairs)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var (name, value) in pairs)
                writer.WriteString(name, value);
            writer.WriteEndObject();
        }

        return JsonDocument.Parse(buffer.ToArray());
    }

    private static JsonDocument WriteObject(params (string Name, string Value)[] pairs) => WriteRequired(pairs);

    private static JsonDocument WriteOptional(params (string Name, string? Value)[] pairs)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var (name, value) in pairs)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    writer.WriteString(name, value);
            }

            writer.WriteEndObject();
        }

        return JsonDocument.Parse(buffer.ToArray());
    }

    private static JsonDocument WriteCloseGroup(string workspaceId)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("workspace_id", workspaceId);
            writer.WriteBoolean("close_group", true);
            writer.WriteEndObject();
        }

        return JsonDocument.Parse(buffer.ToArray());
    }
}
