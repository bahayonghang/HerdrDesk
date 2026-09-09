//! HD-027 protocol codec and HD-028 L1 `herddesk-filebridge serve`.
//!
//! L2 live filesystem, SSH, and TOCTOU remain unverified.

pub mod codec;
pub mod encoding;
pub mod error;
pub mod fs;
pub mod identity;
pub mod json;
pub mod path;
pub mod protocol;
pub mod server;
pub mod sha256;

pub use codec::Session;
pub use error::{codes, Error};
pub use protocol::{
    Direction, Frame, Header, Kind, Mode, Op, Outcome, HEADER_LEN, MAGIC, MAJOR, MAX_DATA,
    MAX_JSON, MINOR,
};
