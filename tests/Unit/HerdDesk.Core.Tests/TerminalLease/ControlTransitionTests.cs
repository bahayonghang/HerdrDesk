using HerdDesk.Contracts;
using HerdDesk.Core;

internal static class ControlTransitionTests
{
    public static (string Name, Action Run)[] All =>
    [
        ("transition table keeps five access values and busy as outcome", ExhaustiveTable),
        ("open select focus frame process stay observing unverified", OpenSelectFocusStayObserve),
        ("request control never opens takeover", RequestControlNoTakeover),
        ("promotion needs four gates", FourGates),
        ("busy stays observing with challenge effect", BusyOutcome),
        ("invalid confirm never opens takeover", InvalidConfirm)
    ];

    static void Check(bool condition)
    {
        if (!condition)
            throw new Exception("assertion_failed");
    }

    static ControlTransitionInput Base(
        TerminalAccess access,
        ControlEvent evt,
        bool verified = false,
        bool hasChallenge = false,
        bool hasCandidate = false,
        bool hasControl = false,
        bool hasObserve = true,
        bool ownership = false,
        bool full = false,
        bool ack = false,
        bool storeExists = true,
        bool storeFresh = true,
        bool capability = true,
        bool challengeValid = false) =>
        new(access, verified, ControlAttemptOutcome.None, hasChallenge, hasCandidate, hasControl, hasObserve,
            ownership, full, ack, storeExists, storeFresh, capability, challengeValid, evt);

    static void ExhaustiveTable()
    {
        foreach (var access in Enum.GetValues<TerminalAccess>())
        foreach (var evt in Enum.GetValues<ControlEvent>())
        {
            var result = ControlTransition.Apply(Base(
                access,
                evt,
                verified: access == TerminalAccess.Controlling,
                hasControl: access == TerminalAccess.Controlling,
                hasCandidate: access == TerminalAccess.Acquiring,
                hasObserve: access != TerminalAccess.Disconnected));
            Check(Enum.IsDefined(result.Access));
            Check(result.Access is TerminalAccess.Disconnected or TerminalAccess.Observing
                or TerminalAccess.Acquiring or TerminalAccess.Controlling or TerminalAccess.Unknown);
            Check(result.Access != TerminalAccess.Controlling || result.ControlVerified);
            Check(result.Access == TerminalAccess.Controlling || !result.ControlVerified);
            if (ControlTransition.MayAutoControl(evt) && access != TerminalAccess.Controlling)
            {
                Check(!result.ControlVerified);
                Check((result.Effects & ControlTransitionEffects.OpenNoTakeoverCandidate) == 0);
                Check((result.Effects & ControlTransitionEffects.OpenTakeoverCandidate) == 0);
                Check((result.Effects & ControlTransitionEffects.SetWritable) == 0);
            }
        }
    }

    static void OpenSelectFocusStayObserve()
    {
        foreach (var evt in new[]
                 {
                     ControlEvent.OpenPane, ControlEvent.SelectPane, ControlEvent.Focus, ControlEvent.FirstFrame,
                     ControlEvent.ProcessAlive, ControlEvent.RendererReady
                 })
        {
            var from = evt is ControlEvent.OpenPane or ControlEvent.SelectPane
                ? TerminalAccess.Disconnected
                : TerminalAccess.Observing;
            var result = ControlTransition.Apply(Base(from, evt, hasObserve: from != TerminalAccess.Disconnected));
            Check(result.Access is TerminalAccess.Observing or TerminalAccess.Disconnected);
            Check(!result.ControlVerified);
            Check((result.Effects & ControlTransitionEffects.SetWritable) == 0);
            Check((result.Effects & ControlTransitionEffects.OpenTakeoverCandidate) == 0);
        }
    }

    static void RequestControlNoTakeover()
    {
        var result = ControlTransition.Apply(Base(TerminalAccess.Observing, ControlEvent.RequestControl));
        Check(result.Access == TerminalAccess.Acquiring);
        Check(!result.ControlVerified);
        Check((result.Effects & ControlTransitionEffects.OpenNoTakeoverCandidate) != 0);
        Check((result.Effects & ControlTransitionEffects.OpenTakeoverCandidate) == 0);
    }

    static void FourGates()
    {
        var missingOwnership = ControlTransition.Apply(Base(
            TerminalAccess.Acquiring, ControlEvent.PromotionCheck, hasCandidate: true, full: true, ack: true));
        Check(missingOwnership.Access == TerminalAccess.Acquiring);
        Check(!missingOwnership.ControlVerified);
        var missingFull = ControlTransition.Apply(Base(
            TerminalAccess.Acquiring, ControlEvent.PromotionCheck, hasCandidate: true, ownership: true, ack: true));
        Check(missingFull.Access == TerminalAccess.Acquiring);
        var missingAck = ControlTransition.Apply(Base(
            TerminalAccess.Acquiring, ControlEvent.PromotionCheck, hasCandidate: true, ownership: true, full: true));
        Check(missingAck.Access == TerminalAccess.Acquiring);
        var stale = ControlTransition.Apply(Base(
            TerminalAccess.Acquiring, ControlEvent.PromotionCheck, hasCandidate: true, ownership: true, full: true,
            ack: true, storeFresh: false));
        Check(stale.Access == TerminalAccess.Acquiring);
        Check(!stale.ControlVerified);
        var ready = ControlTransition.Apply(Base(
            TerminalAccess.Acquiring, ControlEvent.PromotionCheck, hasCandidate: true, ownership: true, full: true,
            ack: true));
        Check(ready.Access == TerminalAccess.Controlling);
        Check(ready.ControlVerified);
        Check((ready.Effects & ControlTransitionEffects.SetWritable) != 0);
    }

    static void BusyOutcome()
    {
        var result = ControlTransition.Apply(Base(
            TerminalAccess.Acquiring, ControlEvent.BusyClassified, hasCandidate: true));
        Check(result.Access == TerminalAccess.Observing);
        Check(!result.ControlVerified);
        Check(result.LastAttempt == ControlAttemptOutcome.Busy);
        Check((result.Effects & ControlTransitionEffects.CreateChallenge) != 0);
        Check((result.Effects & ControlTransitionEffects.OpenTakeoverCandidate) == 0);
        Check((result.Effects & ControlTransitionEffects.SetWritable) == 0);
    }

    static void InvalidConfirm()
    {
        var result = ControlTransition.Apply(Base(
            TerminalAccess.Observing, ControlEvent.ConfirmTakeover, hasChallenge: true, challengeValid: false));
        Check((result.Effects & ControlTransitionEffects.OpenTakeoverCandidate) == 0);
        Check((result.Effects & ControlTransitionEffects.InvalidateChallenge) == 0);
        Check(result.Code == ControlLeaseCodes.TakeoverConfirmationStale);
        var valid = ControlTransition.Apply(Base(
            TerminalAccess.Observing, ControlEvent.ConfirmTakeover, hasChallenge: true, challengeValid: true));
        Check((valid.Effects & ControlTransitionEffects.OpenTakeoverCandidate) != 0);
        Check((valid.Effects & ControlTransitionEffects.OpenNoTakeoverCandidate) == 0);
        var fromAcquire = ControlTransition.Apply(Base(
            TerminalAccess.Acquiring, ControlEvent.ConfirmTakeover, hasChallenge: true, challengeValid: true,
            hasCandidate: true));
        Check((fromAcquire.Effects & ControlTransitionEffects.OpenTakeoverCandidate) == 0);
        Check(fromAcquire.Code == ControlLeaseCodes.TakeoverConfirmationStale);
    }
}
