using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Configuration;
using HerdDesk.Infrastructure.Diagnostics;
using HerdDesk.Infrastructure.Host;
using HerdDesk.Infrastructure.Rpc;
using HerdDesk.Infrastructure.Ssh;
using HerdDesk.Infrastructure.Terminal;
using HerdDesk.Terminal.Web;

namespace HerdDesk.App.Composition;

public sealed class AppServices : IAsyncDisposable
{
    public AppServices(
        AppDataPaths paths,
        IClock clock,
        IDeviceProfileStore deviceProfiles,
        IDiagnosticSink diagnostics,
        IRpcConnectionFactory rpcConnections,
        ITerminalTransportFactory terminalTransports,
        ITerminalRendererFactory terminalRenderers,
        DiagnosticAliasProjector aliases,
        IReadOnlyList<UnavailableCapability> unavailable,
        ISshConnectionTester? sshTester = null,
        IHelperDeploymentService? helperDeployment = null,
        bool observeAuthorized = false)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(deviceProfiles);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(rpcConnections);
        ArgumentNullException.ThrowIfNull(terminalTransports);
        ArgumentNullException.ThrowIfNull(terminalRenderers);
        ArgumentNullException.ThrowIfNull(aliases);
        ArgumentNullException.ThrowIfNull(unavailable);
        Paths = paths;
        Clock = clock;
        DeviceProfiles = deviceProfiles;
        Diagnostics = diagnostics;
        RpcConnections = rpcConnections;
        TerminalTransports = terminalTransports;
        TerminalRenderers = terminalRenderers;
        Aliases = aliases;
        Unavailable = unavailable;
        SshTester = sshTester;
        HelperDeployment = helperDeployment;
        ObserveAuthorized = observeAuthorized;
        HasFakeSuccessAdapter =
            rpcConnections.IsFakeSuccess ||
            terminalTransports.IsFakeSuccess ||
            terminalRenderers.IsFakeSuccess;
    }

    public AppDataPaths Paths { get; }
    public IClock Clock { get; }
    public IDeviceProfileStore DeviceProfiles { get; }
    public IDiagnosticSink Diagnostics { get; }
    public IRpcConnectionFactory RpcConnections { get; }
    public ITerminalTransportFactory TerminalTransports { get; }
    public ITerminalRendererFactory TerminalRenderers { get; }
    public DiagnosticAliasProjector Aliases { get; }
    public IReadOnlyList<UnavailableCapability> Unavailable { get; }
    public ISshConnectionTester? SshTester { get; }
    public IHelperDeploymentService? HelperDeployment { get; }
    public bool HasFakeSuccessAdapter { get; }
    public bool ObserveAuthorized { get; }

    public static AppServices CreateProduction(
        AppDataPaths paths,
        IClock? clock = null,
        bool authorizeObserve = false)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Directory.CreateDirectory(paths.SettingsDirectory);
        Directory.CreateDirectory(paths.CacheDirectory);
        Directory.CreateDirectory(paths.LogDirectory);
        clock ??= new SystemClock();
        var aliases = DiagnosticAliasProjector.LoadOrCreate(paths.DiagnosticSaltFile);
        var diagnostics = new JsonlDiagnosticSink(paths.DiagnosticLogFile);
        var store = new AtomicConfigurationStore(paths);
        var bridgePath = Environment.GetEnvironmentVariable("HERDDESK_BRIDGE_EXECUTABLE");
        var terminalPath = Environment.GetEnvironmentVariable("HERDDESK_TERMINAL_EXECUTABLE");
        var canUseObserveAdapters = authorizeObserve &&
            !string.IsNullOrWhiteSpace(bridgePath) &&
            !string.IsNullOrWhiteSpace(terminalPath) &&
            File.Exists(bridgePath) && File.Exists(terminalPath);
        IRpcConnectionFactory rpc = canUseObserveAdapters
            ? new RpcStdioConnectionFactory(bridgePath!, diagnostics)
            : new UnavailableAdapter("rpc-connection", authorizeObserve
                ? "rpc_bridge_configuration_incomplete"
                : "rpc_bridge_unavailable");
        ITerminalTransportFactory transports = canUseObserveAdapters
            ? new TerminalCliProcessFactory(terminalPath!, diagnostics)
            : new UnavailableAdapter("terminal-transport", authorizeObserve
                ? "terminal_transport_configuration_incomplete"
                : "terminal_transport_unavailable");
        var renderers = new UnavailableAdapter("terminal-renderer", "renderer_host_windows_only");
        var unavailableList = new List<UnavailableCapability>();
        if (!rpc.Available)
            unavailableList.Add(new UnavailableCapability(rpc.Name, authorizeObserve
                ? "rpc_bridge_configuration_incomplete"
                : "rpc_bridge_unavailable"));
        if (!transports.Available)
            unavailableList.Add(new UnavailableCapability(transports.Name, authorizeObserve
                ? "terminal_transport_configuration_incomplete"
                : "terminal_transport_unavailable"));
        unavailableList.Add(new UnavailableCapability(renderers.Name, "renderer_host_windows_only"));
        unavailableList.Add(WebRendererHost.Capability);
        IReadOnlyList<UnavailableCapability> unavailable = unavailableList;
        var ssh = new SshConnectionTestService(paths, clock);
        var helper = new RemoteHelperDeploymentService(paths, ProductInfo.Version, clock);
        return new AppServices(
            paths, clock, store, diagnostics, rpc, transports, renderers, aliases, unavailable, ssh, helper,
            canUseObserveAdapters);
    }

    public async ValueTask DisposeAsync()
    {
        if (SshTester is not null)
            await SshTester.CancelAsync().ConfigureAwait(false);
        if (HelperDeployment is not null)
            await HelperDeployment.CancelAsync().ConfigureAwait(false);
        if (TerminalRenderers is IAsyncDisposable renderer)
            await renderer.DisposeAsync().ConfigureAwait(false);
        if (TerminalTransports is IAsyncDisposable transport)
            await transport.DisposeAsync().ConfigureAwait(false);
        if (RpcConnections is IAsyncDisposable rpc)
            await rpc.DisposeAsync().ConfigureAwait(false);
        await Diagnostics.DisposeAsync().ConfigureAwait(false);
    }
}
