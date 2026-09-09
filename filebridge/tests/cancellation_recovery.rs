use herddesk_filebridge::encoding::b64_encode;
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
        "herddesk-hd028-can-{}-{}",
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
fn cancel_deletes_only_this_job_temp() {
    let root = tmp();
    fs::write(root.join("source.txt"), b"src").unwrap();
    fs::write(root.join("sibling.txt"), b"sib").unwrap();
    let fs = LocalRoot::new(root.clone()).unwrap();
    let parent = fs.stat(&[], false).unwrap();
    let body = b"payload-bytes";
    let req = format!(
        "{{\"protocol\":\"1.0\",\"job\":\"01234567-89ab-4def-8123-456789abcdef\",\"op\":\"write\",\"parent\":\"/\",\"name\":\"{}\",\"mode\":\"create\",\"expected_parent_observation\":\"{}\",\"length\":\"{}\",\"sha256\":\"{}\"}}",
        b64_encode(b"dest.txt"),
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
    let cancel = br#"{"job":"01234567-89ab-4def-8123-456789abcdef","reason":"user"}"#;
    let mut cframe = encode_header(Kind::CancelJson, 2, cancel.len() as u32)
        .unwrap()
        .to_vec();
    cframe.extend_from_slice(cancel);
    stdin.write_all(&cframe).unwrap();
    stdin.flush().unwrap();
    drop(stdin);
    let _ = read_frame(&mut stdout);
    drop(stdout);
    let status = child.wait().unwrap();
    assert!(!status.success());
    assert_eq!(fs::read(root.join("source.txt")).unwrap(), b"src");
    assert_eq!(fs::read(root.join("sibling.txt")).unwrap(), b"sib");
    assert!(!root.join("dest.txt").exists());
    let leftover: Vec<_> = fs::read_dir(&root)
        .unwrap()
        .filter_map(|e| e.ok())
        .map(|e| e.file_name())
        .filter(|n| n.to_string_lossy().starts_with(".herddesk-upload-"))
        .collect();
    assert!(leftover.is_empty());
    cleanup(&root);
}

#[test]
fn eof_without_complete_leaves_source() {
    let root = tmp();
    fs::write(root.join("source.txt"), b"src").unwrap();
    let fs = LocalRoot::new(root.clone()).unwrap();
    let parent = fs.stat(&[], false).unwrap();
    let req = format!(
        "{{\"protocol\":\"1.0\",\"job\":\"01234567-89ab-4def-8123-456789abcdef\",\"op\":\"write\",\"parent\":\"/\",\"name\":\"{}\",\"mode\":\"create\",\"expected_parent_observation\":\"{}\",\"length\":\"1\",\"sha256\":\"{}\"}}",
        b64_encode(b"dest.txt"),
        parent.observation,
        digest_hex(b"x")
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
        drop(stdin);
    }
    let out = child.wait_with_output().unwrap();
    assert_ne!(out.status.code(), Some(0));
    assert_eq!(fs::read(root.join("source.txt")).unwrap(), b"src");
    assert!(!root.join("dest.txt").exists());
    cleanup(&root);
}
