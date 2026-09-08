namespace HerdDesk.Contracts;

public enum ControlIntent
{
    Observe,
    RequestControl,
    ConfirmTakeover,
    CancelAcquire,
    ReleaseControl,
    RecoverObserve
}

public enum LeaseRecoverySignal
{
    ProjectionStale,
    ProjectionReady,
    RendererFailed,
    AppStopping,
    TerminalClosed,
    TerminalStdoutEof,
    TerminalClientExit
}

public enum ControlAttemptOutcome
{
    None,
    Busy,
    Rejected,
    Cancelled,
    Unknown,
    Promoted,
    Released
}

public sealed record ControlBinding(
    PaneKey Pane,
    ConnectionEpoch Epoch,
    TerminalMode Mode,
    string? ControlAttemptId);

public sealed record TakeoverChallengeView(
    string Handle,
    string Breadcrumb,
    string TargetSummary,
    string BusyEvidenceId);

public sealed record InputSubmissionOutcome(
    ulong CommandId,
    TerminalWriteDisposition Disposition,
    string Code,
    long LeaseGeneration);

public sealed record ControlLeaseState(
    PaneKey? Target,
    ConnectionEpoch ProjectionEpoch,
    long ProjectionRevision,
    ControlBinding? ObserveBinding,
    ControlBinding? ControlBinding,
    ControlBinding? CandidateBinding,
    TerminalAccess Access,
    bool ControlVerified,
    string? AttemptId,
    long LeaseGeneration,
    ControlAttemptOutcome LastAttempt,
    string? LastCode,
    TakeoverChallengeView? Challenge,
    InputSubmissionOutcome? LastInput)
{
    public static ControlLeaseState Disconnected { get; } = new(
        null,
        new ConnectionEpoch(0),
        0,
        null,
        null,
        null,
        TerminalAccess.Disconnected,
        false,
        null,
        1,
        ControlAttemptOutcome.None,
        null,
        null,
        null);
}

public sealed record LeaseTargetSnapshot(
    PaneKey Pane,
    bool Exists,
    ConnectionEpoch ProjectionEpoch,
    long ProjectionRevision,
    DeviceFreshness Freshness,
    CapabilityProfile Capabilities,
    string Breadcrumb,
    string TargetSummary);

public interface ILeaseTargetStore
{
    LeaseTargetSnapshot Read(PaneKey pane);
}

public interface IControlBindingHost
{
    ValueTask<ITerminalTransport> OpenObserveAsync(
        PaneKey pane,
        ConnectionEpoch epoch,
        CancellationToken cancellationToken = default);

    ValueTask<ITerminalTransport> OpenControlCandidateAsync(
        PaneKey pane,
        ConnectionEpoch epoch,
        string attemptId,
        TerminalTakeoverAuthorization? takeover,
        CancellationToken cancellationToken = default);
}

public interface IControlLeaseCoordinator : IAsyncDisposable
{
    ControlLeaseState Current { get; }

    IAsyncEnumerable<ControlLeaseState> ReadStatesAsync(CancellationToken cancellationToken = default);

    ValueTask OpenPaneAsync(PaneKey pane, CancellationToken cancellationToken = default);

    ValueTask SelectPaneAsync(PaneKey pane, CancellationToken cancellationToken = default);

    ValueTask NoteFocusAsync(CancellationToken cancellationToken = default);

    ValueTask NoteProcessAliveAsync(CancellationToken cancellationToken = default);

    ValueTask RequestControlAsync(CancellationToken cancellationToken = default);

    ValueTask ConfirmTakeoverAsync(string handle, CancellationToken cancellationToken = default);

    ValueTask CancelAcquireAsync(CancellationToken cancellationToken = default);

    ValueTask ReleaseControlAsync(CancellationToken cancellationToken = default);

    ValueTask RecoverObserveAsync(CancellationToken cancellationToken = default);

    ValueTask NoteRecoverySignalAsync(
        LeaseRecoverySignal signal,
        CancellationToken cancellationToken = default);

    ValueTask<InputSubmissionOutcome> SubmitInputAsync(
        RendererInput input,
        CancellationToken cancellationToken = default);

    ValueTask<InputSubmissionOutcome> SubmitResizeAsync(
        TerminalResizeCommand size,
        CancellationToken cancellationToken = default);

    ValueTask<InputSubmissionOutcome> SubmitScrollAsync(
        TerminalScrollCommand request,
        CancellationToken cancellationToken = default);
}
