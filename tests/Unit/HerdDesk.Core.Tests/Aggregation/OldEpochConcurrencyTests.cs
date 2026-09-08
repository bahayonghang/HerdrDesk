using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class OldEpochConcurrencyTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("old epoch event ack and input never apply across one hundred switches", ZeroCrossWrites)
    ];

    static void ZeroCrossWrites()
    {
        var store = new GlobalProjectionStore();
        AggregationHarness.Check(store.ApplySession(
            AggregationHarness.DeviceState(
                AggregationHarness.DeviceA, 2, ConnectionPhase.Ready, DeviceFreshness.Current,
                AggregationHarness.NamesakeSession(AggregationHarness.DeviceA)),
            0, "alpha").Succeeded);
        AggregationHarness.Check(store.ApplySession(
            AggregationHarness.DeviceState(
                AggregationHarness.DeviceB, 9, ConnectionPhase.Ready, DeviceFreshness.Current,
                AggregationHarness.NamesakeSession(AggregationHarness.DeviceB)),
            1, "beta").Succeeded);
        var paneA = AggregationHarness.PaneKeyOf(AggregationHarness.DeviceA);
        var paneB = AggregationHarness.PaneKeyOf(AggregationHarness.DeviceB);
        var rejected = 0;
        var accepted = 0;
        var leaked = 0;
        Parallel.For(0, 100, i =>
        {
            var oldA = store.ApplySession(
                AggregationHarness.DeviceState(
                    AggregationHarness.DeviceA, 1, ConnectionPhase.Ready, DeviceFreshness.Current,
                    AggregationHarness.NamesakeSession(AggregationHarness.DeviceA)),
                0, "alpha");
            if (!oldA.Succeeded && oldA.Code == AggregationCodes.StaleEpoch)
                Interlocked.Increment(ref rejected);
            var eventIntent = new AggregationWriteIntent(
                AggregationWriteKind.Event, AggregationHarness.DeviceA, paneA.Session, paneA,
                new ConnectionEpoch(1), AggregationHarness.PaneRef(AggregationHarness.DeviceA, 1));
            var eventResult = WriteIntentGuard.Evaluate(store, eventIntent);
            if (!eventResult.Allowed && eventResult.Code == AggregationCodes.StaleEpoch)
                Interlocked.Increment(ref rejected);
            var ackIntent = new AggregationWriteIntent(
                AggregationWriteKind.Ack, AggregationHarness.DeviceB, paneB.Session, paneB,
                new ConnectionEpoch(1), AggregationHarness.PaneRef(AggregationHarness.DeviceB, 1));
            var ackResult = WriteIntentGuard.Evaluate(store, ackIntent);
            if (!ackResult.Allowed && ackResult.Code == AggregationCodes.StaleEpoch)
                Interlocked.Increment(ref rejected);
            var wrongDevice = new AggregationWriteIntent(
                AggregationWriteKind.Event, AggregationHarness.DeviceA, paneB.Session, paneB,
                new ConnectionEpoch(2), AggregationHarness.PaneRef(AggregationHarness.DeviceB, 9));
            var wrong = WriteIntentGuard.Evaluate(store, wrongDevice);
            if (wrong.Allowed)
                Interlocked.Increment(ref leaked);
            else
                Interlocked.Increment(ref rejected);
            var context = new InputContext(paneA, new ConnectionEpoch(2), TerminalAccess.Controlling, true);
            var input = new RendererInput(paneA, new ConnectionEpoch(1), InputOrigin.CommittedText, "x"u8.ToArray());
            var inputResult = WriteIntentGuard.Evaluate(
                store,
                new AggregationWriteIntent(
                    AggregationWriteKind.Input, AggregationHarness.DeviceA, paneA.Session, paneA,
                    new ConnectionEpoch(1), AggregationHarness.PaneRef(AggregationHarness.DeviceA, 1)),
                context, input);
            if (inputResult.Allowed)
                Interlocked.Increment(ref leaked);
            else
                Interlocked.Increment(ref rejected);
            var live = WriteIntentGuard.Evaluate(
                store,
                new AggregationWriteIntent(
                    AggregationWriteKind.Activate, AggregationHarness.DeviceB, paneB.Session, paneB,
                    new ConnectionEpoch(9), AggregationHarness.PaneRef(AggregationHarness.DeviceB, 9)));
            if (live.Allowed)
                Interlocked.Increment(ref accepted);
        });

        AggregationHarness.Check(leaked == 0);
        AggregationHarness.Check(rejected >= 400);
        AggregationHarness.Check(accepted == 100);
        AggregationHarness.Check(store.TryGetPartition(AggregationHarness.DeviceA, out var a) && a.Epoch.Value == 2);
        AggregationHarness.Check(store.TryGetPartition(AggregationHarness.DeviceB, out var b) && b.Epoch.Value == 9);
        AggregationHarness.Check(store.FindPane(paneA) is not null);
        AggregationHarness.Check(store.FindPane(paneB) is not null);
    }
}
