//! Unix WirePath: `/` plus shortest canonical Base64 components.

use crate::encoding::{b64_decode_canonical, b64_encode};
use crate::error::{codes, fail, Error};

pub fn encode(components: &[Vec<u8>]) -> Result<String, Error> {
    for raw in components {
        check_component(raw)?;
    }
    if components.is_empty() {
        return Ok("/".to_string());
    }
    let mut out = String::from("/");
    for (i, raw) in components.iter().enumerate() {
        if i > 0 {
            out.push('/');
        }
        out.push_str(&b64_encode(raw));
    }
    Ok(out)
}

pub fn decode(path: &str) -> Result<Vec<Vec<u8>>, Error> {
    if path.is_empty() || !path.starts_with('/') || path.contains('\0') {
        return Err(fail(codes::INVALID_WIRE_PATH));
    }
    if path == "/" {
        return Ok(Vec::new());
    }
    let rest = &path[1..];
    let bytes = rest.as_bytes();
    let mut components = Vec::new();
    let mut i = 0usize;
    while i < bytes.len() {
        let mut found = None;
        for j in (i + 1)..=bytes.len() {
            if j != bytes.len() && bytes[j] != b'/' {
                continue;
            }
            if let Ok(raw) = std::str::from_utf8(&bytes[i..j]) {
                if let Ok(decoded) = b64_decode_canonical(raw) {
                    found = Some((j, decoded));
                    break;
                }
            }
        }
        let (j, decoded) = found.ok_or(fail(codes::INVALID_WIRE_PATH))?;
        check_component(&decoded)?;
        components.push(decoded);
        if j == bytes.len() {
            break;
        }
        i = j + 1;
        if i == bytes.len() {
            return Err(fail(codes::INVALID_WIRE_PATH));
        }
    }
    Ok(components)
}

pub fn decode_component(s: &str) -> Result<Vec<u8>, Error> {
    let raw = b64_decode_canonical(s).map_err(|_| fail(codes::INVALID_COMPONENT))?;
    check_component(&raw)?;
    Ok(raw)
}

pub fn check_component(raw: &[u8]) -> Result<(), Error> {
    if raw.is_empty() || raw == b"." || raw == b".." || raw.contains(&0) || raw.contains(&b'/') {
        return Err(fail(codes::INVALID_COMPONENT));
    }
    Ok(())
}
