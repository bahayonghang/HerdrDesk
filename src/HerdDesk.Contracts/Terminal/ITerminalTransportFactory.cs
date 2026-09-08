namespace HerdDesk.Contracts;

public interface ITerminalTransportFactory : IAdapterCapability
{
    ValueTask<ITerminalTransport?> OpenAsync(
        TerminalOpenRequest request,
        CancellationToken cancellationToken = default);
}
