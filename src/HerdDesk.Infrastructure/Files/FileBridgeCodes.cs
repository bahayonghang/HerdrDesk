namespace HerdDesk.Infrastructure.Files;

internal static class FileBridgeCodes
{
    public const string ProtocolNotActive = "protocol_not_active";
    public const string ProtocolPollution = "protocol_pollution";
    public const string UnsupportedVersion = "unsupported_version";
    public const string UnknownKind = "unknown_kind";
    public const string UnknownFlags = "unknown_flags";
    public const string PayloadTooLarge = "payload_too_large";
    public const string InvalidPayloadLength = "invalid_payload_length";
    public const string TruncatedHeader = "truncated_header";
    public const string TruncatedPayload = "truncated_payload";
    public const string SequenceGap = "sequence_gap";
    public const string SequenceReplay = "sequence_replay";
    public const string SequenceWrap = "sequence_wrap";
    public const string WrongDirection = "wrong_direction";
    public const string UnexpectedKind = "unexpected_kind";
    public const string SecondRequest = "second_request";
    public const string DataBeforeAccepted = "data_before_accepted";
    public const string ProcessWouldExit = "process_would_exit";
    public const string ControlBudgetExceeded = "control_budget_exceeded";
    public const string EofWithoutTerminal = "eof_without_terminal";
    public const string TerminalResultMismatch = "terminal_result_mismatch";
    public const string InvalidJson = "invalid_json";
    public const string DuplicateJsonKey = "duplicate_json_key";
    public const string JsonFloatRejected = "json_float_rejected";
    public const string JsonNumberInvalid = "json_number_invalid";
    public const string JsonDepthLimit = "json_depth_limit";
    public const string UnknownField = "unknown_field";
    public const string MissingField = "missing_field";
    public const string InvalidFieldType = "invalid_field_type";
    public const string InvalidJob = "invalid_job";
    public const string JobMismatch = "job_mismatch";
    public const string InvalidOp = "invalid_op";
    public const string OpMismatch = "op_mismatch";
    public const string InvalidWirePath = "invalid_wire_path";
    public const string InvalidComponent = "invalid_component";
    public const string InvalidCursor = "invalid_cursor";
    public const string InvalidIdentity = "invalid_identity";
    public const string InvalidHash = "invalid_hash";
    public const string InvalidLength = "invalid_length";
    public const string InvalidObservation = "invalid_observation";
    public const string InvalidLimit = "invalid_limit";
    public const string ReplaceObservationRequired = "replace_observation_required";
    public const string InvalidMode = "invalid_mode";
    public const string InvalidReason = "invalid_reason";
    public const string InvalidCommit = "invalid_commit";
}
