//! One-job stdio server. stdout is protocol only.

use crate::codec::Session;
use crate::encoding::{bounded_b64, observation as parse_obs};
use crate::error::{codes, helper, Error};
use crate::fs::{random16, LocalRoot, WriteJob};
use crate::identity::{identity_b64, EntryKind};
use crate::json::{self, Json};
use crate::path::{self, decode_component};
use crate::protocol::{encode_header, Frame, Kind, Mode, Op, Outcome};
use crate::sha256::digest_hex;
use std::io::{self, Read, Write};
use std::path::PathBuf;

const STDERR_CAP: usize = 4096;

struct WriteSpec {
    parent: Vec<Vec<u8>>,
    name: Vec<u8>,
    sha256: String,
}

pub fn serve_stdio() -> i32 {
    let cwd = match std::env::current_dir() {
        Ok(p) => p,
        Err(_) => {
            let _ = writeln_stderr(b"filebridge_error parent_missing\n");
            return 1;
        }
    };
    match serve(cwd, io::stdin().lock(), io::stdout().lock()) {
        Ok(code) => code,
        Err(err) => {
            let _ = writeln_stderr(format!("filebridge_error {}\n", err.code).as_bytes());
            2
        }
    }
}

fn writeln_stderr(bytes: &[u8]) -> io::Result<()> {
    let mut err = io::stderr().lock();
    let n = bytes.len().min(STDERR_CAP);
    err.write_all(&bytes[..n])?;
    err.flush()
}

pub fn serve<R: Read, W: Write>(root: PathBuf, mut stdin: R, mut stdout: W) -> Result<i32, Error> {
    let fs = LocalRoot::new(root).map_err(|_| Error::new(helper::PARENT_MISSING))?;
    let mut session = Session::new();
    let mut buf = [0u8; 8192];
    let mut write_job: Option<WriteJob> = None;
    let mut write_spec: Option<WriteSpec> = None;
    let mut helper_seq: u32 = 0;
    loop {
        let n = stdin
            .read(&mut buf)
            .map_err(|_| Error::new(codes::TRUNCATED_PAYLOAD))?;
        if n == 0 {
            session.on_stdin_closed()?;
            if let Some(job) = write_job.take() {
                let ident = job.identity.clone();
                let temp = job.temp.clone();
                drop(job);
                let _ = fs.delete_if_identity(&temp, &ident);
            }
            if session.op().is_some()
                && session.outcome() == Outcome::Pending
                && !session.commit_linearized()
            {
                emit_error(
                    &mut session,
                    &mut stdout,
                    &mut helper_seq,
                    helper::CANCELLED,
                    "cancel",
                    false,
                )?;
                return Ok(1);
            }
            let outcome = session.on_eof()?;
            return Ok(match outcome {
                Outcome::Success => 0,
                Outcome::Cancelled | Outcome::Failed => 1,
                _ => 2,
            });
        }
        let frames = match session.push_client(&buf[..n]) {
            Ok(f) => f,
            Err(err) => {
                let _ = writeln_stderr(format!("filebridge_error {}\n", err.code).as_bytes());
                return Err(err);
            }
        };
        for frame in frames {
            match handle_frame(
                &mut session,
                &fs,
                frame,
                &mut write_job,
                &mut write_spec,
                &mut stdout,
                &mut helper_seq,
            ) {
                Ok(Some(code)) => return Ok(code),
                Ok(None) => {}
                Err(err) => {
                    if let Some(job) = write_job.take() {
                        let ident = job.identity.clone();
                        let temp = job.temp.clone();
                        drop(job);
                        let _ = fs.delete_if_identity(&temp, &ident);
                    }
                    if session.job().is_some() && !session.would_exit() {
                        let stage = if matches!(session.op(), Some(Op::Write)) {
                            "transfer"
                        } else {
                            "request"
                        };
                        let _ = emit_error(
                            &mut session,
                            &mut stdout,
                            &mut helper_seq,
                            err.code,
                            stage,
                            false,
                        );
                        return Ok(1);
                    }
                    return Err(err);
                }
            }
        }
    }
}

fn handle_frame<W: Write>(
    session: &mut Session,
    fs: &LocalRoot,
    frame: Frame,
    write_job: &mut Option<WriteJob>,
    write_spec: &mut Option<WriteSpec>,
    stdout: &mut W,
    helper_seq: &mut u32,
) -> Result<Option<i32>, Error> {
    match frame.kind {
        Kind::RequestJson => handle_request(
            session,
            fs,
            &frame.payload,
            write_job,
            write_spec,
            stdout,
            helper_seq,
        ),
        Kind::Data => {
            let job = write_job
                .as_mut()
                .ok_or(Error::new(codes::UNEXPECTED_KIND))?;
            job.write(&frame.payload).map_err(|e| Error::new(e.code))?;
            Ok(None)
        }
        Kind::EndData => {
            let job = write_job.take().ok_or(Error::new(codes::UNEXPECTED_KIND))?;
            let spec = write_spec
                .take()
                .ok_or(Error::new(codes::UNEXPECTED_KIND))?;
            finish_write(session, fs, job, spec, stdout, helper_seq)
        }
        Kind::CancelJson => {
            if let Some(job) = write_job.take() {
                let ident = job.identity.clone();
                let temp = job.temp.clone();
                drop(job);
                let _ = fs.delete_if_identity(&temp, &ident);
            }
            if !session.commit_linearized() {
                emit_error(
                    session,
                    stdout,
                    helper_seq,
                    helper::CANCELLED,
                    "cancel",
                    false,
                )?;
                return Ok(Some(1));
            }
            Ok(None)
        }
        _ => Err(Error::new(codes::UNEXPECTED_KIND)),
    }
}

fn handle_request<W: Write>(
    session: &mut Session,
    fs: &LocalRoot,
    payload: &[u8],
    write_job: &mut Option<WriteJob>,
    write_spec: &mut Option<WriteSpec>,
    stdout: &mut W,
    helper_seq: &mut u32,
) -> Result<Option<i32>, Error> {
    let root = json::parse(payload)?;
    let fields = json::require_object(&root)?;
    let op = session.op().ok_or(Error::new(codes::INVALID_OP))?;
    match op {
        Op::List => op_list(session, fs, fields, stdout, helper_seq),
        Op::Stat => op_stat(session, fs, fields, stdout, helper_seq),
        Op::Read => op_read(session, fs, fields, stdout, helper_seq),
        Op::Write => op_write(
            session, fs, fields, write_job, write_spec, stdout, helper_seq,
        ),
        Op::Rename => op_rename(session, fs, fields, stdout, helper_seq),
    }
}

fn op_list<W: Write>(
    session: &mut Session,
    fs: &LocalRoot,
    fields: &[(String, Json)],
    stdout: &mut W,
    helper_seq: &mut u32,
) -> Result<Option<i32>, Error> {
    let components = path::decode(json::req_string(fields, "path")?)?;
    let limit = json::req_int(fields, "limit")?;
    let cursor = match json::opt_string(fields, "cursor")? {
        Some(c) => Some(bounded_b64(c, codes::INVALID_CURSOR, 4096)?),
        None => None,
    };
    let listed = fs
        .list(&components, limit, cursor.as_deref())
        .map_err(|e| Error::new(e.code))?;
    let dir = listed.dir;
    let entries = listed.entries;
    let has_more = listed.has_more;
    emit_accepted(session, stdout, helper_seq, "list", &dir.identity, 0)?;
    let job = session.job().unwrap().to_string();
    for (raw, display, stat) in &entries {
        let entry = Json::object(vec![
            ("job", Json::String(job.clone())),
            ("name", Json::String(crate::encoding::b64_encode(raw))),
            ("display_name", Json::String(display.clone())),
            ("type", Json::String(stat.kind.as_str().to_string())),
            ("size", Json::String(stat.size.to_string())),
            ("mtime", Json::String(stat.mtime.to_string())),
            (
                "mtime_precision",
                Json::String(stat.mtime_precision.to_string()),
            ),
            ("identity", Json::String(identity_b64(&stat.identity))),
            ("symlink", Json::Bool(stat.symlink)),
            ("observation", Json::String(stat.observation.clone())),
        ]);
        emit(
            session,
            stdout,
            helper_seq,
            Kind::EntryJson,
            &entry.encode(),
        )?;
    }
    emit_complete(
        session,
        stdout,
        helper_seq,
        &path::encode(&components)?,
        entries.len() as u64,
        &digest_hex(b""),
        &dir.observation,
        "not_applicable",
        Some(has_more),
    )?;
    Ok(Some(0))
}

fn op_stat<W: Write>(
    session: &mut Session,
    fs: &LocalRoot,
    fields: &[(String, Json)],
    stdout: &mut W,
    helper_seq: &mut u32,
) -> Result<Option<i32>, Error> {
    let components = path::decode(json::req_string(fields, "path")?)?;
    if let Some(obs) = json::opt_string(fields, "observation")? {
        parse_obs(obs)?;
    }
    let stat = fs.stat(&components, true).map_err(|e| Error::new(e.code))?;
    if let Some(obs) = json::opt_string(fields, "observation")? {
        if obs != stat.observation {
            return Err(Error::new(helper::STALE_TARGET));
        }
    }
    emit_accepted(
        session,
        stdout,
        helper_seq,
        "stat",
        &stat.identity,
        stat.size,
    )?;
    emit_complete(
        session,
        stdout,
        helper_seq,
        &path::encode(&components)?,
        stat.size,
        &stat.sha256,
        &stat.observation,
        "not_applicable",
        None,
    )?;
    Ok(Some(0))
}

fn op_read<W: Write>(
    session: &mut Session,
    fs: &LocalRoot,
    fields: &[(String, Json)],
    stdout: &mut W,
    helper_seq: &mut u32,
) -> Result<Option<i32>, Error> {
    let components = path::decode(json::req_string(fields, "path")?)?;
    let (stat, mut file) = fs.open_read(&components).map_err(|e| Error::new(e.code))?;
    emit_accepted(
        session,
        stdout,
        helper_seq,
        "read",
        &stat.identity,
        stat.size,
    )?;
    let mut buf = vec![0u8; crate::protocol::MAX_DATA as usize];
    loop {
        let n = std::io::Read::read(&mut file, &mut buf)
            .map_err(|_| Error::new(helper::UNSUPPORTED))?;
        if n == 0 {
            break;
        }
        emit(session, stdout, helper_seq, Kind::Data, &buf[..n])?;
    }
    emit_complete(
        session,
        stdout,
        helper_seq,
        &path::encode(&components)?,
        stat.size,
        &stat.sha256,
        &stat.observation,
        "not_applicable",
        None,
    )?;
    Ok(Some(0))
}

fn op_write<W: Write>(
    session: &mut Session,
    fs: &LocalRoot,
    fields: &[(String, Json)],
    write_job: &mut Option<WriteJob>,
    write_spec: &mut Option<WriteSpec>,
    stdout: &mut W,
    helper_seq: &mut u32,
) -> Result<Option<i32>, Error> {
    let mode = Mode::parse(json::req_string(fields, "mode")?)?;
    if mode == Mode::Replace {
        return Err(Error::new(helper::UNSUPPORTED));
    }
    let parent = path::decode(json::req_string(fields, "parent")?)?;
    let name = decode_component(json::req_string(fields, "name")?)?;
    let parent_obs = json::req_string(fields, "expected_parent_observation")?;
    parse_obs(parent_obs)?;
    let sha256 = json::req_string(fields, "sha256")?.to_string();
    let parent_stat = fs.stat(&parent, false).map_err(|e| {
        if e.code == helper::NOT_FOUND {
            Error::new(helper::PARENT_MISSING)
        } else {
            Error::new(e.code)
        }
    })?;
    if parent_stat.kind != EntryKind::Directory {
        return Err(Error::new(helper::NOT_DIRECTORY));
    }
    if parent_stat.observation != parent_obs {
        return Err(Error::new(helper::STALE_TARGET));
    }
    let nonce = random16();
    let job = session.job().unwrap();
    let (temp, file, identity) = fs
        .create_temp(&parent, job, &nonce)
        .map_err(|e| Error::new(e.code))?;
    *write_spec = Some(WriteSpec {
        parent,
        name,
        sha256,
    });
    *write_job = Some(WriteJob {
        temp,
        file,
        identity: identity.clone(),
        sha: crate::sha256::Sha256::new(),
        bytes: 0,
    });
    emit_accepted(session, stdout, helper_seq, "write", &identity, 0)?;
    Ok(None)
}

fn finish_write<W: Write>(
    session: &mut Session,
    fs: &LocalRoot,
    job: WriteJob,
    spec: WriteSpec,
    stdout: &mut W,
    helper_seq: &mut u32,
) -> Result<Option<i32>, Error> {
    let ident = job.identity.clone();
    let (temp, _identity, digest, bytes, file) =
        job.finish_hash().map_err(|e| Error::new(e.code))?;
    drop(file);
    if bytes != session.declared_length() {
        let _ = fs.delete_if_identity(&temp, &ident);
        return Err(Error::new(helper::LENGTH_MISMATCH));
    }
    if digest != spec.sha256 {
        let _ = fs.delete_if_identity(&temp, &ident);
        return Err(Error::new(helper::HASH_MISMATCH));
    }
    let (final_name, _dest) = match fs.commit_create(&temp, &spec.parent, &spec.name, false) {
        Ok(v) => v,
        Err(err) => {
            let _ = fs.delete_if_identity(&temp, &ident);
            return Err(Error::new(err.code));
        }
    };
    session.mark_commit_linearized();
    let mut final_components = spec.parent;
    final_components.push(final_name);
    let stat = fs
        .stat(&final_components, true)
        .map_err(|e| Error::new(e.code))?;
    emit_complete(
        session,
        stdout,
        helper_seq,
        &path::encode(&final_components)?,
        bytes,
        &digest,
        &stat.observation,
        "committed",
        None,
    )?;
    Ok(Some(0))
}

fn op_rename<W: Write>(
    session: &mut Session,
    fs: &LocalRoot,
    fields: &[(String, Json)],
    stdout: &mut W,
    helper_seq: &mut u32,
) -> Result<Option<i32>, Error> {
    let mode = Mode::parse(json::req_string(fields, "mode")?)?;
    if mode == Mode::Replace {
        return Err(Error::new(helper::UNSUPPORTED));
    }
    let source = path::decode(json::req_string(fields, "source")?)?;
    let source_obs = json::req_string(fields, "source_observation")?;
    parse_obs(source_obs)?;
    let parent = path::decode(json::req_string(fields, "parent")?)?;
    let name = decode_component(json::req_string(fields, "name")?)?;
    let parent_obs = json::req_string(fields, "expected_parent_observation")?;
    parse_obs(parent_obs)?;
    let src_stat = fs.stat(&source, true).map_err(|e| Error::new(e.code))?;
    if src_stat.observation != source_obs {
        return Err(Error::new(helper::STALE_TARGET));
    }
    let parent_stat = fs.stat(&parent, false).map_err(|e| Error::new(e.code))?;
    if parent_stat.observation != parent_obs {
        return Err(Error::new(helper::STALE_TARGET));
    }
    emit_accepted(
        session,
        stdout,
        helper_seq,
        "rename",
        &src_stat.identity,
        src_stat.size,
    )?;
    session.mark_commit_linearized();
    fs.rename_create(&source, &parent, &name)
        .map_err(|e| Error::new(e.code))?;
    let mut dest = parent;
    dest.push(name);
    let stat = fs.stat(&dest, true).map_err(|e| Error::new(e.code))?;
    emit_complete(
        session,
        stdout,
        helper_seq,
        &path::encode(&dest)?,
        stat.size,
        &stat.sha256,
        &stat.observation,
        "committed",
        None,
    )?;
    Ok(Some(0))
}

fn emit_accepted<W: Write>(
    session: &mut Session,
    stdout: &mut W,
    helper_seq: &mut u32,
    op: &str,
    identity: &[u8],
    size: u64,
) -> Result<(), Error> {
    let job = session.job().unwrap().to_string();
    let body = Json::object(vec![
        ("job", Json::String(job)),
        ("op", Json::String(op.to_string())),
        ("identity", Json::String(identity_b64(identity))),
        ("size", Json::String(size.to_string())),
    ]);
    emit(
        session,
        stdout,
        helper_seq,
        Kind::AcceptedJson,
        &body.encode(),
    )
}

#[allow(clippy::too_many_arguments)]
fn emit_complete<W: Write>(
    session: &mut Session,
    stdout: &mut W,
    helper_seq: &mut u32,
    path: &str,
    length: u64,
    sha256: &str,
    observation: &str,
    commit: &str,
    has_more: Option<bool>,
) -> Result<(), Error> {
    let job = session.job().unwrap().to_string();
    let mut fields = vec![
        ("job", Json::String(job)),
        ("path", Json::String(path.to_string())),
        ("length", Json::String(length.to_string())),
        ("sha256", Json::String(sha256.to_string())),
        ("observation", Json::String(observation.to_string())),
        ("commit", Json::String(commit.to_string())),
    ];
    if let Some(more) = has_more {
        fields.push(("has_more", Json::Bool(more)));
    }
    emit(
        session,
        stdout,
        helper_seq,
        Kind::CompleteJson,
        &Json::object(fields).encode(),
    )
}

fn emit_error<W: Write>(
    session: &mut Session,
    stdout: &mut W,
    helper_seq: &mut u32,
    code: &str,
    stage: &str,
    retryable: bool,
) -> Result<(), Error> {
    let job = session
        .job()
        .unwrap_or("00000000-0000-4000-8000-000000000000")
        .to_string();
    let body = Json::object(vec![
        ("job", Json::String(job)),
        ("code", Json::String(code.to_string())),
        ("stage", Json::String(stage.to_string())),
        ("retryable", Json::Bool(retryable)),
    ]);
    emit(session, stdout, helper_seq, Kind::ErrorJson, &body.encode())
}

fn emit<W: Write>(
    session: &mut Session,
    stdout: &mut W,
    helper_seq: &mut u32,
    kind: Kind,
    payload: &[u8],
) -> Result<(), Error> {
    let header = encode_header(kind, *helper_seq, payload.len() as u32)?;
    *helper_seq = helper_seq.saturating_add(1);
    let mut frame = header.to_vec();
    frame.extend_from_slice(payload);
    session.push_helper(&frame)?;
    stdout
        .write_all(&frame)
        .map_err(|_| Error::new(codes::PROTOCOL_NOT_ACTIVE))?;
    stdout
        .flush()
        .map_err(|_| Error::new(codes::PROTOCOL_NOT_ACTIVE))?;
    Ok(())
}
