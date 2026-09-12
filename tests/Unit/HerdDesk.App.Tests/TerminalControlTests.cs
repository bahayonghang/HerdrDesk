using System.Collections.Frozen;
using System.Threading.Channels;
using HerdDesk.App;
using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class TerminalControlTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("control bar labels stay explainable without always-takeover", LabelsAndNoBypass),
        ("control bar stays disabled without a pane", NoPaneDisablesActions)
    ];

    static void LabelsAndNoBypass()
    {
        var pane = new PaneKey(
            new SessionKey(new DeviceId(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")), "endpoint", "dev"),
            "ws", "p1");
        var store = new AppLeaseStore(pane);
        var host = new AppLeaseHost();
        var renderer = new AppLeaseRenderer();
        var coordinator = new ControlLeaseCoordinator(store, host, renderer);
        try
        {
            var vm = new TerminalControlViewModel(coordinator);
            AppTestHost.Check(!vm.AlwaysTakeoverEnabled);
            AppTestHost.Check(!vm.HasGlobalSkip);
            AppTestHost.Check(vm.PrimaryActionName == ShellStrings.RequestControl);
            AppTestHost.Check(vm.SecondaryActionName is null);
            coordinator.OpenPaneAsync(pane).AsTask().GetAwaiter().GetResult();
            Wait(coordinator, state => state.ObserveBinding is not null);
            AppTestHost.Check(vm.AccessLabel == ShellStrings.Observing);
            AppTestHost.Check(vm.PrimaryActionName == ShellStrings.RequestControl);
            AppTestHost.Check(vm.SecondaryActionName is null);
            AppTestHost.Check(vm.PrimaryActionEnabled);
            vm.RequestControlFromScreenReader();
            Wait(coordinator, state => state.Access == TerminalAccess.Acquiring);
            AppTestHost.Check(vm.PrimaryActionName == ShellStrings.CancelAcquire);
            AppTestHost.Check(!vm.State.ControlVerified);
            AppTestHost.Check(vm.SecondaryActionName is null);
            AppTestHost.Check(vm.TakeoverWarning == ShellStrings.TakeoverReplacesController);
            AppTestHost.Check(vm.RequestControlAutomationName == ShellStrings.RequestControl);
            vm.KeepObserving();
            Wait(coordinator, state => state.Access == TerminalAccess.Observing);
            AppTestHost.Check(vm.PrimaryActionName == ShellStrings.RequestControl);
            AppTestHost.Check(vm.SecondaryActionName is null);
            AppTestHost.Check(!vm.State.ControlVerified);
        }
        finally
        {
            coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void NoPaneDisablesActions()
    {
        var pane = new PaneKey(
            new SessionKey(new DeviceId(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")), "endpoint", "dev"),
            "ws", "p1");
        var store = new AppLeaseStore(pane);
        var host = new AppLeaseHost();
        var renderer = new AppLeaseRenderer();
        var coordinator = new ControlLeaseCoordinator(store, host, renderer);
        try
        {
            var vm = new TerminalControlViewModel(coordinator);
            AppTestHost.Check(!vm.PrimaryActionEnabled);
            AppTestHost.Check(vm.DisabledReason == ShellStrings.ControlRequiresPane);
            AppTestHost.Check(vm.PrimaryActionName == ShellStrings.RequestControl);
            AppTestHost.Check(vm.SecondaryActionName is null);
            vm.InvokePrimary();
            AppTestHost.Check(vm.State.Access == TerminalAccess.Disconnected);
            AppTestHost.Check(!vm.State.ControlVerified);
        }
        finally
        {
            coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    static void Wait(ControlLeaseCoordinator coordinator, Func<ControlLeaseState, bool> pred)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        WaitAsync(coordinator, pred, cts.Token).GetAwaiter().GetResult();
    }

    static async Task WaitAsync(
        ControlLeaseCoordinator coordinator,
        Func<ControlLeaseState, bool> pred,
        CancellationToken cancellationToken)
    {
        if (pred(coordinator.Current))
            return;
        await foreach (var _ in coordinator.ReadStatesAsync(cancellationToken).ConfigureAwait(false))
        {
            if (pred(coordinator.Current))
                return;
        }

        throw new Exception("wait_timeout");
    }
}

file sealed class AppLeaseStore : ILeaseTargetStore
{
    public AppLeaseStore(PaneKey pane) =>
        Snapshot = new LeaseTargetSnapshot(
            pane, true, new ConnectionEpoch(1), 1, DeviceFreshness.Current,
            new CapabilityProfile("0.9.0", "0.9.0", 22, 1, "sha", "sha",
                FrozenSet.ToFrozenSet(["pane.send_input"], StringComparer.Ordinal)),
            "dev / ws / p1", "p1");

    public LeaseTargetSnapshot Snapshot { get; }

    public LeaseTargetSnapshot Read(PaneKey pane) => Snapshot.Pane == pane
        ? Snapshot
        : Snapshot with { Exists = false };
}

file sealed class AppLeaseHost : IControlBindingHost
{
    public ValueTask<ITerminalTransport> OpenObserveAsync(
        PaneKey pane, ConnectionEpoch epoch, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<ITerminalTransport>(new AppLeaseTransport(pane, epoch, TerminalMode.Observe, null, false));

    public ValueTask<ITerminalTransport> OpenControlCandidateAsync(
        PaneKey pane, ConnectionEpoch epoch, string attemptId, TerminalTakeoverAuthorization? takeover,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<ITerminalTransport>(
            new AppLeaseTransport(pane, epoch, TerminalMode.Control, attemptId, takeover?.Confirmed == true));
}

file sealed class AppLeaseRenderer : ITerminalRenderer
{
    public PaneKey? Pane { get; private set; }
    public ConnectionEpoch? Epoch { get; private set; }
    public RendererSurfaceState SurfaceState => RendererSurfaceState.Bound;

    public ValueTask BindAsync(PaneKey pane, ConnectionEpoch epoch, CancellationToken cancellationToken = default)
    {
        Pane = pane;
        Epoch = epoch;
        return ValueTask.CompletedTask;
    }

    public ValueTask<RenderApplyResult> ApplyAsync(TerminalFrame frame, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new RenderApplyResult(
            true, new RenderConsumption(Epoch ?? new ConnectionEpoch(1), frame.Sequence),
            RendererQueueState.Ready, RendererSurfaceState.Observing, "parse_consumed"));

    public async IAsyncEnumerable<RendererInput> ReadInputsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        yield break;
    }

    public ValueTask SetReadOnlyAsync(bool readOnly, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask FocusAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

file sealed class AppLeaseTransport : ITerminalTransport
{
    private readonly Channel<TerminalTransportEvent> _events = Channel.CreateUnbounded<TerminalTransportEvent>();

    public AppLeaseTransport(PaneKey pane, ConnectionEpoch epoch, TerminalMode mode, string? attempt, bool takeover)
    {
        Pane = pane;
        Epoch = epoch;
        Mode = mode;
        _ = (attempt, takeover);
    }

    public PaneKey Pane { get; }
    public ConnectionEpoch Epoch { get; }
    public TerminalMode Mode { get; }

    public async IAsyncEnumerable<TerminalTransportEvent> ReadEventsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return item;
    }

    public ValueTask<TerminalWriteReceipt> SendInputAsync(
        TerminalInputCommand input, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new TerminalWriteReceipt(1, TerminalWriteDisposition.NotSent, "input_not_sent"));

    public ValueTask<TerminalWriteReceipt> ResizeAsync(
        TerminalResizeCommand size, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new TerminalWriteReceipt(1, TerminalWriteDisposition.NotSent, "observe_no_resize"));

    public ValueTask<TerminalWriteReceipt> ScrollAsync(
        TerminalScrollCommand request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new TerminalWriteReceipt(1, TerminalWriteDisposition.NotSent, "observe_scroll_denied"));

    public ValueTask<TerminalWriteReceipt> ReleaseAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new TerminalWriteReceipt(1, TerminalWriteDisposition.WrittenUnacknowledged, "released"));

    public ValueTask DisposeAsync()
    {
        _events.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
