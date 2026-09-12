using HerdDesk.App.Composition;
using HerdDesk.Infrastructure.Configuration;

namespace HerdDesk.App;

public static class ShellHost
{
    public static ShellViewModel Create(AppServices services, AppActivationCoordinator? activation = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var catalog = new ProjectionCatalog { DaemonAvailable = false };
        return new ShellViewModel(new ShellDependencies
        {
            Profiles = services.DeviceProfiles,
            Catalog = catalog,
            Clock = services.Clock,
            Ownership = activation ?? ConfigurationOwnership.Owner,
            UiPreferences = new UiPreferenceStore(services.Paths),
            Recents = new RecentAccessStore(),
            Exit = new AppExitCoordinator(),
            Aliases = services.Aliases,
            Unavailable = services.Unavailable,
            Activation = activation,
            DiagnosticSink = services.Diagnostics,
            TerminalControl = WorkbenchControlFactory.Create()
        });
    }
}
