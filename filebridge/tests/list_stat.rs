use herddesk_filebridge::encoding::b64_encode;
#[cfg(unix)]
use herddesk_filebridge::error::helper;
use herddesk_filebridge::fs::LocalRoot;
use herddesk_filebridge::identity::EntryKind;
use herddesk_filebridge::protocol::{encode_header, Kind};
use herddesk_filebridge::Session;
use std::fs;
use std::io::Write;
use std::path::PathBuf;
use std::process::{Command, Stdio};
use std::time::{SystemTime, UNIX_EPOCH};

fn tmp() -> PathBuf {
    let p = std::env::temp_dir().join(format!(
        "herddesk-hd028-list-{}-{}",
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

#[test]
fn empty_dir_lists_zero_entries() {
    let root = tmp();
    let fs = LocalRoot::new(root.clone()).unwrap();
    let listed = fs.list(&[], 10, None).unwrap();
    assert_eq!(listed.dir.kind, EntryKind::Directory);
    assert!(listed.entries.is_empty());
    assert!(!listed.has_more);
    cleanup(&root);
}

#[test]
fn space_and_unicode_round_trip_raw_name() {
    let root = tmp();
    fs::write(root.join("hello world.txt"), b"x").unwrap();
    fs::write(root.join("你好.txt"), b"y").unwrap();
    let fs = LocalRoot::new(root.clone()).unwrap();
    let listed = fs.list(&[], 10, None).unwrap();
    let names: Vec<String> = listed.entries.iter().map(|(_, d, _)| d.clone()).collect();
    assert!(names.contains(&"hello world.txt".to_string()));
    assert!(names.contains(&"你好.txt".to_string()));
    for (raw, display, _) in &listed.entries {
        assert_ne!(display, "/");
        assert!(!display.contains('\0'));
        let child = vec![raw.clone()];
        let stat = fs.stat(&child, true).unwrap();
        assert_eq!(stat.kind, EntryKind::File);
    }
    cleanup(&root);
}

#[test]
fn list_does_not_parse_ls_text() {
    let root = tmp();
    fs::write(root.join("a.txt"), b"1").unwrap();
    let fs = LocalRoot::new(root.clone()).unwrap();
    let listed = fs.list(&[], 10, None).unwrap();
    assert_eq!(listed.entries.len(), 1);
    assert_eq!(listed.entries[0].0, b"a.txt");
    cleanup(&root);
}

#[cfg(unix)]
#[test]
fn intermediate_symlink_is_not_followed() {
    let root = fs::canonicalize(tmp()).unwrap();
    let outside = fs::canonicalize(tmp()).unwrap();
    fs::create_dir(outside.join("nested")).unwrap();
    std::os::unix::fs::symlink(&outside, root.join("link")).unwrap();
    let fs = LocalRoot::new(root.clone()).unwrap();
    let err = fs
        .list(&[b"link".to_vec(), b"nested".to_vec()], 10, None)
        .unwrap_err();
    assert_eq!(err.code, helper::UNSUPPORTED);
    let listed = fs.list(&[b"link".to_vec()], 10, None);
    assert!(listed.is_err());
    assert_eq!(listed.unwrap_err().code, helper::UNSUPPORTED);
    cleanup(&root);
    cleanup(&outside);
}

#[cfg(unix)]
#[test]
fn permission_denied_when_injectable() {
    use std::os::unix::fs::PermissionsExt;
    let root = tmp();
    let inner = root.join("locked");
    fs::create_dir(&inner).unwrap();
    fs::write(inner.join("x"), b"1").unwrap();
    fs::set_permissions(&inner, fs::Permissions::from_mode(0o000)).unwrap();
    let fs = LocalRoot::new(root.clone()).unwrap();
    let err = fs.list(&[b"locked".to_vec()], 10, None).unwrap_err();
    fs::set_permissions(&inner, fs::Permissions::from_mode(0o700)).unwrap();
    assert_eq!(err.code, helper::PERMISSION_DENIED);
    cleanup(&root);
}

#[test]
fn serve_list_empty_via_stdio() {
    let root = tmp();
    let mut child = Command::new(env!("CARGO_BIN_EXE_herddesk-filebridge"))
        .args(["serve", "--stdio", "--protocol", "1.0"])
        .current_dir(&root)
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .unwrap();
    let payload = br#"{"protocol":"1.0","job":"01234567-89ab-4def-8123-456789abcdef","op":"list","path":"/","limit":10}"#;
    let mut frame = encode_header(Kind::RequestJson, 0, payload.len() as u32)
        .unwrap()
        .to_vec();
    frame.extend_from_slice(payload);
    {
        let mut stdin = child.stdin.take().unwrap();
        stdin.write_all(&frame).unwrap();
        stdin.flush().unwrap();
    }
    let out = child.wait_with_output().unwrap();
    assert_eq!(out.status.code(), Some(0));
    assert!(out.stdout.starts_with(b"HDFB"));
    let mut session = Session::new();
    session.push_client(&frame).unwrap();
    session.push_helper(&out.stdout).unwrap();
    assert_eq!(
        session
            .on_process_exit(out.status.code().unwrap_or(1))
            .unwrap(),
        herddesk_filebridge::Outcome::Success
    );
    let _ = b64_encode(b"a");
    cleanup(&root);
}
