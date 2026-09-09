//! Strict JSON: UTF-8, depth 32, no duplicate keys, no floats.

use crate::error::{codes, fail, Error};

const MAX_DEPTH: u32 = 32;

#[derive(Debug, Clone, PartialEq)]
pub enum Json {
    Null,
    Bool(bool),
    Int(i64),
    String(String),
    Array(Vec<Json>),
    Object(Vec<(String, Json)>),
}

impl Json {
    pub fn as_object(&self) -> Result<&[(String, Json)], Error> {
        match self {
            Json::Object(fields) => Ok(fields),
            _ => Err(fail(codes::INVALID_FIELD_TYPE)),
        }
    }

    pub fn get<'a>(&'a self, name: &str) -> Option<&'a Json> {
        match self {
            Json::Object(fields) => fields.iter().find(|(k, _)| k == name).map(|(_, v)| v),
            _ => None,
        }
    }

    pub fn encode(&self) -> Vec<u8> {
        let mut out = Vec::new();
        write_json(&mut out, self);
        out
    }

    pub fn object(fields: Vec<(&str, Json)>) -> Json {
        Json::Object(
            fields
                .into_iter()
                .map(|(k, v)| (k.to_string(), v))
                .collect(),
        )
    }
}

fn write_json(out: &mut Vec<u8>, value: &Json) {
    match value {
        Json::Null => out.extend_from_slice(b"null"),
        Json::Bool(true) => out.extend_from_slice(b"true"),
        Json::Bool(false) => out.extend_from_slice(b"false"),
        Json::Int(n) => out.extend_from_slice(n.to_string().as_bytes()),
        Json::String(s) => write_string(out, s),
        Json::Array(items) => {
            out.push(b'[');
            for (i, item) in items.iter().enumerate() {
                if i > 0 {
                    out.push(b',');
                }
                write_json(out, item);
            }
            out.push(b']');
        }
        Json::Object(fields) => {
            out.push(b'{');
            for (i, (k, v)) in fields.iter().enumerate() {
                if i > 0 {
                    out.push(b',');
                }
                write_string(out, k);
                out.push(b':');
                write_json(out, v);
            }
            out.push(b'}');
        }
    }
}

fn write_string(out: &mut Vec<u8>, s: &str) {
    out.push(b'"');
    for ch in s.chars() {
        match ch {
            '"' => out.extend_from_slice(br#"\""#),
            '\\' => out.extend_from_slice(br#"\\"#),
            '\u{0008}' => out.extend_from_slice(br#"\b"#),
            '\u{000c}' => out.extend_from_slice(br#"\f"#),
            '\n' => out.extend_from_slice(br#"\n"#),
            '\r' => out.extend_from_slice(br#"\r"#),
            '\t' => out.extend_from_slice(br#"\t"#),
            c if (c as u32) < 0x20 => {
                let n = c as u32;
                out.extend_from_slice(b"\\u00");
                const HEX: &[u8; 16] = b"0123456789abcdef";
                out.push(HEX[((n >> 4) & 0xf) as usize]);
                out.push(HEX[(n & 0xf) as usize]);
            }
            c => {
                let mut buf = [0u8; 4];
                let encoded = c.encode_utf8(&mut buf);
                out.extend_from_slice(encoded.as_bytes());
            }
        }
    }
    out.push(b'"');
}

struct Parser<'a> {
    bytes: &'a [u8],
    i: usize,
    depth: u32,
}

pub fn parse(bytes: &[u8]) -> Result<Json, Error> {
    if bytes.starts_with(&[0xEF, 0xBB, 0xBF]) {
        return Err(fail(codes::INVALID_JSON));
    }
    let mut p = Parser {
        bytes,
        i: 0,
        depth: 0,
    };
    let value = p.parse_value()?;
    p.skip_ws();
    if p.i != p.bytes.len() {
        return Err(fail(codes::INVALID_JSON));
    }
    Ok(value)
}

impl<'a> Parser<'a> {
    fn skip_ws(&mut self) {
        while self.i < self.bytes.len() {
            match self.bytes[self.i] {
                b' ' | b'\t' | b'\n' | b'\r' => self.i += 1,
                _ => break,
            }
        }
    }

    fn peek(&self) -> Result<u8, Error> {
        self.bytes
            .get(self.i)
            .copied()
            .ok_or(fail(codes::INVALID_JSON))
    }

    fn bump(&mut self) -> Result<u8, Error> {
        let b = self.peek()?;
        self.i += 1;
        Ok(b)
    }

    fn parse_value(&mut self) -> Result<Json, Error> {
        self.skip_ws();
        match self.peek()? {
            b'n' => self.ident(b"null", Json::Null),
            b't' => self.ident(b"true", Json::Bool(true)),
            b'f' => self.ident(b"false", Json::Bool(false)),
            b'"' => Ok(Json::String(self.parse_string()?)),
            b'{' => self.parse_object(),
            b'[' => self.parse_array(),
            b'-' | b'0'..=b'9' => self.parse_number(),
            _ => Err(fail(codes::INVALID_JSON)),
        }
    }

    fn ident(&mut self, lit: &[u8], value: Json) -> Result<Json, Error> {
        for ch in lit {
            if self.bump()? != *ch {
                return Err(fail(codes::INVALID_JSON));
            }
        }
        Ok(value)
    }

    fn enter(&mut self) -> Result<(), Error> {
        self.depth += 1;
        if self.depth > MAX_DEPTH {
            return Err(fail(codes::JSON_DEPTH_LIMIT));
        }
        Ok(())
    }

    fn leave(&mut self) {
        self.depth -= 1;
    }

    fn parse_object(&mut self) -> Result<Json, Error> {
        self.enter()?;
        let _ = self.bump()?;
        self.skip_ws();
        let mut fields: Vec<(String, Json)> = Vec::new();
        if self.peek()? == b'}' {
            let _ = self.bump()?;
            self.leave();
            return Ok(Json::Object(fields));
        }
        loop {
            self.skip_ws();
            if self.peek()? != b'"' {
                return Err(fail(codes::INVALID_JSON));
            }
            let key = self.parse_string()?;
            if fields.iter().any(|(k, _)| k == &key) {
                return Err(fail(codes::DUPLICATE_JSON_KEY));
            }
            self.skip_ws();
            if self.bump()? != b':' {
                return Err(fail(codes::INVALID_JSON));
            }
            let value = self.parse_value()?;
            fields.push((key, value));
            self.skip_ws();
            match self.bump()? {
                b',' => continue,
                b'}' => break,
                _ => return Err(fail(codes::INVALID_JSON)),
            }
        }
        self.leave();
        Ok(Json::Object(fields))
    }

    fn parse_array(&mut self) -> Result<Json, Error> {
        self.enter()?;
        let _ = self.bump()?;
        self.skip_ws();
        let mut items = Vec::new();
        if self.peek()? == b']' {
            let _ = self.bump()?;
            self.leave();
            return Ok(Json::Array(items));
        }
        loop {
            items.push(self.parse_value()?);
            self.skip_ws();
            match self.bump()? {
                b',' => continue,
                b']' => break,
                _ => return Err(fail(codes::INVALID_JSON)),
            }
        }
        self.leave();
        Ok(Json::Array(items))
    }

    fn parse_number(&mut self) -> Result<Json, Error> {
        let start = self.i;
        if self.peek()? == b'-' {
            self.i += 1;
        }
        if self.i >= self.bytes.len() {
            return Err(fail(codes::INVALID_JSON));
        }
        if self.bytes[self.i] == b'0' {
            self.i += 1;
            if self.i < self.bytes.len() && self.bytes[self.i].is_ascii_digit() {
                return Err(fail(codes::JSON_NUMBER_INVALID));
            }
        } else if self.bytes[self.i].is_ascii_digit() {
            while self.i < self.bytes.len() && self.bytes[self.i].is_ascii_digit() {
                self.i += 1;
            }
        } else {
            return Err(fail(codes::INVALID_JSON));
        }
        if self.i < self.bytes.len() && matches!(self.bytes[self.i], b'.' | b'e' | b'E') {
            return Err(fail(codes::JSON_FLOAT_REJECTED));
        }
        let raw = std::str::from_utf8(&self.bytes[start..self.i])
            .map_err(|_| fail(codes::INVALID_JSON))?;
        if raw == "-0" {
            return Err(fail(codes::JSON_NUMBER_INVALID));
        }
        let n = raw
            .parse::<i64>()
            .map_err(|_| fail(codes::JSON_NUMBER_INVALID))?;
        Ok(Json::Int(n))
    }

    fn parse_string(&mut self) -> Result<String, Error> {
        if self.bump()? != b'"' {
            return Err(fail(codes::INVALID_JSON));
        }
        let mut out = String::new();
        loop {
            let ch = self.bump()?;
            match ch {
                b'"' => return Ok(out),
                b'\\' => self.parse_escape(&mut out)?,
                0x00..=0x1F => return Err(fail(codes::INVALID_JSON)),
                _ => {
                    self.i -= 1;
                    let rest = &self.bytes[self.i..];
                    let s = std::str::from_utf8(rest).map_err(|_| fail(codes::INVALID_JSON))?;
                    let mut chars = s.chars();
                    let c = chars.next().ok_or(fail(codes::INVALID_JSON))?;
                    if c == '"' || c == '\\' {
                        return Err(fail(codes::INVALID_JSON));
                    }
                    out.push(c);
                    self.i += c.len_utf8();
                }
            }
        }
    }

    fn parse_escape(&mut self, out: &mut String) -> Result<(), Error> {
        match self.bump()? {
            b'"' => out.push('"'),
            b'\\' => out.push('\\'),
            b'/' => out.push('/'),
            b'b' => out.push('\u{0008}'),
            b'f' => out.push('\u{000c}'),
            b'n' => out.push('\n'),
            b'r' => out.push('\r'),
            b't' => out.push('\t'),
            b'u' => {
                let unit = self.hex4()?;
                if (0xD800..=0xDBFF).contains(&unit) {
                    if self.i + 2 <= self.bytes.len()
                        && self.bytes[self.i] == b'\\'
                        && self.bytes[self.i + 1] == b'u'
                    {
                        self.i += 2;
                        let low = self.hex4()?;
                        if !(0xDC00..=0xDFFF).contains(&low) {
                            return Err(fail(codes::INVALID_JSON));
                        }
                        let cp =
                            0x10000 + (((unit as u32) - 0xD800) << 10) + ((low as u32) - 0xDC00);
                        out.push(char::from_u32(cp).ok_or(fail(codes::INVALID_JSON))?);
                    } else {
                        return Err(fail(codes::INVALID_JSON));
                    }
                } else if (0xDC00..=0xDFFF).contains(&unit) {
                    return Err(fail(codes::INVALID_JSON));
                } else {
                    out.push(char::from_u32(unit as u32).ok_or(fail(codes::INVALID_JSON))?);
                }
            }
            _ => return Err(fail(codes::INVALID_JSON)),
        }
        Ok(())
    }

    fn hex4(&mut self) -> Result<u16, Error> {
        let mut n = 0u16;
        for _ in 0..4 {
            let ch = self.bump()?;
            n <<= 4;
            n |= match ch {
                b'0'..=b'9' => (ch - b'0') as u16,
                b'a'..=b'f' => (ch - b'a' + 10) as u16,
                b'A'..=b'F' => (ch - b'A' + 10) as u16,
                _ => return Err(fail(codes::INVALID_JSON)),
            };
        }
        Ok(n)
    }
}

pub fn require_object(value: &Json) -> Result<&[(String, Json)], Error> {
    value.as_object()
}

pub fn has(fields: &[(String, Json)], name: &str) -> bool {
    fields.iter().any(|(k, _)| k == name)
}

pub fn reject_unknown(fields: &[(String, Json)], allowed: &[&str]) -> Result<(), Error> {
    for (k, _) in fields {
        if !allowed.iter().any(|a| a == k) {
            return Err(fail(codes::UNKNOWN_FIELD));
        }
    }
    Ok(())
}

pub fn req_string<'a>(fields: &'a [(String, Json)], name: &str) -> Result<&'a str, Error> {
    match fields.iter().find(|(k, _)| k == name).map(|(_, v)| v) {
        None => Err(fail(codes::MISSING_FIELD)),
        Some(Json::String(s)) => Ok(s),
        Some(_) => Err(fail(codes::INVALID_FIELD_TYPE)),
    }
}

pub fn opt_string<'a>(fields: &'a [(String, Json)], name: &str) -> Result<Option<&'a str>, Error> {
    match fields.iter().find(|(k, _)| k == name).map(|(_, v)| v) {
        None => Ok(None),
        Some(Json::String(s)) => Ok(Some(s)),
        Some(_) => Err(fail(codes::INVALID_FIELD_TYPE)),
    }
}

pub fn req_int(fields: &[(String, Json)], name: &str) -> Result<i64, Error> {
    match fields.iter().find(|(k, _)| k == name).map(|(_, v)| v) {
        None => Err(fail(codes::MISSING_FIELD)),
        Some(Json::Int(n)) => Ok(*n),
        Some(_) => Err(fail(codes::INVALID_FIELD_TYPE)),
    }
}

pub fn req_bool(fields: &[(String, Json)], name: &str) -> Result<bool, Error> {
    match fields.iter().find(|(k, _)| k == name).map(|(_, v)| v) {
        None => Err(fail(codes::MISSING_FIELD)),
        Some(Json::Bool(v)) => Ok(*v),
        Some(_) => Err(fail(codes::INVALID_FIELD_TYPE)),
    }
}
