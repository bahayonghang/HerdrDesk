using System.Text;
using HerdDesk.Contracts;

namespace HerdDesk.Core;

public sealed class GlobalSearchIndex
{
    private readonly Dictionary<DeviceId, List<SearchDocument>> _byDevice = new();
    private readonly List<SearchDocument> _all = [];

    public int DocumentCount => _all.Count;
    public int TerminalProcessDelta => 0;

    public void ReplaceDevice(DevicePartition partition)
    {
        ArgumentNullException.ThrowIfNull(partition);
        RemoveDevice(partition.Device);
        var documents = Build(partition);
        _byDevice[partition.Device] = documents;
        _all.AddRange(documents);
    }

    public void RemoveDevice(DeviceId device)
    {
        if (!_byDevice.Remove(device, out var removed))
            return;
        if (removed.Count == 0)
            return;
        _all.RemoveAll(item => item.Ref.Device == device);
    }

    public void Clear()
    {
        _byDevice.Clear();
        _all.Clear();
    }

    public GlobalSearchResult Query(
        GlobalSearchQuery query,
        IReadOnlyList<AggregationRecent>? recents,
        AggregateReadiness readiness)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Cancelled)
        {
            return new GlobalSearchResult(
                query.Generation, [], readiness is AggregateReadiness.PartialReady or AggregateReadiness.Loading,
                true, readiness, 0);
        }

        var needle = Normalize(query.Text);
        var hits = new List<SearchDocument>();
        if (recents is not null)
        {
            for (var i = 0; i < recents.Count; i++)
            {
                if (query.Cancelled)
                    return Cancelled(query.Generation, readiness);
                var recent = recents[i];
                if (query.ScopeDevice is { } scope && recent.Ref.Device != scope)
                    continue;
                var expired = IsExpired(recent.Ref);
                var label = LabelFor(recent);
                if (!Matches(needle, label, recent.Ref))
                    continue;
                hits.Add(new SearchDocument(
                    recent.Ref,
                    Normalize(label),
                    recent.Ref.Kind,
                    Breadcrumb(recent.Ref, label),
                    label,
                    BusinessStateKind.Unknown,
                    expired ? ConnectionPhase.Offline : ConnectionPhase.Ready,
                    int.MinValue,
                    recents.Count - i,
                    true));
            }
        }

        for (var i = 0; i < _all.Count; i++)
        {
            if (query.Cancelled)
                return Cancelled(query.Generation, readiness);
            if ((i & 15) == 15 && query.Cancelled)
                return Cancelled(query.Generation, readiness);
            var document = _all[i];
            if (query.ScopeDevice is { } device && document.Ref.Device != device)
                continue;
            if (!Matches(needle, document.DisplayLabel, document.Ref) &&
                !document.NormalizedLabel.Contains(needle, StringComparison.Ordinal) &&
                !document.Kind.ToString().ToLowerInvariant().Contains(needle, StringComparison.Ordinal))
                continue;
            hits.Add(document);
        }

        hits.Sort(Compare);
        return new GlobalSearchResult(
            query.Generation,
            hits,
            readiness is AggregateReadiness.PartialReady or AggregateReadiness.Loading,
            false,
            readiness,
            0);
    }

    private bool IsExpired(GlobalEntityRef target)
    {
        if (!_byDevice.TryGetValue(target.Device, out var documents))
            return true;
        foreach (var document in documents)
        {
            if (!document.Ref.SameTarget(target))
                continue;
            return document.Ref.Stamp.Epoch != target.Stamp.Epoch;
        }

        return true;
    }

    private string LabelFor(AggregationRecent recent)
    {
        if (!_byDevice.TryGetValue(recent.Ref.Device, out var documents))
            return Strip(recent.Label);
        foreach (var document in documents)
        {
            if (document.Ref.SameTarget(recent.Ref) ||
                (recent.Ref.Pane is { } pane && document.Ref.Pane == pane))
                return document.DisplayLabel;
        }

        return Strip(recent.Label);
    }

    private static List<SearchDocument> Build(DevicePartition partition)
    {
        var stamp = new FreshnessStamp(partition.Epoch, partition.Generation);
        var documents = new List<SearchDocument>();
        var deviceRef = new GlobalEntityRef(
            GlobalEntityKind.Device, partition.Device, null, null, null, null, stamp);
        documents.Add(Document(
            deviceRef, partition.DisplayLabel, partition.DisplayLabel, BusinessStateKind.Unknown,
            partition.Phase, partition.UserOrder));
        foreach (var session in partition.Sessions)
        {
            var sessionEpoch = partition.EpochFor(session.Session);
            var sessionStamp = new FreshnessStamp(sessionEpoch, partition.Generation);
            var sessionLabel = session.Session.SessionName ?? session.Session.EndpointKey;
            var sessionRef = new GlobalEntityRef(
                GlobalEntityKind.Session, partition.Device, session.Session, null, null, null, sessionStamp);
            documents.Add(Document(
                sessionRef, sessionLabel,
                string.Join(" / ", [partition.DisplayLabel, sessionLabel]),
                BusinessStateKind.Unknown, partition.Phase, partition.UserOrder));
            foreach (var workspace in session.Workspaces)
            {
                var workspaceRef = new GlobalEntityRef(
                    GlobalEntityKind.Workspace, partition.Device, session.Session, workspace.WorkspaceId,
                    null, null, sessionStamp);
                documents.Add(Document(
                    workspaceRef, workspace.Label,
                    string.Join(" / ", [partition.DisplayLabel, sessionLabel, workspace.Label]),
                    BusinessState.From(workspace.AgentStatus).Kind, partition.Phase, partition.UserOrder));
            }

            foreach (var pane in session.Panes)
            {
                var label = string.IsNullOrEmpty(pane.Label) ? pane.Key.PaneId : pane.Label;
                var workspaceLabel = WorkspaceLabel(session, pane.Key.WorkspaceId);
                var paneRef = new GlobalEntityRef(
                    GlobalEntityKind.Pane, partition.Device, session.Session, pane.Key.WorkspaceId,
                    pane.Key, null, sessionStamp);
                documents.Add(Document(
                    paneRef, label,
                    string.Join(" / ", [partition.DisplayLabel, sessionLabel, workspaceLabel, label]),
                    BusinessState.From(pane.AgentStatus).Kind, partition.Phase, partition.UserOrder));
            }

            foreach (var agent in session.Agents)
            {
                var label = string.IsNullOrEmpty(agent.Name)
                    ? (agent.DisplayAgent ?? agent.Pane.PaneId)
                    : agent.Name;
                var workspaceLabel = WorkspaceLabel(session, agent.Pane.WorkspaceId);
                var entityId = string.IsNullOrWhiteSpace(agent.TerminalId)
                    ? agent.Pane.PaneId
                    : agent.TerminalId;
                var agentRef = new GlobalEntityRef(
                    GlobalEntityKind.Agent, partition.Device, session.Session, agent.Pane.WorkspaceId,
                    agent.Pane, entityId, sessionStamp);
                documents.Add(Document(
                    agentRef, label,
                    string.Join(" / ", [partition.DisplayLabel, sessionLabel, workspaceLabel, label]),
                    BusinessState.From(agent.AgentStatus).Kind, partition.Phase, partition.UserOrder));
            }
        }

        return documents;
    }

    private static SearchDocument Document(
        GlobalEntityRef entity,
        string label,
        string breadcrumb,
        BusinessStateKind business,
        ConnectionPhase phase,
        int userOrder)
    {
        var display = Strip(label);
        return new SearchDocument(
            entity,
            Normalize(display),
            entity.Kind,
            Strip(breadcrumb),
            display,
            business,
            phase,
            userOrder,
            0,
            false);
    }

    private static string WorkspaceLabel(SessionProjection session, string workspaceId)
    {
        foreach (var workspace in session.Workspaces)
        {
            if (workspace.WorkspaceId == workspaceId)
                return workspace.Label;
        }

        return workspaceId;
    }

    private static bool Matches(string needle, string label, GlobalEntityRef entity)
    {
        if (needle.Length == 0)
            return true;
        if (Normalize(label).Contains(needle, StringComparison.Ordinal))
            return true;
        if (entity.Device.Value.ToString("D").ToLowerInvariant().Contains(needle, StringComparison.Ordinal))
            return true;
        if (entity.Session is { } session)
        {
            if (session.EndpointKey.ToLowerInvariant().Contains(needle, StringComparison.Ordinal))
                return true;
            if (session.SessionName is { } name &&
                name.ToLowerInvariant().Contains(needle, StringComparison.Ordinal))
                return true;
        }

        if (entity.WorkspaceId is { } workspace &&
            workspace.ToLowerInvariant().Contains(needle, StringComparison.Ordinal))
            return true;
        if (entity.Pane is { } pane &&
            pane.PaneId.ToLowerInvariant().Contains(needle, StringComparison.Ordinal))
            return true;
        return entity.EntityId is { } id &&
               id.ToLowerInvariant().Contains(needle, StringComparison.Ordinal);
    }

    private static int Compare(SearchDocument left, SearchDocument right)
    {
        if (left.IsRecent != right.IsRecent)
            return left.IsRecent ? -1 : 1;
        if (left.IsRecent && right.IsRecent)
            return right.RecentRank.CompareTo(left.RecentRank);
        var user = left.UserOrder.CompareTo(right.UserOrder);
        if (user != 0)
            return user;
        var device = left.Ref.Device.Value.CompareTo(right.Ref.Device.Value);
        if (device != 0)
            return device;
        var kind = left.Kind.CompareTo(right.Kind);
        if (kind != 0)
            return kind;
        var session = string.CompareOrdinal(SessionPart(left), SessionPart(right));
        if (session != 0)
            return session;
        var workspace = string.CompareOrdinal(left.Ref.WorkspaceId, right.Ref.WorkspaceId);
        if (workspace != 0)
            return workspace;
        var pane = string.CompareOrdinal(left.Ref.Pane?.PaneId, right.Ref.Pane?.PaneId);
        if (pane != 0)
            return pane;
        return string.CompareOrdinal(left.DisplayLabel, right.DisplayLabel);
    }

    private static string SessionPart(SearchDocument document)
    {
        if (document.Ref.Session is not { } session)
            return "";
        return session.EndpointKey + "\u001f" + (session.SessionName ?? "");
    }

    private static GlobalSearchResult Cancelled(long generation, AggregateReadiness readiness) =>
        new(generation, [], readiness is AggregateReadiness.PartialReady or AggregateReadiness.Loading,
            true, readiness, 0);

    internal static string Normalize(string value) => Strip(value).ToLowerInvariant();

    internal static string Strip(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch == '\u001b')
                continue;
            builder.Append(ch);
        }

        return builder.ToString();
    }

    internal static string Breadcrumb(GlobalEntityRef entity, string label)
    {
        var parts = new List<string>(4);
        parts.Add(entity.Device.Value.ToString("D")[..8]);
        if (entity.Session is { } session)
            parts.Add(session.SessionName ?? session.EndpointKey);
        if (!string.IsNullOrEmpty(entity.WorkspaceId))
            parts.Add(entity.WorkspaceId);
        parts.Add(label);
        return string.Join(" / ", parts);
    }
}
