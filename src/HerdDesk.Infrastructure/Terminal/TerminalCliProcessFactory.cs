using System.Security.Cryptography;
using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Process;

namespace HerdDesk.Infrastructure.Terminal;

public sealed class TerminalCliProcessFactory : ITerminalTransportFactory, IAsyncDisposable
{
    private readonly string _executable;
    private readonly IDiagnosticSink? _diagnostics;
    private readonly TerminalTransportOptions _options;
    private readonly IReadOnlyList<string> _argumentPrefix;
    private readonly string _executableSha256;

    public TerminalCliProcessFactory(
        string executable,
        IDiagnosticSink? diagnostics = null,
        TerminalTransportOptions? options = null)
        : this(executable, diagnostics, options, [])
    {
    }

    internal TerminalCliProcessFactory(
        string executable,
        IDiagnosticSink? diagnostics,
        TerminalTransportOptions? options,
        IReadOnlyList<string>? argumentPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        _executable = Path.GetFullPath(executable);
        _diagnostics = diagnostics;
        _options = options ?? new TerminalTransportOptions();
        _argumentPrefix = argumentPrefix ?? [];
        _executableSha256 = File.Exists(_executable) ? Sha256File(_executable) : "";
    }

    public string Name => "terminal-cli";
    public bool Available => File.Exists(_executable);
    public bool IsFakeSuccess => false;
    public string ExecutableSha256 => _executableSha256;

    public ValueTask<ITerminalTransport?> OpenAsync(
        TerminalOpenRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        Validate(request);
        if (!Available)
            throw new TerminalProtocolException(TerminalTransportCodes.ExecutableInvalid);
        if (!string.Equals(Path.GetFullPath(request.ExecutablePath), _executable, StringComparison.OrdinalIgnoreCase))
            throw new TerminalProtocolException(TerminalTransportCodes.ExecutableInvalid);

        var arguments = new List<string>(_argumentPrefix.Count + 16);
        arguments.AddRange(_argumentPrefix);
        arguments.AddRange(TerminalCliArgumentList.Build(request));
        var child = OwnedChildProcess.Start(_executable, arguments);
        try
        {
            return new ValueTask<ITerminalTransport?>(new TerminalCliTransport(
                child, request, _diagnostics, _options, _executableSha256));
        }
        catch
        {
            child.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    internal static void Validate(TerminalOpenRequest request)
    {
        if (!IsValidPane(request.Pane))
            throw new TerminalProtocolException(TerminalTransportCodes.InvalidIdentity);
        if (request.Epoch.Value <= 0)
            throw new TerminalProtocolException(TerminalTransportCodes.InvalidEpoch);
        if (!Enum.IsDefined(request.Mode))
            throw new TerminalProtocolException(TerminalTransportCodes.InvalidMode);
        if (request.Columns == 0 || request.Rows == 0)
            throw new TerminalProtocolException(TerminalTransportCodes.InvalidResize);
        if (!IsValidTarget(request.Target))
            throw new TerminalProtocolException(TerminalTransportCodes.InvalidTarget);
        if (request.SessionName is not null && !IsValidSession(request.SessionName))
            throw new TerminalProtocolException(TerminalTransportCodes.InvalidSession);
        if (!Path.IsPathFullyQualified(request.ExecutablePath))
            throw new TerminalProtocolException(TerminalTransportCodes.ExecutableInvalid);
        if (request.Takeover is not null)
        {
            if (!request.Takeover.Confirmed)
                throw new TerminalProtocolException(TerminalTransportCodes.TakeoverNotConfirmed);
            throw new TerminalProtocolException(TerminalTransportCodes.TakeoverUnverified);
        }
    }

    internal static bool IsValidPane(PaneKey pane) =>
        pane.Session.Device.Value != Guid.Empty &&
        !string.IsNullOrWhiteSpace(pane.Session.EndpointKey) &&
        !string.IsNullOrWhiteSpace(pane.WorkspaceId) &&
        !string.IsNullOrWhiteSpace(pane.PaneId);

    internal static bool IsValidTarget(string target) =>
        !string.IsNullOrWhiteSpace(target) &&
        target[0] != '-' &&
        target.IndexOfAny(['\0', '\r', '\n']) < 0;

    internal static bool IsValidSession(string session) =>
        session.Length > 0 && session.IndexOfAny(['\0', '\r', '\n']) < 0;

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
