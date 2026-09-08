using System.Reflection;
using System.Text.Json;
using HerdDesk.App.Composition;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Configuration;
using HerdDesk.Infrastructure.Host;
using HerdDesk.Infrastructure.Process;
using HerdDesk.Infrastructure.Rpc;
using HerdDesk.Terminal.Web;

if (args.Length > 0 && args[0] == "--fake-bridge")
    return FakeBridgeHost.Run(args.Skip(1).ToArray());
if (args.Length > 0 && args[0] == "--fake-terminal")
    return FakeTerminalHost.Run(args.Skip(1).ToArray());
if (args.Length > 0 && args[0] == "--fake-ssh")
    return FakeSshHost.Run(args.Skip(1).ToArray());
if (args.Length > 0 && args[0] == "--fake-ssh-channel")
    return FakeSshChannelHost.Run(args.Skip(1).ToArray());


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
    ("rpc request and subscription use two os processes", () =>
    {
        var exe = Environment.ProcessPath!;
        Check(Path.IsPathFullyQualified(exe));
        var requestChild = OwnedChildProcess.Start(exe, ["--fake-bridge", "rpc"]);
        var subscribeChild = OwnedChildProcess.Start(exe, ["--fake-bridge", "subscribe"]);
        using var empty = JsonDocument.Parse("{}");
        var request = new RpcRequestConnection(requestChild, new ConnectionEpoch(3), null);
        var subscribe = new RpcSubscriptionConnection(
            subscribeChild, new ConnectionEpoch(3), empty.RootElement.Clone());
        try
        {
            Check(request.ChildProcessId != subscribe.ChildProcessId);
            using var ping = request.RequestAsync("ping", empty.RootElement.Clone()).AsTask()
                .GetAwaiter().GetResult();
            Check(ping.Succeeded);
            var count = 0;
            var read = Task.Run(async () =>
            {
                await foreach (var item in subscribe.ReadEventsAsync())
                {
                    _ = item;
                    count++;
                    if (count >= 3)
                        break;
                }
            });
            Check(read.Wait(TimeSpan.FromSeconds(5)));
            Check(count == 3);
        }
        finally
        {
            subscribe.DisposeAsync().AsTask().GetAwaiter().GetResult();
            request.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }),
    ("production composition still does not auto-connect rpc", () =>
    {
        var root = Path.Combine(Path.GetTempPath(), "herddesk-hd008-compose-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var services = AppServices.CreateProduction(AppDataPaths.FromRoot(root));
            try
            {
                Check(!services.RpcConnections.Available);
                Check(services.RpcConnections.OpenRequestAsync(
                    new SessionKey(new DeviceId(Guid.Parse("11111111-1111-4111-8111-111111111111")),
                        "local-api", null),
                    new ConnectionEpoch(1),
                    "/tmp/herdr.sock").AsTask().GetAwaiter().GetResult() is null);
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
};

cases = [.. cases, .. RpcSchemaCases.All, .. Hd011Cases.All, .. Hd012Cases.All, .. Hd014Cases.All, .. Hd015Cases.All, .. Hd016Cases.All, .. Hd017Cases.All, .. Hd018Cases.All, .. Hd019Cases.All, .. Hd020Cases.All, .. Hd021Cases.All, .. Hd022Cases.All, .. Hd023Cases.All, .. Hd024Cases.All, .. Hd025Cases.All, .. Hd026Cases.All, .. BridgeReleaseManifestTests.All, .. ResourceCommandSchemaTests.All, .. TerminalWireCases.All, .. RemoteRpcStreamContractTests.All, .. RemoteTerminalStreamContractTests.All];

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

    public ValueTask<IRpcRequestConnection?> OpenRequestAsync(
        SessionKey session,
        ConnectionEpoch epoch,
        string socketPath,
        CancellationToken cancellationToken = default)
    {
        _ = (session, epoch, socketPath, cancellationToken);
        return ValueTask.FromResult<IRpcRequestConnection?>(null);
    }

    public ValueTask<IRpcSubscriptionConnection?> OpenSubscriptionAsync(
        SessionKey session,
        ConnectionEpoch epoch,
        string socketPath,
        JsonElement subscribeParameters,
        CancellationToken cancellationToken = default)
    {
        _ = (session, epoch, socketPath, subscribeParameters, cancellationToken);
        return ValueTask.FromResult<IRpcSubscriptionConnection?>(null);
    }
}
