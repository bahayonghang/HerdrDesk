use herddesk_filebridge::encoding::b64_encode;
use herddesk_filebridge::error::helper;
use herddesk_filebridge::fs::LocalRoot;
use herddesk_filebridge::protocol::{encode_header, Kind, HEADER_LEN};
use herddesk_filebridge::sha256::digest_hex;
use std::fs;
use std::io::{Read, Write};
use std::path::PathBuf;
use std::process::{Command, Stdio};
use std::time::{SystemTime, UNIX_EPOCH};

fn tmp() -> PathBuf {
    let p = std::env::temp_dir().join(format!(
        "herddesk-hd028-conf-{}-{}",
        std::process::id(),
        SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_nanos()
    ));
    fs::create_dir_all(&p).unwrap();
    p
}

fn cleanup(p: &PathBuf) {
    let _ = fs::remove_dir_all(p);
}

fn read_frame(stdout: &mut impl Read) -> Vec<u8> {
    let mut header = [0u8; HEADER_LEN];
    stdout.read_exact(&mut header).unwrap();
    let len = u32::from_be_bytes(header[8..12].try_into().unwrap()) as usize;
    let mut payload = vec![0u8; len];
    if len > 0 {
        stdout.read_exact(&mut payload).unwrap();
    }
    let mut frame = header.to_vec();
    frame.extend_from_slice(&payload);
    frame
}

#[test]
fn fail_create_does_not_overwrite() {
    let root = tmp();
    fs::write(root.join("exists.txt"), b"keep-me").unwrap();
    let fs = LocalRoot::new(root.clone()).unwrap();
    let parent = fs.stat(&[], false).unwrap();
    let body = b"new";
    let req = format!(
        "{{\"protocol\":\"1.0\",\"job\":\"01234567-89ab-4def-8123-456789abcdef\",\"op\":\"write\",\"parent\":\"/\",\"name\":\"{}\",\"mode\":\"create\",\"expected_parent_observation\":\"{}\",\"length\":\"{}\",\"sha256\":\"{}\"}}",
        b64_encode(b"exists.txt"),
        parent.observation,
        body.len(),
        digest_hex(body)
    );
    let mut child = Command::new(env!("CARGO_BIN_EXE_herddesk-filebridge"))
        .args(["serve", "--stdio", "--protocol", "1.0"])
        .current_dir(&root)
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .unwrap();
    let mut stdin = child.stdin.take().unwrap();
    let mut stdout = child.stdout.take().unwrap();
    let req_bytes = req.as_bytes();
    let mut frame = encode_header(Kind::RequestJson, 0, req_bytes.len() as u32)
        .unwrap()
        .to_vec();
    frame.extend_from_slice(req_bytes);
    stdin.write_all(&frame).unwrap();
    stdin.flush().unwrap();
    let _accepted = read_frame(&mut stdout);
    let mut data = encode_header(Kind::Data, 1, body.len() as u32)
        .unwrap()
        .to_vec();
    data.extend_from_slice(body);
    stdin.write_all(&data).unwrap();
    stdin
        .write_all(&encode_header(Kind::EndData, 2, 0).unwrap())
        .unwrap();
    stdin.flush().unwrap();
    drop(stdin);
    let terminal = read_frame(&mut stdout);
    drop(stdout);
    let status = child.wait().unwrap();
    assert!(!status.success() || terminal[6] == Kind::ErrorJson as u8);
    assert_eq!(fs::read(root.join("exists.txt")).unwrap(), b"keep-me");
    cleanup(&root);
}

#[test]
fn replace_without_observation_rejected_by_codec() {
    let mut session = herddesk_filebridge::Session::new();
    let req = br#"{"protocol":"1.0","job":"01234567-89ab-4def-8123-456789abcdef","op":"write","parent":"/","name":"YQ==","mode":"replace","expected_parent_observation":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef","length":"0","sha256":"e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"}"#;
    let mut frame = encode_header(Kind::RequestJson, 0, req.len() as u32)
        .unwrap()
        .to_vec();
    frame.extend_from_slice(req);
    let err = session.push_client(&frame).unwrap_err();
    assert_eq!(err.code, "replace_observation_required");
}

#[test]
fn replace_mode_is_unsupported_on_helper() {
    let root = tmp();
    fs::write(root.join("t.txt"), b"old").unwrap();
    let fs = LocalRoot::new(root.clone()).unwrap();
    let parent = fs.stat(&[], false).unwrap();
    let target = fs.stat(&[b"t.txt".to_vec()], false).unwrap();
    let req = format!(
        "{{\"protocol\":\"1.0\",\"job\":\"01234567-89ab-4def-8123-456789abcdef\",\"op\":\"write\",\"parent\":\"/\",\"name\":\"{}\",\"mode\":\"replace\",\"expected_parent_observation\":\"{}\",\"expected_target_observation\":\"{}\",\"length\":\"0\",\"sha256\":\"{}\"}}",
        b64_encode(b"t.txt"),
        parent.observation,
        target.observation,
        digest_hex(b"")
    );
    let mut child = Command::new(env!("CARGO_BIN_EXE_herddesk-filebridge"))
        .args(["serve", "--stdio", "--protocol", "1.0"])
        .current_dir(&root)
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .unwrap();
    {
        let mut stdin = child.stdin.take().unwrap();
        let req_bytes = req.as_bytes();
        let mut frame = encode_header(Kind::RequestJson, 0, req_bytes.len() as u32)
            .unwrap()
            .to_vec();
        frame.extend_from_slice(req_bytes);
        stdin.write_all(&frame).unwrap();
        stdin.flush().unwrap();
    }
    let out = child.wait_with_output().unwrap();
    assert_ne!(out.status.code(), Some(0));
    assert_eq!(fs::read(root.join("t.txt")).unwrap(), b"old");
    let _ = helper::UNSUPPORTED;
    cleanup(&root);
}
