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
        "herddesk-hd028-xfer-{}-{}",
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

fn job() -> &'static str {
    "01234567-89ab-4def-8123-456789abcdef"
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

fn write_via_serve(root: &PathBuf, name: &[u8], body: &[u8]) {
    let fs = LocalRoot::new(root.clone()).unwrap();
    let parent = fs.stat(&[], false).unwrap();
    let req = format!(
        "{{\"protocol\":\"1.0\",\"job\":\"{job}\",\"op\":\"write\",\"parent\":\"/\",\"name\":\"{name}\",\"mode\":\"create\",\"expected_parent_observation\":\"{obs}\",\"length\":\"{len}\",\"sha256\":\"{sha}\"}}",
        job = job(),
        name = b64_encode(name),
        obs = parent.observation,
        len = body.len(),
        sha = digest_hex(body)
    );
    let mut child = Command::new(env!("CARGO_BIN_EXE_herddesk-filebridge"))
        .args(["serve", "--stdio", "--protocol", "1.0"])
        .current_dir(root)
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
    if !body.is_empty() {
        let mut data = encode_header(Kind::Data, 1, body.len() as u32)
            .unwrap()
            .to_vec();
        data.extend_from_slice(body);
        stdin.write_all(&data).unwrap();
        stdin
            .write_all(&encode_header(Kind::EndData, 2, 0).unwrap())
            .unwrap();
    } else {
        stdin
            .write_all(&encode_header(Kind::EndData, 1, 0).unwrap())
            .unwrap();
    }
    stdin.flush().unwrap();
    drop(stdin);
    let _complete = read_frame(&mut stdout);
    drop(stdout);
    let status = child.wait().unwrap();
    assert!(status.success(), "{status:?}");
}

#[test]
fn empty_and_small_payload_hash_match() {
    let root = tmp();
    write_via_serve(&root, b"empty.txt", b"");
    write_via_serve(&root, b"hello.txt", b"hello");
    assert_eq!(fs::read(root.join("empty.txt")).unwrap(), b"");
    assert_eq!(fs::read(root.join("hello.txt")).unwrap(), b"hello");
    assert_eq!(
        digest_hex(&fs::read(root.join("hello.txt")).unwrap()),
        digest_hex(b"hello")
    );
    cleanup(&root);
}
