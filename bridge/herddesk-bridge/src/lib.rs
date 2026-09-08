//! Byte-only local-socket relay. No JSON parser and no shell or TCP listener.

pub mod endpoint;
pub mod error;
pub mod relay;

use std::ffi::OsString;
use std::io::{self, Write};
use std::path::PathBuf;
use std::process::ExitCode;

use crate::error::{
    BRIDGE_CONNECT_DENIED, BRIDGE_CONNECT_FAILED, BRIDGE_ENDPOINT_INVALID, BRIDGE_RELAY_FAILED,
    BRIDGE_USAGE,
};

const VERSION: &str = env!("CARGO_PKG_VERSION");

#[derive(Debug)]
pub enum Command {
    Version,
    Rpc { socket_path: PathBuf },
}

pub fn run() -> ExitCode {
    match run_with_io(
        std::env::args_os().skip(1),
        io::stdin(),
        io::stdout(),
        io::stderr(),
    ) {
        Ok(()) => ExitCode::SUCCESS,
        Err(code) => {
            let _ = writeln!(io::stderr(), "{code}");
            ExitCode::from(1)
        }
    }
}

pub fn run_with_io<I, In, Out, ErrOut>(
    args: I,
    stdin: In,
    mut stdout: Out,
    _stderr: ErrOut,
) -> Result<(), &'static str>
where
    I: IntoIterator<Item = OsString>,
    In: io::Read + Send + 'static,
    Out: io::Write + Send + 'static,
    ErrOut: io::Write,
{
    match parse_args(args)? {
        Command::Version => {
            writeln!(stdout, "herddesk-bridge {VERSION}").map_err(|_| BRIDGE_RELAY_FAILED)?;
            Ok(())
        }
        Command::Rpc { socket_path } => {
            endpoint::validate_socket_path(&socket_path)?;
            let stream = match endpoint::connect(&socket_path) {
                Ok(stream) => stream,
                Err(error) => return Err(map_connect_error(&error)),
            };
            relay::run(stream, stdin, stdout)
        }
    }
}

pub fn parse_args<I>(args: I) -> Result<Command, &'static str>
where
    I: IntoIterator<Item = OsString>,
{
    let args: Vec<OsString> = args.into_iter().collect();
    if args.len() == 1 && args[0] == "--version" {
        return Ok(Command::Version);
    }
    if args.is_empty() {
        return Err(BRIDGE_USAGE);
    }
    if args[0] != "rpc" {
        return Err(BRIDGE_USAGE);
    }
    if args.len() != 3 || args[1] != "--socket-path" {
        return Err(BRIDGE_USAGE);
    }
    if args[2].is_empty() {
        return Err(BRIDGE_ENDPOINT_INVALID);
    }
    Ok(Command::Rpc {
        socket_path: PathBuf::from(&args[2]),
    })
}

fn map_connect_error(error: &io::Error) -> &'static str {
    match error.kind() {
        io::ErrorKind::PermissionDenied => BRIDGE_CONNECT_DENIED,
        io::ErrorKind::InvalidInput | io::ErrorKind::InvalidData => BRIDGE_ENDPOINT_INVALID,
        _ => BRIDGE_CONNECT_FAILED,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn version_is_a_separate_command() {
        let command = parse_args([OsString::from("--version")]).unwrap();
        assert!(matches!(command, Command::Version));
    }

    #[test]
    fn rpc_requires_socket_path_flag() {
        assert_eq!(
            parse_args([OsString::from("rpc")]).unwrap_err(),
            BRIDGE_USAGE
        );
        assert_eq!(
            parse_args([
                OsString::from("rpc"),
                OsString::from("--socket-path"),
                OsString::from("")
            ])
            .unwrap_err(),
            BRIDGE_ENDPOINT_INVALID
        );
    }

    #[test]
    fn unknown_command_is_usage() {
        assert_eq!(
            parse_args([OsString::from("listen")]).unwrap_err(),
            BRIDGE_USAGE
        );
    }
}
