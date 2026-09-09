//! Incremental duplex codec. No filesystem and no child process.

use crate::encoding::{bounded_b64, canonical_uuid, decimal_u64, hex64, observation};
use crate::error::{codes, fail, Error};
use crate::json::{self, Json};
use crate::path::{self, decode_component};
use crate::protocol::{
    kind_direction, parse_header, Direction, Frame, Header, Kind, Mode, Op, Outcome, HEADER_LEN,
    MAX_CONTROL_TOTAL, MAX_CURSOR, MAX_IDENTITY,
};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Phase {
    Start,
    Requested,
    Open,
    WriteEnding,
    Terminal,
}

struct DirBuf {
    header: [u8; HEADER_LEN],
    header_got: usize,
    payload: Vec<u8>,
    payload_need: u32,
    payload_got: usize,
    reading_payload: bool,
    next_seq: u32,
    seq_exhausted: bool,
    pending: Option<Header>,
}

impl DirBuf {
    fn new() -> Self {
        Self {
            header: [0; HEADER_LEN],
            header_got: 0,
            payload: Vec::new(),
            payload_need: 0,
            payload_got: 0,
            reading_payload: false,
            next_seq: 0,
            seq_exhausted: false,
            pending: None,
        }
    }

    fn reset_header(&mut self) {
        self.header_got = 0;
        self.reading_payload = false;
        self.payload.clear();
        self.payload_need = 0;
        self.payload_got = 0;
        self.pending = None;
    }
}

pub struct Session {
    client: DirBuf,
    helper: DirBuf,
    failed: bool,
    phase: Phase,
    job: Option<String>,
    op: Option<Op>,
    declared_length: u64,
    accepted_size: u64,
    data_bytes: u64,
    accepted: bool,
    commit_linearized: bool,
    exclusive_temp: bool,
    exclusive_temp_deleted: bool,
    cancel_requested: bool,
    terminal_complete: bool,
    terminal_error: Option<String>,
    outcome: Outcome,
    would_exit: bool,
    control_bytes: u64,
    auto_replay_forbidden: bool,
}

impl Default for Session {
    fn default() -> Self {
        Self::new()
    }
}

impl Session {
    pub fn new() -> Self {
        Self {
            client: DirBuf::new(),
            helper: DirBuf::new(),
            failed: false,
            phase: Phase::Start,
            job: None,
            op: None,
            declared_length: 0,
            accepted_size: 0,
            data_bytes: 0,
            accepted: false,
            commit_linearized: false,
            exclusive_temp: false,
            exclusive_temp_deleted: false,
            cancel_requested: false,
            terminal_complete: false,
            terminal_error: None,
            outcome: Outcome::Pending,
            would_exit: false,
            control_bytes: 0,
            auto_replay_forbidden: false,
        }
    }

    pub fn outcome(&self) -> Outcome {
        self.outcome
    }

    pub fn exclusive_temp_active(&self) -> bool {
        self.exclusive_temp
    }

    pub fn exclusive_temp_deleted(&self) -> bool {
        self.exclusive_temp_deleted
    }

    pub fn would_exit(&self) -> bool {
        self.would_exit
    }

    pub fn auto_replay_forbidden(&self) -> bool {
        self.auto_replay_forbidden
    }

    pub fn job(&self) -> Option<&str> {
        self.job.as_deref()
    }

    pub fn op(&self) -> Option<Op> {
        self.op
    }

    pub fn declared_length(&self) -> u64 {
        self.declared_length
    }

    pub fn cancel_requested(&self) -> bool {
        self.cancel_requested
    }

    pub fn commit_linearized(&self) -> bool {
        self.commit_linearized
    }

    pub fn mark_commit_linearized(&mut self) {
        self.commit_linearized = true;
    }

    pub fn push_client(&mut self, bytes: &[u8]) -> Result<Vec<Frame>, Error> {
        self.push(Direction::Client, bytes)
    }

    pub fn push_helper(&mut self, bytes: &[u8]) -> Result<Vec<Frame>, Error> {
        self.push(Direction::Helper, bytes)
    }

    pub fn on_stdin_closed(&mut self) -> Result<(), Error> {
        self.guard()?;
        if self.would_exit {
            return Ok(());
        }
        self.cancel_requested = true;
        self.drop_temp_if_uncommitted();
        Ok(())
    }

    pub fn on_eof(&mut self) -> Result<Outcome, Error> {
        self.guard()?;
        if self.client.header_got > 0 || self.client.reading_payload {
            return self.die(if self.client.reading_payload {
                codes::TRUNCATED_PAYLOAD
            } else {
                codes::TRUNCATED_HEADER
            });
        }
        if self.helper.header_got > 0 || self.helper.reading_payload {
            return self.die(if self.helper.reading_payload {
                codes::TRUNCATED_PAYLOAD
            } else {
                codes::TRUNCATED_HEADER
            });
        }
        if self.phase != Phase::Terminal {
            self.outcome = Outcome::Unknown;
            if matches!(self.op, Some(Op::Write) | Some(Op::Rename)) {
                self.auto_replay_forbidden = true;
            }
            // Disconnect without a terminal result is final. A later Complete
            // must not overwrite Unknown or clear auto_replay_forbidden.
            self.failed = true;
        }
        Ok(self.outcome)
    }

    pub fn on_process_exit(&mut self, code: i32) -> Result<Outcome, Error> {
        self.guard()?;
        if self.terminal_complete && code == 0 {
            self.outcome = Outcome::Success;
            return Ok(self.outcome);
        }
        if self.terminal_error.is_some() && code != 0 {
            if self.terminal_error.as_deref() == Some("cancelled") {
                self.outcome = Outcome::Cancelled;
            } else {
                self.outcome = Outcome::Failed;
            }
            return Ok(self.outcome);
        }
        self.outcome = Outcome::Unknown;
        if matches!(self.op, Some(Op::Write) | Some(Op::Rename)) {
            self.auto_replay_forbidden = true;
        }
        self.die(codes::TERMINAL_RESULT_MISMATCH)
    }

    fn push(&mut self, dir: Direction, mut bytes: &[u8]) -> Result<Vec<Frame>, Error> {
        self.guard()?;
        let mut out = Vec::new();
        while !bytes.is_empty() {
            match self.take_frame(dir, &mut bytes) {
                Ok(Some(frame)) => out.push(frame),
                Ok(None) => break,
                Err(err) => {
                    self.failed = true;
                    return Err(err);
                }
            }
        }
        Ok(out)
    }

    fn take_frame(&mut self, dir: Direction, bytes: &mut &[u8]) -> Result<Option<Frame>, Error> {
        if self.would_exit {
            return Err(fail(codes::PROCESS_WOULD_EXIT));
        }
        let ready = {
            let buf = match dir {
                Direction::Client => &mut self.client,
                Direction::Helper => &mut self.helper,
            };
            if !buf.reading_payload {
                let need = HEADER_LEN - buf.header_got;
                let n = need.min(bytes.len());
                buf.header[buf.header_got..buf.header_got + n].copy_from_slice(&bytes[..n]);
                buf.header_got += n;
                *bytes = &bytes[n..];
                if buf.header_got < HEADER_LEN {
                    return Ok(None);
                }
                let header = parse_header(&buf.header)?;
                if buf.seq_exhausted {
                    return Err(fail(codes::SEQUENCE_WRAP));
                }
                if header.seq < buf.next_seq {
                    return Err(fail(codes::SEQUENCE_REPLAY));
                }
                if header.seq > buf.next_seq {
                    return Err(fail(codes::SEQUENCE_GAP));
                }
                if header.payload_len == 0 {
                    let seq = header.seq;
                    let kind = header.kind;
                    advance_seq(buf);
                    buf.reset_header();
                    Some((kind, seq, Vec::new()))
                } else {
                    buf.payload = vec![0u8; header.payload_len as usize];
                    buf.payload_need = header.payload_len;
                    buf.payload_got = 0;
                    buf.reading_payload = true;
                    buf.pending = Some(header);
                    None
                }
            } else {
                None
            }
        };
        if let Some((kind, seq, payload)) = ready {
            self.dispatch(dir, kind, seq, payload.clone())?;
            return Ok(Some(Frame {
                direction: dir,
                kind,
                seq,
                payload,
            }));
        }
        let complete = {
            let buf = match dir {
                Direction::Client => &mut self.client,
                Direction::Helper => &mut self.helper,
            };
            if !buf.reading_payload {
                return Ok(None);
            }
            let need = (buf.payload_need as usize) - buf.payload_got;
            let n = need.min(bytes.len());
            buf.payload[buf.payload_got..buf.payload_got + n].copy_from_slice(&bytes[..n]);
            buf.payload_got += n;
            *bytes = &bytes[n..];
            if buf.payload_got < buf.payload_need as usize {
                return Ok(None);
            }
            let header = buf.pending.take().ok_or(fail(codes::PROTOCOL_NOT_ACTIVE))?;
            let payload = std::mem::take(&mut buf.payload);
            advance_seq(buf);
            buf.reset_header();
            Some((header.kind, header.seq, payload))
        };
        let (kind, seq, payload) = complete.ok_or(fail(codes::PROTOCOL_NOT_ACTIVE))?;
        self.dispatch(dir, kind, seq, payload.clone())?;
        Ok(Some(Frame {
            direction: dir,
            kind,
            seq,
            payload,
        }))
    }

    fn dispatch(
        &mut self,
        dir: Direction,
        kind: Kind,
        _seq: u32,
        payload: Vec<u8>,
    ) -> Result<(), Error> {
        if kind == Kind::RequestJson {
            if dir != Direction::Client {
                return Err(fail(codes::WRONG_DIRECTION));
            }
            return self.on_request(&payload);
        }
        if self.job.is_none() {
            if kind == Kind::Data {
                return Err(fail(codes::UNEXPECTED_KIND));
            }
            match kind_direction(kind, None) {
                Ok(natural) if natural != dir => return Err(fail(codes::WRONG_DIRECTION)),
                _ => return Err(fail(codes::UNEXPECTED_KIND)),
            }
        }
        let expected = kind_direction(kind, self.op)?;
        if expected != dir {
            return Err(fail(codes::WRONG_DIRECTION));
        }
        match kind {
            Kind::CancelJson => self.on_cancel(&payload),
            Kind::AcceptedJson => self.on_accepted(&payload),
            Kind::EntryJson => self.on_entry(&payload),
            Kind::ProgressJson => self.on_progress(&payload),
            Kind::CompleteJson => self.on_complete(&payload),
            Kind::ErrorJson => self.on_error(&payload),
            Kind::Data => self.on_data(&payload),
            Kind::EndData => self.on_end_data(),
            Kind::RequestJson => Err(fail(codes::SECOND_REQUEST)),
        }
    }

    fn on_request(&mut self, payload: &[u8]) -> Result<(), Error> {
        if self.job.is_some() {
            return Err(fail(codes::SECOND_REQUEST));
        }
        if self.phase != Phase::Start {
            return Err(fail(codes::SECOND_REQUEST));
        }
        self.add_control(payload.len())?;
        let root = json::parse(payload)?;
        let fields = json::require_object(&root)?;
        let protocol = json::req_string(fields, "protocol")?;
        if protocol != "1.0" {
            return Err(fail(codes::UNSUPPORTED_VERSION));
        }
        let job = json::req_string(fields, "job")?;
        canonical_uuid(job)?;
        let op = Op::parse(json::req_string(fields, "op")?)?;
        match op {
            Op::List => self.parse_list(fields)?,
            Op::Stat | Op::Read => self.parse_stat_read(fields)?,
            Op::Write => self.parse_write(fields)?,
            Op::Rename => self.parse_rename(fields)?,
        }
        self.job = Some(job.to_string());
        self.op = Some(op);
        self.phase = Phase::Requested;
        if matches!(op, Op::Write | Op::Rename) {
            self.exclusive_temp = true;
        }
        Ok(())
    }

    fn parse_list(&mut self, fields: &[(String, Json)]) -> Result<(), Error> {
        json::reject_unknown(
            fields,
            &["protocol", "job", "op", "path", "limit", "cursor"],
        )?;
        path::decode(json::req_string(fields, "path")?)?;
        let limit = json::req_int(fields, "limit")?;
        if !(1..=1000).contains(&limit) {
            return Err(fail(codes::INVALID_LIMIT));
        }
        if let Some(cursor) = json::opt_string(fields, "cursor")? {
            bounded_b64(cursor, codes::INVALID_CURSOR, MAX_CURSOR)?;
        }
        Ok(())
    }

    fn parse_stat_read(&self, fields: &[(String, Json)]) -> Result<(), Error> {
        json::reject_unknown(fields, &["protocol", "job", "op", "path", "observation"])?;
        path::decode(json::req_string(fields, "path")?)?;
        if let Some(obs) = json::opt_string(fields, "observation")? {
            observation(obs)?;
        }
        Ok(())
    }

    fn parse_write(&mut self, fields: &[(String, Json)]) -> Result<(), Error> {
        let mode = Mode::parse(json::req_string(fields, "mode")?)?;
        let mut allowed: Vec<&str> = vec![
            "protocol",
            "job",
            "op",
            "parent",
            "name",
            "mode",
            "expected_parent_observation",
            "length",
            "sha256",
        ];
        if mode == Mode::Replace {
            if !json::has(fields, "expected_target_observation") {
                return Err(fail(codes::REPLACE_OBSERVATION_REQUIRED));
            }
            allowed.push("expected_target_observation");
        }
        json::reject_unknown(fields, &allowed)?;
        path::decode(json::req_string(fields, "parent")?)?;
        decode_component(json::req_string(fields, "name")?)?;
        observation(json::req_string(fields, "expected_parent_observation")?)?;
        if mode == Mode::Replace {
            observation(json::req_string(fields, "expected_target_observation")?)?;
        }
        self.declared_length = decimal_u64(json::req_string(fields, "length")?)?;
        hex64(json::req_string(fields, "sha256")?)?;
        Ok(())
    }

    fn parse_rename(&mut self, fields: &[(String, Json)]) -> Result<(), Error> {
        let mode = Mode::parse(json::req_string(fields, "mode")?)?;
        let mut allowed: Vec<&str> = vec![
            "protocol",
            "job",
            "op",
            "source",
            "source_observation",
            "parent",
            "name",
            "mode",
            "expected_parent_observation",
        ];
        if mode == Mode::Replace {
            if !json::has(fields, "expected_target_observation") {
                return Err(fail(codes::REPLACE_OBSERVATION_REQUIRED));
            }
            allowed.push("expected_target_observation");
        }
        json::reject_unknown(fields, &allowed)?;
        path::decode(json::req_string(fields, "source")?)?;
        observation(json::req_string(fields, "source_observation")?)?;
        path::decode(json::req_string(fields, "parent")?)?;
        decode_component(json::req_string(fields, "name")?)?;
        observation(json::req_string(fields, "expected_parent_observation")?)?;
        if mode == Mode::Replace {
            observation(json::req_string(fields, "expected_target_observation")?)?;
        }
        Ok(())
    }

    fn on_cancel(&mut self, payload: &[u8]) -> Result<(), Error> {
        if self.phase == Phase::Start {
            return Err(fail(codes::UNEXPECTED_KIND));
        }
        self.add_control(payload.len())?;
        let root = json::parse(payload)?;
        let fields = json::require_object(&root)?;
        json::reject_unknown(fields, &["job", "reason"])?;
        self.same_job(json::req_string(fields, "job")?)?;
        if json::req_string(fields, "reason")? != "user" {
            return Err(fail(codes::INVALID_REASON));
        }
        self.cancel_requested = true;
        self.drop_temp_if_uncommitted();
        Ok(())
    }

    fn on_accepted(&mut self, payload: &[u8]) -> Result<(), Error> {
        if self.phase != Phase::Requested {
            return Err(fail(codes::UNEXPECTED_KIND));
        }
        self.add_control(payload.len())?;
        let root = json::parse(payload)?;
        let fields = json::require_object(&root)?;
        json::reject_unknown(fields, &["job", "op", "identity", "size"])?;
        self.same_job(json::req_string(fields, "job")?)?;
        let op = Op::parse(json::req_string(fields, "op")?)?;
        if Some(op) != self.op {
            return Err(fail(codes::OP_MISMATCH));
        }
        bounded_b64(
            json::req_string(fields, "identity")?,
            codes::INVALID_IDENTITY,
            MAX_IDENTITY,
        )?;
        self.accepted_size = decimal_u64(json::req_string(fields, "size")?)?;
        self.accepted = true;
        self.phase = Phase::Open;
        Ok(())
    }

    fn on_entry(&mut self, payload: &[u8]) -> Result<(), Error> {
        if self.op != Some(Op::List) || self.phase != Phase::Open {
            return Err(fail(codes::UNEXPECTED_KIND));
        }
        self.add_control(payload.len())?;
        let root = json::parse(payload)?;
        let fields = json::require_object(&root)?;
        json::reject_unknown(
            fields,
            &[
                "job",
                "name",
                "display_name",
                "type",
                "size",
                "mtime",
                "mtime_precision",
                "identity",
                "symlink",
                "observation",
            ],
        )?;
        self.same_job(json::req_string(fields, "job")?)?;
        decode_component(json::req_string(fields, "name")?)?;
        let display = json::req_string(fields, "display_name")?;
        if display.is_empty() || display.contains('\0') {
            return Err(fail(codes::INVALID_FIELD_TYPE));
        }
        let typ = json::req_string(fields, "type")?;
        if !matches!(typ, "file" | "directory" | "symlink" | "other") {
            return Err(fail(codes::INVALID_FIELD_TYPE));
        }
        decimal_u64(json::req_string(fields, "size")?)?;
        decimal_u64(json::req_string(fields, "mtime")?)?;
        decimal_u64(json::req_string(fields, "mtime_precision")?)?;
        bounded_b64(
            json::req_string(fields, "identity")?,
            codes::INVALID_IDENTITY,
            MAX_IDENTITY,
        )?;
        let symlink = json::req_bool(fields, "symlink")?;
        if (typ == "symlink") != symlink {
            return Err(fail(codes::INVALID_FIELD_TYPE));
        }
        observation(json::req_string(fields, "observation")?)?;
        Ok(())
    }

    fn on_progress(&mut self, payload: &[u8]) -> Result<(), Error> {
        if self.phase != Phase::Open && self.phase != Phase::WriteEnding {
            return Err(fail(codes::UNEXPECTED_KIND));
        }
        self.add_control(payload.len())?;
        let root = json::parse(payload)?;
        let fields = json::require_object(&root)?;
        json::reject_unknown(fields, &["job", "bytes"])?;
        self.same_job(json::req_string(fields, "job")?)?;
        decimal_u64(json::req_string(fields, "bytes")?)?;
        Ok(())
    }

    fn on_complete(&mut self, payload: &[u8]) -> Result<(), Error> {
        if self.cancel_requested && !self.commit_linearized {
            return Err(fail(codes::UNEXPECTED_KIND));
        }
        match self.op {
            Some(Op::Write) if self.phase != Phase::WriteEnding => {
                return Err(fail(codes::UNEXPECTED_KIND));
            }
            Some(Op::List | Op::Stat | Op::Read | Op::Rename) if self.phase != Phase::Open => {
                return Err(fail(codes::UNEXPECTED_KIND));
            }
            None => return Err(fail(codes::UNEXPECTED_KIND)),
            _ => {}
        }
        self.add_control(payload.len())?;
        let root = json::parse(payload)?;
        let fields = json::require_object(&root)?;
        let mut allowed = vec!["job", "path", "length", "sha256", "observation", "commit"];
        if self.op == Some(Op::List) {
            allowed.push("has_more");
        }
        json::reject_unknown(fields, &allowed)?;
        self.same_job(json::req_string(fields, "job")?)?;
        path::decode(json::req_string(fields, "path")?)?;
        let length = decimal_u64(json::req_string(fields, "length")?)?;
        hex64(json::req_string(fields, "sha256")?)?;
        observation(json::req_string(fields, "observation")?)?;
        let commit = json::req_string(fields, "commit")?;
        match self.op {
            Some(Op::Write | Op::Rename) => {
                if commit != "committed" {
                    return Err(fail(codes::INVALID_COMMIT));
                }
            }
            _ => {
                if commit != "not_applicable" {
                    return Err(fail(codes::INVALID_COMMIT));
                }
            }
        }
        if self.op == Some(Op::List) {
            let _ = json::req_bool(fields, "has_more")?;
        }
        if self.op == Some(Op::Write) && length != self.declared_length {
            return Err(fail(codes::INVALID_LENGTH));
        }
        if self.op == Some(Op::Read) && (length != self.data_bytes || length != self.accepted_size)
        {
            return Err(fail(codes::INVALID_LENGTH));
        }
        self.exclusive_temp = false;
        self.finish_terminal(true, None)
    }

    fn on_error(&mut self, payload: &[u8]) -> Result<(), Error> {
        if self.phase == Phase::Start || self.phase == Phase::Terminal {
            return Err(fail(codes::UNEXPECTED_KIND));
        }
        self.add_control(payload.len())?;
        let root = json::parse(payload)?;
        let fields = json::require_object(&root)?;
        json::reject_unknown(fields, &["job", "code", "stage", "retryable"])?;
        self.same_job(json::req_string(fields, "job")?)?;
        let code = json::req_string(fields, "code")?;
        if !error_code_syntax(code) {
            return Err(fail(codes::INVALID_FIELD_TYPE));
        }
        let stage = json::req_string(fields, "stage")?;
        if !matches!(
            stage,
            "request" | "transfer" | "commit" | "cancel" | "unknown"
        ) {
            return Err(fail(codes::INVALID_FIELD_TYPE));
        }
        let _ = json::req_bool(fields, "retryable")?;
        if self.commit_linearized && code == "cancelled" {
            return Err(fail(codes::UNEXPECTED_KIND));
        }
        if self.cancel_requested && !self.commit_linearized && code != "cancelled" {
            return Err(fail(codes::UNEXPECTED_KIND));
        }
        if self.cancel_requested && !self.commit_linearized {
            self.drop_temp_if_uncommitted();
        }
        self.finish_terminal(false, Some(code.to_string()))
    }

    fn on_data(&mut self, payload: &[u8]) -> Result<(), Error> {
        if !self.accepted {
            return Err(fail(codes::DATA_BEFORE_ACCEPTED));
        }
        match self.op {
            Some(Op::Write) if self.phase == Phase::Open => {
                self.data_bytes = self
                    .data_bytes
                    .checked_add(payload.len() as u64)
                    .ok_or(fail(codes::INVALID_LENGTH))?;
                if self.data_bytes > self.declared_length {
                    return Err(fail(codes::INVALID_LENGTH));
                }
                Ok(())
            }
            Some(Op::Read) if self.phase == Phase::Open => {
                self.data_bytes = self
                    .data_bytes
                    .checked_add(payload.len() as u64)
                    .ok_or(fail(codes::INVALID_LENGTH))?;
                if self.data_bytes > self.accepted_size {
                    return Err(fail(codes::INVALID_LENGTH));
                }
                Ok(())
            }
            _ => Err(fail(codes::UNEXPECTED_KIND)),
        }
    }

    fn on_end_data(&mut self) -> Result<(), Error> {
        if !self.accepted {
            return Err(fail(codes::DATA_BEFORE_ACCEPTED));
        }
        if self.op != Some(Op::Write) || self.phase != Phase::Open {
            return Err(fail(codes::UNEXPECTED_KIND));
        }
        if self.data_bytes != self.declared_length {
            return Err(fail(codes::INVALID_LENGTH));
        }
        self.phase = Phase::WriteEnding;
        Ok(())
    }

    fn finish_terminal(&mut self, complete: bool, error: Option<String>) -> Result<(), Error> {
        self.phase = Phase::Terminal;
        self.would_exit = true;
        if complete {
            self.terminal_complete = true;
            self.outcome = Outcome::Pending;
        } else {
            let cancelled = error.as_deref() == Some("cancelled");
            self.terminal_error = error;
            self.outcome = if cancelled {
                Outcome::Cancelled
            } else {
                Outcome::Failed
            };
            if cancelled {
                self.drop_temp_if_uncommitted();
            }
            if matches!(self.op, Some(Op::Write) | Some(Op::Rename)) {
                self.auto_replay_forbidden = true;
            }
        }
        Ok(())
    }

    fn drop_temp_if_uncommitted(&mut self) {
        if !self.commit_linearized && self.exclusive_temp {
            self.exclusive_temp_deleted = true;
            self.exclusive_temp = false;
        }
    }

    fn same_job(&self, job: &str) -> Result<(), Error> {
        canonical_uuid(job)?;
        match &self.job {
            Some(expected) if expected == job => Ok(()),
            _ => Err(fail(codes::JOB_MISMATCH)),
        }
    }

    fn add_control(&mut self, n: usize) -> Result<(), Error> {
        let next = self.control_bytes.saturating_add(n as u64);
        if next > MAX_CONTROL_TOTAL {
            return Err(fail(codes::CONTROL_BUDGET_EXCEEDED));
        }
        self.control_bytes = next;
        Ok(())
    }

    fn guard(&self) -> Result<(), Error> {
        if self.failed {
            Err(fail(codes::PROTOCOL_NOT_ACTIVE))
        } else {
            Ok(())
        }
    }

    fn die<T>(&mut self, code: &'static str) -> Result<T, Error> {
        self.failed = true;
        Err(fail(code))
    }

    #[cfg(test)]
    pub fn debug_force_next_seq(&mut self, dir: Direction, seq: u32) {
        let buf = match dir {
            Direction::Client => &mut self.client,
            Direction::Helper => &mut self.helper,
        };
        buf.next_seq = seq;
        buf.seq_exhausted = false;
    }
}

fn advance_seq(buf: &mut DirBuf) {
    if buf.next_seq == u32::MAX {
        buf.seq_exhausted = true;
    } else {
        buf.next_seq += 1;
    }
}

fn error_code_syntax(s: &str) -> bool {
    let b = s.as_bytes();
    if b.is_empty() || b.len() > 64 {
        return false;
    }
    b[0].is_ascii_lowercase()
        && b.iter()
            .all(|ch| ch.is_ascii_lowercase() || ch.is_ascii_digit() || *ch == b'_')
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::protocol::{encode_header, MAGIC};

    #[test]
    fn wrap_after_max_seq_is_rejected() {
        let mut session = Session::new();
        session.debug_force_next_seq(Direction::Client, u32::MAX);
        let payload = br#"{"protocol":"1.0","job":"01234567-89ab-4def-8123-456789abcdef","op":"list","path":"/","limit":1}"#;
        let mut frame = encode_header(Kind::RequestJson, u32::MAX, payload.len() as u32)
            .unwrap()
            .to_vec();
        frame.extend_from_slice(payload);
        session.push_client(&frame).unwrap();
        let mut second = MAGIC.to_vec();
        second.extend_from_slice(&[1, 0, Kind::CancelJson as u8, 0]);
        second.extend_from_slice(&0u32.to_be_bytes());
        second.extend_from_slice(&0u32.to_be_bytes());
        let err = session.push_client(&second).unwrap_err();
        assert_eq!(err.code, codes::SEQUENCE_WRAP);
        assert_eq!(
            session.push_client(&second).unwrap_err().code,
            codes::PROTOCOL_NOT_ACTIVE
        );
    }

    fn push_json(session: &mut Session, dir: Direction, kind: Kind, seq: u32, payload: &[u8]) {
        let mut frame = encode_header(kind, seq, payload.len() as u32)
            .unwrap()
            .to_vec();
        frame.extend_from_slice(payload);
        match dir {
            Direction::Client => session.push_client(&frame).unwrap(),
            Direction::Helper => session.push_helper(&frame).unwrap(),
        };
    }

    #[test]
    fn eof_without_terminal_latches_and_forbids_replay() {
        let mut session = Session::new();
        let req = br#"{"protocol":"1.0","job":"01234567-89ab-4def-8123-456789abcdef","op":"write","parent":"/","name":"YQ==","mode":"replace","expected_parent_observation":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef","expected_target_observation":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef","length":"0","sha256":"e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"}"#;
        push_json(&mut session, Direction::Client, Kind::RequestJson, 0, req);
        let acc = br#"{"job":"01234567-89ab-4def-8123-456789abcdef","op":"write","identity":"YQ==","size":"0"}"#;
        push_json(&mut session, Direction::Helper, Kind::AcceptedJson, 0, acc);
        let end = encode_header(Kind::EndData, 1, 0).unwrap();
        session.push_client(&end).unwrap();
        session.mark_commit_linearized();
        assert_eq!(session.on_eof().unwrap(), Outcome::Unknown);
        assert!(session.auto_replay_forbidden());
        let complete = br#"{"job":"01234567-89ab-4def-8123-456789abcdef","path":"/YQ==","length":"0","sha256":"e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855","observation":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef","commit":"committed"}"#;
        let mut frame = encode_header(Kind::CompleteJson, 1, complete.len() as u32)
            .unwrap()
            .to_vec();
        frame.extend_from_slice(complete);
        assert_eq!(
            session.push_helper(&frame).unwrap_err().code,
            codes::PROTOCOL_NOT_ACTIVE
        );
        assert_eq!(session.outcome(), Outcome::Unknown);
        assert!(session.auto_replay_forbidden());
    }

    #[test]
    fn display_name_is_not_a_wire_path() {
        let mut session = Session::new();
        let req = br#"{"protocol":"1.0","job":"01234567-89ab-4def-8123-456789abcdef","op":"list","path":"/","limit":1}"#;
        push_json(&mut session, Direction::Client, Kind::RequestJson, 0, req);
        let acc = br#"{"job":"01234567-89ab-4def-8123-456789abcdef","op":"list","identity":"YQ==","size":"0"}"#;
        push_json(&mut session, Direction::Helper, Kind::AcceptedJson, 0, acc);
        let entry = br#"{"job":"01234567-89ab-4def-8123-456789abcdef","name":"YQ==","display_name":"../etc/passwd","type":"file","size":"0","mtime":"0","mtime_precision":"1","identity":"YQ==","symlink":false,"observation":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"}"#;
        push_json(&mut session, Direction::Helper, Kind::EntryJson, 1, entry);
        let mut later = Session::new();
        let bad = br#"{"protocol":"1.0","job":"01234567-89ab-4def-8123-456789abcdef","op":"list","path":"../etc/passwd","limit":1}"#;
        let mut frame = encode_header(Kind::RequestJson, 0, bad.len() as u32)
            .unwrap()
            .to_vec();
        frame.extend_from_slice(bad);
        assert_eq!(
            later.push_client(&frame).unwrap_err().code,
            codes::INVALID_WIRE_PATH
        );
    }

    #[test]
    fn list_limit_bounds() {
        let mut low = Session::new();
        let zero = br#"{"protocol":"1.0","job":"01234567-89ab-4def-8123-456789abcdef","op":"list","path":"/","limit":0}"#;
        let mut frame = encode_header(Kind::RequestJson, 0, zero.len() as u32)
            .unwrap()
            .to_vec();
        frame.extend_from_slice(zero);
        assert_eq!(
            low.push_client(&frame).unwrap_err().code,
            codes::INVALID_LIMIT
        );

        let mut high = Session::new();
        let over = br#"{"protocol":"1.0","job":"01234567-89ab-4def-8123-456789abcdef","op":"list","path":"/","limit":1001}"#;
        let mut frame = encode_header(Kind::RequestJson, 0, over.len() as u32)
            .unwrap()
            .to_vec();
        frame.extend_from_slice(over);
        assert_eq!(
            high.push_client(&frame).unwrap_err().code,
            codes::INVALID_LIMIT
        );
    }
}
