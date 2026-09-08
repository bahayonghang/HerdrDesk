using HerdDesk.Contracts;
using HerdDesk.Core;

namespace HerdDesk.Infrastructure.Terminal;

internal sealed class TerminalOwnershipClassifier
{
    private readonly string _executableSha256;
    private readonly IReadOnlyList<TerminalOwnershipFingerprint> _fingerprints;

    public TerminalOwnershipClassifier(
        string executableSha256,
        IReadOnlyList<TerminalOwnershipFingerprint> fingerprints)
    {
        _executableSha256 = executableSha256;
        _fingerprints = fingerprints;
    }

    public TerminalLeaseResult ForFirstFrame(TerminalOpenRequest request, bool processAlive)
    {
        if (request.Mode == TerminalMode.Observe)
        {
            return TerminalLeaseProbe.Map(new TerminalLeaseObservation(
                TerminalLeaseOperation.Observe,
                TerminalAccess.Disconnected,
                ControlVerifiedBefore: false,
                FirstFrameSeen: true,
                ProcessAlive: processAlive,
                WindowFocused: false));
        }

        return TerminalLeaseProbe.Map(new TerminalLeaseObservation(
            TerminalLeaseOperation.RequestControl,
            TerminalAccess.Observing,
            ControlVerifiedBefore: false,
            FirstFrameSeen: true,
            ProcessAlive: processAlive,
            WindowFocused: false,
            AdapterProvedWriteOwnership: false));
    }

    public TerminalLeaseResult? ForStderr(
        TerminalOpenRequest request,
        string code,
        Func<string, bool> windowContains)
    {
        var matched = MatchIdentity(request, windowContains);
        if (matched is not null)
            return MapVerified(request, matched);
        if (code == TerminalTransportCodes.Busy)
        {
            return TerminalLeaseProbe.Map(new TerminalLeaseObservation(
                TerminalLeaseOperation.RequestControl,
                TerminalAccess.Observing,
                ControlVerifiedBefore: false,
                ControlSignal: TerminalControlSignal.Busy));
        }

        if (code == TerminalTransportCodes.Rejected)
        {
            return TerminalLeaseProbe.Map(new TerminalLeaseObservation(
                TerminalLeaseOperation.RequestControl,
                TerminalAccess.Observing,
                ControlVerifiedBefore: false,
                ControlSignal: TerminalControlSignal.Rejected));
        }

        return null;
    }

    private TerminalOwnershipFingerprint? MatchIdentity(
        TerminalOpenRequest request,
        Func<string, bool>? windowContains)
    {
        var attempt = request.ControlAttemptId;
        if (string.IsNullOrWhiteSpace(attempt))
            return null;
        foreach (var fingerprint in _fingerprints)
        {
            if (!string.Equals(fingerprint.ExecutableSha256, _executableSha256, StringComparison.OrdinalIgnoreCase))
                continue;
            if (fingerprint.Mode != request.Mode)
                continue;
            if (!string.Equals(fingerprint.ControlAttemptId, attempt, StringComparison.Ordinal))
                continue;
            if (windowContains is not null && !windowContains(fingerprint.Marker))
                continue;
            if (windowContains is null && fingerprint.Marker.Length > 0)
                continue;
            return fingerprint;
        }

        return null;
    }

    private static TerminalLeaseResult MapVerified(
        TerminalOpenRequest request,
        TerminalOwnershipFingerprint fingerprint)
    {
        var operation = request.Takeover?.Confirmed == true
            ? TerminalLeaseOperation.RequestTakeover
            : TerminalLeaseOperation.RequestControl;
        return TerminalLeaseProbe.Map(new TerminalLeaseObservation(
            operation,
            TerminalAccess.Observing,
            ControlVerifiedBefore: false,
            AdapterProvedWriteOwnership: fingerprint.AdapterProvedWriteOwnership,
            TakeoverConfirmed: request.Takeover?.Confirmed == true));
    }
}
