using HerdDesk.Core;

internal static class RecoveryBackoffTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("equal jitter table matches 1-2 2-4 4-8 8-16 then 15-30", EqualJitterTable),
        ("equal jitter clamps overflow and unit interval", EqualJitterClamp),
        ("non retry kinds produce zero delay", NonRetryZeroDelay),
        ("explicit retry stays blocked without evidence revision", ExplicitRetryFailClosed)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static void EqualJitterTable()
    {
        var units = new[] { 0.0, 0.5, Math.BitDecrement(1.0) };
        for (var n = 0; n <= 100; n++)
        {
            var (minSeconds, maxSeconds) = n switch
            {
                0 => (1.0, 2.0),
                1 => (2.0, 4.0),
                2 => (4.0, 8.0),
                3 => (8.0, 16.0),
                _ => (15.0, 30.0)
            };
            foreach (var unit in units)
            {
                var delay = RecoveryBackoff.EqualJitter(n, unit);
                Check(delay >= TimeSpan.FromSeconds(minSeconds));
                Check(delay < TimeSpan.FromSeconds(maxSeconds) || delay == TimeSpan.FromSeconds(30));
                Check(delay >= RecoveryBackoff.MinDelay);
                Check(delay <= RecoveryBackoff.Cap);
            }
        }

        Check(RecoveryBackoff.EqualJitter(0, 0) == TimeSpan.FromSeconds(1));
        Check(RecoveryBackoff.EqualJitter(1, 0) == TimeSpan.FromSeconds(2));
        Check(RecoveryBackoff.EqualJitter(2, 0) == TimeSpan.FromSeconds(4));
        Check(RecoveryBackoff.EqualJitter(3, 0) == TimeSpan.FromSeconds(8));
        Check(RecoveryBackoff.EqualJitter(4, 0) == TimeSpan.FromSeconds(15));
        Check(RecoveryBackoff.EqualJitter(0, 0.5) == TimeSpan.FromMilliseconds(1500));
    }

    static void EqualJitterClamp()
    {
        Check(RecoveryBackoff.EqualJitter(-3, 0) == TimeSpan.FromSeconds(1));
        Check(RecoveryBackoff.EqualJitter(0, -1) == TimeSpan.FromSeconds(1));
        Check(RecoveryBackoff.EqualJitter(0, double.NaN) == TimeSpan.FromSeconds(1));
        var high = RecoveryBackoff.EqualJitter(8, 2);
        Check(high >= TimeSpan.FromSeconds(15));
        Check(high <= TimeSpan.FromSeconds(30));
    }

    static void NonRetryZeroDelay()
    {
        var session = DeviceSessionGraphs.DefaultSession();
        foreach (var cause in new[]
                 {
                     RecoveryCause.AuthenticationBlocked, RecoveryCause.UnsupportedAuthentication,
                     RecoveryCause.HostKeyUnknown, RecoveryCause.HostKeyChanged,
                     RecoveryCause.DaemonUnavailable, RecoveryCause.ProtocolPollution,
                     RecoveryCause.Incompatible, RecoveryCause.UnknownBlocked, RecoveryCause.Cancelled
                 })
        {
            var failure = RecoveryPolicy.Create(
                RecoveryScope.Rpc, cause, session, null, new HerdDesk.Contracts.ConnectionEpoch(1), false);
            var decision = RecoveryPolicy.DecideRemote(failure, 0, new SequenceRetryRandom(0.9));
            Check(decision.Delay == TimeSpan.Zero);
            Check(decision.Action is RecoveryAction.Stop or RecoveryAction.AwaitUser);
            Check(!decision.StartDaemon);
        }
    }

    static void ExplicitRetryFailClosed()
    {
        var device = DeviceSessionGraphs.DefaultSession().Device;
        var authMissing = new RecoveryBlockSnapshot(
            device, 3, RecoveryCause.AuthenticationBlocked, null, null,
            RecoveryCodes.AuthenticationActionRequired);
        var sameProfile = new SessionRecoveryContext(3, 9, 1, true, true);
        Check(!RecoveryPolicy.CanExplicitRetry(authMissing, sameProfile));
        var auth = authMissing with { CredentialRevision = 5 };
        Check(!RecoveryPolicy.CanExplicitRetry(auth, sameProfile with { CredentialRevision = 5 }));
        Check(RecoveryPolicy.CanExplicitRetry(auth, sameProfile with { CredentialRevision = 6 }));
        var hostMissing = new RecoveryBlockSnapshot(
            device, 3, RecoveryCause.HostKeyChanged, null, null,
            RecoveryCodes.HostKeyReviewRequired);
        Check(!RecoveryPolicy.CanExplicitRetry(
            hostMissing, new SessionRecoveryContext(4, 1, 9, true, true)));
        var host = hostMissing with { KnownHostRevision = 8 };
        Check(RecoveryPolicy.CanExplicitRetry(host, new SessionRecoveryContext(3, 1, 9, true, true)));
        Check(!RecoveryPolicy.CanExplicitRetry(host, new SessionRecoveryContext(3, 1, 9, false, true)));
    }
}
