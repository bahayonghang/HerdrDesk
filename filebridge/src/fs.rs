//! Local filesystem adapter for the helper. Unix openat lives behind cfg(unix).

use crate::error::helper;
#[cfg(windows)]
use crate::identity::portable_identity;
#[cfg(unix)]
use crate::identity::unix_identity;
#[cfg(windows)]
use crate::identity::windows_identity;
use crate::identity::{observation, EntryKind};
use crate::path;
use crate::sha256::{digest_hex, Sha256};
use std::fs::{self, File, OpenOptions};
use std::io::{self, Read, Write};
use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};

#[derive(Debug, Clone)]
pub struct FsError {
    pub code: &'static str,
}

impl FsError {
    pub fn new(code: &'static str) -> Self {
        Self { code }
    }
}

fn map_io(err: io::Error) -> FsError {
    match err.kind() {
        io::ErrorKind::NotFound => FsError::new(helper::NOT_FOUND),
        io::ErrorKind::PermissionDenied => FsError::new(helper::PERMISSION_DENIED),
        io::ErrorKind::AlreadyExists => FsError::new(helper::NAME_EXISTS),
        _ => FsError::new(helper::UNSUPPORTED),
    }
}

#[derive(Debug)]
pub struct ListResult {
    pub dir: Stat,
    pub entries: Vec<(Vec<u8>, String, Stat)>,
    pub has_more: bool,
}

#[derive(Debug, Clone)]
pub struct Stat {
    pub kind: EntryKind,
    pub size: u64,
    pub mtime: u64,
    pub mtime_precision: u64,
    pub identity: Vec<u8>,
    pub symlink: bool,
    pub observation: String,
    pub sha256: String,
}

pub struct LocalRoot {
    root: PathBuf,
}

impl LocalRoot {
    pub fn new(root: PathBuf) -> Result<Self, FsError> {
        let root = fs::canonicalize(&root).map_err(map_io)?;
        Ok(Self { root })
    }

    pub fn resolve(&self, components: &[Vec<u8>]) -> Result<PathBuf, FsError> {
        let mut current = self.root.clone();
        for (i, raw) in components.iter().enumerate() {
            push_component(&mut current, raw)?;
            if !current.starts_with(&self.root) {
                return Err(FsError::new("invalid_path"));
            }
            let last = i + 1 == components.len();
            let meta = fs::symlink_metadata(&current).map_err(map_io)?;
            let is_link = meta.file_type().is_symlink() || unix_is_symlink(&meta);
            if is_link {
                if last {
                    break;
                }
                return Err(FsError::new(helper::UNSUPPORTED));
            }
            if !last && !meta.file_type().is_dir() {
                return Err(FsError::new(helper::NOT_DIRECTORY));
            }
        }
        Ok(current)
    }

    pub fn list(
        &self,
        components: &[Vec<u8>],
        limit: i64,
        cursor: Option<&[u8]>,
    ) -> Result<ListResult, FsError> {
        let dir = self.resolve(components)?;
        let dir_stat = self.stat_path(components, &dir, false)?;
        if dir_stat.symlink {
            return Err(FsError::new(helper::UNSUPPORTED));
        }
        if dir_stat.kind != EntryKind::Directory {
            return Err(FsError::new(helper::NOT_DIRECTORY));
        }
        let mut names: Vec<PathBuf> = fs::read_dir(&dir)
            .map_err(map_io)?
            .filter_map(|e| e.ok().map(|e| e.path()))
            .collect();
        names.sort();
        let mut entries = Vec::new();
        let mut seen = cursor.is_none();
        let mut has_more = false;
        for path in names {
            let raw = match file_name_raw(&path) {
                Some(n) if n != b"." && n != b".." => n,
                _ => continue,
            };
            if path::check_component(&raw).is_err() {
                continue;
            }
            let name = std::str::from_utf8(&raw)
                .map(|s| s.to_string())
                .unwrap_or_else(|_| "\u{FFFD}".to_string());
            if !seen {
                if cursor == Some(raw.as_slice()) {
                    seen = true;
                }
                continue;
            }
            if entries.len() as i64 >= limit {
                has_more = true;
                break;
            }
            let mut child = components.to_vec();
            child.push(raw.clone());
            if let Ok(stat) = self.stat_path(&child, &path, false) {
                entries.push((raw, name, stat));
            }
        }
        Ok(ListResult {
            dir: dir_stat,
            entries,
            has_more,
        })
    }

    pub fn stat(&self, components: &[Vec<u8>], hash_file: bool) -> Result<Stat, FsError> {
        let path = self.resolve(components)?;
        self.stat_path(components, &path, hash_file)
    }

    fn stat_path(
        &self,
        components: &[Vec<u8>],
        path: &Path,
        hash_file: bool,
    ) -> Result<Stat, FsError> {
        let meta = fs::symlink_metadata(path).map_err(map_io)?;
        let file_type = meta.file_type();
        let symlink = file_type.is_symlink();
        let kind = if symlink {
            EntryKind::Symlink
        } else if file_type.is_dir() {
            EntryKind::Directory
        } else if file_type.is_file() {
            EntryKind::File
        } else {
            EntryKind::Other
        };
        let size = if kind == EntryKind::File {
            meta.len()
        } else {
            0
        };
        let mtime = meta
            .modified()
            .ok()
            .and_then(|t| t.duration_since(UNIX_EPOCH).ok())
            .map(|d| d.as_secs())
            .unwrap_or(0);
        let identity = file_identity(path, &meta, size, mtime);
        let sha256 = if hash_file && kind == EntryKind::File {
            hash_path(path)?
        } else {
            digest_hex(b"")
        };
        let obs = observation(components, true, kind, &identity, size, mtime, 1);
        Ok(Stat {
            kind,
            size,
            mtime,
            mtime_precision: 1,
            identity,
            symlink,
            observation: obs,
            sha256,
        })
    }

    pub fn open_read(&self, components: &[Vec<u8>]) -> Result<(Stat, File), FsError> {
        let path = self.resolve(components)?;
        let stat = self.stat_path(components, &path, true)?;
        if stat.kind != EntryKind::File {
            return Err(FsError::new(helper::IS_DIRECTORY));
        }
        if stat.symlink {
            return Err(FsError::new(helper::UNSUPPORTED));
        }
        let file = open_nofollow_read(&path)?;
        Ok((stat, file))
    }

    pub fn create_temp(
        &self,
        parent: &[Vec<u8>],
        job: &str,
        nonce: &[u8; 16],
    ) -> Result<(PathBuf, File, Vec<u8>), FsError> {
        let parent_path = self.resolve(parent)?;
        let parent_stat = self.stat_path(parent, &parent_path, false)?;
        if parent_stat.kind != EntryKind::Directory {
            return Err(FsError::new(helper::NOT_DIRECTORY));
        }
        if parent_stat.symlink {
            return Err(FsError::new(helper::UNSUPPORTED));
        }
        let job_n = job.replace('-', "");
        let name = format!(".herddesk-upload-{job_n}-{}.tmp", crate::sha256::hex(nonce));
        let temp = parent_path.join(&name);
        if !temp.starts_with(&self.root) {
            return Err(FsError::new("invalid_path"));
        }
        let file = create_new_nofollow(&temp)?;
        let meta = file.metadata().map_err(map_io)?;
        let mtime = meta
            .modified()
            .ok()
            .and_then(|t| t.duration_since(UNIX_EPOCH).ok())
            .map(|d| d.as_secs())
            .unwrap_or(0);
        let identity = file_identity(&temp, &meta, 0, mtime);
        Ok((temp, file, identity))
    }

    pub fn commit_create(
        &self,
        temp: &Path,
        parent: &[Vec<u8>],
        name: &[u8],
        keep_both: bool,
    ) -> Result<(Vec<u8>, PathBuf), FsError> {
        let parent_path = self.resolve(parent)?;
        let attempts = if keep_both { 1000 } else { 1 };
        for i in 0..attempts {
            let candidate = crate::identity::keepboth_candidate(name, i);
            path::check_component(&candidate).map_err(|_| FsError::new("invalid_path"))?;
            let text =
                std::str::from_utf8(&candidate).map_err(|_| FsError::new(helper::UNSUPPORTED))?;
            let dest = parent_path.join(text);
            if !dest.starts_with(&self.root) {
                continue;
            }
            match rename_noreplace(temp, &dest) {
                Ok(()) => return Ok((candidate, dest)),
                Err(err) if err.code == helper::NAME_EXISTS && keep_both => continue,
                Err(err) => return Err(err),
            }
        }
        Err(FsError::new(helper::NAME_EXISTS))
    }

    pub fn delete_if_identity(&self, path: &Path, identity: &[u8]) -> Result<(), FsError> {
        if !path.starts_with(&self.root) {
            return Err(FsError::new("invalid_path"));
        }
        let meta = match fs::symlink_metadata(path) {
            Ok(m) => m,
            Err(_) => return Ok(()),
        };
        let mtime = meta
            .modified()
            .ok()
            .and_then(|t| t.duration_since(UNIX_EPOCH).ok())
            .map(|d| d.as_secs())
            .unwrap_or(0);
        let current = file_identity(path, &meta, meta.len(), mtime);
        if current != identity {
            return Err(FsError::new(helper::CONFLICT));
        }
        fs::remove_file(path).map_err(map_io)?;
        Ok(())
    }

    pub fn rename_create(
        &self,
        source: &[Vec<u8>],
        parent: &[Vec<u8>],
        name: &[u8],
    ) -> Result<PathBuf, FsError> {
        let from = self.resolve(source)?;
        let parent_path = self.resolve(parent)?;
        path::check_component(name).map_err(|_| FsError::new("invalid_path"))?;
        let text = std::str::from_utf8(name).map_err(|_| FsError::new(helper::UNSUPPORTED))?;
        let to = parent_path.join(text);
        if !to.starts_with(&self.root) {
            return Err(FsError::new("invalid_path"));
        }
        rename_noreplace(&from, &to)?;
        Ok(to)
    }
}

fn hash_path(path: &Path) -> Result<String, FsError> {
    let mut file = open_nofollow_read(path)?;
    let mut sha = Sha256::new();
    let mut buf = [0u8; 65536];
    loop {
        let n = file.read(&mut buf).map_err(map_io)?;
        if n == 0 {
            break;
        }
        sha.update(&buf[..n]);
    }
    Ok(crate::sha256::hex(&sha.finish()))
}

fn file_identity(path: &Path, meta: &fs::Metadata, size: u64, mtime: u64) -> Vec<u8> {
    #[cfg(windows)]
    {
        let _ = meta;
        if let Ok(file) = open_for_id(path) {
            if let Some(id) = windows_id_from_file(&file) {
                return id;
            }
        }
        portable_identity(path.to_string_lossy().as_bytes(), size, mtime)
    }
    #[cfg(unix)]
    {
        let _ = (path, size, mtime);
        use std::os::unix::fs::MetadataExt;
        unix_identity(meta.dev(), meta.ino())
    }
}

#[cfg(windows)]
fn open_for_id(path: &Path) -> io::Result<File> {
    use std::os::windows::fs::OpenOptionsExt;
    OpenOptions::new()
        .read(true)
        .custom_flags(0x0020_0000 | 0x0200_0000)
        .open(path)
}

#[cfg(windows)]
fn windows_id_from_file(file: &File) -> Option<Vec<u8>> {
    use std::os::windows::io::AsRawHandle;
    #[repr(C)]
    struct ByHandleFileInformation {
        file_attributes: u32,
        creation_low: u32,
        creation_high: u32,
        access_low: u32,
        access_high: u32,
        write_low: u32,
        write_high: u32,
        volume_serial_number: u32,
        file_size_high: u32,
        file_size_low: u32,
        number_of_links: u32,
        file_index_high: u32,
        file_index_low: u32,
    }
    #[link(name = "kernel32")]
    extern "system" {
        fn GetFileInformationByHandle(
            handle: *mut core::ffi::c_void,
            info: *mut ByHandleFileInformation,
        ) -> i32;
    }
    let mut info = ByHandleFileInformation {
        file_attributes: 0,
        creation_low: 0,
        creation_high: 0,
        access_low: 0,
        access_high: 0,
        write_low: 0,
        write_high: 0,
        volume_serial_number: 0,
        file_size_high: 0,
        file_size_low: 0,
        number_of_links: 0,
        file_index_high: 0,
        file_index_low: 0,
    };
    let ok = unsafe { GetFileInformationByHandle(file.as_raw_handle(), &mut info) };
    if ok == 0 {
        return None;
    }
    let index = ((info.file_index_high as u64) << 32) | u64::from(info.file_index_low);
    Some(windows_identity(info.volume_serial_number, index))
}

fn open_nofollow_read(path: &Path) -> Result<File, FsError> {
    #[cfg(windows)]
    {
        use std::os::windows::fs::OpenOptionsExt;
        OpenOptions::new()
            .read(true)
            .custom_flags(0x0020_0000)
            .open(path)
            .map_err(map_io)
    }
    #[cfg(any(target_os = "linux", target_os = "macos"))]
    {
        use std::os::unix::fs::OpenOptionsExt;
        OpenOptions::new()
            .read(true)
            .custom_flags(libc_o_nofollow())
            .open(path)
            .map_err(map_io)
    }
    #[cfg(not(any(windows, target_os = "linux", target_os = "macos")))]
    {
        let _ = path;
        Err(FsError::new(helper::UNSUPPORTED))
    }
}

fn create_new_nofollow(path: &Path) -> Result<File, FsError> {
    #[cfg(windows)]
    {
        use std::os::windows::fs::OpenOptionsExt;
        OpenOptions::new()
            .write(true)
            .read(true)
            .create_new(true)
            .custom_flags(0x0020_0000)
            .open(path)
            .map_err(map_io)
    }
    #[cfg(any(target_os = "linux", target_os = "macos"))]
    {
        use std::os::unix::fs::OpenOptionsExt;
        OpenOptions::new()
            .write(true)
            .read(true)
            .create_new(true)
            .custom_flags(libc_o_nofollow())
            .open(path)
            .map_err(map_io)
    }
    #[cfg(not(any(windows, target_os = "linux", target_os = "macos")))]
    {
        let _ = path;
        Err(FsError::new(helper::UNSUPPORTED))
    }
}

#[cfg(target_os = "linux")]
fn libc_o_nofollow() -> i32 {
    0x20000
}

#[cfg(target_os = "macos")]
fn libc_o_nofollow() -> i32 {
    0x0100
}

fn rename_noreplace(from: &Path, to: &Path) -> Result<(), FsError> {
    #[cfg(windows)]
    {
        use std::os::windows::ffi::OsStrExt;
        let from_w: Vec<u16> = from.as_os_str().encode_wide().chain(Some(0)).collect();
        let to_w: Vec<u16> = to.as_os_str().encode_wide().chain(Some(0)).collect();
        #[link(name = "kernel32")]
        extern "system" {
            fn MoveFileExW(old: *const u16, new: *const u16, flags: u32) -> i32;
            fn GetLastError() -> u32;
        }
        unsafe {
            if MoveFileExW(from_w.as_ptr(), to_w.as_ptr(), 0) != 0 {
                return Ok(());
            }
            let err = GetLastError();
            if err == 80 || err == 183 {
                return Err(FsError::new(helper::NAME_EXISTS));
            }
            if err == 5 {
                return Err(FsError::new(helper::PERMISSION_DENIED));
            }
            Err(FsError::new(helper::UNSUPPORTED))
        }
    }
    #[cfg(target_os = "linux")]
    {
        renameat2_noreplace(from, to)
    }
    #[cfg(target_os = "macos")]
    {
        renameatx_np_excl(from, to)
    }
    #[cfg(not(any(windows, target_os = "linux", target_os = "macos")))]
    {
        let _ = (from, to);
        Err(FsError::new(helper::UNSUPPORTED))
    }
}

#[cfg(any(target_os = "linux", target_os = "macos"))]
fn path_c_string(path: &Path) -> Result<std::ffi::CString, FsError> {
    use std::os::unix::ffi::OsStrExt;
    std::ffi::CString::new(path.as_os_str().as_bytes()).map_err(|_| FsError::new("invalid_path"))
}

#[cfg(any(target_os = "linux", target_os = "macos"))]
fn map_rename_errno(err: io::Error) -> FsError {
    match err.raw_os_error() {
        Some(17) => FsError::new(helper::NAME_EXISTS),
        Some(13) => FsError::new(helper::PERMISSION_DENIED),
        Some(2) => FsError::new(helper::NOT_FOUND),
        Some(38) | Some(22) | Some(95) => FsError::new(helper::UNSUPPORTED),
        _ => map_io(err),
    }
}

#[cfg(target_os = "linux")]
fn renameat2_noreplace(from: &Path, to: &Path) -> Result<(), FsError> {
    let old = path_c_string(from)?;
    let new = path_c_string(to)?;
    const AT_FDCWD: i32 = -100;
    const RENAME_NOREPLACE: u32 = 1;
    extern "C" {
        fn renameat2(
            olddirfd: i32,
            oldpath: *const i8,
            newdirfd: i32,
            newpath: *const i8,
            flags: u32,
        ) -> i32;
    }
    let rc = unsafe {
        renameat2(
            AT_FDCWD,
            old.as_ptr(),
            AT_FDCWD,
            new.as_ptr(),
            RENAME_NOREPLACE,
        )
    };
    if rc == 0 {
        Ok(())
    } else {
        Err(map_rename_errno(io::Error::last_os_error()))
    }
}

#[cfg(target_os = "macos")]
fn renameatx_np_excl(from: &Path, to: &Path) -> Result<(), FsError> {
    let old = path_c_string(from)?;
    let new = path_c_string(to)?;
    const AT_FDCWD: i32 = -100;
    const RENAME_EXCL: u32 = 0x0004;
    extern "C" {
        fn renameatx_np(fromfd: i32, from: *const i8, tofd: i32, to: *const i8, flags: u32) -> i32;
    }
    let rc = unsafe { renameatx_np(AT_FDCWD, old.as_ptr(), AT_FDCWD, new.as_ptr(), RENAME_EXCL) };
    if rc == 0 {
        Ok(())
    } else {
        Err(map_rename_errno(io::Error::last_os_error()))
    }
}

fn unix_is_symlink(meta: &fs::Metadata) -> bool {
    #[cfg(unix)]
    {
        use std::os::unix::fs::MetadataExt;
        const S_IFMT: u32 = 0o170000;
        const S_IFLNK: u32 = 0o120000;
        (meta.mode() & S_IFMT) == S_IFLNK
    }
    #[cfg(not(unix))]
    {
        let _ = meta;
        false
    }
}

fn push_component(current: &mut PathBuf, raw: &[u8]) -> Result<(), FsError> {
    path::check_component(raw).map_err(|_| FsError::new("invalid_path"))?;
    #[cfg(unix)]
    {
        use std::os::unix::ffi::OsStrExt;
        current.push(std::ffi::OsStr::from_bytes(raw));
    }
    #[cfg(not(unix))]
    {
        let name = std::str::from_utf8(raw).map_err(|_| FsError::new(helper::UNSUPPORTED))?;
        if name.contains('\\') {
            return Err(FsError::new("invalid_path"));
        }
        current.push(name);
    }
    Ok(())
}

fn file_name_raw(path: &Path) -> Option<Vec<u8>> {
    #[cfg(unix)]
    {
        use std::os::unix::ffi::OsStrExt;
        path.file_name().map(|n| n.as_bytes().to_vec())
    }
    #[cfg(not(unix))]
    {
        path.file_name()
            .and_then(|n| n.to_str())
            .map(|s| s.as_bytes().to_vec())
    }
}

pub fn random16() -> [u8; 16] {
    let mut out = [0u8; 16];
    fill_random(&mut out);
    out
}

fn fill_random(buf: &mut [u8]) {
    #[cfg(windows)]
    fill_random_windows(buf);
    #[cfg(unix)]
    fill_random_unix(buf);
}

#[cfg(windows)]
fn fill_random_windows(buf: &mut [u8]) {
    #[link(name = "advapi32")]
    extern "system" {
        #[link_name = "SystemFunction036"]
        fn rtl_gen_random(buf: *mut u8, len: u32) -> u8;
    }
    let ok = unsafe { rtl_gen_random(buf.as_mut_ptr(), buf.len() as u32) };
    if ok == 0 {
        let nanos = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .map(|d| d.as_nanos())
            .unwrap_or(1);
        for (i, b) in buf.iter_mut().enumerate() {
            *b = ((nanos >> ((i % 8) * 8)) as u8).wrapping_add(i as u8);
        }
    }
}

#[cfg(unix)]
fn fill_random_unix(buf: &mut [u8]) {
    if let Ok(mut f) = File::open("/dev/urandom") {
        let _ = f.read_exact(buf);
        return;
    }
    let nanos = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_nanos())
        .unwrap_or(1);
    for (i, b) in buf.iter_mut().enumerate() {
        *b = ((nanos >> ((i % 8) * 8)) as u8).wrapping_add(i as u8);
    }
}

pub struct WriteJob {
    pub temp: PathBuf,
    pub file: File,
    pub identity: Vec<u8>,
    pub sha: Sha256,
    pub bytes: u64,
}

impl WriteJob {
    pub fn write(&mut self, chunk: &[u8]) -> Result<(), FsError> {
        self.file.write_all(chunk).map_err(map_io)?;
        self.sha.update(chunk);
        self.bytes += chunk.len() as u64;
        Ok(())
    }

    pub fn finish_hash(self) -> Result<(PathBuf, Vec<u8>, String, u64, File), FsError> {
        let mut file = self.file;
        file.flush().map_err(map_io)?;
        let digest = crate::sha256::hex(&self.sha.finish());
        Ok((self.temp, self.identity, digest, self.bytes, file))
    }
}
