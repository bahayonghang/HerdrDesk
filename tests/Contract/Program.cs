using System.Reflection;
using HerdDesk.App.Composition;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Configuration;
using HerdDesk.Infrastructure.Host;
using HerdDesk.Terminal.Web;

static void Check(bool condition)
{
    if (!condition)
        throw new Exception("assertion_failed");
}

static void CheckNoUiOrSsh(Assembly assembly)
{
    foreach (var name in assembly.GetReferencedAssemblies().Select(item => item.Name!))
    {
        var lower = name.ToLowerInvariant();
        Check(!lower.Contains("windowsappsdk"));
        Check(!lower.Contains("webview2"));
        Check(!lower.Contains("winui"));
        Check(!lower.Contains("ssh"));
    }
}

var cases = new (string Name, Action Run)[]
{
    ("contracts stay bcl-only types", () =>
    {
        var type = typeof(DeviceProfile);
        Check(type.GetProperty("Password") is null);
        Check(type.GetProperty("PrivateKey") is null);
        Check(type.GetProperty("Token") is null);
        Check(typeof(DiagnosticEvent).GetProperty("Message") is null);
        Check(typeof(DiagnosticEvent).GetProperty("Exception") is null);
        Check(typeof(DiagnosticEvent).GetProperty("Payload") is null);
        CheckNoUiOrSsh(typeof(DeviceId).Assembly);
        Check(typeof(DeviceId).Assembly.GetName().Name == "HerdDesk.Contracts");
        var refs = typeof(DeviceId).Assembly.GetReferencedAssemblies().Select(item => item.Name!).ToArray();
        Check(!refs.Contains("HerdDesk.Core"));
        Check(!refs.Contains("HerdDesk.App"));
    }),
    ("core depends only on contracts among herddesk assemblies", () =>
    {
        CheckNoUiOrSsh(typeof(InputPolicy).Assembly);
        var refs = typeof(InputPolicy).Assembly.GetReferencedAssemblies().Select(item => item.Name!).ToArray();
        Check(refs.Contains("HerdDesk.Contracts"));
        Check(!refs.Contains("HerdDesk.Infrastructure"));
        Check(!refs.Contains("HerdDesk.App"));
        Check(!refs.Contains("HerdDesk.Terminal.Web"));
    }),
    ("infrastructure depends on contracts and does not reference app or tests", () =>
    {
        CheckNoUiOrSsh(typeof(AtomicConfigurationStore).Assembly);
        var refs = typeof(AtomicConfigurationStore).Assembly.GetReferencedAssemblies()
            .Select(item => item.Name!).ToArray();
        Check(refs.Contains("HerdDesk.Contracts"));
        Check(!refs.Contains("HerdDesk.App"));
        Check(!refs.Contains("HerdDesk.Terminal.Web"));
        foreach (var name in refs)
            Check(!name.Contains("Tests", StringComparison.OrdinalIgnoreCase));
    }),
    ("terminal web depends only on contracts among herddesk assemblies", () =>
    {
        CheckNoUiOrSsh(typeof(WebRendererHost).Assembly);
        var refs = typeof(WebRendererHost).Assembly.GetReferencedAssemblies().Select(item => item.Name!).ToArray();
        Check(refs.Contains("HerdDesk.Contracts"));
        Check(!refs.Contains("HerdDesk.Core"));
        Check(!refs.Contains("HerdDesk.App"));
        Check(!refs.Contains("HerdDesk.Infrastructure"));
        Check(WebRendererHost.PackageStatus == "UNVERIFIED");
    }),
    ("production composition does not register fake-success adapters", () =>
    {
        var root = Path.Combine(Path.GetTempPath(), "herddesk-hd007-compose-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var services = AppServices.CreateProduction(AppDataPaths.FromRoot(root));
            try
            {
                Check(!services.HasFakeSuccessAdapter);
                Check(!services.RpcConnections.Available);
                Check(!services.TerminalTransports.Available);
                Check(!services.TerminalRenderers.Available);
                Check(!services.RpcConnections.IsFakeSuccess);
                Check(services.Unavailable.Count >= 3);
                Check(WebRendererHost.PackageStatus == "UNVERIFIED");
                Check(services.Unavailable.Any(item => item.Name == "terminal-renderer-web"));
            }
            finally
            {
                services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }),
    ("tests can replace clock diagnostics and transport factory", () =>
    {
        var root = Path.Combine(Path.GetTempPath(), "herddesk-hd007-replace-" + Guid.NewGuid().ToString("N"));
        var paths = AppDataPaths.FromRoot(root);
        Directory.CreateDirectory(paths.SettingsDirectory);
        Directory.CreateDirectory(paths.LogDirectory);
        var clock = new SystemClock();
        var store = new AtomicConfigurationStore(paths);
        var sink = new HerdDesk.Infrastructure.Diagnostics.JsonlDiagnosticSink(paths.DiagnosticLogFile);
        var rpc = new FakeSuccessAdapter();
        var transports = new UnavailableAdapter("terminal-transport", "terminal_transport_unavailable");
        var renderers = new UnavailableAdapter("terminal-renderer", "renderer_packages_unverified");
        var aliases = new HerdDesk.Infrastructure.Diagnostics.DiagnosticAliasProjector(new byte[16]);
        var services = new AppServices(
            paths, clock, store, sink, rpc, transports, renderers, aliases,
            [transports.Capability, renderers.Capability]);
        try
        {
            Check(services.HasFakeSuccessAdapter);
            Check(ReferenceEquals(services.Clock, clock));
            Check(ReferenceEquals(services.Diagnostics, sink));
            Check(ReferenceEquals(services.RpcConnections, rpc));
        }
        finally
        {
            services.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Directory.Delete(root, true);
        }
    }),
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
Console.WriteLine($"{cases.Length - failed}/{cases.Length} contract tests passed; no live Windows/daemon validation.");
return failed == 0 ? 0 : 1;

sealed class FakeSuccessAdapter : IRpcConnectionFactory
{
    public string Name => "fake-rpc";
    public bool Available => true;
    public bool IsFakeSuccess => true;
}
