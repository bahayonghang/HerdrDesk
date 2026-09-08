using System.Text.Json;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Process;

namespace HerdDesk.Infrastructure.Rpc;

public sealed class RpcStdioConnectionFactory : IRpcConnectionFactory, IAsyncDisposable
{
    private readonly string _bridgeExecutable;
    private readonly IDiagnosticSink? _diagnostics;

    public RpcStdioConnectionFactory(string bridgeExecutable, IDiagnosticSink? diagnostics = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bridgeExecutable);
        _bridgeExecutable = Path.GetFullPath(bridgeExecutable);
        _diagnostics = diagnostics;
    }

    public string Name => "rpc-stdio-bridge";
    public bool Available => File.Exists(_bridgeExecutable);
    public bool IsFakeSuccess => false;

    public async ValueTask<IRpcRequestConnection?> OpenRequestAsync(
        SessionKey session,
        ConnectionEpoch epoch,
        string socketPath,
        CancellationToken cancellationToken = default)
    {
        _ = session;
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryStart(socketPath, out var child, out _))
            return null;
        try
        {
            return new RpcRequestConnection(child, epoch, _diagnostics);
        }
        catch
        {
            await child.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask<IRpcSubscriptionConnection?> OpenSubscriptionAsync(
        SessionKey session,
        ConnectionEpoch epoch,
        string socketPath,
        JsonElement subscribeParameters,
        CancellationToken cancellationToken = default)
    {
        _ = session;
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryStart(socketPath, out var child, out _))
            return null;
        try
        {
            return new RpcSubscriptionConnection(child, epoch, subscribeParameters);
        }
        catch
        {
            await child.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private bool TryStart(string socketPath, out OwnedChildProcess child, out string? code)
    {
        child = null!;
        code = RpcSocketPath.RejectReason(socketPath);
        if (code is not null)
            return false;
        if (!Available)
        {
            code = RpcCodes.ExecutableInvalid;
            return false;
        }
        child = OwnedChildProcess.Start(_bridgeExecutable, ["rpc", "--socket-path", socketPath]);
        return true;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
