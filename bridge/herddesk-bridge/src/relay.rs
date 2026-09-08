//! Fixed-size bidirectional byte pumps. Protocol bytes stay opaque.

use std::io::{self, Read, Write};
use std::sync::mpsc;
use std::thread;

use interprocess::local_socket::Stream;
use interprocess::TryClone as _;

use crate::error::BRIDGE_RELAY_FAILED;

pub const PUMP_BUFFER_BYTES: usize = 64 * 1024;

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
enum PumpEvent {
    UploadEof,
    DownloadEof,
    Failed,
}

pub fn run<In, Out>(stream: Stream, stdin: In, stdout: Out) -> Result<(), &'static str>
where
    In: Read + Send + 'static,
    Out: Write + Send + 'static,
{
    let send = stream.try_clone().map_err(|_| BRIDGE_RELAY_FAILED)?;
    let recv = stream;
    let (tx, rx) = mpsc::channel();
    let upload_tx = tx.clone();
    let upload = thread::Builder::new()
        .name("herddesk-bridge-upload".into())
        .spawn(move || upload_pump(stdin, send, upload_tx))
        .map_err(|_| BRIDGE_RELAY_FAILED)?;
    let download = thread::Builder::new()
        .name("herddesk-bridge-download".into())
        .spawn(move || download_pump(recv, stdout, tx))
        .map_err(|_| BRIDGE_RELAY_FAILED)?;

    let mut failed = false;
    let mut download_done = false;
    while !download_done {
        match rx.recv() {
            Ok(PumpEvent::DownloadEof) => download_done = true,
            Ok(PumpEvent::UploadEof) => {}
            Ok(PumpEvent::Failed) | Err(_) => {
                failed = true;
                download_done = true;
            }
        }
    }
    let _ = download.join();
    // Upload may still block on stdin after peer EOF. Parent close or process
    // exit unblocks it. Joining here would hang the coordinator.
    drop(upload);
    if failed {
        Err(BRIDGE_RELAY_FAILED)
    } else {
        Ok(())
    }
}

fn upload_pump<In: Read>(mut stdin: In, mut send: Stream, tx: mpsc::Sender<PumpEvent>) {
    let mut buffer = [0_u8; PUMP_BUFFER_BYTES];
    let event = loop {
        match stdin.read(&mut buffer) {
            Ok(0) => {
                shutdown_write_after_stdin_eof(&send);
                break PumpEvent::UploadEof;
            }
            Ok(n) => {
                if write_all_partial(&mut send, &buffer[..n]).is_err() {
                    break PumpEvent::Failed;
                }
            }
            Err(error) if error.kind() == io::ErrorKind::Interrupted => {}
            Err(_) => break PumpEvent::Failed,
        }
    };
    let _ = tx.send(event);
}

fn download_pump<Out: Write>(mut recv: Stream, mut stdout: Out, tx: mpsc::Sender<PumpEvent>) {
    let mut buffer = [0_u8; PUMP_BUFFER_BYTES];
    let event = loop {
        match recv.read(&mut buffer) {
            Ok(0) => {
                let _ = stdout.flush();
                break PumpEvent::DownloadEof;
            }
            Ok(n) => {
                if write_all_partial(&mut stdout, &buffer[..n]).is_err() {
                    break PumpEvent::Failed;
                }
            }
            Err(error) if error.kind() == io::ErrorKind::Interrupted => {}
            Err(_) => break PumpEvent::Failed,
        }
    };
    let _ = tx.send(event);
}

fn write_all_partial<W: Write>(writer: &mut W, mut data: &[u8]) -> io::Result<()> {
    while !data.is_empty() {
        match writer.write(data) {
            Ok(0) => {
                return Err(io::Error::new(
                    io::ErrorKind::WriteZero,
                    BRIDGE_RELAY_FAILED,
                ))
            }
            Ok(n) => data = &data[n..],
            Err(error) if error.kind() == io::ErrorKind::Interrupted => {}
            Err(error) => return Err(error),
        }
    }
    Ok(())
}

fn shutdown_write_after_stdin_eof(_send: &Stream) {
    #[cfg(unix)]
    {
        use std::net::Shutdown;
        if let Stream::UdSocket(inner) = _send {
            let _ = inner.inner().shutdown(Shutdown::Write);
        }
    }
    #[cfg(windows)]
    {
        // Named-pipe portable shutdown is a no-op. Do not log a successful half-close.
    }
}

#[cfg(test)]
mod tests {
    use super::PUMP_BUFFER_BYTES;

    #[test]
    fn pump_buffer_is_fixed_64kib() {
        assert_eq!(PUMP_BUFFER_BYTES, 64 * 1024);
    }
}
