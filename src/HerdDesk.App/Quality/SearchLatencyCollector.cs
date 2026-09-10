using System.Diagnostics;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Quality;

namespace HerdDesk.App.Quality;

public sealed record SearchLatencyReport(
    int ProjectionCount,
    int PartitionCount,
    int SampleCount,
    int WarmupCount,
    IReadOnlyList<double> SampleMs,
    double P50Ms,
    double P95Ms,
    double MaxMs,
    int ResultCount,
    int TerminalProcessDelta,
    bool LiveThreeDevice,
    bool ClosesAc19,
    bool ClosesAc21,
    bool ClosesAc28);

public static class SearchLatencyCollector
{
    public const int RequiredProjections = 100;
    public const int DefaultSamples = 40;
    public const int DefaultWarmup = 10;

    static readonly DeviceId DeviceA = new(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
    static readonly DeviceId DeviceB = new(Guid.Parse("11111111-2222-3333-4444-555555555555"));
    static readonly DeviceId DeviceC = new(Guid.Parse("22222222-3333-4444-5555-666666666666"));

    public static SearchLatencyReport Measure(
        int samples = DefaultSamples,
        int warmup = DefaultWarmup)
    {
        if (samples < 1)
            throw new ArgumentOutOfRangeException(nameof(samples));
        if (warmup < 0)
            throw new ArgumentOutOfRangeException(nameof(warmup));

        var store = new GlobalProjectionStore();
        SeedHundred(store);
        if (store.DocumentCount < RequiredProjections)
            throw new InvalidOperationException("search_projections_short");

        var recents = new RecentAccessStore();
        var resolver = new GlobalTargetResolver(store);
        var viewModel = new GlobalSearchViewModel(store, recents, resolver);
        var walls = new List<double>(samples);
        for (var i = 0; i < warmup + samples; i++)
        {
            var watch = Stopwatch.StartNew();
            viewModel.UpdateQuery("pane-");
            watch.Stop();
            if (i >= warmup)
                walls.Add(watch.Elapsed.TotalMilliseconds);
        }

        var summary = EmpiricalPercentile.Summarize(walls);
        return new SearchLatencyReport(
            store.DocumentCount,
            store.PartitionCount,
            walls.Count,
            warmup,
            walls,
            summary.P50,
            summary.P95,
            summary.Max,
            viewModel.Results.Count,
            viewModel.TerminalProcessDelta,
            false,
            false,
            false,
            false);
    }

    public static void Write(SearchLatencyReport report, string path)
    {
        ArgumentNullException.ThrowIfNull(report);
        QualityJson.WriteFile(path, writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("document_kind", "hd033_search_latency_in_memory");
            writer.WriteBoolean("live_three_device", report.LiveThreeDevice);
            writer.WriteBoolean("closes_ac19", report.ClosesAc19);
            writer.WriteBoolean("closes_ac21", report.ClosesAc21);
            writer.WriteBoolean("closes_ac28", report.ClosesAc28);
            writer.WriteBoolean("herdr_executed", false);
            writer.WriteBoolean("invented_timings", false);
            writer.WriteNumber("projection_count", report.ProjectionCount);
            writer.WriteNumber("partition_count", report.PartitionCount);
            writer.WriteNumber("sample_count", report.SampleCount);
            writer.WriteNumber("warmup_count", report.WarmupCount);
            writer.WriteNumber("result_count", report.ResultCount);
            writer.WriteNumber("terminal_process_delta", report.TerminalProcessDelta);
            writer.WriteNumber("p50_ms", report.P50Ms);
            writer.WriteNumber("p95_ms", report.P95Ms);
            writer.WriteNumber("max_ms", report.MaxMs);
            writer.WritePropertyName("sample_ms");
            writer.WriteStartArray();
            foreach (var sample in report.SampleMs)
                writer.WriteNumberValue(sample);
            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    static void SeedHundred(GlobalProjectionStore store)
    {
        var devices = new[] { DeviceA, DeviceB, DeviceC };
        var remaining = RequiredProjections;
        var index = 0;
        for (var d = 0; d < devices.Length; d++)
        {
            var device = devices[d];
            var session = new SessionKey(device, "named-session", "dev");
            var paneBudget = d == devices.Length - 1
                ? Math.Max(1, remaining - 4)
                : 30;
            var panes = new List<PaneProjection>(paneBudget);
            for (var i = 0; i < paneBudget; i++)
            {
                var workspace = i % 2 == 0 ? "w1" : "w2";
                var key = new PaneKey(session, workspace, "p" + index.ToString("D3"));
                panes.Add(Pane(key, "pane-" + index.ToString("D3")));
                index++;
            }

            WorkspaceProjection[] workspaces =
            [
                Workspace(session, "w1", "one", (ulong)panes.Count(item => item.Key.WorkspaceId == "w1")),
                Workspace(session, "w2", "two", (ulong)panes.Count(item => item.Key.WorkspaceId == "w2"))
            ];
            var projected = SessionState(session, workspaces, panes);
            var snapshot = new DeviceProjectionSnapshot(
                new ConnectionEpoch(d + 1),
                d + 1,
                ConnectionPhase.Ready,
                [new ProjectedDevice(device, Compatible(), [projected])]);
            var state = new DeviceSessionState(
                session,
                new ConnectionEpoch(d + 1),
                ConnectionPhase.Ready,
                DeviceFreshness.Current,
                Compatible(),
                snapshot,
                0,
                0,
                null,
                true,
                0,
                0,
                0,
                0);
            if (!store.ApplySession(state, d, "lab-" + d).Succeeded)
                throw new InvalidOperationException("search_seed_failed");
            remaining = RequiredProjections - store.DocumentCount;
        }
    }

    static CapabilityProfile Compatible() =>
        CapabilityGate.Evaluate(SchemaCompatibilityBinding.PinnedMatchingRuntimeForTests("0.9.0"), 22, "0.9.0");

    static WireEnum<AgentStatusKind> Idle() => new("idle", AgentStatusKind.Idle);

    static WorkspaceProjection Workspace(SessionKey session, string id, string label, ulong panes) =>
        new(session, id, 1, label, false, panes, 1, "t1", Idle(), null);

    static PaneProjection Pane(PaneKey key, string label) =>
        new(key, "term-" + key.PaneId, "t1", false, label, "claude", KnownAgentKind.Claude,
            "Claude", Idle(), 1);

    static AgentProjection Agent(PaneProjection pane) =>
        new(pane.Key.Session, pane.TerminalId, pane.TabId, pane.Key, pane.Label, pane.AgentRaw, pane.AgentKind,
            pane.DisplayAgent, pane.AgentStatus, pane.Focused, pane.Revision);

    static SessionProjection SessionState(
        SessionKey session,
        IReadOnlyList<WorkspaceProjection> workspaces,
        IReadOnlyList<PaneProjection> panes) =>
        new(session, "0.9.0", 22, workspaces.FirstOrDefault()?.WorkspaceId, "t1",
            panes.FirstOrDefault()?.Key.PaneId, workspaces, [], panes, [], panes.Select(Agent).ToArray());
}
