//! Stable protocol codes. Display is the code only.

use std::fmt;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Error {
    pub code: &'static str,
}

impl Error {
    pub const fn new(code: &'static str) -> Self {
        Self { code }
    }
}

impl fmt::Display for Error {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(self.code)
    }
}

impl std::error::Error for Error {}

pub fn fail(code: &'static str) -> Error {
    Error::new(code)
}

pub mod codes {
    pub const PROTOCOL_NOT_ACTIVE: &str = "protocol_not_active";
    pub const PROTOCOL_POLLUTION: &str = "protocol_pollution";
    pub const UNSUPPORTED_VERSION: &str = "unsupported_version";
    pub const UNKNOWN_KIND: &str = "unknown_kind";
    pub const UNKNOWN_FLAGS: &str = "unknown_flags";
    pub const PAYLOAD_TOO_LARGE: &str = "payload_too_large";
    pub const INVALID_PAYLOAD_LENGTH: &str = "invalid_payload_length";
    pub const TRUNCATED_HEADER: &str = "truncated_header";
    pub const TRUNCATED_PAYLOAD: &str = "truncated_payload";
    pub const SEQUENCE_GAP: &str = "sequence_gap";
    pub const SEQUENCE_REPLAY: &str = "sequence_replay";
    pub const SEQUENCE_WRAP: &str = "sequence_wrap";
    pub const WRONG_DIRECTION: &str = "wrong_direction";
    pub const UNEXPECTED_KIND: &str = "unexpected_kind";
    pub const SECOND_REQUEST: &str = "second_request";
    pub const DATA_BEFORE_ACCEPTED: &str = "data_before_accepted";
    pub const PROCESS_WOULD_EXIT: &str = "process_would_exit";
    pub const CONTROL_BUDGET_EXCEEDED: &str = "control_budget_exceeded";
    pub const EOF_WITHOUT_TERMINAL: &str = "eof_without_terminal";
    pub const TERMINAL_RESULT_MISMATCH: &str = "terminal_result_mismatch";
    pub const INVALID_JSON: &str = "invalid_json";
    pub const DUPLICATE_JSON_KEY: &str = "duplicate_json_key";
    pub const JSON_FLOAT_REJECTED: &str = "json_float_rejected";
    pub const JSON_NUMBER_INVALID: &str = "json_number_invalid";
    pub const JSON_DEPTH_LIMIT: &str = "json_depth_limit";
    pub const UNKNOWN_FIELD: &str = "unknown_field";
    pub const MISSING_FIELD: &str = "missing_field";
    pub const INVALID_FIELD_TYPE: &str = "invalid_field_type";
    pub const INVALID_JOB: &str = "invalid_job";
    pub const JOB_MISMATCH: &str = "job_mismatch";
    pub const INVALID_OP: &str = "invalid_op";
    pub const OP_MISMATCH: &str = "op_mismatch";
    pub const INVALID_WIRE_PATH: &str = "invalid_wire_path";
    pub const INVALID_COMPONENT: &str = "invalid_component";
    pub const INVALID_CURSOR: &str = "invalid_cursor";
    pub const INVALID_IDENTITY: &str = "invalid_identity";
    pub const INVALID_HASH: &str = "invalid_hash";
    pub const INVALID_LENGTH: &str = "invalid_length";
    pub const INVALID_OBSERVATION: &str = "invalid_observation";
    pub const INVALID_LIMIT: &str = "invalid_limit";
    pub const REPLACE_OBSERVATION_REQUIRED: &str = "replace_observation_required";
    pub const INVALID_MODE: &str = "invalid_mode";
    pub const INVALID_REASON: &str = "invalid_reason";
    pub const INVALID_COMMIT: &str = "invalid_commit";
}

pub mod helper {
    pub const CANCELLED: &str = "cancelled";
    pub const CONFLICT: &str = "conflict";
    pub const STALE_TARGET: &str = "stale_target";
    pub const NOT_FOUND: &str = "not_found";
    pub const PERMISSION_DENIED: &str = "permission_denied";
    pub const NAME_EXISTS: &str = "name_exists";
    pub const PARENT_MISSING: &str = "parent_missing";
    pub const IS_DIRECTORY: &str = "is_directory";
    pub const NOT_DIRECTORY: &str = "not_directory";
    pub const HASH_MISMATCH: &str = "hash_mismatch";
    pub const LENGTH_MISMATCH: &str = "length_mismatch";
    pub const UNSUPPORTED: &str = "unsupported";
    pub const OUTCOME_UNKNOWN: &str = "outcome_unknown";
    pub const MAPPING_REQUIRED: &str = "mapping_required";
}
