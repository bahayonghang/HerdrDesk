using System.Text.Json;
using System.Xml.Linq;
using HerdDesk.App;
using HerdDesk.App.Quality;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Quality;

if (args.Length >= 1 && args[0] == "--hd033-collect")
{
    var dest = args.Length >= 2
        ? args[1]
        : Path.Combine(RepoRoot(), "probe-results");
    return Hd033LocalCapture.Run(RepoRoot(), dest);
}

if (args.Length >= 1 && args[0] == "--hd033-ui-smoke")
{
    var dest = args.Length >= 2
        ? args[1]
        : Path.Combine(RepoRoot(), "probe-results");
    return Hd033LocalCapture.RunUiSmoke(RepoRoot(), dest);
}

var cases = new (string Name, Action Run)[]
{
    ("xaml four zones and automation names", XamlSurface),
    ("activation file lock is cross-process", ActivationLock),
    ("named panes stay isolated and selection is observe-only", NavigationIdentity),
    ("search composition does not open palette", SearchComposition),
    ("settings diagnostics about remain reachable", Routes),
    ("narrow overlay keeps breadcrumb", Responsive),
    ("hd033 collectors run from start state", Hd033Collectors)
};

var failed = 0;
foreach (var test in cases)
{
    try
    {
        test.Run();
        Console.WriteLine("PASS " + test.Name);
    }
    catch (Exception error)
    {
        failed++;
        Console.Error.WriteLine("FAIL " + test.Name + ": " + error.GetType().Name + " " + error.Message);
    }
}

Console.WriteLine(
    $"{cases.Length - failed}/{cases.Length} integration windows tests passed; no live WinUI window.");
return failed == 0 ? 0 : 1;

static void Check(bool condition)
{
    if (!condition)
        throw new Exception("assertion_failed");
}

static string RepoRoot()
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

static void XamlSurface()
{
    var app = Path.Combine(RepoRoot(), "src", "HerdDesk.App");
    var found = Directory.EnumerateFiles(app, "*.xaml", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(app, path).Replace('\\', '/'))
        .Where(rel => rel.Split('/').All(part =>
            !part.Equals("bin", StringComparison.OrdinalIgnoreCase) &&
            !part.Equals("obj", StringComparison.OrdinalIgnoreCase)))
        .OrderBy(item => item, StringComparer.Ordinal)
        .ToArray();
    Check(found.SequenceEqual(ShellSurface.Pages));
    var joined = string.Join('\n', found.Select(rel => File.ReadAllText(Path.Combine(app, rel))));
    foreach (var name in ShellSurface.AutomationNames)
        Check(joined.Contains("AutomationProperties.Name=\"" + name + "\"", StringComparison.Ordinal));
    foreach (var name in AccessibilityNameCatalog.ShellAutomationNames)
        Check(AccessibilityNameCatalog.XamlDeclares(RepoRoot(), name));
    Check(joined.Contains("x:Class=\"HerdDesk.App." + ShellSurface.WindowTypeName + "\"", StringComparison.Ordinal));
    var shell = XDocument.Load(Path.Combine(app, "Views", "ShellPage.xaml"));
    Check(shell.Root is not null);
    var host = File.ReadAllText(Path.Combine(app, "Controls", "TerminalHost.xaml"));
    Check(host.Contains("<WebView2", StringComparison.Ordinal));
    Check(!File.Exists(Path.Combine(app, "Devices", "EditDevicePage.xaml")));
}

static void ActivationLock()
{
    var root = Path.Combine(Path.GetTempPath(), "herddesk-hd011-iw-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var intent = Path.Combine(root, "intent.json");
        var name = "HerdDeskHD011IW" + Guid.NewGuid().ToString("N");
        using var primary = AppActivationCoordinator.Claim(name, intent);
        using var secondary = AppActivationCoordinator.Claim(name, intent);
        Check(primary.IsPrimary);
        Check(primary.OwnsConfigurationWriter);
        Check(!secondary.IsPrimary);
        Check(!secondary.OwnsConfigurationWriter);
        Check(typeof(AppActivationCoordinator).GetField("_lockStream",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) is not null);
        Check(secondary.Redirect(new ActivationIntent(ActivationKind.Settings)));
        var received = primary.PollRedirect();
        Check(received is not null);
        Check(received!.Kind == ActivationKind.Settings);
        Check(typeof(ActivationIntent).GetProperty("Command") is null);
        Check(typeof(ActivationIntent).GetProperty("Input") is null);
        Check(typeof(ActivationIntent).GetProperty("Takeover") is null);
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

static void NavigationIdentity()
{
    var deviceA = new DeviceId(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
    var deviceB = new DeviceId(Guid.Parse("11111111-2222-3333-4444-555555555555"));
    var sessionA = new SessionKey(deviceA, "named-session", "dev");
    var sessionB = new SessionKey(deviceB, "named-session", "dev");
    var paneA = new PaneKey(sessionA, "ws", "p1");
    var paneB = new PaneKey(sessionB, "ws", "p1");
    var catalog = new ProjectionCatalog
    {
        Snapshot = TwoPanes(deviceA, deviceB, sessionA, sessionB, paneA, paneB),
        DaemonAvailable = true,
        RendererReadyDefault = true
    };
    var shell = new ShellViewModel(new ShellDependencies
    {
        Profiles = new MemoryDeviceProfileStore
        {
            Snapshot = new ConfigurationSnapshot(
                1, 1,
                [
                    new DeviceProfile(deviceA, "lab", ConnectionKinds.Local, "/tmp/herdr",
                        [SessionProfile.Named("dev")])
                ])
        },
        Catalog = catalog
    });
    shell.StartAsync().AsTask().GetAwaiter().GetResult();
    shell.ExpandAll();
    var first = shell.VisibleItems.First(item => item.Pane == paneA);
    var second = shell.VisibleItems.First(item => item.Pane == paneB);
    Check(first.IdentityKey != second.IdentityKey);
    Check(first.Label == second.Label);
    shell.Select(first);
    Check(shell.Selection.Pane == paneA);
    Check(shell.Selection.Device == deviceA);
    Check(!shell.ControlVerified);
    Check(shell.Access != TerminalAccess.Controlling);
}

static void SearchComposition()
{
    var shell = EmptyShell();
    shell.StartAsync().AsTask().GetAwaiter().GetResult();
    shell.Search.IsComposing = true;
    Check(!shell.HandleAccelerator(ShellAccelerator.OpenSearch));
    Check(!shell.Search.IsOpen);
    shell.Search.IsComposing = false;
    Check(shell.HandleAccelerator(ShellAccelerator.OpenSearch));
    Check(shell.Search.IsOpen);
    Check(shell.HiddenTerminalBridgeCount == 0);
}

static void Routes()
{
    var shell = EmptyShell();
    shell.StartAsync().AsTask().GetAwaiter().GetResult();
    Check(shell.Lifecycle == ShellLifecycle.NoDevices);
    Check(!shell.DaemonOnline);
    shell.OpenSettings();
    Check(shell.Route == ShellRoute.Settings);
    Check(shell.SshAvailability.Kind == RouteAvailabilityKind.Disabled);
    shell.OpenDiagnostics();
    Check(shell.Route == ShellRoute.Diagnostics);
    Check(shell.Diagnostics.ImplicitWrites == 0);
    shell.OpenAbout();
    Check(shell.Route == ShellRoute.About);
    Check(shell.Diagnostics.AboutStatement == ProductInfo.IndependentClientStatement);
}

static void Responsive()
{
    var shell = EmptyShell();
    shell.StartAsync().AsTask().GetAwaiter().GetResult();
    shell.SetWidth(400);
    Check(shell.Layout == LayoutBreakpoint.Narrow);
    shell.ToggleNavigationOverlay();
    Check(shell.NavigationOverlayOpen);
    Check(!string.IsNullOrWhiteSpace(shell.Breadcrumb));
    shell.SetWidth(1400);
    Check(shell.Layout == LayoutBreakpoint.Wide);
}

static void Hd033Collectors()
{
    var root = RepoRoot();
    var manifest = EnvironmentManifestCollector.Collect(root);
    using var pin = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "global.json")));
    var sdk = pin.RootElement.GetProperty("sdk");
    Check(manifest.LogicalCpus >= 1);
    Check(manifest.SdkPinVersion == sdk.GetProperty("version").GetString());
    Check(manifest.SdkPinRollForward == sdk.GetProperty("rollForward").GetString());
    Check(!manifest.NarratorStartedByCollector);
    var search = SearchLatencyCollector.Measure();
    Check(search.ProjectionCount >= SearchLatencyCollector.RequiredProjections);
    Check(search.SampleCount == SearchLatencyCollector.DefaultSamples);
    Check(!search.LiveThreeDevice);
    Check(!search.ClosesAc19);
    Check(!search.ClosesAc21);
    Check(!search.ClosesAc28);
    Check(search.P95Ms < 100);
    var idle = ProcessResourceSampler.MeasureIdle(TimeSpan.FromMilliseconds(50));
    Check(!idle.EightHourSeries);
    Check(idle.After.ProcessId == Environment.ProcessId);
    Check(idle.After.OwnedProcessCount >= 0);
    if (!ColdStartSampler.TryResolveComposeOnly(AppContext.BaseDirectory, out var executable, out var prefix))
    {
        var net10 = Path.Combine(root, "src", "HerdDesk.App", "bin", "Release", "net10.0");
        if (!ColdStartSampler.TryResolveComposeOnly(net10, out executable, out prefix))
            throw new Exception("app_host_missing");
    }
    var temp = Path.Combine(Path.GetTempPath(), "herddesk-hd033-iw-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);
    try
    {
        var args = new List<string>(prefix.Count + 1);
        args.AddRange(prefix);
        args.Add(temp);
        var report = ColdStartSampler.Run(executable, args, 1, TimeSpan.FromSeconds(30), "compose-only");
        Check(report.SampleCount == 1);
        Check(!report.FirstInteractivePixel);
        Check(report.ParserConsumedIsNotPresentation);
        Check(!report.LaunchedUi);
        Check(report.Samples[0].ExitCode == 0);
    }
    finally
    {
        Directory.Delete(temp, true);
    }

    foreach (var name in AccessibilityNameCatalog.KeyboardAndNarratorNames)
        Check(!string.IsNullOrWhiteSpace(name));
}

static ShellViewModel EmptyShell() =>
    new(new ShellDependencies
    {
        Profiles = new MemoryDeviceProfileStore(),
        Catalog = new ProjectionCatalog()
    });

static DeviceProjectionSnapshot TwoPanes(
    DeviceId deviceA,
    DeviceId deviceB,
    SessionKey sessionA,
    SessionKey sessionB,
    PaneKey paneA,
    PaneKey paneB)
{
    var compatible = CapabilityGate.Evaluate(
        SchemaCompatibilityBinding.PinnedMatchingRuntimeForTests("0.9.0"), 22, "0.9.0");
    return new DeviceProjectionSnapshot(
        new ConnectionEpoch(1),
        1,
        ConnectionPhase.Ready,
        [
            DeviceOf(deviceA, compatible, sessionA, paneA),
            DeviceOf(deviceB, compatible, sessionB, paneB)
        ]);
}

static ProjectedDevice DeviceOf(
    DeviceId device,
    CapabilityProfile capabilities,
    SessionKey session,
    PaneKey pane)
{
    var idle = new WireEnum<AgentStatusKind>("idle", AgentStatusKind.Idle);
    var workspace = new WorkspaceProjection(session, "ws", 1, "lab", false, 1, 1, "t1", idle, null);
    var paneRow = new PaneProjection(
        pane, "term-" + pane.PaneId, "t1", false, "main", "claude", KnownAgentKind.Claude, "Claude", idle, 1);
    var sessionRow = new SessionProjection(
        session, "0.9.0", 22, "ws", "t1", pane.PaneId, [workspace], [], [paneRow], [],
        [
            new AgentProjection(
                session, paneRow.TerminalId, paneRow.TabId, pane, paneRow.Label, paneRow.AgentRaw,
                paneRow.AgentKind, paneRow.DisplayAgent, paneRow.AgentStatus, paneRow.Focused, paneRow.Revision)
        ]);
    return new ProjectedDevice(device, capabilities, [sessionRow]);
}

internal sealed class MemoryDeviceProfileStore : IDeviceProfileStore
{
    public ConfigurationSnapshot Snapshot { get; set; } = ConfigurationSnapshot.Empty;

    public ValueTask<ConfigurationLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        if (Snapshot.Devices.Count == 0)
            return ValueTask.FromResult(new ConfigurationLoadResult(null, ConfigurationCodes.Missing));
        return ValueTask.FromResult(new ConfigurationLoadResult(Snapshot, null));
    }

    public ValueTask<ConfigurationWriteResult> SaveDeviceAsync(
        DeviceProfile profile,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        _ = (profile, expectedRevision, cancellationToken);
        return ValueTask.FromResult(new ConfigurationWriteResult(null, ConfigurationCodes.InvalidIdentity));
    }

    public ValueTask<ConfigurationWriteResult> DeleteDeviceAsync(
        DeviceId device,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        _ = (device, expectedRevision, cancellationToken);
        return ValueTask.FromResult(new ConfigurationWriteResult(null, ConfigurationCodes.InvalidIdentity));
    }

    public ValueTask<ConfigurationWriteResult> RestoreFromBackupAsync(
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return ValueTask.FromResult(new ConfigurationWriteResult(null, ConfigurationCodes.InvalidIdentity));
    }
}
