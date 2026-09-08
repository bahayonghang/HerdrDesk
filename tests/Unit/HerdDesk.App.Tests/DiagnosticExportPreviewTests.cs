using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Diagnostics;

internal static class DiagnosticExportPreviewTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("default preview is redacted and requires confirm", RedactedPreview),
        ("unavailable providers still open diagnostics", ProviderUnavailable)
    ];

    static void RedactedPreview()
    {
        var snapshot = AppTestHost.TwoNamedPanes();
        var catalog = new ProjectionCatalog
        {
            Snapshot = snapshot,
            DaemonAvailable = true,
            LastErrorCode = "rpc_connection_lost",
            QueueBytes = 12
        };
        var aliases = new DiagnosticAliasProjector(new byte[32]);
        var shell = new ShellViewModel(new ShellDependencies
        {
            Profiles = new MemoryDeviceProfileStore
            {
                Snapshot = new ConfigurationSnapshot(
                    1, 1,
                    [
                        new DeviceProfile(
                            AppTestHost.DeviceA, "lab", ConnectionKinds.Local, "/tmp/herdr",
                            [SessionProfile.Named("dev")])
                    ])
            },
            Catalog = catalog,
            Aliases = aliases,
            Unavailable = [new UnavailableCapability("rpc-connection", "rpc_bridge_unavailable")]
        });
        shell.StartAsync().AsTask().GetAwaiter().GetResult();
        shell.OpenDiagnostics();
        AppTestHost.Check(shell.Diagnostics.Opened);
        AppTestHost.Check(shell.Diagnostics.ImplicitWrites == 0);
        var preview = shell.Diagnostics.Preview!;
        AppTestHost.Check(preview.IsRedactedDefault);
        AppTestHost.Check(!preview.Confirmed);
        var joined = string.Join('\n', preview.Fields.Select(item => item.Name + "=" + item.Value));
        AppTestHost.Check(!joined.Contains("password", StringComparison.OrdinalIgnoreCase));
        AppTestHost.Check(!joined.Contains("/tmp/herdr", StringComparison.Ordinal));
        AppTestHost.Check(!joined.Contains(AppTestHost.DeviceA.Value.ToString("D"), StringComparison.Ordinal));
        AppTestHost.Check(preview.Fields.Any(item => item.Name == "error_category"));
        AppTestHost.Check(preview.Fields.Any(item => item.Name == "epoch"));
        AppTestHost.Check(preview.Fields.Any(item => item.Name == "queue_bytes"));
        var root = AppTestHost.TempRoot();
        try
        {
            var path = Path.Combine(root, "export.json");
            AppTestHost.Check(!shell.Diagnostics.TryExport(path));
            AppTestHost.Check(!File.Exists(path));
            AppTestHost.Check(shell.Diagnostics.ConfirmExport());
            AppTestHost.Check(shell.Diagnostics.TryExport(path));
            var text = File.ReadAllText(path);
            AppTestHost.Check(text.Contains("\"redacted\": true", StringComparison.Ordinal) ||
                              text.Contains("\"redacted\":true", StringComparison.Ordinal));
            AppTestHost.Check(!text.Contains("/tmp/herdr", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    static void ProviderUnavailable()
    {
        var shell = AppTestHost.Shell();
        shell.StartAsync().AsTask().GetAwaiter().GetResult();
        shell.OpenDiagnostics();
        AppTestHost.Check(shell.Diagnostics.Opened);
        AppTestHost.Check(shell.Diagnostics.Preview is not null);
        AppTestHost.Check(shell.Diagnostics.Preview!.Fields.Any(item =>
            item.Name.StartsWith("unavailable_", StringComparison.Ordinal)));
        AppTestHost.Check(shell.Diagnostics.AboutStatement == ProductInfo.IndependentClientStatement);
        AppTestHost.Check(shell.Diagnostics.Version == ProductInfo.Version);
        shell.OpenAbout();
        AppTestHost.Check(shell.Route == ShellRoute.About);
        AppTestHost.Check(shell.AboutAvailability.Kind == RouteAvailabilityKind.Enabled);
    }
}
