using HerdDesk.Contracts;

namespace HerdDesk.Core;

public static class WriteIntentGuard
{
    public static AggregationWriteResult Evaluate(
        GlobalProjectionStore store,
        AggregationWriteIntent intent,
        InputContext? context = null,
        RendererInput? input = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(intent);
        if (intent.Device.Value == Guid.Empty || intent.Epoch.Value <= 0)
            return new(false, AggregationCodes.InvalidIdentity);
        if (intent.Pane is { } pane && pane.Session.Device != intent.Device)
            return new(false, AggregationCodes.WrongDevice);
        if (intent.Session is { } session && session.Device != intent.Device)
            return new(false, AggregationCodes.WrongDevice);
        if (!store.TryGetPartition(intent.Device, out var partition))
            return new(false, AggregationCodes.Expired);
        var epoch = intent.Session is { } key ? partition.EpochFor(key) : partition.Epoch;
        if (intent.Epoch != epoch)
            return new(false, AggregationCodes.StaleEpoch);
        if (intent.Target is { } target && target.Device != intent.Device)
            return new(false, AggregationCodes.WrongDevice);
        if (intent.Target is { } named && named.Stamp.Epoch.Value > 0 && named.Stamp.Epoch != epoch)
            return new(false, AggregationCodes.StaleEpoch);
        if (partition.Readiness is PartitionReadiness.Incompatible)
            return new(false, AggregationCodes.Incompatible);
        if (partition.Readiness is PartitionReadiness.AuthRequired)
            return new(false, AggregationCodes.AuthRequired);
        if (partition.Readiness is PartitionReadiness.PermissionDenied)
            return new(false, AggregationCodes.PermissionDenied);
        if (partition.Readiness is PartitionReadiness.Offline)
            return new(false, AggregationCodes.Offline);
        if (partition.Readiness is PartitionReadiness.Stale or PartitionReadiness.Loading
            or PartitionReadiness.Cancelling)
            return new(false, AggregationCodes.Stale);
        if (partition.Readiness is PartitionReadiness.Error or PartitionReadiness.Empty)
            return new(false, partition.ErrorCode ?? AggregationCodes.Error);
        if (intent.Kind == AggregationWriteKind.Input)
        {
            if (context is null || input is null)
                return new(false, AggregationCodes.InvalidIdentity);
            if (context.ActivePane.Session.Device != intent.Device ||
                input.Pane.Session.Device != intent.Device)
                return new(false, AggregationCodes.WrongDevice);
            var decision = InputPolicy.Evaluate(context, input);
            return new(decision.Allowed, decision.Code);
        }

        if (intent.Pane is { } paneKey && store.FindPane(paneKey) is null)
            return new(false, AggregationCodes.Expired);
        return new(true, AggregationCodes.Allowed);
    }
}
