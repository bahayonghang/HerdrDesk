namespace HerdDesk.Core;

public static class ControlLeaseCodes
{
    public const string Observing = "observing";
    public const string Acquiring = "acquiring";
    public const string ControlVerified = "control_verified";
    public const string TargetStale = "target_stale";
    public const string ControlBusy = "control_busy";
    public const string ControlRejected = "control_rejected";
    public const string OwnershipUnverified = "ownership_unverified";
    public const string TakeoverConfirmationStale = "takeover_confirmation_stale";
    public const string CandidateBackpressure = "candidate_backpressure";
    public const string TerminalDisconnected = "terminal_disconnected";
    public const string InputNotSent = "input_not_sent";
    public const string InputOutcomeUnknown = "input_outcome_unknown";
    public const string ControlNotVerified = "control_not_verified";
    public const string ObserveNoResize = "observe_no_resize";
    public const string ObserveScrollDenied = "observe_scroll_denied";
    public const string WrongPane = "wrong_pane";
    public const string StaleEpoch = "stale_epoch";
    public const string Cancelled = "cancelled";
    public const string Released = "released";
    public const string InvalidIdentity = "invalid_identity";
    public const string CapabilityUnavailable = "control_capability_unavailable";
    public const string InitialFullRequired = "initial_full_frame_required";
}
