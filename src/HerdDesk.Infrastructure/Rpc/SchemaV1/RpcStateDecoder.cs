using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.Rpc.SchemaV1;

public sealed class RpcStateDecoder : IRpcStateDecoder
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public DecodeResult<DecodedSessionSnapshot> DecodeSnapshot(
        JsonElement document,
        SessionKey session,
        ConnectionEpoch epoch,
        SchemaCompatibilityBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        try
        {
            var root = UnwrapEnvelope(document, "session_snapshot", "snapshot");
            var dto = ReadSnapshot(root);
            return DecodeResult<DecodedSessionSnapshot>.Ok(ToDecoded(dto, session, epoch));
        }
        catch (DecodeFail error)
        {
            return DecodeResult<DecodedSessionSnapshot>.Fail(error.Message);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            return DecodeResult<DecodedSessionSnapshot>.Fail(ProjectionCodes.FieldTypeInvalid);
        }
    }

    public DecodeResult<DecodedRpcEvent> DecodeEvent(
        JsonElement document,
        SessionKey session,
        ConnectionEpoch epoch,
        SchemaCompatibilityBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        try
        {
            var root = UnwrapEvent(document);
            var dto = ReadEvent(root);
            return DecodeResult<DecodedRpcEvent>.Ok(ToDecoded(dto, session, epoch));
        }
        catch (DecodeFail error)
        {
            return DecodeResult<DecodedRpcEvent>.Fail(error.Message);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            return DecodeResult<DecodedRpcEvent>.Fail(ProjectionCodes.FieldTypeInvalid);
        }
    }

    public DecodeResult<ProjectionEntityChangeSet> DecodeEntityRead(
        string operation,
        JsonElement document,
        SessionKey session,
        ConnectionEpoch epoch,
        SchemaCompatibilityBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        _ = (session, epoch);
        if (string.IsNullOrWhiteSpace(operation))
            return DecodeResult<ProjectionEntityChangeSet>.Fail(ProjectionCodes.FullSnapshotRequired);
        try
        {
            var payload = UnwrapEntity(document, PayloadName(operation));
            return operation switch
            {
                "workspace.get" => DecodeResult<ProjectionEntityChangeSet>.Ok(new ProjectionEntityChangeSet(
                    session, [ToDecoded(ReadWorkspace(payload))], [], [], [], [])),
                "tab.get" => DecodeResult<ProjectionEntityChangeSet>.Ok(new ProjectionEntityChangeSet(
                    session, [], [ToDecoded(ReadTab(payload))], [], [], [])),
                "pane.get" => DecodeResult<ProjectionEntityChangeSet>.Ok(new ProjectionEntityChangeSet(
                    session, [], [], [ToDecoded(ReadPane(payload))], [], [])),
                "agent.get" => DecodeResult<ProjectionEntityChangeSet>.Ok(new ProjectionEntityChangeSet(
                    session, [], [], [], [ToDecoded(ReadAgent(payload))], [])),
                "pane.layout" => DecodeResult<ProjectionEntityChangeSet>.Ok(new ProjectionEntityChangeSet(
                    session, [], [], [], [], [ToDecoded(ReadLayout(payload))])),
                _ => DecodeResult<ProjectionEntityChangeSet>.Fail(ProjectionCodes.FullSnapshotRequired)
            };
        }
        catch (DecodeFail error)
        {
            return DecodeResult<ProjectionEntityChangeSet>.Fail(error.Message);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            return DecodeResult<ProjectionEntityChangeSet>.Fail(ProjectionCodes.FieldTypeInvalid);
        }
    }

    private static string PayloadName(string operation) => operation switch
    {
        "workspace.get" => "workspace",
        "tab.get" => "tab",
        "pane.get" => "pane",
        "agent.get" => "agent",
        "pane.layout" => "layout",
        _ => "result"
    };

    private static JsonElement UnwrapEntity(JsonElement document, string payloadName)
    {
        StrictJson.RequireObject(document);
        if (document.TryGetProperty("error", out var error) &&
            error.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
            throw new DecodeFail(ProjectionCodes.ErrorEnvelope);
        var current = document;
        if (document.TryGetProperty("result", out var result) &&
            result.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
        {
            StrictJson.RequireObject(result);
            current = result;
        }
        if (current.TryGetProperty(payloadName, out var payload) &&
            payload.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
        {
            StrictJson.RequireObject(payload);
            return payload;
        }
        return current;
    }

    public DecodeResult<DecodedSessionSnapshot> DecodeSnapshotBytes(
        ReadOnlyMemory<byte> utf8,
        SessionKey session,
        ConnectionEpoch epoch,
        SchemaCompatibilityBinding binding)
    {
        try
        {
            _ = StrictUtf8.GetCharCount(utf8.Span);
            using var document = JsonDocument.Parse(utf8, StrictJson.ParseOptions);
            StrictJson.CheckDuplicateKeys(document.RootElement);
            return DecodeSnapshot(document.RootElement, session, epoch, binding);
        }
        catch (DecodeFail error)
        {
            return DecodeResult<DecodedSessionSnapshot>.Fail(error.Message);
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException
            or InvalidOperationException or FormatException)
        {
            return DecodeResult<DecodedSessionSnapshot>.Fail(ClassifyParseFailure(error));
        }
    }

    public DecodeResult<DecodedRpcEvent> DecodeEventBytes(
        ReadOnlyMemory<byte> utf8,
        SessionKey session,
        ConnectionEpoch epoch,
        SchemaCompatibilityBinding binding)
    {
        try
        {
            _ = StrictUtf8.GetCharCount(utf8.Span);
            using var document = JsonDocument.Parse(utf8, StrictJson.ParseOptions);
            StrictJson.CheckDuplicateKeys(document.RootElement);
            return DecodeEvent(document.RootElement, session, epoch, binding);
        }
        catch (DecodeFail error)
        {
            return DecodeResult<DecodedRpcEvent>.Fail(error.Message);
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException
            or InvalidOperationException or FormatException)
        {
            return DecodeResult<DecodedRpcEvent>.Fail(ClassifyParseFailure(error));
        }
    }

    private static string ClassifyParseFailure(Exception error)
    {
        var message = error.Message;
        if (message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("already defined", StringComparison.OrdinalIgnoreCase))
            return ProjectionCodes.DuplicateJsonKey;
        return ProjectionCodes.FieldTypeInvalid;
    }

    public static SnapshotDto ReadSnapshotDto(JsonElement document) =>
        ReadSnapshot(UnwrapEnvelope(document, "session_snapshot", "snapshot"));

    private static JsonElement UnwrapEnvelope(JsonElement document, string resultType, string payloadName)
    {
        StrictJson.RequireObject(document);
        if (document.TryGetProperty("error", out var error) &&
            error.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
            throw new DecodeFail(ProjectionCodes.ErrorEnvelope);
        var current = document;
        if (document.TryGetProperty("result", out var result) &&
            result.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
        {
            StrictJson.RequireObject(result);
            current = result;
        }
        if (current.TryGetProperty("type", out var typeElement) &&
            typeElement.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
        {
            var type = StrictJson.GetString(typeElement);
            if (type != resultType)
                throw new DecodeFail(ProjectionCodes.FieldTypeInvalid);
            if (!current.TryGetProperty(payloadName, out var payload) ||
                payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                throw new DecodeFail(ProjectionCodes.RequiredFieldMissing);
            StrictJson.RequireObject(payload);
            return payload;
        }
        return current;
    }

    private static JsonElement UnwrapEvent(JsonElement document)
    {
        StrictJson.RequireObject(document);
        if (document.TryGetProperty("error", out var error) &&
            error.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
            throw new DecodeFail(ProjectionCodes.ErrorEnvelope);
        if (document.TryGetProperty("result", out var result) &&
            result.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
        {
            StrictJson.RequireObject(result);
            return result;
        }
        return document;
    }

    private static SnapshotDto ReadSnapshot(JsonElement element)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var extras = StrictJson.Partition(element, SchemaMaps.SnapshotFields, fields);
        return new SnapshotDto(
            StrictJson.RequiredString(fields, "version"),
            StrictJson.RequiredProtocol(fields, "protocol"),
            StrictJson.OptionalString(fields, "focused_workspace_id"),
            StrictJson.OptionalString(fields, "focused_tab_id"),
            StrictJson.OptionalString(fields, "focused_pane_id"),
            ReadArray(fields, "workspaces", ReadWorkspace),
            ReadArray(fields, "tabs", ReadTab),
            ReadArray(fields, "panes", ReadPane),
            ReadArray(fields, "layouts", ReadLayout),
            ReadArray(fields, "agents", ReadAgent),
            extras);
    }

    private static EventDto ReadEvent(JsonElement element)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var extras = StrictJson.Partition(element, SchemaMaps.EventFields, fields);
        var eventValue = StrictJson.RequiredEnum(fields, "event", SchemaMaps.Events);
        WorkspaceDto? workspace = null;
        TabDto? tab = null;
        PaneDto? pane = null;
        LayoutDto? layout = null;
        string? workspaceId = null;
        string? tabId = null;
        string? paneId = null;
        bool? closeGroup = null;
        FrozenDictionary<string, JsonElement> dataExtras = FrozenDictionary<string, JsonElement>.Empty;
        if (StrictJson.Has(fields, "data"))
        {
            var data = StrictJson.RequiredObjectField(fields, "data");
            var dataFields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            dataExtras = StrictJson.Partition(data, SchemaMaps.EventDataKnown, dataFields);
            workspaceId = StrictJson.OptionalString(dataFields, "workspace_id");
            tabId = StrictJson.OptionalString(dataFields, "tab_id");
            paneId = StrictJson.OptionalString(dataFields, "pane_id");
            closeGroup = StrictJson.OptionalBool(dataFields, "close_group");
            var workspaceElement = StrictJson.OptionalObject(dataFields, "workspace");
            if (workspaceElement is { } workspaceValue)
                workspace = ReadWorkspace(workspaceValue);
            var tabElement = StrictJson.OptionalObject(dataFields, "tab");
            if (tabElement is { } tabValue)
                tab = ReadTab(tabValue);
            var paneElement = StrictJson.OptionalObject(dataFields, "pane");
            if (paneElement is { } paneValue)
                pane = ReadPane(paneValue);
            var layoutElement = StrictJson.OptionalObject(dataFields, "layout");
            if (layoutElement is { } layoutValue)
                layout = ReadLayout(layoutValue);
        }
        else
            throw new DecodeFail(ProjectionCodes.RequiredFieldMissing);
        return new EventDto(
            eventValue, workspaceId, tabId, paneId, workspace, tab, pane, layout, closeGroup,
            MergeExtras(extras, dataExtras));
    }

    private static WorkspaceDto ReadWorkspace(JsonElement element)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var extras = StrictJson.Partition(element, SchemaMaps.WorkspaceFields, fields);
        WorktreeDto? worktree = null;
        var worktreeElement = StrictJson.OptionalObject(fields, "worktree");
        if (worktreeElement is { } value)
            worktree = ReadWorktree(value);
        return new WorkspaceDto(
            StrictJson.RequiredString(fields, "workspace_id"),
            StrictJson.RequiredUInt64(fields, "number"),
            StrictJson.RequiredString(fields, "label"),
            StrictJson.RequiredBool(fields, "focused"),
            StrictJson.RequiredUInt64(fields, "pane_count"),
            StrictJson.RequiredUInt64(fields, "tab_count"),
            StrictJson.RequiredString(fields, "active_tab_id"),
            StrictJson.RequiredEnum(fields, "agent_status", SchemaMaps.AgentStatus),
            StrictJson.OptionalStringMap(fields, "tokens"),
            worktree,
            extras);
    }

    private static WorktreeDto ReadWorktree(JsonElement element)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var extras = StrictJson.Partition(element, SchemaMaps.WorktreeFields, fields);
        return new WorktreeDto(
            StrictJson.RequiredString(fields, "repo_key"),
            StrictJson.RequiredString(fields, "repo_name"),
            StrictJson.RequiredString(fields, "repo_root"),
            StrictJson.RequiredString(fields, "checkout_path"),
            StrictJson.RequiredBool(fields, "is_linked_worktree"),
            extras);
    }

    private static TabDto ReadTab(JsonElement element)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var extras = StrictJson.Partition(element, SchemaMaps.TabFields, fields);
        return new TabDto(
            StrictJson.RequiredString(fields, "tab_id"),
            StrictJson.RequiredString(fields, "workspace_id"),
            StrictJson.RequiredUInt64(fields, "number"),
            StrictJson.RequiredString(fields, "label"),
            StrictJson.RequiredBool(fields, "focused"),
            StrictJson.RequiredUInt64(fields, "pane_count"),
            StrictJson.RequiredEnum(fields, "agent_status", SchemaMaps.AgentStatus),
            extras);
    }

    private static PaneDto ReadPane(JsonElement element)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var extras = StrictJson.Partition(element, SchemaMaps.PaneFields, fields);
        if (fields.TryGetValue("scroll", out var scroll) &&
            scroll.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
            StrictJson.RequireObject(scroll);
        AgentSessionDto? agentSession = null;
        var sessionElement = StrictJson.OptionalObject(fields, "agent_session");
        if (sessionElement is { } value)
            agentSession = ReadAgentSession(value);
        return new PaneDto(
            StrictJson.RequiredString(fields, "pane_id"),
            StrictJson.RequiredString(fields, "terminal_id"),
            StrictJson.RequiredString(fields, "workspace_id"),
            StrictJson.RequiredString(fields, "tab_id"),
            StrictJson.RequiredBool(fields, "focused"),
            StrictJson.OptionalString(fields, "cwd"),
            StrictJson.OptionalString(fields, "foreground_cwd"),
            StrictJson.OptionalString(fields, "label"),
            StrictJson.OptionalString(fields, "agent"),
            StrictJson.OptionalString(fields, "title"),
            StrictJson.OptionalString(fields, "display_agent"),
            StrictJson.RequiredEnum(fields, "agent_status", SchemaMaps.AgentStatus),
            StrictJson.OptionalStringMap(fields, "state_labels"),
            StrictJson.OptionalStringMap(fields, "tokens"),
            agentSession,
            StrictJson.RequiredUInt64(fields, "revision"),
            extras);
    }

    private static AgentDto ReadAgent(JsonElement element)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var extras = StrictJson.Partition(element, SchemaMaps.AgentFields, fields);
        if (fields.TryGetValue("launch_pending", out var launch) &&
            launch.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null &&
            launch.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new DecodeFail(ProjectionCodes.FieldTypeInvalid);
        if (fields.TryGetValue("interactive_ready", out var ready) &&
            ready.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null &&
            ready.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new DecodeFail(ProjectionCodes.FieldTypeInvalid);
        if (fields.TryGetValue("state_change_seq", out var seq) &&
            seq.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
            _ = StrictJson.RequiredUInt64(fields, "state_change_seq");
        return new AgentDto(
            StrictJson.RequiredString(fields, "terminal_id"),
            StrictJson.RequiredString(fields, "workspace_id"),
            StrictJson.RequiredString(fields, "tab_id"),
            StrictJson.RequiredString(fields, "pane_id"),
            StrictJson.OptionalString(fields, "name"),
            StrictJson.OptionalString(fields, "agent"),
            StrictJson.OptionalString(fields, "title"),
            StrictJson.OptionalString(fields, "display_agent"),
            StrictJson.RequiredEnum(fields, "agent_status", SchemaMaps.AgentStatus),
            StrictJson.RequiredBool(fields, "focused"),
            StrictJson.RequiredUInt64(fields, "revision"),
            extras);
    }

    private static AgentSessionDto ReadAgentSession(JsonElement element)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var extras = StrictJson.Partition(element, SchemaMaps.AgentSessionFields, fields);
        return new AgentSessionDto(
            StrictJson.RequiredString(fields, "source"),
            StrictJson.RequiredString(fields, "agent"),
            StrictJson.RequiredEnum(fields, "kind", SchemaMaps.SessionRef),
            StrictJson.RequiredString(fields, "value"),
            extras);
    }

    private static LayoutDto ReadLayout(JsonElement element)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var extras = StrictJson.Partition(element, SchemaMaps.LayoutFields, fields);
        return new LayoutDto(
            StrictJson.RequiredString(fields, "workspace_id"),
            StrictJson.RequiredString(fields, "tab_id"),
            StrictJson.RequiredBool(fields, "zoomed"),
            ReadRect(StrictJson.RequiredObjectField(fields, "area")),
            StrictJson.RequiredString(fields, "focused_pane_id"),
            ReadArray(fields, "panes", ReadLayoutPane),
            ReadArray(fields, "splits", ReadLayoutSplit),
            extras);
    }

    private static LayoutPaneDto ReadLayoutPane(JsonElement element)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        _ = StrictJson.Partition(element, SchemaMaps.LayoutPaneFields, fields);
        return new LayoutPaneDto(
            StrictJson.RequiredString(fields, "pane_id"),
            StrictJson.RequiredBool(fields, "focused"),
            ReadRect(StrictJson.RequiredObjectField(fields, "rect")));
    }

    private static LayoutSplitDto ReadLayoutSplit(JsonElement element)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        _ = StrictJson.Partition(element, SchemaMaps.LayoutSplitFields, fields);
        return new LayoutSplitDto(
            StrictJson.RequiredString(fields, "id"),
            StrictJson.RequiredEnum(fields, "direction", SchemaMaps.Split),
            StrictJson.RequiredFiniteNumber(fields, "ratio"),
            ReadRect(StrictJson.RequiredObjectField(fields, "rect")));
    }

    private static LayoutRectDto ReadRect(JsonElement element)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        _ = StrictJson.Partition(element, SchemaMaps.RectFields, fields);
        return new LayoutRectDto(
            StrictJson.RequiredUInt16(fields, "x"),
            StrictJson.RequiredUInt16(fields, "y"),
            StrictJson.RequiredUInt16(fields, "width"),
            StrictJson.RequiredUInt16(fields, "height"));
    }

    private static IReadOnlyList<T> ReadArray<T>(
        Dictionary<string, JsonElement> fields,
        string name,
        Func<JsonElement, T> read)
    {
        var array = StrictJson.RequiredArray(fields, name);
        var list = new List<T>();
        foreach (var item in array.EnumerateArray())
            list.Add(read(item));
        return list;
    }

    private static DecodedSessionSnapshot ToDecoded(
        SnapshotDto dto,
        SessionKey session,
        ConnectionEpoch epoch) =>
        new(
            session,
            epoch,
            dto.Version,
            dto.Protocol,
            dto.FocusedWorkspaceId,
            dto.FocusedTabId,
            dto.FocusedPaneId,
            dto.Workspaces.Select(ToDecoded).ToArray(),
            dto.Tabs.Select(ToDecoded).ToArray(),
            dto.Panes.Select(ToDecoded).ToArray(),
            dto.Layouts.Select(ToDecoded).ToArray(),
            dto.Agents.Select(ToDecoded).ToArray(),
            dto.Extensions);

    private static DecodedWorkspace ToDecoded(WorkspaceDto dto) =>
        new(
            dto.WorkspaceId,
            dto.Number,
            dto.Label,
            dto.Focused,
            dto.PaneCount,
            dto.TabCount,
            dto.ActiveTabId,
            dto.AgentStatus.ToWire(),
            dto.Tokens,
            dto.Worktree is null ? null : new DecodedWorktree(
                dto.Worktree.RepoKey, dto.Worktree.RepoName, dto.Worktree.RepoRoot,
                dto.Worktree.CheckoutPath, dto.Worktree.IsLinkedWorktree, dto.Worktree.Extensions),
            dto.Extensions);

    private static DecodedTab ToDecoded(TabDto dto) =>
        new(dto.TabId, dto.WorkspaceId, dto.Number, dto.Label, dto.Focused, dto.PaneCount,
            dto.AgentStatus.ToWire(), dto.Extensions);

    private static DecodedPane ToDecoded(PaneDto dto) =>
        new(
            dto.PaneId,
            dto.TerminalId,
            dto.WorkspaceId,
            dto.TabId,
            dto.Focused,
            dto.Cwd,
            dto.ForegroundCwd,
            dto.Label,
            dto.Agent,
            SchemaMaps.AgentKind(dto.Agent),
            dto.Title,
            null,
            dto.DisplayAgent,
            dto.AgentStatus.ToWire(),
            dto.StateLabels,
            dto.Tokens,
            dto.AgentSession is null
                ? null
                : new DecodedAgentSession(
                    dto.AgentSession.Source, dto.AgentSession.Agent, dto.AgentSession.Kind.ToWire(),
                    dto.AgentSession.Value, dto.AgentSession.Extensions),
            dto.Revision,
            dto.Extensions);

    private static DecodedLayout ToDecoded(LayoutDto dto) =>
        new(
            dto.WorkspaceId,
            dto.TabId,
            dto.Zoomed,
            new DecodedLayoutRect(dto.Area.X, dto.Area.Y, dto.Area.Width, dto.Area.Height),
            dto.FocusedPaneId,
            dto.Panes.Select(item => new DecodedLayoutPane(
                item.PaneId, item.Focused,
                new DecodedLayoutRect(item.Rect.X, item.Rect.Y, item.Rect.Width, item.Rect.Height)))
                .ToArray(),
            dto.Splits.Select(item => new DecodedLayoutSplit(
                item.Id, item.Direction.ToWire(), item.Ratio,
                new DecodedLayoutRect(item.Rect.X, item.Rect.Y, item.Rect.Width, item.Rect.Height)))
                .ToArray(),
            dto.Extensions);

    private static DecodedAgent ToDecoded(AgentDto dto) =>
        new(
            dto.TerminalId,
            dto.WorkspaceId,
            dto.TabId,
            dto.PaneId,
            dto.Name,
            dto.Agent,
            SchemaMaps.AgentKind(dto.Agent),
            dto.Title,
            dto.DisplayAgent,
            dto.AgentStatus.ToWire(),
            dto.Focused,
            dto.Revision,
            dto.Extensions);

    private static DecodedRpcEvent ToDecoded(EventDto dto, SessionKey session, ConnectionEpoch epoch) =>
        new(
            session,
            epoch,
            dto.Event.ToWire(),
            dto.WorkspaceId,
            dto.TabId,
            dto.PaneId,
            dto.Workspace is null ? null : ToDecoded(dto.Workspace),
            dto.Tab is null ? null : ToDecoded(dto.Tab),
            dto.Pane is null ? null : ToDecoded(dto.Pane),
            dto.Layout is null ? null : ToDecoded(dto.Layout),
            dto.CloseGroup,
            dto.Extensions);

    private static FrozenDictionary<string, JsonElement> MergeExtras(
        FrozenDictionary<string, JsonElement> left,
        FrozenDictionary<string, JsonElement> right)
    {
        if (left.Count == 0)
            return right;
        if (right.Count == 0)
            return left;
        var map = new Dictionary<string, JsonElement>(left, StringComparer.Ordinal);
        foreach (var pair in right)
            map[pair.Key] = pair.Value;
        return map.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
