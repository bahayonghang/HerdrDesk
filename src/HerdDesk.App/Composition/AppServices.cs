using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Configuration;
using HerdDesk.Infrastructure.Diagnostics;
using HerdDesk.Infrastructure.Host;
using HerdDesk.Infrastructure.Ssh;
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
        ISshConnectionTester? sshTester = null)
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
    public bool HasFakeSuccessAdapter { get; }

    public static AppServices CreateProduction(AppDataPaths paths, IClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Directory.CreateDirectory(paths.SettingsDirectory);
        Directory.CreateDirectory(paths.CacheDirectory);
        Directory.CreateDirectory(paths.LogDirectory);
        clock ??= new SystemClock();
        var aliases = DiagnosticAliasProjector.LoadOrCreate(paths.DiagnosticSaltFile);
        var diagnostics = new JsonlDiagnosticSink(paths.DiagnosticLogFile);
        var store = new AtomicConfigurationStore(paths);
        var rpc = new UnavailableAdapter("rpc-connection", "rpc_bridge_unavailable");
        var transports = new UnavailableAdapter("terminal-transport", "terminal_transport_unavailable");
        var renderers = new UnavailableAdapter("terminal-renderer", "renderer_packages_unverified");
        UnavailableCapability[] unavailable =
        [
            rpc.Capability,
            transports.Capability,
            renderers.Capability,
            WebRendererHost.Capability
        ];
        var ssh = new SshConnectionTestService(paths, clock);
        return new AppServices(
            paths, clock, store, diagnostics, rpc, transports, renderers, aliases, unavailable, ssh);
    }

    public async ValueTask DisposeAsync()
    {
        if (SshTester is not null)
            await SshTester.CancelAsync().ConfigureAwait(false);
        if (TerminalRenderers is IAsyncDisposable renderer)
            await renderer.DisposeAsync().ConfigureAwait(false);
        if (TerminalTransports is IAsyncDisposable transport)
            await transport.DisposeAsync().ConfigureAwait(false);
        if (RpcConnections is IAsyncDisposable rpc)
            await rpc.DisposeAsync().ConfigureAwait(false);
        await Diagnostics.DisposeAsync().ConfigureAwait(false);
    }
}
