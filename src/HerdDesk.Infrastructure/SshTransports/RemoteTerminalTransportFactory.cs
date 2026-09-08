using System.Security.Cryptography;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Process;
using HerdDesk.Infrastructure.Ssh;
using HerdDesk.Infrastructure.Terminal;

namespace HerdDesk.Infrastructure.SshTransports;

internal sealed class RemoteTerminalTransportFactory
{
    private readonly OpenSshLocator _locator;
    private readonly ISshChildProcessStarter _starter;
    private readonly IDiagnosticSink? _diagnostics;
    private readonly TerminalTransportOptions _options;
    private readonly string _executableSha256;

    public RemoteTerminalTransportFactory(
        OpenSshLocator locator,
        IDiagnosticSink? diagnostics = null,
        TerminalTransportOptions? options = null,
        ISshChildProcessStarter? starter = null)
    {
        ArgumentNullException.ThrowIfNull(locator);
        _locator = locator;
        _diagnostics = diagnostics;
        _options = options ?? new TerminalTransportOptions();
        _starter = starter ?? OwnedSshChildProcessStarter.Instance;
        _executableSha256 = File.Exists(locator.SshExecutable) ? Sha256File(locator.SshExecutable) : "";
    }

    public bool TryOpen(
        RemoteSessionLaunch launch,
        TerminalOpenRequest request,
        out ITerminalTransport? transport,
        out SshProcessSpec? spec,
        out string code)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(request);
        transport = null;
        spec = null;
        if (request.Epoch != launch.Epoch)
        {
            code = TerminalTransportCodes.StaleEpoch;
            return false;
        }

        var local = request with { ExecutablePath = _locator.SshExecutable };
        try
        {
            TerminalCliProcessFactory.Validate(local);
        }
        catch (TerminalProtocolException error)
        {
            code = error.Message;
            return false;
        }

        if (!SshProcessSpecFactory.TryRemoteTerminal(
                _locator,
                launch.Settings,
                launch.KnownHostsFile,
                launch.Settings.RemoteHerdrPath,
                local,
                out spec,
                out code))
            return false;

        OwnedChildProcess? child = null;
        try
        {
            child = _starter.Start(spec);
            transport = new TerminalCliTransport(child, local, _diagnostics, _options, _executableSha256);
            WriteDiagnostic("terminal-open", DiagnosticOutcome.Success, null, launch);
            return true;
        }
        catch (Exception)
        {
            if (child is not null)
                child.DisposeAsync().AsTask().GetAwaiter().GetResult();
            code = SshTransportCodes.ChildExited;
            WriteDiagnostic("terminal-open", DiagnosticOutcome.Failure, code, launch);
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

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
