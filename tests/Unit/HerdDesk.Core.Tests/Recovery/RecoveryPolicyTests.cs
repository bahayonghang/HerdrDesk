using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class RecoveryPolicyTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("taxonomy maps each cause to a stable retry class", Taxonomy),
        ("backoff is 1 2 4 8 16 30 seconds plus bounded jitter", BackoffBounds),
        ("attempt seven awaits user and never starts a daemon", ExhaustThenAwait),
        ("stop and await-user causes schedule no delay", NonRetryCauses)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static SessionKey Session() => DeviceSessionGraphs.DefaultSession();

    static void Taxonomy()
    {
        foreach (var cause in Enum.GetValues<RecoveryCause>())
        {
            var failure = RecoveryPolicy.Create(
                cause is RecoveryCause.AppStopping or RecoveryCause.ManualDisconnect
                    ? RecoveryScope.Application
                    : cause == RecoveryCause.RendererFailure ? RecoveryScope.Renderer : RecoveryScope.Rpc,
                cause,
                Session(),
                null,
                new ConnectionEpoch(1),
                cause is RecoveryCause.ManualDisconnect or RecoveryCause.AppStopping);
            Check(failure.DiagnosticId == RecoveryPolicy.Code(cause));
            Check(!failure.DiagnosticId.Contains('\\', StringComparison.Ordinal));
            Check(!failure.DiagnosticId.Contains('/', StringComparison.Ordinal));
            var decision = RecoveryPolicy.Decide(failure, 1, ZeroRecoveryEntropy.Instance);
            Check(!decision.StartDaemon);
            if (failure.RetryClass == RecoveryRetryClass.Transient)
            {
                Check(decision.Action == RecoveryAction.RetryAfter);
                Check(decision.Delay == TimeSpan.FromSeconds(1));
            }
            else if (failure.RetryClass == RecoveryRetryClass.Stop)
                Check(decision.Action == RecoveryAction.Stop);
            else
                Check(decision.Action == RecoveryAction.AwaitUser);
        }

        var request = RecoveryPolicy.ClassifyRpc(
            Session(), new ConnectionEpoch(2), RpcCodes.RequestLost, RpcFailureKind.ConnectionLost,
            true, false, false, false);
        Check(request.Cause == RecoveryCause.RequestEof);
        var sub = RecoveryPolicy.ClassifyRpc(
            Session(), new ConnectionEpoch(2), RpcCodes.SubscriptionLost, RpcFailureKind.ConnectionLost,
            false, true, false, false);
        Check(sub.Cause == RecoveryCause.SubscriptionEof);
        Check(sub.Cause != request.Cause);
        var bridge = RecoveryPolicy.ClassifyRpc(
            Session(), new ConnectionEpoch(2), RpcCodes.ChildExited, RpcFailureKind.ConnectionLost,
            true, false, false, false);
        Check(bridge.Cause == RecoveryCause.RpcBridgeExit);
        var down = RecoveryPolicy.ClassifyRpc(
            Session(), new ConnectionEpoch(2), RpcCodes.Unavailable, RpcFailureKind.Unavailable,
            false, false, false, false);
        Check(down.Cause == RecoveryCause.DaemonUnreachable);
        var schema = RecoveryPolicy.ClassifyRpc(
            Session(), new ConnectionEpoch(2), ProjectionCodes.SchemaIncompatible, RpcFailureKind.Protocol,
            false, false, false, false);
        Check(schema.Cause == RecoveryCause.SchemaIncompatible);
        var proto = RecoveryPolicy.ClassifyRpc(
            Session(), new ConnectionEpoch(2), RpcCodes.ProtocolPollution, RpcFailureKind.Protocol,
            false, false, false, false);
        Check(proto.Cause == RecoveryCause.ProtocolError);
        var stop = RecoveryPolicy.ClassifyRpc(
            Session(), new ConnectionEpoch(2), RpcCodes.RequestLost, RpcFailureKind.ConnectionLost,
            true, false, false, true);
        Check(stop.Cause == RecoveryCause.AppStopping);
        var manual = RecoveryPolicy.ClassifyRpc(
            Session(), new ConnectionEpoch(2), RpcCodes.RequestLost, RpcFailureKind.ConnectionLost,
            true, false, true, false);
        Check(manual.Cause == RecoveryCause.ManualDisconnect);
    }

    static void BackoffBounds()
    {
        var expected = new[] { 1, 2, 4, 8, 16, 30 };
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            var zero = RecoveryBackoff.Delay(attempt, ZeroRecoveryEntropy.Instance);
            Check(zero == TimeSpan.FromSeconds(expected[attempt - 1]));
            var max = RecoveryBackoff.Delay(attempt, new MaxJitter());
            Check(max >= zero);
            Check(max <= RecoveryBackoff.Cap);
        }

        Check(RecoveryBackoff.Delay(6, ZeroRecoveryEntropy.Instance) == TimeSpan.FromSeconds(30));
        Check(RecoveryBackoff.Delay(9, new MaxJitter()) == RecoveryBackoff.Cap);
    }

    static void ExhaustThenAwait()
    {
        var failure = RecoveryPolicy.Create(
            RecoveryScope.Rpc, RecoveryCause.RequestEof, Session(), null, new ConnectionEpoch(1), false);
        var sixth = RecoveryPolicy.Decide(failure, 6, ZeroRecoveryEntropy.Instance);
        Check(sixth.Action == RecoveryAction.RetryAfter);
        Check(sixth.Delay == TimeSpan.FromSeconds(30));
        Check(!sixth.StartDaemon);
        var seventh = RecoveryPolicy.Decide(failure, 7, ZeroRecoveryEntropy.Instance);
        Check(seventh.Action == RecoveryAction.AwaitUser);
        Check(seventh.Delay == TimeSpan.Zero);
        Check(!seventh.StartDaemon);
        Check(RecoveryPolicy.Manual(true, RecoveryCause.RequestEof).Action == RecoveryAction.Stop);
        Check(RecoveryPolicy.Manual(false, RecoveryCause.RequestEof).Action == RecoveryAction.RetryNow);
        Check(!RecoveryPolicy.Manual(false, RecoveryCause.RequestEof).StartDaemon);
    }

    static void NonRetryCauses()
    {
        foreach (var cause in new[]
                 {
                     RecoveryCause.ManualDisconnect, RecoveryCause.AppStopping, RecoveryCause.SchemaIncompatible,
                     RecoveryCause.ProtocolError, RecoveryCause.Authentication, RecoveryCause.HostKeyChanged,
                     RecoveryCause.Cancellation
                 })
        {
            var failure = RecoveryPolicy.Create(
                RecoveryScope.Application, cause, Session(), null, new ConnectionEpoch(1),
                cause is RecoveryCause.ManualDisconnect or RecoveryCause.AppStopping);
            var decision = RecoveryPolicy.Decide(failure, 1, new MaxJitter());
            Check(decision.Action is RecoveryAction.Stop or RecoveryAction.AwaitUser);
            Check(decision.Delay == TimeSpan.Zero);
            Check(!decision.StartDaemon);
        }
    }

    sealed class MaxJitter : IRecoveryEntropy
    {
        public TimeSpan NextJitter(TimeSpan maxInclusive) => maxInclusive;
    }
}
