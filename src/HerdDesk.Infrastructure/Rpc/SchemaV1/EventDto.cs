using System.Collections.Frozen;
using System.Text.Json;
using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.Rpc.SchemaV1;

public sealed record EventDto(
    SchemaValue<RpcEventKind> Event,
    string? WorkspaceId,
    string? TabId,
    string? PaneId,
    WorkspaceDto? Workspace,
    TabDto? Tab,
    PaneDto? Pane,
    LayoutDto? Layout,
    bool? CloseGroup,
    FrozenDictionary<string, JsonElement> Extensions);
