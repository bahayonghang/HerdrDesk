//! Independent golden-vector runner. Does not call the C# codec.

use herddesk_filebridge::json::{self, Json};
use herddesk_filebridge::protocol::{encode_header, Direction, Kind};
use herddesk_filebridge::{codes, Session, HEADER_LEN, MAX_JSON};
use std::fs;
use std::path::PathBuf;

fn vectors_dir() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("spec/test-vectors")
}

fn field<'a>(obj: &'a [(String, Json)], name: &str) -> Option<&'a Json> {
    obj.iter().find(|(k, _)| k == name).map(|(_, v)| v)
}

fn as_obj(v: &Json) -> &[(String, Json)] {
    match v {
        Json::Object(fields) => fields,
        _ => panic!("object_required"),
    }
}

fn as_str<'a>(obj: &'a [(String, Json)], name: &str) -> &'a str {
    match field(obj, name) {
        Some(Json::String(s)) => s,
        _ => panic!("missing {name}"),
    }
}

fn opt_str<'a>(obj: &'a [(String, Json)], name: &str) -> Option<&'a str> {
    match field(obj, name) {
        Some(Json::String(s)) => Some(s),
        _ => None,
    }
}

fn as_i64(obj: &[(String, Json)], name: &str) -> i64 {
    match field(obj, name) {
        Some(Json::Int(n)) => *n,
        _ => panic!("missing {name}"),
    }
}

fn opt_i64(obj: &[(String, Json)], name: &str) -> Option<i64> {
    match field(obj, name) {
        Some(Json::Int(n)) => Some(*n),
        Some(Json::Null) => None,
        None => None,
        _ => panic!("bad {name}"),
    }
}

fn as_bool(obj: &[(String, Json)], name: &str, default: bool) -> bool {
    match field(obj, name) {
        Some(Json::Bool(v)) => *v,
        None => default,
        _ => panic!("bad {name}"),
    }
}

struct VectorCase {
    id: String,
    file: String,
    expect: String,
    error: Option<String>,
    outcome: Option<String>,
    exit_code: Option<i32>,
    eof: bool,
    skip_exit: bool,
    commit_after: Option<usize>,
    exclusive_deleted: Option<bool>,
    replay_forbidden: bool,
    frames: Vec<FrameMeta>,
}

struct FrameMeta {
    dir: Direction,
    length: usize,
}

fn load_cases() -> Vec<VectorCase> {
    let text = fs::read(vectors_dir().join("manifest.json")).expect("manifest");
    let root = json::parse(&text).expect("manifest json");
    let obj = as_obj(&root);
    let list = match field(obj, "vectors") {
        Some(Json::Array(items)) => items,
        _ => panic!("vectors"),
    };
    list.iter()
        .map(|item| {
            let v = as_obj(item);
            let frames = match field(v, "frames") {
                Some(Json::Array(items)) => items
                    .iter()
                    .map(|f| {
                        let f = as_obj(f);
                        FrameMeta {
                            dir: match as_str(f, "dir") {
                                "client" => Direction::Client,
                                _ => Direction::Helper,
                            },
                            length: as_i64(f, "length") as usize,
                        }
                    })
                    .collect(),
                _ => panic!("frames"),
            };
            VectorCase {
                id: as_str(v, "id").to_string(),
                file: as_str(v, "file").to_string(),
                expect: as_str(v, "expect").to_string(),
                error: opt_str(v, "error").map(str::to_string),
                outcome: opt_str(v, "outcome").map(str::to_string),
                exit_code: opt_i64(v, "exit_code").map(|n| n as i32),
                eof: as_bool(v, "eof_after_frames", true),
                skip_exit: as_bool(v, "skip_exit", false),
                commit_after: opt_i64(v, "commit_linearized_after_frame").map(|n| n as usize),
                exclusive_deleted: match field(v, "exclusive_temp_deleted") {
                    Some(Json::Bool(b)) => Some(*b),
                    _ => None,
                },
                replay_forbidden: as_bool(v, "auto_replay_forbidden", false),
                frames,
            }
        })
        .collect()
}

fn run(case: &VectorCase, chunk: Option<usize>) -> Result<Session, String> {
    let bytes = fs::read(vectors_dir().join(&case.file)).expect("vector");
    let mut session = Session::new();
    let mut offset = 0usize;
    for (index, frame) in case.frames.iter().enumerate() {
        let end = offset + frame.length;
        if end > bytes.len() {
            return Err("truncated_file".into());
        }
        let slice = &bytes[offset..end];
        offset = end;
        let push_err = feed(&mut session, frame.dir, slice, chunk);
        if let Some(err) = push_err {
            if case.expect == "reject" {
                return finish_reject(session, case, err);
            }
            return Err(format!("{}: unexpected {}", case.id, err));
        }
        if case.commit_after == Some(index) {
            session.mark_commit_linearized();
        }
    }
    if case.eof {
        match session.on_eof() {
            Ok(_) => {}
            Err(err) => {
                if case.expect == "reject" {
                    return finish_reject(session, case, err.code.to_string());
                }
                return Err(format!("{}: eof {}", case.id, err.code));
            }
        }
    }
    if case.expect == "reject" {
        return Err(format!("{}: expected reject", case.id));
    }
    if !case.skip_exit {
        if let Some(code) = case.exit_code {
            match session.on_process_exit(code) {
                Ok(_) => {}
                Err(err) => {
                    if case.error.as_deref() == Some(err.code) {
                        return Ok(session);
                    }
                    return Err(format!("{}: exit {}", case.id, err.code));
                }
            }
        }
    }
    Ok(session)
}

fn feed(
    session: &mut Session,
    dir: Direction,
    slice: &[u8],
    chunk: Option<usize>,
) -> Option<String> {
    if let Some(size) = chunk {
        let mut i = 0;
        while i < slice.len() {
            let n = (i + size).min(slice.len());
            let r = match dir {
                Direction::Client => session.push_client(&slice[i..n]),
                Direction::Helper => session.push_helper(&slice[i..n]),
            };
            if let Err(err) = r {
                return Some(err.code.to_string());
            }
            i = n;
        }
        None
    } else {
        match dir {
            Direction::Client => session.push_client(slice),
            Direction::Helper => session.push_helper(slice),
        }
        .err()
        .map(|e| e.code.to_string())
    }
}

fn finish_reject(session: Session, case: &VectorCase, err: String) -> Result<Session, String> {
    if let Some(expected) = &case.error {
        if err != *expected {
            return Err(format!("{}: got {err} want {expected}", case.id));
        }
    }
    Ok(session)
}

fn assert_ok(case: &VectorCase, session: &Session) {
    if let Some(name) = &case.outcome {
        let got = format!("{:?}", session.outcome()).to_lowercase();
        assert!(got.contains(name), "{} outcome {got} want {name}", case.id);
    }
    if let Some(flag) = case.exclusive_deleted {
        assert_eq!(
            session.exclusive_temp_deleted(),
            flag,
            "{} temp deleted",
            case.id
        );
    }
    if case.replay_forbidden {
        assert!(session.auto_replay_forbidden(), "{} replay", case.id);
    }
}

#[test]
fn golden_vectors_whole_and_one_byte() {
    for case in load_cases() {
        let whole = run(&case, None).unwrap_or_else(|e| panic!("{e}"));
        if case.expect == "accept" {
            assert_ok(&case, &whole);
        }
        let split = run(&case, Some(1)).unwrap_or_else(|e| panic!("byte {e}"));
        if case.expect == "accept" {
            assert_ok(&case, &split);
        }
        assert_eq!(
            format!("{:?}", whole.outcome()),
            format!("{:?}", split.outcome()),
            "{} partial",
            case.id
        );
    }
}

#[test]
fn random_chunks_match_whole() {
    let mut seed = 0x9e37_79b9u32;
    for case in load_cases() {
        seed = seed.wrapping_mul(1664525).wrapping_add(1013904223);
        let size = (seed % 17 + 1) as usize;
        let whole = run(&case, None).unwrap_or_else(|e| panic!("{e}"));
        let chunked = run(&case, Some(size)).unwrap_or_else(|e| panic!("chunk {e}"));
        assert_eq!(
            format!("{:?}", whole.outcome()),
            format!("{:?}", chunked.outcome()),
            "{} chunk {size}",
            case.id
        );
    }
}

fn entry_payload(target: usize) -> Vec<u8> {
    let prefix = r#"{"job":"01234567-89ab-4def-8123-456789abcdef","name":"YQ==","display_name":""#;
    let suffix = r#"","type":"file","size":"0","mtime":"0","mtime_precision":"1","identity":"YQ==","symlink":false,"observation":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"}"#;
    let used = prefix.len() + suffix.len();
    assert!(target >= used);
    let mut body = Vec::with_capacity(target);
    body.extend_from_slice(prefix.as_bytes());
    body.extend(std::iter::repeat_n(b'x', target - used));
    body.extend_from_slice(suffix.as_bytes());
    assert_eq!(body.len(), target);
    body
}

fn list_prefix() -> (Session, u32) {
    let mut session = Session::new();
    let req = br#"{"protocol":"1.0","job":"01234567-89ab-4def-8123-456789abcdef","op":"list","path":"/","limit":1}"#;
    let mut frame = encode_header(Kind::RequestJson, 0, req.len() as u32)
        .unwrap()
        .to_vec();
    frame.extend_from_slice(req);
    session.push_client(&frame).unwrap();
    let acc = br#"{"job":"01234567-89ab-4def-8123-456789abcdef","op":"list","identity":"YQ==","size":"0"}"#;
    let mut frame = encode_header(Kind::AcceptedJson, 0, acc.len() as u32)
        .unwrap()
        .to_vec();
    frame.extend_from_slice(acc);
    session.push_helper(&frame).unwrap();
    (session, 1)
}

#[test]
fn json_one_mib_accepted_one_over_rejected_before_payload() {
    let payload = entry_payload(MAX_JSON as usize);
    let (mut session, seq) = list_prefix();
    let mut frame = encode_header(Kind::EntryJson, seq, payload.len() as u32)
        .unwrap()
        .to_vec();
    frame.extend_from_slice(&payload);
    session.push_helper(&frame).expect("1MiB json");

    let (mut session, seq) = list_prefix();
    let header = encode_header(Kind::EntryJson, seq, MAX_JSON + 1);
    assert_eq!(header.unwrap_err().code, codes::PAYLOAD_TOO_LARGE);
    let mut raw = [0u8; HEADER_LEN];
    raw[0..4].copy_from_slice(b"HDFB");
    raw[4] = 1;
    raw[5] = 0;
    raw[6] = Kind::EntryJson as u8;
    raw[8..12].copy_from_slice(&(MAX_JSON + 1).to_be_bytes());
    raw[12..16].copy_from_slice(&seq.to_be_bytes());
    let err = session.push_helper(&raw).unwrap_err();
    assert_eq!(err.code, codes::PAYLOAD_TOO_LARGE);
}

#[test]
fn data_one_mib_and_oversize() {
    let mut session = Session::new();
    let req = br#"{"protocol":"1.0","job":"01234567-89ab-4def-8123-456789abcdef","op":"read","path":"/YQ=="}"#;
    let mut frame = encode_header(Kind::RequestJson, 0, req.len() as u32)
        .unwrap()
        .to_vec();
    frame.extend_from_slice(req);
    session.push_client(&frame).unwrap();
    let acc = br#"{"job":"01234567-89ab-4def-8123-456789abcdef","op":"read","identity":"YQ==","size":"1048576"}"#;
    let mut frame = encode_header(Kind::AcceptedJson, 0, acc.len() as u32)
        .unwrap()
        .to_vec();
    frame.extend_from_slice(acc);
    session.push_helper(&frame).unwrap();
    let data = vec![0u8; MAX_JSON as usize];
    let mut frame = encode_header(Kind::Data, 1, data.len() as u32)
        .unwrap()
        .to_vec();
    frame.extend_from_slice(&data);
    session.push_helper(&frame).expect("1MiB data");

    let mut raw = [0u8; HEADER_LEN];
    raw[0..4].copy_from_slice(b"HDFB");
    raw[4] = 1;
    raw[5] = 0;
    raw[6] = Kind::Data as u8;
    raw[8..12].copy_from_slice(&(MAX_JSON + 1).to_be_bytes());
    raw[12..16].copy_from_slice(&2u32.to_be_bytes());
    let err = session.push_helper(&raw).unwrap_err();
    assert_eq!(err.code, codes::PAYLOAD_TOO_LARGE);
}
