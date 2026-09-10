using HerdDesk.App;
using HerdDesk.App.Quality;
using HerdDesk.Infrastructure.Quality;

internal static class Hd033CollectorTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("environment manifest collector starts from this process", EnvironmentFromStart),
        ("search latency collector uses one hundred store projections", SearchFromStart),
        ("idle and handle collectors sample a short window", IdleAndHandlesFromStart),
        ("cold-start sampler records one compose-only process", ColdStartFromStart),
        ("accessibility catalog matches shipped shell names", AccessibilityFromStart)
    ];

    static void EnvironmentFromStart()
    {
        var root = FindRepoRoot();
        var manifest = EnvironmentManifestCollector.Collect(root);
        AppTestHost.Check(manifest.LogicalCpus >= 1);
        AppTestHost.Check(manifest.SdkPinVersion == "10.0.400");
        AppTestHost.Check(manifest.SdkPinRollForward == "disable");
        AppTestHost.Check(!manifest.NarratorStartedByCollector);
        AppTestHost.Check(manifest.CapturedAtUtc != default);
        if (OperatingSystem.IsWindows())
        {
            AppTestHost.Check(manifest.IsWindows);
            AppTestHost.Check(manifest.SystemDpi is null or > 0);
        }

        var path = Path.Combine(AppTestHost.TempRoot(), "environment.json");
        EnvironmentManifestCollector.Write(manifest, path);
        var text = File.ReadAllText(path);
        AppTestHost.Check(text.Contains("\"document_kind\": \"hd033_environment_manifest\"", StringComparison.Ordinal));
        AppTestHost.Check(text.Contains("\"herdr_executed\": false", StringComparison.Ordinal));
        AppTestHost.Check(text.Contains("\"live_narrator\": false", StringComparison.Ordinal));
        AppTestHost.Check(!text.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    static void SearchFromStart()
    {
        var report = SearchLatencyCollector.Measure();
        AppTestHost.Check(report.ProjectionCount >= SearchLatencyCollector.RequiredProjections);
        AppTestHost.Check(report.PartitionCount == 3);
        AppTestHost.Check(report.SampleCount == SearchLatencyCollector.DefaultSamples);
        AppTestHost.Check(report.SampleMs.Count == report.SampleCount);
        AppTestHost.Check(report.ResultCount >= SearchLatencyCollector.RequiredProjections);
        AppTestHost.Check(report.TerminalProcessDelta == 0);
        AppTestHost.Check(!report.LiveThreeDevice);
        AppTestHost.Check(!report.ClosesAc19);
        AppTestHost.Check(!report.ClosesAc21);
        AppTestHost.Check(!report.ClosesAc28);
        AppTestHost.Check(report.P95Ms < 100);
        AppTestHost.Check(report.P50Ms <= report.P95Ms);
        AppTestHost.Check(report.P95Ms <= report.MaxMs);
        var summary = EmpiricalPercentile.Summarize(report.SampleMs);
        AppTestHost.Check(summary.P95 == report.P95Ms);
        var path = Path.Combine(AppTestHost.TempRoot(), "search.json");
        SearchLatencyCollector.Write(report, path);
        var text = File.ReadAllText(path);
        AppTestHost.Check(text.Contains("\"live_three_device\": false", StringComparison.Ordinal));
        AppTestHost.Check(text.Contains("\"closes_ac19\": false", StringComparison.Ordinal));
        AppTestHost.Check(text.Contains("\"closes_ac21\": false", StringComparison.Ordinal));
        AppTestHost.Check(text.Contains("\"closes_ac28\": false", StringComparison.Ordinal));
        AppTestHost.Check(text.Contains("\"herdr_executed\": false", StringComparison.Ordinal));
        AppTestHost.Check(text.Contains("\"invented_timings\": false", StringComparison.Ordinal));
    }

    static void IdleAndHandlesFromStart()
    {
        var before = ProcessResourceSampler.CaptureCurrent();
        AppTestHost.Check(before.ProcessId == Environment.ProcessId);
        AppTestHost.Check(before.TotalProcessorSeconds >= 0);
        AppTestHost.Check(before.OwnedProcessCount >= 0);
        var sample = ProcessResourceSampler.MeasureIdle(TimeSpan.FromMilliseconds(50));
        AppTestHost.Check(!sample.EightHourSeries);
        AppTestHost.Check(sample.LogicalCpus >= 1);
        AppTestHost.Check(sample.WallSeconds > 0);
        AppTestHost.Check(sample.After.ProcessId == before.ProcessId);
        AppTestHost.Check(sample.After.TotalProcessorSeconds >= sample.Before.TotalProcessorSeconds);
        if (OperatingSystem.IsWindows())
        {
            AppTestHost.Check(sample.After.HandleCount is > 0);
            AppTestHost.Check(sample.After.WorkingSetBytes is > 0);
        }

        var path = Path.Combine(AppTestHost.TempRoot(), "idle.json");
        ProcessResourceSampler.Write(sample, path);
        var text = File.ReadAllText(path);
        AppTestHost.Check(text.Contains("\"eight_hour_series\": false", StringComparison.Ordinal));
        AppTestHost.Check(text.Contains("\"eight_hour_soak_executed\": false", StringComparison.Ordinal));
        AppTestHost.Check(text.Contains("\"herdr_executed\": false", StringComparison.Ordinal));
        AppTestHost.Check(text.Contains("\"invented_timings\": false", StringComparison.Ordinal));
        AppTestHost.Check(text.Contains("\"mib_bytes\": 1048576", StringComparison.Ordinal));
    }

    static void ColdStartFromStart()
    {
        if (!TryResolveComposeOnly(out var executable, out var prefix))
            throw new Exception("app_host_missing");
        var root = AppTestHost.TempRoot();
        var args = new List<string>(prefix.Count + 1);
        args.AddRange(prefix);
        args.Add(root);
        var report = ColdStartSampler.Run(
            executable, args, 1, TimeSpan.FromSeconds(30), "compose-only");
        AppTestHost.Check(report.SampleCount == 1);
        AppTestHost.Check(!report.FirstInteractivePixel);
        AppTestHost.Check(report.ParserConsumedIsNotPresentation);
        AppTestHost.Check(!report.LaunchedUi);
        AppTestHost.Check(report.Samples[0].ExitCode == 0);
        AppTestHost.Check(!report.Samples[0].TimedOut);
        AppTestHost.Check(report.P95Ms > 0);
        var path = Path.Combine(AppTestHost.TempRoot(), "cold-start.json");
        ColdStartSampler.Write(report, path);
        var text = File.ReadAllText(path);
        AppTestHost.Check(text.Contains("\"first_interactive_pixel\": false", StringComparison.Ordinal));
        AppTestHost.Check(text.Contains("\"parser_consumed_is_not_presentation\": true", StringComparison.Ordinal));
        AppTestHost.Check(text.Contains("\"launched_ui\": false", StringComparison.Ordinal));
        AppTestHost.Check(text.Contains("\"herdr_executed\": false", StringComparison.Ordinal));
        AppTestHost.Check(text.Contains("\"invented_timings\": false", StringComparison.Ordinal));
    }

    static void AccessibilityFromStart()
    {
        var root = FindRepoRoot();
        AppTestHost.Check(AccessibilityNameCatalog.ShellAutomationNames.SequenceEqual(ShellSurface.AutomationNames));
        var xaml = AccessibilityNameCatalog.JoinedXaml(root);
        foreach (var name in ShellSurface.AutomationNames)
        {
            AppTestHost.Check(AccessibilityNameCatalog.XamlDeclares(root, name));
            AppTestHost.Check(xaml.Contains("AutomationProperties.Name=\"" + name + "\"", StringComparison.Ordinal));
        }

        foreach (var name in AccessibilityNameCatalog.KeyboardAndNarratorNames)
            AppTestHost.Check(!string.IsNullOrWhiteSpace(name));
        AppTestHost.Check(AccessibilityNameCatalog.KeyboardAndNarratorNames.Contains(ShellStrings.RequestControl));
        AppTestHost.Check(AccessibilityNameCatalog.KeyboardAndNarratorNames.Contains(ShellStrings.ReleaseControl));
        AppTestHost.Check(AccessibilityNameCatalog.KeyboardAndNarratorNames.Contains(ShellStrings.ConfirmClose));
    }

    static bool TryResolveComposeOnly(out string executable, out List<string> arguments)
    {
        if (ColdStartSampler.TryResolveComposeOnly(AppContext.BaseDirectory, out executable, out arguments))
            return true;
        var net10 = Path.Combine(FindRepoRoot(), "src", "HerdDesk.App", "bin", "Release", "net10.0");
        return ColdStartSampler.TryResolveComposeOnly(net10, out executable, out arguments);
    }

    static string FindRepoRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "HerdDesk.slnx")))
                return directory;
            directory = Directory.GetParent(directory)?.FullName ?? "";
        }

        throw new Exception("repo_root_missing");
    }
}
