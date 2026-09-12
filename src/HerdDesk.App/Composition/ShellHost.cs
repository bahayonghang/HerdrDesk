using HerdDesk.App.Composition;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Configuration;
using HerdDesk.Infrastructure.Rpc.SchemaV1;

namespace HerdDesk.App;

public static class ShellHost
{
    public static ShellViewModel Create(AppServices services, AppActivationCoordinator? activation = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var catalog = new ProjectionCatalog { DaemonAvailable = false };
        var aggregate = new GlobalProjectionStore();
        var exit = new AppExitCoordinator();
        var observe = services.ObserveAuthorized
            ? new ObserveConnectionOrchestrator(
                services.RpcConnections,
                new RpcStateDecoder(),
                services.Diagnostics,
                catalog,
                SchemaCompatibilityBinding.PinnedUnverified(ProductInfo.Version))
            : null;
        var observeTransport = services.ObserveAuthorized
            ? new ObserveTransportOrchestrator(services.TerminalTransports, exit)
            : null;
        return new ShellViewModel(new ShellDependencies
        {
            Profiles = services.DeviceProfiles,
            Catalog = catalog,
            Clock = services.Clock,
            Ownership = activation ?? ConfigurationOwnership.Owner,
            UiPreferences = new UiPreferenceStore(services.Paths),
            Recents = new RecentAccessStore(),
            Exit = exit,
            Aliases = services.Aliases,
            Unavailable = services.Unavailable,
            Activation = activation,
            DiagnosticSink = services.Diagnostics,
            TerminalControl = WorkbenchControlFactory.Create(),
            Aggregate = aggregate,
            ObserveConnections = observe,
            ObserveTransport = observeTransport
        });
    }
}
