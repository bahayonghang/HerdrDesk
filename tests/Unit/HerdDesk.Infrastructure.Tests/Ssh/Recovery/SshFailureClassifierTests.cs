using HerdDesk.Contracts;
using HerdDesk.Core;
using HerdDesk.Infrastructure.Ssh;
using HerdDesk.Infrastructure.SshTransports;

internal static class SshFailureClassifierTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("known stages map onto recovery causes", KnownStages),
        ("localized and malicious stderr stay unknown blocked", StderrFailClosed),
        ("unknown defaults to manual block", UnknownBlocked),
        ("classifier output has no secrets", NoSecrets)
    ];

    static SessionKey Session() =>
        new(new DeviceId(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")), "named-session", "dev");

    static void KnownStages()
    {
        var epoch = new ConnectionEpoch(2);
        var auth = SshFailureClassifier.Classify(
            Session(), epoch, test: Result(SshConnectionTestPhase.Failed, SshCodes.AuthFailed));
        SshFixtures.Check(auth.Cause == RecoveryCause.AuthenticationBlocked);
        SshFixtures.Check(auth.RetryClass == RecoveryRetryClass.AwaitUser);
        var unsupported = SshFailureClassifier.Classify(
            Session(), epoch, test: Result(SshConnectionTestPhase.Failed, SshCodes.AuthUnsupported));
        SshFixtures.Check(unsupported.Cause == RecoveryCause.UnsupportedAuthentication);
        var unknownHost = SshFailureClassifier.Classify(
            Session(), epoch,
            hostKey: new HostKeyAssessment(HostKeyStatus.UnknownCandidate, null, 1));
        SshFixtures.Check(unknownHost.Cause == RecoveryCause.HostKeyUnknown);
        var changed = SshFailureClassifier.Classify(
            Session(), epoch,
            hostKey: new HostKeyAssessment(HostKeyStatus.Changed, null, 2));
        SshFixtures.Check(changed.Cause == RecoveryCause.HostKeyChanged);
        var pollution = SshFailureClassifier.Classify(
            Session(), epoch, transport: Outcome(SshTransportCodes.StdoutProtocolPollution));
        SshFixtures.Check(pollution.Cause == RecoveryCause.ProtocolPollution);
        SshFixtures.Check(pollution.RetryClass == RecoveryRetryClass.AwaitUser);
        var truncated = SshFailureClassifier.Classify(
            Session(), epoch, transport: Outcome(SshTransportCodes.StreamTruncated));
        SshFixtures.Check(truncated.Cause == RecoveryCause.TransientTransport);
        SshFixtures.Check(truncated.RetryClass == RecoveryRetryClass.Transient);
        var daemon = SshFailureClassifier.Classify(
            Session(), epoch, transport: Outcome(SshTransportCodes.DaemonUnavailable));
        SshFixtures.Check(daemon.Cause == RecoveryCause.DaemonUnavailable);
        SshFixtures.Check(daemon.RetryClass == RecoveryRetryClass.AwaitUser);
        var cancelled = SshFailureClassifier.Classify(
            Session(), epoch, transport: Outcome(SshTransportCodes.TransportCancelled));
        SshFixtures.Check(cancelled.Cause == RecoveryCause.Cancelled);
        SshFixtures.Check(cancelled.RetryClass == RecoveryRetryClass.Stop);
        var testCancelled = SshFailureClassifier.Classify(
            Session(), epoch, test: Result(SshConnectionTestPhase.Cancelled, SshCodes.TestCancelled));
        SshFixtures.Check(testCancelled.Cause == RecoveryCause.Cancelled);
        SshFixtures.Check(testCancelled.RetryClass == RecoveryRetryClass.Stop);
        var unavailableHost = SshFailureClassifier.Classify(
            Session(), epoch, hostKey: new HostKeyAssessment(HostKeyStatus.Unavailable, null, 1));
        SshFixtures.Check(unavailableHost.Cause == RecoveryCause.UnknownBlocked);
        SshFixtures.Check(unavailableHost.RetryClass == RecoveryRetryClass.AwaitUser);
    }

    static void StderrFailClosed()
    {
        var epoch = new ConnectionEpoch(3);
        var localized = SshFailureClassifier.Classify(
            Session(), epoch,
            transport: Outcome(SshTransportCodes.UnclassifiedExit),
            stderr: "连接被拒绝 Permission denied");
        SshFixtures.Check(localized.Cause == RecoveryCause.UnknownBlocked);
        SshFixtures.Check(localized.RetryClass == RecoveryRetryClass.AwaitUser);
        SshFixtures.Check(localized.DiagnosticId != "连接被拒绝 Permission denied");
        var malicious = SshFailureClassifier.Classify(
            Session(), epoch,
            transport: Outcome(SshTransportCodes.UnclassifiedExit),
            stderr: "password=supersecret-password -----BEGIN PRIVATE KEY-----");
        SshFixtures.Check(malicious.Cause == RecoveryCause.UnknownBlocked);
        SshFixtures.Check(!malicious.DiagnosticId.Contains("password", StringComparison.OrdinalIgnoreCase));
        SshFixtures.Check(!malicious.DiagnosticId.Contains("PRIVATE", StringComparison.Ordinal));
    }

    static void UnknownBlocked()
    {
        var failure = SshFailureClassifier.Classify(Session(), new ConnectionEpoch(1));
        SshFixtures.Check(failure.Cause == RecoveryCause.UnknownBlocked);
        SshFixtures.Check(failure.RetryClass == RecoveryRetryClass.AwaitUser);
        var weird = SshFailureClassifier.Classify(
            Session(), new ConnectionEpoch(1),
            transport: Outcome("ssh: some localized banner"));
        SshFixtures.Check(weird.Cause == RecoveryCause.UnknownBlocked);
        SshFixtures.Check(weird.RetryClass == RecoveryRetryClass.AwaitUser);
    }

    static void NoSecrets()
    {
        var failure = SshFailureClassifier.Classify(
            Session(), new ConnectionEpoch(4),
            test: Result(SshConnectionTestPhase.Failed, SshCodes.AuthFailed),
            stderr: "password=supersecret-password host=evil.example path=/home/canary");
        SshFixtures.Check(!failure.DiagnosticId.Contains("password", StringComparison.OrdinalIgnoreCase));
        SshFixtures.Check(!failure.DiagnosticId.Contains("evil.example", StringComparison.Ordinal));
        SshFixtures.Check(!failure.DiagnosticId.Contains("/home/canary", StringComparison.Ordinal));
        SshFixtures.Check(!failure.DiagnosticId.Contains('\\', StringComparison.Ordinal));
    }

    static SshConnectionTestResult Result(SshConnectionTestPhase phase, string code) =>
        new(phase, code, [], null, null);

    static SshTransportOutcome Outcome(string code) =>
        SshTransportMapper.Outcome(
            SshChannelKind.RequestRpc, SshTransportStage.Failed, new ConnectionEpoch(2),
            code, 1, 0, 0, 255, 11);
}
