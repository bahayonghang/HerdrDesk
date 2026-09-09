//! HD-027 L1 filebridge protocol codec.
//!
//! No filesystem operations. No `main.rs`. The command
//! `herddesk-filebridge serve --stdio --protocol 1.0` is not implemented.

pub mod codec;
pub mod encoding;
pub mod error;
pub mod json;
pub mod path;
pub mod protocol;

pub use codec::Session;
pub use error::{codes, Error};
pub use protocol::{
    Direction, Frame, Header, Kind, Mode, Op, Outcome, HEADER_LEN, MAGIC, MAJOR, MAX_DATA,
    MAX_JSON, MINOR,
};
