//! Observation and file identity bytes. Shared with the C# local adapter.

use crate::encoding::b64_encode;
use crate::sha256::{digest, hex};

const MAGIC: &[u8] = b"HD028OBS1";

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum EntryKind {
    Missing = 0,
    File = 1,
    Directory = 2,
    Symlink = 3,
    Other = 4,
}

impl EntryKind {
    pub fn as_str(self) -> &'static str {
        match self {
            EntryKind::File => "file",
            EntryKind::Directory => "directory",
            EntryKind::Symlink => "symlink",
            EntryKind::Other => "other",
            EntryKind::Missing => "other",
        }
    }
}

pub fn observation(
    components: &[Vec<u8>],
    exists: bool,
    kind: EntryKind,
    identity: &[u8],
    size: u64,
    mtime: u64,
    mtime_precision: u64,
) -> String {
    let mut buf = Vec::new();
    buf.extend_from_slice(MAGIC);
    buf.push(0);
    for component in components {
        let len = u16::try_from(component.len()).unwrap_or(u16::MAX);
        buf.extend_from_slice(&len.to_le_bytes());
        buf.extend_from_slice(&component[..len as usize]);
    }
    buf.push(0);
    buf.push(if exists { 1 } else { 0 });
    buf.push(kind as u8);
    let id_len = u16::try_from(identity.len()).unwrap_or(u16::MAX);
    buf.extend_from_slice(&id_len.to_le_bytes());
    buf.extend_from_slice(&identity[..id_len as usize]);
    buf.extend_from_slice(&size.to_le_bytes());
    buf.extend_from_slice(&mtime.to_le_bytes());
    buf.extend_from_slice(&mtime_precision.to_le_bytes());
    hex(&digest(&buf))
}

pub fn windows_identity(volume: u32, index: u64) -> Vec<u8> {
    let mut raw = Vec::with_capacity(13);
    raw.push(b'W');
    raw.extend_from_slice(&volume.to_le_bytes());
    raw.extend_from_slice(&index.to_le_bytes());
    raw
}

pub fn unix_identity(dev: u64, ino: u64) -> Vec<u8> {
    let mut raw = Vec::with_capacity(17);
    raw.push(b'U');
    raw.extend_from_slice(&dev.to_le_bytes());
    raw.extend_from_slice(&ino.to_le_bytes());
    raw
}

pub fn portable_identity(path: &[u8], size: u64, mtime: u64) -> Vec<u8> {
    let mut raw = Vec::with_capacity(17 + path.len());
    raw.push(b'P');
    raw.extend_from_slice(&size.to_le_bytes());
    raw.extend_from_slice(&mtime.to_le_bytes());
    raw.extend_from_slice(path);
    if raw.len() > 4096 {
        raw.truncate(4096);
    }
    raw
}

pub fn identity_b64(raw: &[u8]) -> String {
    b64_encode(raw)
}

pub fn keepboth_candidate(name: &[u8], attempt: u32) -> Vec<u8> {
    if attempt == 0 {
        return name.to_vec();
    }
    let suffix = format!(" ({attempt})");
    let suffix_b = suffix.as_bytes();
    let mut dot = None;
    for i in (0..name.len()).rev() {
        if name[i] == b'.' {
            dot = Some(i);
            break;
        }
    }
    match dot {
        Some(d) if d > 0 => {
            let mut out = Vec::with_capacity(name.len() + suffix_b.len());
            out.extend_from_slice(&name[..d]);
            out.extend_from_slice(suffix_b);
            out.extend_from_slice(&name[d..]);
            out
        }
        _ => {
            let mut out = Vec::with_capacity(name.len() + suffix_b.len());
            out.extend_from_slice(name);
            out.extend_from_slice(suffix_b);
            out
        }
    }
}
