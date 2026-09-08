using System.Text.Json;
using HerdDesk.Contracts;
using HerdDesk.Infrastructure.Process;
using HerdDesk.Infrastructure.Rpc;
using HerdDesk.Infrastructure.Ssh;

namespace HerdDesk.Infrastructure.SshTransports;

internal sealed class RemoteRpcConnectionFactory
{
    private readonly OpenSshLocator _locator;
    private readonly ISshChildProcessStarter _starter;
    private readonly IDiagnosticSink? _diagnostics;

    public RemoteRpcConnectionFactory(
        OpenSshLocator locator,
        IDiagnosticSink? diagnostics = null,
        ISshChildProcessStarter? starter = null)
    {
        ArgumentNullException.ThrowIfNull(locator);
        _locator = locator;
        _diagnostics = diagnostics;
        _starter = starter ?? OwnedSshChildProcessStarter.Instance;
    }

    public bool TryOpenRequest(
        RemoteSessionLaunch launch,
        out IRpcRequestConnection? connection,
        out SshProcessSpec? spec,
        out string code)
    {
        ArgumentNullException.ThrowIfNull(launch);
        connection = null;
        spec = null;
        var helper = launch.Settings.RemoteHelperPath;
        if (helper is null)
        {
            code = SshTransportCodes.TrustRequired;
            return false;
        }

        if (!SshProcessSpecFactory.TryRemoteRpc(
                _locator,
                launch.Settings,
                launch.KnownHostsFile,
                helper,
                launch.SocketPath,
                out spec,
                out code))
            return false;

        OwnedChildProcess? child = null;
        try
        {
            child = _starter.Start(spec);
            connection = new RpcRequestConnection(child, launch.Epoch, _diagnostics);
            WriteDiagnostic("rpc-request", DiagnosticOutcome.Success, null, launch);
            return true;
        }
        catch (Exception)
        {
            if (child is not null)
                child.DisposeAsync().AsTask().GetAwaiter().GetResult();
            code = SshTransportCodes.ChildExited;
            WriteDiagnostic("rpc-request", DiagnosticOutcome.Failure, code, launch);
            return false;
        }
    }

    public bool TryOpenSubscription(
        RemoteSessionLaunch launch,
        JsonElement subscribeParameters,
        out IRpcSubscriptionConnection? connection,
        out SshProcessSpec? spec,
        out string code)
    {
        ArgumentNullException.ThrowIfNull(launch);
        connection = null;
        spec = null;
        var helper = launch.Settings.RemoteHelperPath;
        if (helper is null)
        {
            code = SshTransportCodes.TrustRequired;
            return false;
        }

        if (!SshProcessSpecFactory.TryRemoteRpc(
                _locator,
                launch.Settings,
                launch.KnownHostsFile,
                helper,
                launch.SocketPath,
                out spec,
                out code))
            return false;

        OwnedChildProcess? child = null;
        try
        {
            child = _starter.Start(spec);
            connection = new RpcSubscriptionConnection(child, launch.Epoch, subscribeParameters);
            WriteDiagnostic("rpc-event", DiagnosticOutcome.Success, null, launch);
            return true;
        }
        catch (Exception)
        {
            if (child is not null)
                child.DisposeAsync().AsTask().GetAwaiter().GetResult();
            code = SshTransportCodes.ChildExited;
            WriteDiagnostic("rpc-event", DiagnosticOutcome.Failure, code, launch);
            return false;
        }
    }

    private void WriteDiagnostic(
        string operation, DiagnosticOutcome outcome, string? code, RemoteSessionLaunch launch)
    {
        _diagnostics?.TryWrite(new DiagnosticEvent(
            DateTimeOffset.UtcNow,
            "ssh-transport",
            operation,
            outcome,
            code,
            launch.Epoch.Value,
            null,
            null,
            null,
            null));
    }
}
