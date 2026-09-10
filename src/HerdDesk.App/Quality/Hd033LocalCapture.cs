using HerdDesk.Infrastructure.Quality;

namespace HerdDesk.App.Quality;

public static class Hd033LocalCapture
{
    public const int ColdStartCount = 30;

    public static int Run(string repoRoot, string outputDirectory, int coldStartCount = ColdStartCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        if (coldStartCount < 1)
            throw new ArgumentOutOfRangeException(nameof(coldStartCount));
        Directory.CreateDirectory(outputDirectory);

        var environment = EnvironmentManifestCollector.Collect(repoRoot);
        EnvironmentManifestCollector.Write(
            environment, Path.Combine(outputDirectory, "hd033-environment-manifest.json"));

        var search = SearchLatencyCollector.Measure();
        SearchLatencyCollector.Write(search, Path.Combine(outputDirectory, "hd033-search-latency.json"));

        var idle = ProcessResourceSampler.MeasureIdle(TimeSpan.FromMilliseconds(200));
        ProcessResourceSampler.Write(idle, Path.Combine(outputDirectory, "hd033-idle-short-window.json"));
        ProcessResourceSampler.WriteSnapshot(
            idle.After, Path.Combine(outputDirectory, "hd033-handle-snapshot.json"));

        var net10 = Path.Combine(repoRoot, "src", "HerdDesk.App", "bin", "Release", "net10.0");
        var composeCount = 0;
        if (ColdStartSampler.TryResolveComposeOnly(net10, out var composeExe, out var composeArgs) ||
            ColdStartSampler.TryResolveComposeOnly(AppContext.BaseDirectory, out composeExe, out composeArgs))
        {
            var roots = new List<string>(coldStartCount);
            try
            {
                var report = ColdStartSampler.Run(
                    composeExe,
                    composeArgs,
                    coldStartCount,
                    TimeSpan.FromSeconds(30),
                    "compose-only",
                    index =>
                    {
                        var root = Path.Combine(
                            Path.GetTempPath(),
                            "herddesk-hd033-compose-" + index.ToString("D2") + "-" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(root);
                        roots.Add(root);
                        var args = new List<string>(composeArgs.Count + 1);
                        args.AddRange(composeArgs);
                        args.Add(root);
                        return args;
                    });
                ColdStartSampler.Write(report, Path.Combine(outputDirectory, "hd033-cold-start-compose-only.json"));
                composeCount = report.SampleCount;
            }
            finally
            {
                foreach (var root in roots)
                {
                    try
                    {
                        Directory.Delete(root, true);
                    }
                    catch (IOException)
                    {
                    }
                }
            }
        }

        var windowsHost = Path.Combine(
            repoRoot,
            "src",
            "HerdDesk.App",
            "bin",
            "Release",
            "net10.0-windows10.0.19041.0",
            "win-x64");
        var smokeCount = 0;
        if (ColdStartSampler.TryResolveShellSmoke(windowsHost, out var smokeExe, out var smokeArgs))
        {
            var smoke = ColdStartSampler.Run(
                smokeExe,
                smokeArgs,
                coldStartCount,
                TimeSpan.FromSeconds(30),
                "shell-smoke");
            ColdStartSampler.Write(smoke, Path.Combine(outputDirectory, "hd033-cold-start-shell-smoke.json"));
            smokeCount = smoke.SampleCount;
        }

        Console.WriteLine("hd033_local_capture=written");
        Console.WriteLine("environment=1");
        Console.WriteLine("search_samples=" + search.SampleCount);
        Console.WriteLine("search_p95_ms=" + search.P95Ms.ToString("0.###"));
        Console.WriteLine("compose_only_samples=" + composeCount);
        Console.WriteLine("shell_smoke_samples=" + smokeCount);
        Console.WriteLine("first_interactive_pixel=false");
        Console.WriteLine("live_unverified=true");
        return composeCount + smokeCount > 0 || search.SampleCount > 0 ? 0 : 1;
    }

    public static int RunUiSmoke(string repoRoot, string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var windowsHost = Path.Combine(
            repoRoot,
            "src",
            "HerdDesk.App",
            "bin",
            "Release",
            "net10.0-windows10.0.19041.0",
            "win-x64");
        var host = ColdStartSampler.FindAppHost(windowsHost);
        if (host is null)
        {
            Console.WriteLine("ui_smoke=skipped_no_host");
            return 0;
        }

        var root = Path.Combine(Path.GetTempPath(), "herddesk-hd033-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var report = ColdStartSampler.Run(
                host, ["--ui", root], 1, TimeSpan.FromSeconds(8), "ui");
            ColdStartSampler.Write(report, Path.Combine(outputDirectory, "hd033-ui-smoke.json"));
            Console.WriteLine("ui_smoke=recorded");
            Console.WriteLine("timed_out=" + report.Samples[0].TimedOut);
            Console.WriteLine("exit_code=" + report.Samples[0].ExitCode);
            Console.WriteLine("first_interactive_pixel=false");
            Console.WriteLine("live_unverified=true");
            return 0;
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
