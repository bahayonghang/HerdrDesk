//! Platform local-socket mapping aligned with herdr v0.9.0 `src/ipc.rs`.
//! Windows: Path → to_string_lossy → GenericNamespaced. Marker files are not streams.

use std::io;
use std::path::Path;

#[cfg(unix)]
use interprocess::local_socket::GenericFilePath;
#[cfg(windows)]
use interprocess::local_socket::GenericNamespaced;
use interprocess::local_socket::{prelude::*, Listener, ListenerOptions, Stream};

use crate::error::BRIDGE_ENDPOINT_INVALID;

const CLIENT_SOCKET_SUFFIX: &str = "-client.sock";
const CLIENT_SOCKET_NAME: &str = "herdr-client.sock";

pub fn validate_socket_path(path: &Path) -> Result<(), &'static str> {
    if path.as_os_str().is_empty() {
        return Err(BRIDGE_ENDPOINT_INVALID);
    }
    let lossy = path.to_string_lossy();
    if lossy.is_empty() || lossy.contains('\0') || lossy.contains('\r') || lossy.contains('\n') {
        return Err(BRIDGE_ENDPOINT_INVALID);
    }
    if is_remote_smb(&lossy) {
        return Err(BRIDGE_ENDPOINT_INVALID);
    }
    if is_binary_client_socket(path, &lossy) {
        return Err(BRIDGE_ENDPOINT_INVALID);
    }
    Ok(())
}

pub fn connect(path: &Path) -> io::Result<Stream> {
    validate_socket_path(path).map_err(|code| io::Error::new(io::ErrorKind::InvalidInput, code))?;
    connect_mapped(path)
}

pub fn bind_local_listener(path: &Path) -> io::Result<Listener> {
    validate_socket_path(path).map_err(|code| io::Error::new(io::ErrorKind::InvalidInput, code))?;
    bind_mapped(path)
}

fn connect_mapped(path: &Path) -> io::Result<Stream> {
    #[cfg(windows)]
    {
        let owned = path.to_string_lossy().into_owned();
        let name = owned.to_ns_name::<GenericNamespaced>()?;
        Stream::connect(name)
    }
    #[cfg(unix)]
    {
        let name = path.to_fs_name::<GenericFilePath>()?;
        Stream::connect(name)
    }
}

fn bind_mapped(path: &Path) -> io::Result<Listener> {
    #[cfg(windows)]
    {
        let owned = path.to_string_lossy().into_owned();
        let name = owned.to_ns_name::<GenericNamespaced>()?;
        ListenerOptions::new()
            .name(name)
            .reclaim_name(true)
            .create_sync()
    }
    #[cfg(unix)]
    {
        let name = path.to_fs_name::<GenericFilePath>()?;
        ListenerOptions::new()
            .name(name)
            .reclaim_name(true)
            .create_sync()
    }
}

fn is_remote_smb(lossy: &str) -> bool {
    let normalized = lossy.replace('/', "\\");
    if !normalized.starts_with("\\\\") {
        return false;
    }
    !(normalized.starts_with("\\\\.\\") || normalized.starts_with("\\\\?\\"))
}

fn is_binary_client_socket(path: &Path, lossy: &str) -> bool {
    if let Some(name) = path.file_name().and_then(|value| value.to_str()) {
        if name.eq_ignore_ascii_case(CLIENT_SOCKET_NAME)
            || name.to_ascii_lowercase().ends_with(CLIENT_SOCKET_SUFFIX)
        {
            return true;
        }
    }
    let lower = lossy.to_ascii_lowercase();
    lower.ends_with(CLIENT_SOCKET_SUFFIX) || lower.contains("herdr-client.sock")
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::path::PathBuf;

    #[test]
    fn rejects_empty_and_nul() {
        assert!(validate_socket_path(Path::new("")).is_err());
        assert!(validate_socket_path(&PathBuf::from("ok\0bad")).is_err());
    }

    #[test]
    fn rejects_remote_smb_and_client_socket() {
        assert!(validate_socket_path(Path::new(r"\\server\pipe\herdr")).is_err());
        assert!(validate_socket_path(Path::new("/tmp/herdr-client.sock")).is_err());
        assert!(validate_socket_path(Path::new("/tmp/custom-api-client.sock")).is_err());
    }

    #[test]
    fn accepts_explicit_local_api_path() {
        assert!(validate_socket_path(Path::new("/tmp/herdr.sock")).is_ok());
        assert!(validate_socket_path(Path::new(r"C:\Temp\herdr.sock")).is_ok());
    }
}
