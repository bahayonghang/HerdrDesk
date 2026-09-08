using System.Diagnostics;
using System.Text;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Configuration;
using HerdDesk.Infrastructure.Diagnostics;
using HerdDesk.Infrastructure.Ssh;

internal static class RemoteHelperDeploymentServiceTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("confirm without consent does not write remote files", ZeroConsent),
        ("tampered payload fails before upload", TamperBeforeUpload),
        ("version or hash change after plan requires a new consent", ConsentStale),
        ("cancel at awaiting consent leaves no always-allow and no remote write", CancelNoPersist),
        ("cancel during upload removes only this staging", CancelDuringUpload),
        ("hard-link no-clobber and selftest publish a private version", PublishPrivate),
        ("selftest failure removes the final created by this job", SelftestFailure),
        ("receipt write failure leaves unpublished current", ReceiptWriteFailure),
        ("concurrent different hash cannot replace a hash in use", ConcurrentDifferentHash),
        ("unsupported hard-link leaves helper unavailable", HardLinkUnavailable),
        ("canary host path stderr stay out of results and logs", RedactedLogs)
    ];

    static void ZeroConsent()
    {
        var root = HelperFixtures.TempRoot();
        try
        {
            var runner = new FakeHelperRemoteRunner();
            var service = HelperFixtures.Service(root, runner);
            var result = service.ConfirmAsync(
                    new HelperConsentValues(HelperFixtures.Device, "0.1.0", HelperFixtures.Sha(HelperFixtures.Payload)))
                .AsTask().GetAwaiter().GetResult();
            HelperFixtures.Check(result.Code == HelperCodes.ConsentRequired);
            HelperFixtures.Check(runner.Remote.Mutations == 0);
            HelperFixtures.Check(!runner.Started.Any(item => item.Kind == SshProcessKind.HelperBootstrap));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void TamperBeforeUpload()
    {
        var runner = new FakeHelperRemoteRunner();
        var planner = HelperFixtures.Planner(runner);
        var planned = planner.PlanAsync(HelperFixtures.Device, HelperFixtures.Session, HelperFixtures.Settings())
            .AsTask().GetAwaiter().GetResult();
        HelperFixtures.Check(planned.Succeeded);
        var tampered = planned.Payload!.ToArray();
        tampered[0] ^= 0xff;
        var started = runner.Started.Count;
        var published = HelperFixtures.Publisher(runner).PublishAsync(
                planned.Plan!,
                HelperFixtures.Settings(),
                tampered,
                HelperRemoteScripts.NewStagingId())
            .AsTask().GetAwaiter().GetResult();
        HelperFixtures.Check(published.Code == HelperCodes.HashMismatch);
        HelperFixtures.Check(runner.Started.Count == started);
        HelperFixtures.Check(!runner.Started.Any(item => item.Kind == SshProcessKind.HelperBootstrap));
        HelperFixtures.Check(runner.Remote.Mutations == 0);
    }

    static void ConsentStale()
    {
        var root = HelperFixtures.TempRoot();
        try
        {
            var runner = new FakeHelperRemoteRunner();
            var service = HelperFixtures.Service(root, runner);
            var planned = service.PlanAsync(
                    HelperFixtures.Device, HelperFixtures.Session, HelperFixtures.Settings())
                .AsTask().GetAwaiter().GetResult();
            HelperFixtures.Check(planned.Phase == HelperDeploymentPhase.AwaitingConsent);
            var stale = service.ConfirmAsync(
                    new HelperConsentValues(HelperFixtures.Device, "9.9.9", planned.Plan!.Sha256))
                .AsTask().GetAwaiter().GetResult();
            HelperFixtures.Check(stale.Code == HelperCodes.ConsentStale);
            HelperFixtures.Check(!runner.Started.Any(item => item.Kind == SshProcessKind.HelperBootstrap));
            var hashStale = service.ConfirmAsync(
                    new HelperConsentValues(HelperFixtures.Device, planned.Plan.Version, new string('a', 64)))
                .AsTask().GetAwaiter().GetResult();
            HelperFixtures.Check(hashStale.Code == HelperCodes.ConsentStale);
            HelperFixtures.Check(runner.Remote.Mutations == 0);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void CancelNoPersist()
    {
        var root = HelperFixtures.TempRoot();
        try
        {
            var runner = new FakeHelperRemoteRunner();
            var service = HelperFixtures.Service(root, runner);
            _ = service.PlanAsync(HelperFixtures.Device, HelperFixtures.Session, HelperFixtures.Settings())
                .AsTask().GetAwaiter().GetResult();
            var cancelled = service.CancelAsync().AsTask().GetAwaiter().GetResult();
            HelperFixtures.Check(cancelled.Code == HelperCodes.Cancelled);
            HelperFixtures.Check(cancelled.Phase == HelperDeploymentPhase.Cancelled);
            HelperFixtures.Check(runner.Remote.Mutations == 0);
            HelperFixtures.Check(typeof(RemoteHelperDeploymentService).GetProperty("AlwaysAllow") is null);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void CancelDuringUpload()
    {
        var root = HelperFixtures.TempRoot();
        try
        {
            var runner = new FakeHelperRemoteRunner();
            var service = HelperFixtures.Service(root, runner);
            var planned = service.PlanAsync(
                    HelperFixtures.Device, HelperFixtures.Session, HelperFixtures.Settings())
                .AsTask().GetAwaiter().GetResult();
            HelperFixtures.Check(planned.Phase == HelperDeploymentPhase.AwaitingConsent);
            runner.Delay = TimeSpan.FromSeconds(30);
            var confirm = service.ConfirmAsync(
                    new HelperConsentValues(planned.Plan!.Device, planned.Plan.Version, planned.Plan.Sha256))
                .AsTask();
            var waited = Stopwatch.StartNew();
            while (!runner.Started.Any(item => item.Kind == SshProcessKind.HelperBootstrap) &&
                   waited.Elapsed < TimeSpan.FromSeconds(5))
                Thread.Sleep(10);
            HelperFixtures.Check(runner.Started.Any(item => item.Kind == SshProcessKind.HelperBootstrap));
            var cancelled = service.CancelAsync().AsTask().GetAwaiter().GetResult();
            HelperFixtures.Check(cancelled.Code == HelperCodes.Cancelled);
            HelperFixtures.Check(cancelled.Phase == HelperDeploymentPhase.Cancelled);
            var confirmResult = confirm.GetAwaiter().GetResult();
            HelperFixtures.Check(confirmResult.Code == HelperCodes.Cancelled);
            HelperFixtures.Check(!runner.Remote.Exists(
                runner.Remote.FinalPath(planned.Plan.Version, planned.Plan.Target.Triple)));
            HelperFixtures.Check(runner.Remote.StagingCount == 0);
            HelperFixtures.Check(typeof(RemoteHelperDeploymentService).GetProperty("AlwaysAllow") is null);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void PublishPrivate()
    {
        var root = HelperFixtures.TempRoot();
        try
        {
            var runner = new FakeHelperRemoteRunner();
            var service = HelperFixtures.Service(root, runner);
            var planned = service.PlanAsync(
                    HelperFixtures.Device, HelperFixtures.Session, HelperFixtures.Settings())
                .AsTask().GetAwaiter().GetResult();
            HelperFixtures.Check(planned.Phase == HelperDeploymentPhase.AwaitingConsent);
            HelperFixtures.Check(planned.Plan!.PrivateDirectory.StartsWith("/home/lab/.herddesk/helper/",
                StringComparison.Ordinal));
            var confirmed = service.ConfirmAsync(
                    new HelperConsentValues(planned.Plan.Device, planned.Plan.Version, planned.Plan.Sha256))
                .AsTask().GetAwaiter().GetResult();
            HelperFixtures.Check(confirmed.Succeeded);
            HelperFixtures.Check(confirmed.Receipt is not null);
            var final = runner.Remote.FinalPath(planned.Plan.Version, planned.Plan.Target.Triple);
            HelperFixtures.Check(runner.Remote.Exists(final));
            HelperFixtures.Check(runner.Remote.Read(final)!.SequenceEqual(HelperFixtures.Payload));
            HelperFixtures.Check(!final.StartsWith("/usr", StringComparison.Ordinal));
            HelperFixtures.Check(!final.StartsWith("/opt", StringComparison.Ordinal));
            HelperFixtures.Check(runner.Started.Any(item => item.Kind == SshProcessKind.HelperBootstrap));
            HelperFixtures.Check(runner.Started.Where(item => item.Kind == SshProcessKind.HelperBootstrap)
                .All(item => item.Arguments.Contains("-T")));
            HelperFixtures.Check(runner.Started.SelectMany(item => item.Arguments)
                .All(item => !RemoteHelperPublisher.ForbiddenArgument(item)));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void SelftestFailure()
    {
        var root = HelperFixtures.TempRoot();
        try
        {
            var runner = new FakeHelperRemoteRunner();
            runner.Remote.SelftestSucceeds = false;
            var service = HelperFixtures.Service(root, runner);
            var planned = service.PlanAsync(
                    HelperFixtures.Device, HelperFixtures.Session, HelperFixtures.Settings())
                .AsTask().GetAwaiter().GetResult();
            var confirmed = service.ConfirmAsync(
                    new HelperConsentValues(planned.Plan!.Device, planned.Plan.Version, planned.Plan.Sha256))
                .AsTask().GetAwaiter().GetResult();
            HelperFixtures.Check(confirmed.Code == HelperCodes.SelftestFailed);
            HelperFixtures.Check(!runner.Remote.Exists(
                runner.Remote.FinalPath(planned.Plan.Version, planned.Plan.Target.Triple)));
            HelperFixtures.Check(runner.Remote.StagingCount == 0);
            var current = service.Receipts.ReadCurrentAsync(new HelperReceiptKey(
                    planned.Plan.Device,
                    planned.Plan.Session.EndpointKey,
                    planned.Plan.Session.SessionName,
                    planned.Plan.Target.Triple,
                    planned.Plan.RemoteHomeSha256))
                .AsTask().GetAwaiter().GetResult();
            HelperFixtures.Check(current is null);

            runner.Remote.SelftestSucceeds = true;
            var retryPlan = service.PlanAsync(
                    HelperFixtures.Device, HelperFixtures.Session, HelperFixtures.Settings())
                .AsTask().GetAwaiter().GetResult();
            var retry = service.ConfirmAsync(
                    new HelperConsentValues(retryPlan.Plan!.Device, retryPlan.Plan.Version, retryPlan.Plan.Sha256))
                .AsTask().GetAwaiter().GetResult();
            HelperFixtures.Check(retry.Succeeded);
            HelperFixtures.Check(runner.Remote.Exists(
                runner.Remote.FinalPath(retryPlan.Plan.Version, retryPlan.Plan.Target.Triple)));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void ReceiptWriteFailure()
    {
        var root = HelperFixtures.TempRoot();
        try
        {
            var runner = new FakeHelperRemoteRunner();
            var service = HelperFixtures.Service(root, runner, hooks: new HelperReceiptStoreHooks
            {
                Commit = (_, _, _) => throw new IOException("replace_boom")
            });
            var planned = service.PlanAsync(
                    HelperFixtures.Device, HelperFixtures.Session, HelperFixtures.Settings())
                .AsTask().GetAwaiter().GetResult();
            var confirmed = service.ConfirmAsync(
                    new HelperConsentValues(planned.Plan!.Device, planned.Plan.Version, planned.Plan.Sha256))
                .AsTask().GetAwaiter().GetResult();
            HelperFixtures.Check(confirmed.Code == HelperCodes.ActivationFailed);
            HelperFixtures.Check(runner.Remote.Exists(
                runner.Remote.FinalPath(planned.Plan.Version, planned.Plan.Target.Triple)));
            HelperFixtures.Check(service.Receipts.ReadCurrentAsync(new HelperReceiptKey(
                    planned.Plan.Device,
                    planned.Plan.Session.EndpointKey,
                    planned.Plan.Session.SessionName,
                    planned.Plan.Target.Triple,
                    planned.Plan.RemoteHomeSha256))
                .AsTask().GetAwaiter().GetResult() is null);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void ConcurrentDifferentHash()
    {
        var root = HelperFixtures.TempRoot();
        try
        {
            var shared = new FakeHelperRemoteRunner();
            var first = HelperFixtures.Service(root, shared, HelperFixtures.BuiltProvider());
            var planned = first.PlanAsync(
                    HelperFixtures.Device, HelperFixtures.Session, HelperFixtures.Settings())
                .AsTask().GetAwaiter().GetResult();
            var ok = first.ConfirmAsync(
                    new HelperConsentValues(planned.Plan!.Device, planned.Plan.Version, planned.Plan.Sha256))
                .AsTask().GetAwaiter().GetResult();
            HelperFixtures.Check(ok.Succeeded);
            HelperFixtures.Check(first.Receipts.AcquireLeaseAsync(
                    ok.Receipt!.Key, new ConnectionEpoch(4), planned.Plan.Version, planned.Plan.Sha256)
                .AsTask().GetAwaiter().GetResult() is null);

            var otherManifest = HelperFixtures.BuiltProvider(HelperFixtures.OtherPayload);
            var secondRunner = new FakeHelperRemoteRunner(shared.Remote);
            var second = HelperFixtures.Service(root, secondRunner, otherManifest);
            var secondPlan = second.PlanAsync(
                    HelperFixtures.Device, HelperFixtures.Session, HelperFixtures.Settings())
                .AsTask().GetAwaiter().GetResult();
            HelperFixtures.Check(secondPlan.Phase == HelperDeploymentPhase.AwaitingConsent);
            HelperFixtures.Check(secondPlan.Plan is not null);
            var published = second.ConfirmAsync(
                    new HelperConsentValues(
                        secondPlan.Plan!.Device, secondPlan.Plan.Version, secondPlan.Plan.Sha256))
                .AsTask().GetAwaiter().GetResult();
            HelperFixtures.Check(published.Code == HelperCodes.VersionCollision);
            var final = shared.Remote.Read(shared.Remote.FinalPath(planned.Plan.Version, planned.Plan.Target.Triple));
            HelperFixtures.Check(final!.SequenceEqual(HelperFixtures.Payload));
            var current = first.Receipts.ReadCurrentAsync(ok.Receipt.Key).AsTask().GetAwaiter().GetResult();
            HelperFixtures.Check(current!.Sha256 == planned.Plan.Sha256);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void HardLinkUnavailable()
    {
        var root = HelperFixtures.TempRoot();
        try
        {
            var runner = new FakeHelperRemoteRunner();
            runner.Remote.HardLinkSupported = false;
            var service = HelperFixtures.Service(root, runner);
            var planned = service.PlanAsync(
                    HelperFixtures.Device, HelperFixtures.Session, HelperFixtures.Settings())
                .AsTask().GetAwaiter().GetResult();
            var confirmed = service.ConfirmAsync(
                    new HelperConsentValues(planned.Plan!.Device, planned.Plan.Version, planned.Plan.Sha256))
                .AsTask().GetAwaiter().GetResult();
            HelperFixtures.Check(confirmed.Code == HelperCodes.PlatformUnsupported);
            HelperFixtures.Check(!runner.Remote.Exists(
                runner.Remote.FinalPath(planned.Plan.Version, planned.Plan.Target.Triple)));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void RedactedLogs()
    {
        var root = HelperFixtures.TempRoot();
        try
        {
            var paths = AppDataPaths.FromRoot(root);
            Directory.CreateDirectory(paths.SettingsDirectory);
            Directory.CreateDirectory(paths.LogDirectory);
            var runner = new FakeHelperRemoteRunner();
            runner.Remote.ProbeStderr = HelperFixtures.CanaryStderr;
            runner.Remote.ProbeStdout = "linux\nx86_64\n" + HelperFixtures.CanaryPath + "\n";
            var sink = new JsonlDiagnosticSink(paths.DiagnosticLogFile);
            var aliases = DiagnosticAliasProjector.LoadOrCreate(paths.DiagnosticSaltFile);
            var service = new RemoteHelperDeploymentService(
                paths,
                HelperFixtures.BuiltProvider(),
                SshFixtures.Locator(),
                runner,
                diagnostics: sink,
                aliases: aliases);
            var planned = service.PlanAsync(
                    HelperFixtures.Device, HelperFixtures.Session, HelperFixtures.Settings())
                .AsTask().GetAwaiter().GetResult();
            HelperFixtures.Check(planned.Code == HelperCodes.PlatformUnsupported);
            sink.DisposeAsync().AsTask().GetAwaiter().GetResult();
            var logs = File.Exists(paths.DiagnosticLogFile)
                ? File.ReadAllText(paths.DiagnosticLogFile, Encoding.UTF8)
                : "";
            HelperFixtures.Check(!logs.Contains(HelperFixtures.CanaryHost, StringComparison.Ordinal));
            HelperFixtures.Check(!logs.Contains(HelperFixtures.CanaryPath, StringComparison.Ordinal));
            HelperFixtures.Check(!logs.Contains(HelperFixtures.CanaryStderr, StringComparison.Ordinal));
            HelperFixtures.Check(planned.Code == HelperCodes.PlatformUnsupported);
            var dumped = planned.ToString();
            HelperFixtures.Check(!dumped.Contains(HelperFixtures.CanaryStderr, StringComparison.Ordinal));
            foreach (var spec in runner.Started)
            {
                HelperFixtures.Check(!spec.Arguments.Contains(HelperFixtures.CanaryStderr));
                HelperFixtures.Check(!spec.Arguments.Contains(HelperFixtures.CanaryPath));
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
