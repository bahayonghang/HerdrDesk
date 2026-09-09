//! Canonical UUID, decimal, hex, and padded Base64.

use crate::error::{codes, fail, Error};

const B64: &[u8; 64] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

pub fn canonical_uuid(s: &str) -> Result<(), Error> {
    if s.len() != 36 {
        return Err(fail(codes::INVALID_JOB));
    }
    let b = s.as_bytes();
    for (i, ch) in b.iter().enumerate() {
        if i == 8 || i == 13 || i == 18 || i == 23 {
            if *ch != b'-' {
                return Err(fail(codes::INVALID_JOB));
            }
        } else if !ch.is_ascii_hexdigit() || (*ch >= b'A' && *ch <= b'F') {
            return Err(fail(codes::INVALID_JOB));
        }
    }
    Ok(())
}

pub fn hex64(s: &str) -> Result<(), Error> {
    if s.len() != 64 {
        return Err(fail(codes::INVALID_HASH));
    }
    if !s.bytes().all(|ch| matches!(ch, b'0'..=b'9' | b'a'..=b'f')) {
        return Err(fail(codes::INVALID_HASH));
    }
    Ok(())
}

pub fn observation(s: &str) -> Result<(), Error> {
    hex64(s).map_err(|_| fail(codes::INVALID_OBSERVATION))
}

pub fn decimal_u64(s: &str) -> Result<u64, Error> {
    if s.is_empty() {
        return Err(fail(codes::INVALID_LENGTH));
    }
    if s == "0" {
        return Ok(0);
    }
    if s.as_bytes()[0] == b'0' {
        return Err(fail(codes::INVALID_LENGTH));
    }
    if !s.bytes().all(|ch| ch.is_ascii_digit()) {
        return Err(fail(codes::INVALID_LENGTH));
    }
    s.parse::<u64>().map_err(|_| fail(codes::INVALID_LENGTH))
}

pub fn b64_encode(raw: &[u8]) -> String {
    let mut out = String::new();
    let mut i = 0;
    while i + 3 <= raw.len() {
        let n = ((raw[i] as u32) << 16) | ((raw[i + 1] as u32) << 8) | (raw[i + 2] as u32);
        out.push(B64[((n >> 18) & 63) as usize] as char);
        out.push(B64[((n >> 12) & 63) as usize] as char);
        out.push(B64[((n >> 6) & 63) as usize] as char);
        out.push(B64[(n & 63) as usize] as char);
        i += 3;
    }
    match raw.len() - i {
        1 => {
            let n = (raw[i] as u32) << 16;
            out.push(B64[((n >> 18) & 63) as usize] as char);
            out.push(B64[((n >> 12) & 63) as usize] as char);
            out.push('=');
            out.push('=');
        }
        2 => {
            let n = ((raw[i] as u32) << 16) | ((raw[i + 1] as u32) << 8);
            out.push(B64[((n >> 18) & 63) as usize] as char);
            out.push(B64[((n >> 12) & 63) as usize] as char);
            out.push(B64[((n >> 6) & 63) as usize] as char);
            out.push('=');
        }
        _ => {}
    }
    out
}

fn b64_val(ch: u8) -> Option<u8> {
    match ch {
        b'A'..=b'Z' => Some(ch - b'A'),
        b'a'..=b'z' => Some(ch - b'a' + 26),
        b'0'..=b'9' => Some(ch - b'0' + 52),
        b'+' => Some(62),
        b'/' => Some(63),
        _ => None,
    }
}

pub fn b64_decode_canonical(s: &str) -> Result<Vec<u8>, Error> {
    if !s.len().is_multiple_of(4) || s.is_empty() {
        return Err(fail(codes::INVALID_JSON));
    }
    let bytes = s.as_bytes();
    let mut pad = 0usize;
    if bytes[bytes.len() - 1] == b'=' {
        pad += 1;
        if bytes[bytes.len() - 2] == b'=' {
            pad += 1;
        }
    }
    if pad > 2 {
        return Err(fail(codes::INVALID_JSON));
    }
    for (i, ch) in bytes.iter().enumerate() {
        if *ch == b'=' {
            if i < bytes.len() - pad {
                return Err(fail(codes::INVALID_JSON));
            }
        } else if b64_val(*ch).is_none() {
            return Err(fail(codes::INVALID_JSON));
        }
    }
    let mut out = Vec::with_capacity(s.len() / 4 * 3);
    let mut i = 0;
    while i < bytes.len() {
        let a = b64_val(bytes[i]).unwrap_or(0);
        let b = b64_val(bytes[i + 1]).unwrap_or(0);
        let c = if bytes[i + 2] == b'=' {
            0
        } else {
            b64_val(bytes[i + 2]).unwrap_or(0)
        };
        let d = if bytes[i + 3] == b'=' {
            0
        } else {
            b64_val(bytes[i + 3]).unwrap_or(0)
        };
        out.push((a << 2) | (b >> 4));
        if bytes[i + 2] != b'=' {
            out.push((b << 4) | (c >> 2));
        }
        if bytes[i + 3] != b'=' {
            out.push((c << 6) | d);
        }
        i += 4;
    }
    if b64_encode(&out) != s {
        return Err(fail(codes::INVALID_JSON));
    }
    Ok(out)
}

pub fn b64_decode_named(s: &str, invalid: &'static str) -> Result<Vec<u8>, Error> {
    b64_decode_canonical(s).map_err(|_| fail(invalid))
}

pub fn bounded_b64(s: &str, invalid: &'static str, max: usize) -> Result<Vec<u8>, Error> {
    let raw = b64_decode_named(s, invalid)?;
    if raw.is_empty() || raw.len() > max {
        return Err(fail(invalid));
    }
    Ok(raw)
}
