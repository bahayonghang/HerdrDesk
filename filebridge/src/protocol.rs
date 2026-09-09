//! Header, kinds, and frame constants.

use crate::error::{codes, fail, Error};

pub const MAGIC: [u8; 4] = *b"HDFB";
pub const HEADER_LEN: usize = 16;
pub const MAJOR: u8 = 1;
pub const MINOR: u8 = 0;
pub const MAX_JSON: u32 = 1024 * 1024;
pub const MAX_DATA: u32 = 1024 * 1024;
pub const MAX_CONTROL_TOTAL: u64 = 16 * 1024 * 1024;
pub const MAX_CURSOR: usize = 4096;
pub const MAX_IDENTITY: usize = 4096;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum Kind {
    RequestJson = 0x01,
    Data = 0x02,
    EndData = 0x03,
    CancelJson = 0x04,
    AcceptedJson = 0x11,
    EntryJson = 0x12,
    ProgressJson = 0x13,
    CompleteJson = 0x14,
    ErrorJson = 0x7F,
}

impl Kind {
    pub fn from_u8(value: u8) -> Result<Self, Error> {
        match value {
            0x01 => Ok(Kind::RequestJson),
            0x02 => Ok(Kind::Data),
            0x03 => Ok(Kind::EndData),
            0x04 => Ok(Kind::CancelJson),
            0x11 => Ok(Kind::AcceptedJson),
            0x12 => Ok(Kind::EntryJson),
            0x13 => Ok(Kind::ProgressJson),
            0x14 => Ok(Kind::CompleteJson),
            0x7F => Ok(Kind::ErrorJson),
            _ => Err(fail(codes::UNKNOWN_KIND)),
        }
    }

    pub fn is_json(self) -> bool {
        !matches!(self, Kind::Data | Kind::EndData)
    }

    pub fn max_payload(self) -> u32 {
        match self {
            Kind::EndData => 0,
            Kind::Data => MAX_DATA,
            _ => MAX_JSON,
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Direction {
    Client,
    Helper,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Op {
    List,
    Stat,
    Read,
    Write,
    Rename,
}

impl Op {
    pub fn parse(s: &str) -> Result<Self, Error> {
        match s {
            "list" => Ok(Op::List),
            "stat" => Ok(Op::Stat),
            "read" => Ok(Op::Read),
            "write" => Ok(Op::Write),
            "rename" => Ok(Op::Rename),
            _ => Err(fail(codes::INVALID_OP)),
        }
    }

    pub fn as_str(self) -> &'static str {
        match self {
            Op::List => "list",
            Op::Stat => "stat",
            Op::Read => "read",
            Op::Write => "write",
            Op::Rename => "rename",
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Mode {
    Create,
    Replace,
}

impl Mode {
    pub fn parse(s: &str) -> Result<Self, Error> {
        match s {
            "create" => Ok(Mode::Create),
            "replace" => Ok(Mode::Replace),
            _ => Err(fail(codes::INVALID_MODE)),
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Outcome {
    Pending,
    Success,
    Failed,
    Cancelled,
    Unknown,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Header {
    pub kind: Kind,
    pub payload_len: u32,
    pub seq: u32,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Frame {
    pub direction: Direction,
    pub kind: Kind,
    pub seq: u32,
    pub payload: Vec<u8>,
}

pub fn parse_header(bytes: &[u8]) -> Result<Header, Error> {
    if bytes.len() != HEADER_LEN {
        return Err(fail(codes::TRUNCATED_HEADER));
    }
    if bytes[0..4] != MAGIC {
        return Err(fail(codes::PROTOCOL_POLLUTION));
    }
    if bytes[4] != MAJOR || bytes[5] != MINOR {
        return Err(fail(codes::UNSUPPORTED_VERSION));
    }
    if bytes[7] != 0 {
        return Err(fail(codes::UNKNOWN_FLAGS));
    }
    let kind = Kind::from_u8(bytes[6])?;
    let payload_len = u32::from_be_bytes([bytes[8], bytes[9], bytes[10], bytes[11]]);
    let seq = u32::from_be_bytes([bytes[12], bytes[13], bytes[14], bytes[15]]);
    if kind == Kind::EndData {
        if payload_len != 0 {
            return Err(fail(codes::INVALID_PAYLOAD_LENGTH));
        }
    } else if payload_len > kind.max_payload() {
        return Err(fail(codes::PAYLOAD_TOO_LARGE));
    }
    Ok(Header {
        kind,
        payload_len,
        seq,
    })
}

pub fn encode_header(kind: Kind, seq: u32, payload_len: u32) -> Result<[u8; HEADER_LEN], Error> {
    if kind == Kind::EndData {
        if payload_len != 0 {
            return Err(fail(codes::INVALID_PAYLOAD_LENGTH));
        }
    } else if payload_len > kind.max_payload() {
        return Err(fail(codes::PAYLOAD_TOO_LARGE));
    }
    let mut out = [0u8; HEADER_LEN];
    out[0..4].copy_from_slice(&MAGIC);
    out[4] = MAJOR;
    out[5] = MINOR;
    out[6] = kind as u8;
    out[7] = 0;
    out[8..12].copy_from_slice(&payload_len.to_be_bytes());
    out[12..16].copy_from_slice(&seq.to_be_bytes());
    Ok(out)
}

pub fn kind_direction(kind: Kind, op: Option<Op>) -> Result<Direction, Error> {
    match kind {
        Kind::RequestJson | Kind::EndData | Kind::CancelJson => Ok(Direction::Client),
        Kind::AcceptedJson
        | Kind::EntryJson
        | Kind::ProgressJson
        | Kind::CompleteJson
        | Kind::ErrorJson => Ok(Direction::Helper),
        Kind::Data => match op {
            Some(Op::Write) => Ok(Direction::Client),
            Some(Op::Read) => Ok(Direction::Helper),
            _ => Err(fail(codes::UNEXPECTED_KIND)),
        },
    }
}
