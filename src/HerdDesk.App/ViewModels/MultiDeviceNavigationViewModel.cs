using System.Globalization;
using HerdDesk.Contracts;
using HerdDesk.Core;

namespace HerdDesk.App;

public interface IReconnectRequestor
{
    bool Available { get; }
    string Request(DeviceId device);
}

public sealed class MultiDeviceNavigationViewModel
{
    private readonly ProjectionCatalog _catalog;
    private readonly NavigationCoordinator _navigation;

    public MultiDeviceNavigationViewModel(
        ProjectionCatalog catalog,
        NavigationCoordinator navigation,
        IReconnectRequestor? reconnect = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(navigation);
        _catalog = catalog;
        _navigation = navigation;
        Reconnect = reconnect;
        ReconnectAvailability = reconnect is { Available: true }
            ? new RouteAvailability(RouteAvailabilityKind.Enabled)
            : new RouteAvailability(
                RouteAvailabilityKind.Disabled,
                AggregationCodes.ReconnectProviderUnverified,
                ShellStrings.Reconnect);
    }

    public IReconnectRequestor? Reconnect { get; }
    public RouteAvailability ReconnectAvailability { get; }
    public AggregateReadiness Readiness { get; private set; } = AggregateReadiness.Empty;
    public int ReadyCount { get; private set; }
    public int TotalCount { get; private set; }
    public string HeaderText { get; private set; } = "0/0";
    public bool PartialBanner { get; private set; }
    public string PartialBannerText { get; private set; } = "";
    public IReadOnlyList<DeviceConnectionSummary> Devices { get; private set; } = [];
    public string? LastReconnectCode { get; private set; }

    public void Rebuild()
    {
        if (_catalog.Aggregate is { } store)
        {
            var view = store.Read();
            Readiness = view.Readiness;
            ReadyCount = view.ReadyCount;
            TotalCount = view.TotalCount;
            Devices = view.Partitions.Select(ToSummary).ToArray();
        }
        else
        {
            var devices = _catalog.DevicesForTree();
            TotalCount = devices.Count;
            ReadyCount = devices.Count(item => _catalog.PhaseFor(item.Device) == ConnectionPhase.Ready);
            Readiness = TotalCount == 0
                ? AggregateReadiness.Empty
                : ReadyCount == TotalCount
                    ? AggregateReadiness.Ready
                    : ReadyCount == 0
                        ? AggregateReadiness.Loading
                        : AggregateReadiness.PartialReady;
            Devices = devices.Select(ToSummary).ToArray();
        }

        HeaderText = ReadyCount.ToString(CultureInfo.InvariantCulture) + "/" +
                     TotalCount.ToString(CultureInfo.InvariantCulture);
        PartialBanner = Readiness is AggregateReadiness.PartialReady or AggregateReadiness.Loading
            or AggregateReadiness.Degraded;
        PartialBannerText = PartialBanner ? ShellStrings.PartialResults : "";
    }

    public void SelectDevice(DeviceId device)
    {
        var identity = NavigationIdentity.Format(NavigationKind.Device, device, null, null, null);
        var item = _navigation.Find(identity);
        if (item is not null)
            _navigation.Select(item);
    }

    public string RequestReconnect(DeviceId device)
    {
        if (Reconnect is not { Available: true })
        {
            LastReconnectCode = AggregationCodes.ReconnectProviderUnverified;
            return LastReconnectCode;
        }

        LastReconnectCode = Reconnect.Request(device);
        return LastReconnectCode;
    }

    private DeviceConnectionSummary ToSummary(DevicePartition partition)
    {
        var writes = partition.Readiness == PartitionReadiness.Ready &&
                     partition.Freshness == DeviceFreshness.Current &&
                     partition.Phase == ConnectionPhase.Ready;
        var reason = writes
            ? null
            : partition.Readiness switch
            {
                PartitionReadiness.AuthRequired => AggregationCodes.AuthRequired,
                PartitionReadiness.PermissionDenied => AggregationCodes.PermissionDenied,
                PartitionReadiness.Incompatible => AggregationCodes.Incompatible,
                PartitionReadiness.Offline => AggregationCodes.Offline,
                PartitionReadiness.Stale or PartitionReadiness.Loading => AggregationCodes.Stale,
                _ => partition.ErrorCode ?? AggregationCodes.Error
            };
        var status = StatusOf(partition);
        return new DeviceConnectionSummary(
            partition.Device,
            partition.DisplayLabel,
            partition.Phase,
            partition.Freshness,
            partition.Readiness,
            partition.ErrorCode,
            writes,
            reason,
            status,
            ReconnectAvailability.Kind == RouteAvailabilityKind.Enabled &&
            partition.Readiness is PartitionReadiness.Offline or PartitionReadiness.Stale
                or PartitionReadiness.Error,
            ReconnectAvailability.Kind == RouteAvailabilityKind.Enabled
                ? null
                : AggregationCodes.ReconnectProviderUnverified,
            partition.Phase.ToString(),
            partition.Freshness.ToString(),
            "disconnected",
            "unavailable");
    }

    private DeviceConnectionSummary ToSummary(ProjectedDevice device)
    {
        var phase = _catalog.PhaseFor(device.Device);
        var freshness = _catalog.FreshnessFor(device.Device);
        var readiness = _catalog.ReadinessFor(device.Device) ??
                        (phase == ConnectionPhase.Ready
                            ? PartitionReadiness.Ready
                            : phase == ConnectionPhase.Offline
                                ? PartitionReadiness.Offline
                                : phase == ConnectionPhase.Stale
                                    ? PartitionReadiness.Stale
                                    : phase == ConnectionPhase.Incompatible
                                        ? PartitionReadiness.Incompatible
                                        : PartitionReadiness.Loading);
        var partition = new DevicePartition(
            device.Device,
            _catalog.EpochFor(device.Device),
            0,
            phase,
            freshness,
            device.Capabilities,
            readiness,
            _catalog.ErrorFor(device.Device),
            _catalog.LabelFor(device.Device),
            0,
            device.Sessions,
            []);
        return ToSummary(partition);
    }

    private static StatusPresentation StatusOf(DevicePartition partition)
    {
        if (partition.Phase == ConnectionPhase.WaitingForCapacity)
            return new(
                ShellCodes.Loading, ShellStrings.WaitingForCapacity, "waiting-for-capacity",
                RecoveryActionKind.None, "", false);
        if (partition.Phase == ConnectionPhase.PausedForCapacity)
            return new(
                ShellCodes.Stale, ShellStrings.PausedForCapacity, "paused-for-capacity",
                RecoveryActionKind.RetryProjection, ShellStrings.Reconnect, true);
        return StatusOf(partition.Readiness);
    }

    private static StatusPresentation StatusOf(PartitionReadiness readiness) =>
        readiness switch
        {
            PartitionReadiness.Loading => new(
                ShellCodes.Loading, ShellStrings.Loading, "loading", RecoveryActionKind.None, "", false),
            PartitionReadiness.AuthRequired => new(
                ShellCodes.AuthRequired, ShellStrings.AuthRequired, "auth",
                RecoveryActionKind.OpenSettings, ShellStrings.Settings, true),
            PartitionReadiness.PermissionDenied => new(
                ShellCodes.PermissionDenied, ShellStrings.PermissionDenied, "permission",
                RecoveryActionKind.OpenDiagnostics, ShellStrings.Diagnostics, true),
            PartitionReadiness.Incompatible => new(
                ShellCodes.Incompatible, ShellStrings.Incompatible, "incompatible",
                RecoveryActionKind.OpenDiagnostics, ShellStrings.Diagnostics, true),
            PartitionReadiness.Stale => new(
                ShellCodes.Stale, ShellStrings.Stale, "stale",
                RecoveryActionKind.RetryProjection, ShellStrings.Reconnect, true),
            PartitionReadiness.Offline => new(
                ShellCodes.Offline, ShellStrings.Offline, "offline",
                RecoveryActionKind.RetryProjection, ShellStrings.Reconnect, true),
            PartitionReadiness.Empty => new(
                ShellCodes.Empty, ShellStrings.Empty, "empty",
                RecoveryActionKind.AddDevice, ShellStrings.AddDevice, true),
            PartitionReadiness.Error or PartitionReadiness.Cancelling => new(
                ShellCodes.Error, ShellStrings.Error, "error",
                RecoveryActionKind.OpenDiagnostics, ShellStrings.Diagnostics, true),
            _ => new("ready", ShellStrings.Ready, "ready", RecoveryActionKind.None, "", false)
        };
}
