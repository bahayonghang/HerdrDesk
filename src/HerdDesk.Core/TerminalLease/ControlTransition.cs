using HerdDesk.Contracts;

namespace HerdDesk.Core;

public enum ControlEvent
{
    OpenPane,
    SelectPane,
    Focus,
    FirstFrame,
    ProcessAlive,
    RendererReady,
    RequestControl,
    OwnershipMatched,
    OwnershipMissing,
    CandidateFull,
    RendererAck,
    PromotionCheck,
    StoreRecheckStale,
    BusyClassified,
    RejectedClassified,
    UnknownClassified,
    ConfirmTakeover,
    ConfirmInvalid,
    CancelAcquire,
    ReleaseControl,
    TransportLost,
    RecoverObserve,
    CandidateClosed,
    ProjectionBecameStale,
    ProjectionReady,
    RendererFailed,
    AppStopping,
    TerminalClosed,
    TerminalStdoutEnded,
    TerminalProcessExited
}

[Flags]
public enum ControlTransitionEffects
{
    None = 0,
    OpenObserve = 1,
    OpenNoTakeoverCandidate = 2,
    OpenTakeoverCandidate = 4,
    CreateChallenge = 8,
    ConsumeChallenge = 16,
    InvalidateChallenge = 32,
    Promote = 64,
    RevokeWrite = 128,
    CloseCandidate = 256,
    CloseControl = 512,
    CloseObserve = 1024,
    IncrementGeneration = 2048,
    LatchOwnership = 4096,
    SetWritable = 8192
}

public sealed record ControlTransitionInput(
    TerminalAccess Access,
    bool ControlVerified,
    ControlAttemptOutcome LastAttempt,
    bool HasChallenge,
    bool HasCandidate,
    bool HasControl,
    bool HasObserve,
    bool OwnershipProved,
    bool CandidateHasFull,
    bool RendererAcked,
    bool StoreExists,
    bool StoreFresh,
    bool CapabilityAvailable,
    bool ChallengeValid,
    ControlEvent Event);

public sealed record ControlTransitionResult(
    TerminalAccess Access,
    bool ControlVerified,
    ControlAttemptOutcome LastAttempt,
    string Code,
    ControlTransitionEffects Effects);

public static class ControlTransition
{
    public static ControlTransitionResult Apply(ControlTransitionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Finish(ApplyCore(input));
    }

    public static bool MayAutoControl(ControlEvent evt) =>
        evt is ControlEvent.Focus or ControlEvent.FirstFrame or ControlEvent.ProcessAlive
            or ControlEvent.RendererReady;

    private static ControlTransitionResult ApplyCore(ControlTransitionInput input)
    {
        if (MayAutoControl(input.Event))
            return Stay(input, input.Access, input.LastAttempt, CodeFor(input), ControlTransitionEffects.None);

        return input.Event switch
        {
            ControlEvent.OpenPane or ControlEvent.SelectPane => OpenOrSelect(input),
            ControlEvent.RequestControl => RequestControl(input),
            ControlEvent.OwnershipMatched => OwnershipMatched(input),
            ControlEvent.OwnershipMissing => FailUnknown(input, ControlLeaseCodes.OwnershipUnverified),
            ControlEvent.CandidateFull or ControlEvent.RendererAck or ControlEvent.PromotionCheck =>
                PromotionCheck(input),
            ControlEvent.StoreRecheckStale => StoreStale(input),
            ControlEvent.BusyClassified => Busy(input),
            ControlEvent.RejectedClassified => Rejected(input),
            ControlEvent.UnknownClassified => FailUnknown(input, ControlLeaseCodes.OwnershipUnverified),
            ControlEvent.ConfirmTakeover => ConfirmTakeover(input),
            ControlEvent.ConfirmInvalid => ConfirmInvalid(input),
            ControlEvent.CancelAcquire => CancelAcquire(input),
            ControlEvent.ReleaseControl => ReleaseControl(input),
            ControlEvent.TransportLost => TransportLost(input),
            ControlEvent.RecoverObserve => RecoverObserve(input),
            ControlEvent.CandidateClosed => CandidateClosed(input),
            ControlEvent.ProjectionBecameStale => ProjectionStale(input),
            ControlEvent.ProjectionReady => ProjectionReady(input),
            ControlEvent.RendererFailed => RendererFailed(input),
            ControlEvent.AppStopping => AppStopping(input),
            ControlEvent.TerminalClosed => TerminalFault(input, ControlLeaseCodes.TerminalClosed),
            ControlEvent.TerminalStdoutEnded => TerminalFault(input, ControlLeaseCodes.TerminalStdoutEof),
            ControlEvent.TerminalProcessExited => TerminalFault(input, ControlLeaseCodes.TerminalClientExit),
            _ => Stay(input, NoVerify(input.Access), input.LastAttempt, CodeFor(input), ControlTransitionEffects.None)
        };
    }

    private static ControlTransitionResult OpenOrSelect(ControlTransitionInput input)
    {
        var effects = ControlTransitionEffects.OpenObserve | ControlTransitionEffects.InvalidateChallenge |
                      ControlTransitionEffects.RevokeWrite | ControlTransitionEffects.CloseCandidate |
                      ControlTransitionEffects.CloseControl;
        if (input.HasObserve || input.HasCandidate || input.HasControl ||
            input.Access is TerminalAccess.Controlling or TerminalAccess.Acquiring or TerminalAccess.Unknown)
            effects |= ControlTransitionEffects.IncrementGeneration;
        return new(
            TerminalAccess.Observing,
            false,
            ControlAttemptOutcome.None,
            ControlLeaseCodes.Observing,
            effects);
    }

    private static ControlTransitionResult RequestControl(ControlTransitionInput input)
    {
        if (input.Access == TerminalAccess.Controlling && input.ControlVerified)
            return Stay(input, TerminalAccess.Controlling, ControlAttemptOutcome.Promoted,
                ControlLeaseCodes.ControlVerified, ControlTransitionEffects.None);
        if (input.Access == TerminalAccess.Acquiring)
            return Stay(input, TerminalAccess.Acquiring, input.LastAttempt, ControlLeaseCodes.Acquiring,
                ControlTransitionEffects.None);
        if (!input.StoreExists || !input.StoreFresh)
            return Stay(input, NoVerify(input.Access), input.LastAttempt, ControlLeaseCodes.TargetStale,
                ControlTransitionEffects.InvalidateChallenge);
        if (!input.CapabilityAvailable)
            return Stay(input, NoVerify(input.Access), input.LastAttempt,
                ControlLeaseCodes.CapabilityUnavailable, ControlTransitionEffects.InvalidateChallenge);
        if (input.Access is TerminalAccess.Disconnected or TerminalAccess.Unknown && !input.HasObserve)
            return new(
                TerminalAccess.Observing,
                false,
                ControlAttemptOutcome.None,
                ControlLeaseCodes.Observing,
                ControlTransitionEffects.OpenObserve | ControlTransitionEffects.InvalidateChallenge);
        return new(
            TerminalAccess.Acquiring,
            false,
            ControlAttemptOutcome.None,
            ControlLeaseCodes.Acquiring,
            ControlTransitionEffects.OpenNoTakeoverCandidate | ControlTransitionEffects.InvalidateChallenge);
    }

    private static ControlTransitionResult OwnershipMatched(ControlTransitionInput input)
    {
        if (input.Access != TerminalAccess.Acquiring || !input.HasCandidate)
            return Stay(input, NoVerify(input.Access), input.LastAttempt, CodeFor(input),
                ControlTransitionEffects.None);
        var latched = PromotionCheck(input with { OwnershipProved = true });
        return latched with { Effects = latched.Effects | ControlTransitionEffects.LatchOwnership };
    }

    private static ControlTransitionResult PromotionCheck(ControlTransitionInput input)
    {
        if (input.Access != TerminalAccess.Acquiring)
            return Stay(input, NoVerify(input.Access), input.LastAttempt, CodeFor(input),
                ControlTransitionEffects.None);
        if (!input.OwnershipProved || !input.CandidateHasFull || !input.RendererAcked)
            return Stay(input, TerminalAccess.Acquiring, input.LastAttempt, ControlLeaseCodes.Acquiring,
                ControlTransitionEffects.None);
        if (!input.StoreExists || !input.StoreFresh)
            return Stay(input, TerminalAccess.Acquiring, input.LastAttempt, ControlLeaseCodes.TargetStale,
                ControlTransitionEffects.None);
        return new(
            TerminalAccess.Controlling,
            true,
            ControlAttemptOutcome.Promoted,
            ControlLeaseCodes.ControlVerified,
            ControlTransitionEffects.Promote | ControlTransitionEffects.CloseObserve |
            ControlTransitionEffects.SetWritable | ControlTransitionEffects.InvalidateChallenge);
    }

    private static ControlTransitionResult StoreStale(ControlTransitionInput input)
    {
        if (input.Access == TerminalAccess.Acquiring)
            return Stay(input, TerminalAccess.Acquiring, input.LastAttempt, ControlLeaseCodes.TargetStale,
                ControlTransitionEffects.RevokeWrite | ControlTransitionEffects.CloseCandidate);
        if (input.Access == TerminalAccess.Controlling)
            return FailUnknown(input, ControlLeaseCodes.TargetStale);
        return Stay(input, NoVerify(input.Access), input.LastAttempt, ControlLeaseCodes.TargetStale,
            ControlTransitionEffects.InvalidateChallenge | ControlTransitionEffects.RevokeWrite);
    }

    private static ControlTransitionResult Busy(ControlTransitionInput input)
    {
        if (input.Access != TerminalAccess.Acquiring)
            return Stay(input, NoVerify(input.Access), input.LastAttempt, CodeFor(input),
                ControlTransitionEffects.None);
        return new(
            TerminalAccess.Observing,
            false,
            ControlAttemptOutcome.Busy,
            ControlLeaseCodes.ControlBusy,
            ControlTransitionEffects.CloseCandidate | ControlTransitionEffects.CreateChallenge |
            ControlTransitionEffects.RevokeWrite);
    }

    private static ControlTransitionResult Rejected(ControlTransitionInput input)
    {
        if (input.Access != TerminalAccess.Acquiring)
            return Stay(input, NoVerify(input.Access), input.LastAttempt, CodeFor(input),
                ControlTransitionEffects.None);
        return new(
            TerminalAccess.Observing,
            false,
            ControlAttemptOutcome.Rejected,
            ControlLeaseCodes.ControlRejected,
            ControlTransitionEffects.CloseCandidate | ControlTransitionEffects.InvalidateChallenge |
            ControlTransitionEffects.RevokeWrite);
    }

    private static ControlTransitionResult ConfirmTakeover(ControlTransitionInput input)
    {
        if (input.Access != TerminalAccess.Observing || !input.HasChallenge || !input.ChallengeValid)
            return ConfirmInvalid(input);
        if (!input.StoreExists || !input.StoreFresh || !input.CapabilityAvailable)
            return ConfirmInvalid(input);
        return new(
            TerminalAccess.Acquiring,
            false,
            ControlAttemptOutcome.None,
            ControlLeaseCodes.Acquiring,
            ControlTransitionEffects.ConsumeChallenge | ControlTransitionEffects.OpenTakeoverCandidate |
            ControlTransitionEffects.CloseCandidate);
    }

    private static ControlTransitionResult ConfirmInvalid(ControlTransitionInput input) =>
        Stay(input, input.Access, input.LastAttempt, ControlLeaseCodes.TakeoverConfirmationStale,
            ControlTransitionEffects.None);

    private static ControlTransitionResult CancelAcquire(ControlTransitionInput input)
    {
        if (input.Access != TerminalAccess.Acquiring && !input.HasChallenge)
            return Stay(input, NoVerify(input.Access), input.LastAttempt, CodeFor(input),
                ControlTransitionEffects.InvalidateChallenge);
        return new(
            TerminalAccess.Observing,
            false,
            ControlAttemptOutcome.Cancelled,
            ControlLeaseCodes.Cancelled,
            ControlTransitionEffects.CloseCandidate | ControlTransitionEffects.InvalidateChallenge |
            ControlTransitionEffects.RevokeWrite);
    }

    private static ControlTransitionResult ReleaseControl(ControlTransitionInput input)
    {
        var effects = ControlTransitionEffects.RevokeWrite | ControlTransitionEffects.CloseCandidate |
                      ControlTransitionEffects.CloseControl | ControlTransitionEffects.InvalidateChallenge |
                      ControlTransitionEffects.IncrementGeneration | ControlTransitionEffects.OpenObserve;
        return new(
            TerminalAccess.Disconnected,
            false,
            ControlAttemptOutcome.Released,
            ControlLeaseCodes.Released,
            effects);
    }

    private static ControlTransitionResult TransportLost(ControlTransitionInput input)
    {
        var access = input.Access is TerminalAccess.Controlling or TerminalAccess.Acquiring
            ? TerminalAccess.Unknown
            : TerminalAccess.Disconnected;
        var outcome = access == TerminalAccess.Unknown
            ? ControlAttemptOutcome.Unknown
            : input.LastAttempt;
        return new(
            access,
            false,
            outcome,
            ControlLeaseCodes.TerminalDisconnected,
            ControlTransitionEffects.RevokeWrite | ControlTransitionEffects.CloseCandidate |
            ControlTransitionEffects.CloseControl | ControlTransitionEffects.CloseObserve |
            ControlTransitionEffects.InvalidateChallenge | ControlTransitionEffects.IncrementGeneration);
    }

    private static ControlTransitionResult RecoverObserve(ControlTransitionInput input) =>
        new(
            TerminalAccess.Disconnected,
            false,
            ControlAttemptOutcome.None,
            ControlLeaseCodes.Observing,
            ControlTransitionEffects.OpenObserve | ControlTransitionEffects.RevokeWrite |
            ControlTransitionEffects.CloseCandidate | ControlTransitionEffects.CloseControl |
            ControlTransitionEffects.InvalidateChallenge | ControlTransitionEffects.IncrementGeneration);

    private static ControlTransitionResult ProjectionStale(ControlTransitionInput input) =>
        new(
            TerminalAccess.Disconnected,
            false,
            ControlAttemptOutcome.Unknown,
            ControlLeaseCodes.TargetStale,
            ControlTransitionEffects.RevokeWrite | ControlTransitionEffects.CloseCandidate |
            ControlTransitionEffects.CloseControl | ControlTransitionEffects.CloseObserve |
            ControlTransitionEffects.InvalidateChallenge | ControlTransitionEffects.IncrementGeneration);

    private static ControlTransitionResult ProjectionReady(ControlTransitionInput input)
    {
        if (!input.StoreExists || !input.StoreFresh)
            return new(
                TerminalAccess.Disconnected,
                false,
                ControlAttemptOutcome.None,
                input.StoreExists ? ControlLeaseCodes.TargetStale : ControlLeaseCodes.PaneClosed,
                ControlTransitionEffects.RevokeWrite | ControlTransitionEffects.CloseCandidate |
                ControlTransitionEffects.CloseControl | ControlTransitionEffects.InvalidateChallenge);
        return RecoverObserve(input);
    }

    private static ControlTransitionResult RendererFailed(ControlTransitionInput input) =>
        TerminalFault(input, ControlLeaseCodes.RendererFailure);

    private static ControlTransitionResult AppStopping(ControlTransitionInput input) =>
        new(
            TerminalAccess.Disconnected,
            false,
            ControlAttemptOutcome.None,
            ControlLeaseCodes.AppStopping,
            ControlTransitionEffects.RevokeWrite | ControlTransitionEffects.CloseCandidate |
            ControlTransitionEffects.CloseControl | ControlTransitionEffects.CloseObserve |
            ControlTransitionEffects.InvalidateChallenge | ControlTransitionEffects.IncrementGeneration);

    private static ControlTransitionResult TerminalFault(ControlTransitionInput input, string code)
    {
        if (input.StoreExists && input.StoreFresh)
            return RecoverObserve(input) with { Code = code };
        return new(
            TerminalAccess.Disconnected,
            false,
            ControlAttemptOutcome.Unknown,
            code,
            ControlTransitionEffects.RevokeWrite | ControlTransitionEffects.CloseCandidate |
            ControlTransitionEffects.CloseControl | ControlTransitionEffects.CloseObserve |
            ControlTransitionEffects.InvalidateChallenge | ControlTransitionEffects.IncrementGeneration);
    }

    private static ControlTransitionResult CandidateClosed(ControlTransitionInput input)
    {
        if (input.Access == TerminalAccess.Unknown && input.HasObserve)
            return new(
                TerminalAccess.Observing,
                false,
                ControlAttemptOutcome.Unknown,
                ControlLeaseCodes.OwnershipUnverified,
                ControlTransitionEffects.RevokeWrite);
        return Stay(input, NoVerify(input.Access), input.LastAttempt, CodeFor(input),
            ControlTransitionEffects.None);
    }

    private static ControlTransitionResult FailUnknown(ControlTransitionInput input, string code) =>
        new(
            TerminalAccess.Unknown,
            false,
            ControlAttemptOutcome.Unknown,
            code,
            ControlTransitionEffects.RevokeWrite | ControlTransitionEffects.CloseCandidate |
            ControlTransitionEffects.InvalidateChallenge);

    private static ControlTransitionResult Stay(
        ControlTransitionInput input,
        TerminalAccess access,
        ControlAttemptOutcome outcome,
        string code,
        ControlTransitionEffects effects) =>
        new(access, access == TerminalAccess.Controlling && input.ControlVerified, outcome, code, effects);

    private static TerminalAccess NoVerify(TerminalAccess access) =>
        access == TerminalAccess.Controlling ? TerminalAccess.Acquiring : access;

    private static string CodeFor(ControlTransitionInput input)
    {
        if (!string.IsNullOrEmpty(input.LastAttempt.ToString()) && input.LastAttempt != ControlAttemptOutcome.None)
        {
            return input.LastAttempt switch
            {
                ControlAttemptOutcome.Busy => ControlLeaseCodes.ControlBusy,
                ControlAttemptOutcome.Rejected => ControlLeaseCodes.ControlRejected,
                ControlAttemptOutcome.Cancelled => ControlLeaseCodes.Cancelled,
                ControlAttemptOutcome.Unknown => ControlLeaseCodes.OwnershipUnverified,
                ControlAttemptOutcome.Promoted => ControlLeaseCodes.ControlVerified,
                ControlAttemptOutcome.Released => ControlLeaseCodes.Released,
                _ => AccessCode(input.Access)
            };
        }

        return AccessCode(input.Access);
    }

    private static string AccessCode(TerminalAccess access) =>
        access switch
        {
            TerminalAccess.Observing => ControlLeaseCodes.Observing,
            TerminalAccess.Acquiring => ControlLeaseCodes.Acquiring,
            TerminalAccess.Controlling => ControlLeaseCodes.ControlVerified,
            TerminalAccess.Unknown => ControlLeaseCodes.OwnershipUnverified,
            _ => ControlLeaseCodes.TerminalDisconnected
        };

    private static ControlTransitionResult Finish(ControlTransitionResult result)
    {
        var access = result.Access;
        var verified = result.ControlVerified;
        var effects = result.Effects;
        var code = result.Code;
        if (access != TerminalAccess.Controlling)
        {
            verified = false;
            effects &= ~ControlTransitionEffects.SetWritable;
        }
        else if (!verified)
        {
            access = TerminalAccess.Acquiring;
            verified = false;
            code = ControlLeaseCodes.OwnershipUnverified;
            effects &= ~ControlTransitionEffects.SetWritable;
            effects |= ControlTransitionEffects.RevokeWrite;
        }

        if ((effects & ControlTransitionEffects.OpenNoTakeoverCandidate) != 0 &&
            (effects & ControlTransitionEffects.OpenTakeoverCandidate) != 0)
            effects &= ~ControlTransitionEffects.OpenTakeoverCandidate;

        return new(access, verified, result.LastAttempt, code, effects);
    }
}
