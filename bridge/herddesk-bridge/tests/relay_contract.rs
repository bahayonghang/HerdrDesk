//! Fake local-socket peers. Bytes stay opaque; stdout must equal the peer body.

use std::io::{Read, Write};
use std::path::PathBuf;
use std::process::{Command, Stdio};
use std::sync::{Arc, Mutex};
use std::thread;
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use herddesk_bridge::endpoint;
use herddesk_bridge::relay::{self, PUMP_BUFFER_BYTES};
use interprocess::local_socket::prelude::*;
use interprocess::local_socket::Stream;

fn unique_path(label: &str) -> PathBuf {
    let nanos = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_nanos())
        .unwrap_or(0);
    std::env::temp_dir().join(format!("hd8-{}-{}-{label}", std::process::id(), nanos))
}

fn spawn_peer<F>(listener: interprocess::local_socket::Listener, body: F) -> thread::JoinHandle<()>
where
    F: FnOnce(Stream) + Send + 'static,
{
    thread::spawn(move || {
        let conn = listener.accept().expect("accept");
        body(conn);
    })
}

fn echo_until(mut stream: Stream, expected: usize) {
    if expected == 0 {
        return;
    }
    let mut got = 0;
    let mut buf = [0_u8; 1024];
    while got < expected {
        match stream.read(&mut buf) {
            Ok(0) => break,
            Ok(n) => {
                if stream.write_all(&buf[..n]).is_err() {
                    break;
                }
                got += n;
            }
            Err(_) => break,
        }
    }
}

fn fragmenting_until(mut stream: Stream, expected: usize) {
    let mut got = 0;
    let mut buf = [0_u8; 64];
    while got < expected {
        match stream.read(&mut buf) {
            Ok(0) => break,
            Ok(n) => {
                for byte in &buf[..n] {
                    if stream.write_all(&[*byte]).is_err() {
                        return;
                    }
                }
                got += n;
            }
            Err(_) => break,
        }
    }
}

struct CollectingWriter(Arc<Mutex<Vec<u8>>>);

impl Write for CollectingWriter {
    fn write(&mut self, buf: &[u8]) -> std::io::Result<usize> {
        self.0.lock().expect("stdout lock").extend_from_slice(buf);
        Ok(buf.len())
    }

    fn flush(&mut self) -> std::io::Result<()> {
        Ok(())
    }
}

struct SlowWriter {
    inner: CollectingWriter,
    delay: Duration,
}

impl Write for SlowWriter {
    fn write(&mut self, buf: &[u8]) -> std::io::Result<usize> {
        let n = buf.len().min(32);
        thread::sleep(self.delay);
        self.inner.write(&buf[..n])
    }

    fn flush(&mut self) -> std::io::Result<()> {
        self.inner.flush()
    }
}

fn relay_once(
    path: &std::path::Path,
    input: &[u8],
    peer: impl FnOnce(Stream) + Send + 'static,
) -> Vec<u8> {
    let listener = endpoint::bind_local_listener(path).expect("bind");
    let peer = spawn_peer(listener, peer);
    let stream = endpoint::connect(path).expect("connect");
    let collected = Arc::new(Mutex::new(Vec::new()));
    let stdout = CollectingWriter(Arc::clone(&collected));
    relay::run(stream, std::io::Cursor::new(input.to_vec()), stdout).expect("relay");
    peer.join().expect("peer");
    let out = collected.lock().expect("stdout").clone();
    out
}

#[test]
fn echo_random_bytes_match() {
    let path = unique_path("echo");
    let mut input = vec![0_u8; PUMP_BUFFER_BYTES + 97];
    for (i, b) in input.iter_mut().enumerate() {
        *b = (i.wrapping_mul(31) % 251) as u8;
    }
    let expected = input.len();
    let out = relay_once(&path, &input, move |s| echo_until(s, expected));
    assert_eq!(out, input);
}

#[test]
fn fragmented_peer_bytes_match() {
    let path = unique_path("frag");
    let input: Vec<u8> = (0..4000).map(|i| (i % 251) as u8).collect();
    let expected = input.len();
    let out = relay_once(&path, &input, move |s| fragmenting_until(s, expected));
    assert_eq!(out, input);
}

#[test]
fn empty_payload_stays_empty() {
    let path = unique_path("empty");
    let out = relay_once(&path, b"", move |s| echo_until(s, 0));
    assert!(out.is_empty());
}

#[test]
fn binary_payload_is_not_utf8() {
    let path = unique_path("bin");
    let input = [0_u8, 255, 10, 13, 0, 0x80, 0x7f];
    let expected = input.len();
    let out = relay_once(&path, &input, move |s| echo_until(s, expected));
    assert_eq!(out, input);
}

#[test]
fn slow_reader_still_matches_and_uses_fixed_buffer() {
    let path = unique_path("slow");
    let listener = endpoint::bind_local_listener(&path).expect("bind");
    let input: Vec<u8> = (0..8192).map(|i| (i % 199) as u8).collect();
    let expected = input.len();
    let peer = spawn_peer(listener, move |s| echo_until(s, expected));
    let stream = endpoint::connect(&path).expect("connect");
    let collected = Arc::new(Mutex::new(Vec::new()));
    let stdout = SlowWriter {
        inner: CollectingWriter(Arc::clone(&collected)),
        delay: Duration::from_millis(1),
    };
    assert_eq!(PUMP_BUFFER_BYTES, 64 * 1024);
    relay::run(stream, std::io::Cursor::new(input.clone()), stdout).expect("relay");
    peer.join().expect("peer");
    assert_eq!(*collected.lock().expect("stdout"), input);
}

#[cfg(unix)]
#[test]
fn unix_stdin_eof_still_delivers_peer_tail() {
    let path = unique_path("tail");
    let tail = b"peer-tail-after-eof".to_vec();
    let tail_clone = tail.clone();
    let listener = endpoint::bind_local_listener(&path).expect("bind");
    let peer = spawn_peer(listener, move |mut stream| {
        let mut buf = [0_u8; 32];
        loop {
            match stream.read(&mut buf) {
                Ok(0) => break,
                Ok(_) => {}
                Err(_) => return,
            }
        }
        let _ = stream.write_all(&tail_clone);
    });
    let stream = endpoint::connect(&path).expect("connect");
    let collected = Arc::new(Mutex::new(Vec::new()));
    let stdout = CollectingWriter(Arc::clone(&collected));
    relay::run(stream, std::io::Cursor::new(b"upload".to_vec()), stdout).expect("relay");
    peer.join().expect("peer");
    assert_eq!(*collected.lock().expect("stdout"), tail);
}

#[cfg(windows)]
#[test]
fn windows_does_not_claim_half_close() {
    let path = unique_path("no-half-close");
    let out = relay_once(&path, b"abc", move |s| echo_until(s, 3));
    assert_eq!(out, b"abc");
    let src = include_str!("../src/relay.rs");
    assert!(!src.contains("half_close_succeeded"));
}

#[test]
fn unicode_path_echo() {
    let nanos = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_nanos())
        .unwrap_or(0);
    let path = std::env::temp_dir().join(format!("hd8-{}-{}-牧台", std::process::id(), nanos));
    let out = relay_once(&path, b"unicode-path", move |s| echo_until(s, 12));
    assert_eq!(out, b"unicode-path");
}

#[test]
fn binary_rejects_client_socket_and_smb_before_connect() {
    assert!(
        endpoint::validate_socket_path(std::path::Path::new("/tmp/herdr-client.sock")).is_err()
    );
    assert!(
        endpoint::validate_socket_path(std::path::Path::new(r"\\fileserver\pipe\herdr")).is_err()
    );
}

#[test]
fn version_command_writes_only_version_line() {
    let exe = env!("CARGO_BIN_EXE_herddesk-bridge");
    let output = Command::new(exe)
        .arg("--version")
        .output()
        .expect("spawn version");
    assert!(output.status.success());
    let stdout = String::from_utf8(output.stdout).expect("utf8");
    assert!(stdout.starts_with("herddesk-bridge "));
    assert!(!stdout.contains("welcome"));
    assert!(!stdout.contains("handshake"));
    assert!(output.stderr.is_empty());
}

#[test]
fn spawned_bridge_echoes_peer_bytes_and_keeps_stderr_clean() {
    let path = unique_path("spawn-echo");
    let listener = endpoint::bind_local_listener(&path).expect("bind");
    let input: Vec<u8> = (0..2048).map(|i| (i % 211) as u8).collect();
    let expected = input.len();
    let peer = spawn_peer(listener, move |s| echo_until(s, expected));
    let exe = env!("CARGO_BIN_EXE_herddesk-bridge");
    let mut child = Command::new(exe)
        .arg("rpc")
        .arg("--socket-path")
        .arg(&path)
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .expect("spawn bridge");
    {
        let mut stdin = child.stdin.take().expect("stdin");
        stdin.write_all(&input).expect("write");
    }
    let output = child.wait_with_output().expect("wait");
    peer.join().expect("peer");
    assert!(output.status.success());
    assert_eq!(output.stdout, input);
    assert!(output.stderr.is_empty());
}

#[test]
fn invalid_socket_path_writes_stable_code_on_stderr() {
    let exe = env!("CARGO_BIN_EXE_herddesk-bridge");
    let output = Command::new(exe)
        .arg("rpc")
        .arg("--socket-path")
        .arg("/tmp/herdr-client.sock")
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .output()
        .expect("spawn");
    assert!(!output.status.success());
    assert!(output.stdout.is_empty());
    let stderr = String::from_utf8_lossy(&output.stderr);
    assert_eq!(stderr.trim(), "bridge_endpoint_invalid");
    assert!(!stderr.contains("herdr-client"));
}
