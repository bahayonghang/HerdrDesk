using System.Security.Cryptography;
using System.Text;
using HerdDesk.Contracts;

namespace HerdDesk.Core;

internal sealed class TakeoverChallenge
{
    private readonly string _nonce;
    private int _consumed;

    private TakeoverChallenge(
        string handle,
        string nonce,
        PaneKey pane,
        ConnectionEpoch projectionEpoch,
        long projectionRevision,
        ConnectionEpoch observeEpoch,
        string busyEvidenceId,
        string attemptId,
        string breadcrumb,
        string targetSummary,
        string targetSummaryHash)
    {
        Handle = handle;
        _nonce = nonce;
        Pane = pane;
        ProjectionEpoch = projectionEpoch;
        ProjectionRevision = projectionRevision;
        ObserveEpoch = observeEpoch;
        BusyEvidenceId = busyEvidenceId;
        AttemptId = attemptId;
        Breadcrumb = breadcrumb;
        TargetSummary = targetSummary;
        TargetSummaryHash = targetSummaryHash;
    }

    public string Handle { get; }
    public PaneKey Pane { get; }
    public ConnectionEpoch ProjectionEpoch { get; }
    public ConnectionEpoch ObserveEpoch { get; }
    public long ProjectionRevision { get; }
    public string BusyEvidenceId { get; }
    public string AttemptId { get; }
    public string Breadcrumb { get; }
    public string TargetSummary { get; }
    public string TargetSummaryHash { get; }
    public bool Consumed => Volatile.Read(ref _consumed) != 0;

    public TakeoverChallengeView View => new(Handle, Breadcrumb, TargetSummary, BusyEvidenceId);

    public static TakeoverChallenge Create(
        LeaseTargetSnapshot store,
        ConnectionEpoch observeEpoch,
        string attemptId,
        string busyEvidenceId)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        ArgumentException.ThrowIfNullOrWhiteSpace(busyEvidenceId);
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var handle = Guid.NewGuid().ToString("N");
        var summary = store.TargetSummary ?? "";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(summary)));
        return new(
            handle,
            nonce,
            store.Pane,
            store.ProjectionEpoch,
            store.ProjectionRevision,
            observeEpoch,
            busyEvidenceId,
            attemptId,
            store.Breadcrumb,
            summary,
            hash);
    }

    public bool Matches(string handle, LeaseTargetSnapshot store, ControlLeaseState state)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(state);
        if (Consumed)
            return false;
        if (string.IsNullOrWhiteSpace(handle) || !string.Equals(handle, Handle, StringComparison.Ordinal))
            return false;
        if (!store.Exists || store.Freshness != DeviceFreshness.Current)
            return false;
        if (store.Pane != Pane || state.Target != Pane)
            return false;
        if (state.Access != TerminalAccess.Observing || state.LastAttempt != ControlAttemptOutcome.Busy)
            return false;
        if (store.ProjectionEpoch != ProjectionEpoch || store.ProjectionRevision != ProjectionRevision)
            return false;
        if (state.ObserveBinding is null || state.ObserveBinding.Epoch != ObserveEpoch)
            return false;
        if (!string.Equals(store.TargetSummary, TargetSummary, StringComparison.Ordinal))
            return false;
        return string.Equals(state.Challenge?.BusyEvidenceId, BusyEvidenceId, StringComparison.Ordinal);
    }

    public bool TryConsume(string handle, LeaseTargetSnapshot store, ControlLeaseState state)
    {
        if (!Matches(handle, store, state))
            return false;
        if (Interlocked.Exchange(ref _consumed, 1) != 0)
            return false;
        _ = _nonce;
        return true;
    }
}
