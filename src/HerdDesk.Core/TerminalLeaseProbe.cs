using HerdDesk.Contracts;

namespace HerdDesk.Core;

/// <summary>
/// Maps observed terminal-session CLI/protocol facts to TerminalAccess.
/// Does not call herdr, send input, or invent a Granted wire message.
/// First frame, process alive, and window focus do not set ControlVerified.
/// </summary>
public static class TerminalLeaseProbe
{
    public static TerminalLeaseResult Map(TerminalLeaseObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        if (!Enum.IsDefined(observation.Operation))
            return Result(TerminalAccess.Unknown, false, TerminalStreamEndKind.Unknown, false,
                "invalid_observation", "diag-invalid-observation");

        var streamEnd = ClassifyStreamEnd(
            observation.StdoutEofSeen,
            observation.TerminalClosedSeen,
            observation.BridgeProcessExited);
        var paneExit = observation.PaneAliveObserved == false;

        if (IsFictionalGranted(observation.ObservedWireType) ||
            IsFictionalGranted(observation.ControlSignal))
            return Result(TerminalAccess.Unknown, false, streamEnd, paneExit,
                "fictional_granted_rejected", "diag-fictional-granted");

        if (observation.Operation == TerminalLeaseOperation.Observe && observation.InputSent)
            return Result(TerminalAccess.Observing, false, streamEnd, paneExit,
                "observe_input_denied", "diag-observe-input-denied");

        return observation.Operation switch
        {
            TerminalLeaseOperation.Observe => MapObserve(streamEnd, paneExit),
            TerminalLeaseOperation.RequestControl => MapRequestControl(observation, streamEnd, paneExit),
            TerminalLeaseOperation.RequestTakeover => MapRequestTakeover(observation, streamEnd, paneExit),
            TerminalLeaseOperation.ResizeWhileVerified => MapResize(observation, streamEnd, paneExit),
            TerminalLeaseOperation.Release => MapRelease(observation, streamEnd, paneExit),
            _ => Result(TerminalAccess.Unknown, false, streamEnd, paneExit,
                "invalid_observation", "diag-invalid-observation"),
        };
    }

    public static TerminalStreamEndKind ClassifyStreamEnd(
        bool stdoutEofSeen,
        bool terminalClosedSeen,
        bool bridgeProcessExited)
    {
        if (terminalClosedSeen)
            return TerminalStreamEndKind.TerminalClosed;
        if (stdoutEofSeen)
            return TerminalStreamEndKind.StdoutEof;
        if (bridgeProcessExited)
            return TerminalStreamEndKind.BridgeProcessExit;
        return TerminalStreamEndKind.None;
    }

    private static TerminalLeaseResult MapObserve(
        TerminalStreamEndKind streamEnd,
        bool paneExit)
    {
        if (paneExit)
            return Result(TerminalAccess.Disconnected, false, streamEnd, true,
                "disconnected", "diag-disconnected");
        if (streamEnd != TerminalStreamEndKind.None)
            return Result(TerminalAccess.Disconnected, false, streamEnd, false,
                "disconnected", "diag-disconnected");
        return Result(TerminalAccess.Observing, false, TerminalStreamEndKind.None, false,
            "observing", "diag-observing");
    }

    private static TerminalLeaseResult MapRequestControl(
        TerminalLeaseObservation observation,
        TerminalStreamEndKind streamEnd,
        bool paneExit)
    {
        if (paneExit)
            return Result(TerminalAccess.Disconnected, false, streamEnd, true,
                "disconnected", "diag-disconnected");
        if (observation.ControlSignal == TerminalControlSignal.Busy)
            return Result(TerminalAccess.Observing, false, TerminalStreamEndKind.None, false,
                "busy", "diag-busy");
        if (observation.ControlSignal == TerminalControlSignal.Rejected)
            return Result(TerminalAccess.Observing, false, TerminalStreamEndKind.None, false,
                "rejected", "diag-rejected");
        if (observation.ControlSignal == TerminalControlSignal.TakeoverRequired &&
            !observation.TakeoverConfirmed)
            return Result(TerminalAccess.Observing, false, TerminalStreamEndKind.None, false,
                "takeover_required", "diag-takeover-required");
        if (!Enum.IsDefined(observation.ControlSignal) ||
            observation.ControlSignal == TerminalControlSignal.Unknown)
            return Result(TerminalAccess.Unknown, false, streamEnd, false,
                "unknown_control_signal", "diag-unknown-signal");
        if (AdapterProved(observation) && streamEnd == TerminalStreamEndKind.None)
            return Result(TerminalAccess.Controlling, true, TerminalStreamEndKind.None, false,
                "control_verified", "diag-control-verified");
        if (streamEnd != TerminalStreamEndKind.None)
            return Result(TerminalAccess.Disconnected, false, streamEnd, false,
                "disconnected", "diag-disconnected");
        if (observation.InputSent && !observation.InputAcknowledged)
            return Result(TerminalAccess.Acquiring, false, TerminalStreamEndKind.None, false,
                "input_result_unknown", "diag-input-result-unknown");
        return Result(TerminalAccess.Acquiring, false, TerminalStreamEndKind.None, false,
            "control_unconfirmed", "diag-control-unconfirmed");
    }

    private static TerminalLeaseResult MapRequestTakeover(
        TerminalLeaseObservation observation,
        TerminalStreamEndKind streamEnd,
        bool paneExit)
    {
        if (!observation.TakeoverConfirmed)
            return Result(TerminalAccess.Observing, false, streamEnd, paneExit,
                "takeover_not_confirmed", "diag-takeover-not-confirmed");
        if (paneExit)
            return Result(TerminalAccess.Disconnected, false, streamEnd, true,
                "disconnected", "diag-disconnected");
        if (streamEnd != TerminalStreamEndKind.None)
            return Result(TerminalAccess.Disconnected, false, streamEnd, false,
                "disconnected", "diag-disconnected");
        if (AdapterProved(observation))
            return Result(TerminalAccess.Controlling, true, TerminalStreamEndKind.None, false,
                "control_verified", "diag-control-verified");
        return Result(TerminalAccess.Acquiring, false, TerminalStreamEndKind.None, false,
            "acquiring", "diag-acquiring");
    }

    private static TerminalLeaseResult MapResize(
        TerminalLeaseObservation observation,
        TerminalStreamEndKind streamEnd,
        bool paneExit)
    {
        if (paneExit)
            return Result(TerminalAccess.Disconnected, false, streamEnd, true,
                "disconnected", "diag-disconnected");
        if (streamEnd != TerminalStreamEndKind.None)
            return Result(TerminalAccess.Disconnected, false, streamEnd, false,
                "disconnected", "diag-disconnected");
        if (observation.AccessBefore != TerminalAccess.Controlling ||
            !observation.ControlVerifiedBefore ||
            !AdapterProved(observation))
            return Result(
                observation.AccessBefore,
                false,
                TerminalStreamEndKind.None,
                false,
                "control_not_verified",
                "diag-control-not-verified");
        if (!observation.ResizeAcknowledged)
            return Result(TerminalAccess.Controlling, true, TerminalStreamEndKind.None, false,
                "resize_unacknowledged", "diag-resize-unacknowledged");
        return Result(TerminalAccess.Controlling, true, TerminalStreamEndKind.None, false,
            "resized", "diag-resized");
    }

    private static TerminalLeaseResult MapRelease(
        TerminalLeaseObservation observation,
        TerminalStreamEndKind streamEnd,
        bool paneExit)
    {
        var code = observation.ReleaseAcknowledged ? "released" : "release_unacknowledged";
        var diagnostic = observation.ReleaseAcknowledged
            ? "diag-released"
            : "diag-release-unacknowledged";
        if (paneExit)
            return Result(TerminalAccess.Disconnected, false, streamEnd, true, code, diagnostic);
        return Result(TerminalAccess.Observing, false, streamEnd, false, code, diagnostic);
    }

    private static bool AdapterProved(TerminalLeaseObservation observation) =>
        observation.AdapterProvedWriteOwnership &&
        !IsFictionalGranted(observation.ObservedWireType);

    private static bool IsFictionalGranted(string? wireType) =>
        string.Equals(wireType, "terminal.granted", StringComparison.Ordinal);

    private static bool IsFictionalGranted(TerminalControlSignal signal) =>
        string.Equals(signal.ToString(), "Granted", StringComparison.Ordinal);

    private static TerminalLeaseResult Result(
        TerminalAccess access,
        bool controlVerified,
        TerminalStreamEndKind streamEnd,
        bool paneExitVerified,
        string code,
        string diagnosticId)
    {
        if (access != TerminalAccess.Controlling)
            controlVerified = false;
        else if (!controlVerified)
            access = TerminalAccess.Acquiring;
        return new TerminalLeaseResult(
            access,
            controlVerified,
            streamEnd,
            paneExitVerified,
            code,
            diagnosticId);
    }
}
