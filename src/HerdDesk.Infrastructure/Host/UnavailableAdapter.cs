using System.Text.Json;
using HerdDesk.Contracts;

namespace HerdDesk.Infrastructure.Host;

public sealed class UnavailableAdapter :
    IRpcConnectionFactory,
    ITerminalTransportFactory,
    ITerminalRendererFactory,
    IAsyncDisposable
{
    public UnavailableAdapter(string name, string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Name = name;
        Code = code;
    }

    public string Name { get; }
    public string Code { get; }
    public bool Available => false;
    public bool IsFakeSuccess => false;

    public UnavailableCapability Capability => new(Name, Code);

    public ValueTask<IRpcRequestConnection?> OpenRequestAsync(
        SessionKey session,
        ConnectionEpoch epoch,
        string socketPath,
        CancellationToken cancellationToken = default)
    {
        _ = (session, epoch, socketPath, cancellationToken);
        return ValueTask.FromResult<IRpcRequestConnection?>(null);
    }

    public ValueTask<IRpcSubscriptionConnection?> OpenSubscriptionAsync(
        SessionKey session,
        ConnectionEpoch epoch,
        string socketPath,
        JsonElement subscribeParameters,
        CancellationToken cancellationToken = default)
    {
        _ = (session, epoch, socketPath, subscribeParameters, cancellationToken);
        return ValueTask.FromResult<IRpcSubscriptionConnection?>(null);
    }

    public ValueTask<ITerminalTransport?> OpenAsync(
        TerminalOpenRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = (request, cancellationToken);
        return ValueTask.FromResult<ITerminalTransport?>(null);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
