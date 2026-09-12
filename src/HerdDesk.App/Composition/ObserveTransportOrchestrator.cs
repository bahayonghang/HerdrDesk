using HerdDesk.Contracts;

namespace HerdDesk.App.Composition;

/// <summary>App-owned observe transport seam. RPC and terminal stdio stay separate.</summary>
public sealed class ObserveTransportOrchestrator
{
    private readonly ITerminalTransportFactory _transports;
    private readonly AppExitCoordinator _exit;

    public ObserveTransportOrchestrator(ITerminalTransportFactory transports, AppExitCoordinator exit)
    {
        ArgumentNullException.ThrowIfNull(transports);
        ArgumentNullException.ThrowIfNull(exit);
        _transports = transports;
        _exit = exit;
    }

    public async ValueTask<ObserveTransportResult> OpenAsync(
        PaneKey pane,
        ConnectionEpoch epoch,
        string executablePath,
        string target,
        TerminalHostSession host,
        ushort columns = 80,
        ushort rows = 24,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (!_transports.Available)
            return ObserveTransportResult.Failed("terminal_transport_unavailable");
        if (epoch.Value <= 0)
            return ObserveTransportResult.Failed("terminal_invalid_epoch");

        var transport = await _transports.OpenAsync(new TerminalOpenRequest(
            pane, epoch, TerminalMode.Observe, executablePath, target, columns, rows), cancellationToken)
            .ConfigureAwait(false);
        if (transport is null)
            return ObserveTransportResult.Failed("terminal_transport_unavailable");

        try
        {
            host.Bind(pane, epoch, readOnly: true);
            if (transport is IChildProcessIdentity identity && identity.ChildProcessId is { } id)
                _exit.RegisterOwned(id, transport, OwnedChildKind.TerminalCli);
            _ = PumpAsync(transport, host, pane, epoch, _exit.LifetimeToken);
            return ObserveTransportResult.Success(transport);
        }
        catch
        {
            await transport.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task PumpAsync(
        ITerminalTransport transport,
        TerminalHostSession host,
        PaneKey pane,
        ConnectionEpoch epoch,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in transport.ReadEventsAsync(cancellationToken).ConfigureAwait(false))
            {
                if (item is not TerminalFrameArrived arrived || arrived.Pane != pane || arrived.Epoch != epoch)
                    continue;
                try
                {
                    var frame = arrived.Frame;
                    host.ApplyFrame(new TerminalFrame(
                        frame.Sequence, frame.Columns, frame.Rows, frame.Full, frame.Bytes.ToArray()));
                }
                finally
                {
                    arrived.Dispose();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}

public readonly record struct ObserveTransportResult(ITerminalTransport? Transport, string? Code)
{
    public bool Succeeded => Transport is not null && Code is null;
    public static ObserveTransportResult Failed(string code) => new(null, code);
    public static ObserveTransportResult Success(ITerminalTransport transport) => new(transport, null);
}
