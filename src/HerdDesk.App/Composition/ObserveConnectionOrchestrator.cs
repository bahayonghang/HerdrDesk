using HerdDesk.Contracts;
using HerdDesk.Core;

namespace HerdDesk.App.Composition;

/// <summary>Explicit local observe connection coordinator; it never owns the daemon.</summary>
public sealed class ObserveConnectionOrchestrator : IAsyncDisposable
{
    private readonly IRpcConnectionFactory _rpc;
    private readonly IRpcStateDecoder _decoder;
    private readonly IDiagnosticSink _diagnostics;
    private readonly ProjectionCatalog _catalog;
    private readonly SchemaCompatibilityBinding _binding;
    private readonly TimeProvider _time;
    private readonly Dictionary<SessionKey, DeviceSession> _sessions = [];
    private readonly CancellationTokenSource _lifetime = new();

    public ObserveConnectionOrchestrator(
        IRpcConnectionFactory rpc,
        IRpcStateDecoder decoder,
        IDiagnosticSink diagnostics,
        ProjectionCatalog catalog,
        SchemaCompatibilityBinding? binding = null,
        TimeProvider? time = null)
    {
        _rpc = rpc; _decoder = decoder; _diagnostics = diagnostics; _catalog = catalog;
        _binding = binding ?? SchemaCompatibilityBinding.PinnedUnverified("unknown");
        _time = time ?? TimeProvider.System;
        _catalog.Aggregate ??= new GlobalProjectionStore();
    }

    public async ValueTask<bool> ConnectAsync(DeviceProfile profile, SessionProfile session,
        CancellationToken cancellationToken = default)
    {
        if (!_rpc.Available || profile.Device.Value == Guid.Empty ||
            session.Kind != SessionProfileKind.ExplicitEndpoint ||
            string.IsNullOrWhiteSpace(session.CanonicalLocation))
            return false;
        var key = session.ToSessionKey(profile.Device);
        var resolved = EndpointResolver.Resolve(profile.Device, key,
            EndpointPreference.Explicit(session.CanonicalLocation), EndpointResolutionConfig.Empty);
        if (!resolved.Resolved || resolved.Endpoint is null)
            return false;
        var actor = new DeviceSession(key, _rpc, _decoder,
            new DeviceProjectionStore(), _binding, _time, _diagnostics);
        _sessions[key] = actor;
        try
        {
            await actor.ConnectAsync(resolved.Endpoint.CanonicalLocation, cancellationToken).ConfigureAwait(false);
            _ = PumpAsync(actor, _lifetime.Token);
            return true;
        }
        catch
        {
            await actor.DisposeAsync().ConfigureAwait(false);
            _sessions.Remove(key);
            return false;
        }
    }

    private async Task PumpAsync(DeviceSession actor, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var state in actor.ReadStatesAsync(cancellationToken).ConfigureAwait(false))
            {
                _catalog.Aggregate!.ApplySession(state);
                _catalog.Snapshot = state.Projection;
                _catalog.Freshness = state.Freshness;
                _catalog.LastErrorCode = state.LastErrorCode;
                _catalog.DaemonAvailable = state.Phase is not ConnectionPhase.Offline;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        foreach (var session in _sessions.Values)
            await session.NotifyAppStoppingAsync().ConfigureAwait(false);
        foreach (var session in _sessions.Values)
            await session.DisposeAsync().ConfigureAwait(false);
        _sessions.Clear();
        _lifetime.Dispose();
    }
}
