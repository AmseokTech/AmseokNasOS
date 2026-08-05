//--------------------------//
//--------隔离低权限 Web Terminal 的 PTY 生命周期---------//
//--------Isolates the low-privilege Web Terminal PTY lifecycle--------//
//-------------------------//
use std::env;
use std::fs;
use std::io::{self, Read, Write};
use std::os::unix::fs::{FileTypeExt, MetadataExt, PermissionsExt};
use std::os::unix::net::{UnixListener, UnixStream};
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicUsize, Ordering};
use std::sync::{Arc, Mutex};
use std::thread;
use std::time::Duration;

use nix::sys::socket::{getsockopt, sockopt::PeerCredentials};
use nix::unistd::{Uid, User};
use portable_pty::{CommandBuilder, PtySize, native_pty_system};
use serde::{Deserialize, Serialize};
use tracing::{error, info, warn};
use tracing_subscriber::EnvFilter;

const DEFAULT_SOCKET_PATH: &str = "/run/amseoknas-terminal/terminal.sock";
const DEFAULT_SHELL_PATH: &str = "/bin/bash";
const DEFAULT_WORKING_DIRECTORY: &str = "/var/lib/amseoknas-terminal";
const MAX_OPEN_FRAME_BYTES: usize = 4 * 1024;
const MAX_DATA_FRAME_BYTES: usize = 64 * 1024;
const MIN_COLUMNS: u16 = 20;
const MAX_COLUMNS: u16 = 300;
const MIN_ROWS: u16 = 5;
const MAX_ROWS: u16 = 120;

const CLIENT_STDIN: u8 = 0x01;
const CLIENT_RESIZE: u8 = 0x02;
const CLIENT_CLOSE: u8 = 0x03;
const SERVER_OPENED: u8 = 0x10;
const SERVER_STDOUT: u8 = 0x11;
const SERVER_EXITED: u8 = 0x12;
const SERVER_ERROR: u8 = 0x13;

static ACTIVE_SESSIONS: AtomicUsize = AtomicUsize::new(0);

#[derive(Clone, Debug)]
struct BrokerConfig {
    socket_path: PathBuf,
    allowed_uid: u32,
    shell_path: PathBuf,
    working_directory: PathBuf,
    max_sessions: usize,
}

impl BrokerConfig {
    fn from_environment() -> Result<Self, String> {
        let allowed_uid = match env::var("AMSEOKNAS_TERMINAL_ALLOWED_UID") {
            Ok(value) => value
                .parse::<u32>()
                .map_err(|_| "AMSEOKNAS_TERMINAL_ALLOWED_UID must be a numeric UID".to_owned())?,
            Err(_) => {
                let user_name = env::var("AMSEOKNAS_TERMINAL_ALLOWED_USER")
                    .unwrap_or_else(|_| "amseoknas-api".to_owned());
                User::from_name(&user_name)
                    .map_err(|error| format!("failed to resolve allowed API user: {error}"))?
                    .ok_or_else(|| format!("allowed API user does not exist: {user_name}"))?
                    .uid
                    .as_raw()
            }
        };
        let socket_path = env::var_os("AMSEOKNAS_TERMINAL_SOCKET")
            .map(PathBuf::from)
            .unwrap_or_else(|| PathBuf::from(DEFAULT_SOCKET_PATH));
        let shell_path = PathBuf::from(DEFAULT_SHELL_PATH);
        let working_directory = env::var_os("AMSEOKNAS_TERMINAL_WORKING_DIRECTORY")
            .map(PathBuf::from)
            .unwrap_or_else(|| PathBuf::from(DEFAULT_WORKING_DIRECTORY));
        let max_sessions = env::var("AMSEOKNAS_TERMINAL_MAX_SESSIONS")
            .ok()
            .map(|value| value.parse::<usize>())
            .transpose()
            .map_err(|_| "AMSEOKNAS_TERMINAL_MAX_SESSIONS must be numeric".to_owned())?
            .unwrap_or(4);

        if !shell_path.is_absolute() || !working_directory.is_absolute() {
            return Err("shell and working directory paths must be absolute".to_owned());
        }
        if !shell_path.is_file() {
            return Err(format!("shell does not exist: {}", shell_path.display()));
        }
        if !working_directory.is_dir() {
            return Err(format!(
                "working directory does not exist: {}",
                working_directory.display()
            ));
        }
        if !(1..=32).contains(&max_sessions) {
            return Err("max sessions must be between 1 and 32".to_owned());
        }

        Ok(Self {
            socket_path,
            allowed_uid,
            shell_path,
            working_directory,
            max_sessions,
        })
    }
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct OpenRequest {
    protocol_version: u16,
    session_id: String,
    profile: String,
    columns: u16,
    rows: u16,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct OpenedResponse<'a> {
    protocol_version: u16,
    session_id: &'a str,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct ExitResponse {
    exit_code: Option<u32>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct ErrorResponse<'a> {
    code: &'a str,
    message: &'a str,
}

struct SessionSlot;

impl SessionSlot {
    fn acquire(max_sessions: usize) -> Option<Self> {
        ACTIVE_SESSIONS
            .fetch_update(Ordering::AcqRel, Ordering::Acquire, |current| {
                (current < max_sessions).then_some(current + 1)
            })
            .ok()
            .map(|_| Self)
    }
}

impl Drop for SessionSlot {
    fn drop(&mut self) {
        ACTIVE_SESSIONS.fetch_sub(1, Ordering::AcqRel);
    }
}

fn main() {
    tracing_subscriber::fmt()
        .with_env_filter(
            EnvFilter::try_from_default_env().unwrap_or_else(|_| EnvFilter::new("info")),
        )
        .without_time()
        .init();

    if let Err(message) = run() {
        error!(error = %message, "terminal broker stopped");
        std::process::exit(1);
    }
}

fn run() -> Result<(), String> {
    let config = Arc::new(BrokerConfig::from_environment()?);
    prepare_socket_path(&config.socket_path)?;
    let listener = UnixListener::bind(&config.socket_path)
        .map_err(|error| format!("failed to bind terminal socket: {error}"))?;
    fs::set_permissions(&config.socket_path, fs::Permissions::from_mode(0o660))
        .map_err(|error| format!("failed to protect terminal socket: {error}"))?;

    info!(socket = %config.socket_path.display(), "terminal broker listening");
    for connection in listener.incoming() {
        let stream = match connection {
            Ok(stream) => stream,
            Err(error) => {
                warn!(error = %error, "failed to accept terminal connection");
                continue;
            }
        };
        let config = Arc::clone(&config);
        thread::spawn(move || {
            if let Err(error) = handle_connection(stream, &config) {
                warn!(error = %error, "terminal connection rejected or closed");
            }
        });
    }

    Ok(())
}

fn prepare_socket_path(path: &Path) -> Result<(), String> {
    let parent = path
        .parent()
        .ok_or_else(|| "terminal socket requires a parent directory".to_owned())?;
    if !parent.is_dir() {
        return Err(format!(
            "terminal socket directory does not exist: {}",
            parent.display()
        ));
    }
    if !path.exists() {
        return Ok(());
    }
    if UnixStream::connect(path).is_ok() {
        return Err("another terminal broker is already listening".to_owned());
    }

    let metadata = fs::symlink_metadata(path)
        .map_err(|error| format!("failed to inspect stale terminal socket: {error}"))?;
    if !metadata.file_type().is_socket() || metadata.uid() != Uid::effective().as_raw() {
        return Err("refusing to replace a terminal socket not owned by this service".to_owned());
    }

    fs::remove_file(path).map_err(|error| format!("failed to remove stale socket: {error}"))
}

fn handle_connection(mut stream: UnixStream, config: &BrokerConfig) -> Result<(), String> {
    stream
        .set_read_timeout(Some(Duration::from_secs(10)))
        .map_err(|error| error.to_string())?;
    let credentials = getsockopt(&stream, PeerCredentials).map_err(|error| error.to_string())?;
    if credentials.uid() != config.allowed_uid {
        return Err(format!("peer UID {} is not allowed", credentials.uid()));
    }
    let _slot = SessionSlot::acquire(config.max_sessions)
        .ok_or_else(|| "terminal session capacity reached".to_owned())?;

    let open_payload = read_sized_payload(&mut stream, MAX_OPEN_FRAME_BYTES)
        .map_err(|error| format!("invalid open frame: {error}"))?;
    let request: OpenRequest = serde_json::from_slice(&open_payload)
        .map_err(|error| format!("invalid open request: {error}"))?;
    validate_open_request(&request)?;

    stream
        .set_read_timeout(None)
        .map_err(|error| error.to_string())?;
    run_terminal_session(stream, request, config)
}

fn validate_open_request(request: &OpenRequest) -> Result<(), String> {
    if request.protocol_version != 1 {
        return Err("unsupported terminal protocol version".to_owned());
    }
    if request.profile != "maintenance" {
        return Err("unknown terminal profile".to_owned());
    }
    if request.session_id.len() != 36
        || !request
            .session_id
            .bytes()
            .all(|value| value.is_ascii_hexdigit() || value == b'-')
    {
        return Err("session ID must be a canonical UUID".to_owned());
    }
    validate_size(request.columns, request.rows)
}

fn validate_size(columns: u16, rows: u16) -> Result<(), String> {
    if !(MIN_COLUMNS..=MAX_COLUMNS).contains(&columns) || !(MIN_ROWS..=MAX_ROWS).contains(&rows) {
        return Err("terminal dimensions are outside the allowed range".to_owned());
    }
    Ok(())
}

fn run_terminal_session(
    stream: UnixStream,
    request: OpenRequest,
    config: &BrokerConfig,
) -> Result<(), String> {
    let pty_system = native_pty_system();
    let pair = pty_system
        .openpty(pty_size(request.columns, request.rows))
        .map_err(|error| format!("failed to open PTY: {error}"))?;

    let mut command = CommandBuilder::new(&config.shell_path);
    command.arg("--noprofile");
    command.arg("--norc");
    command.cwd(&config.working_directory);
    command.env_clear();
    command.env("HOME", &config.working_directory);
    command.env("LANG", "C.UTF-8");
    command.env("PATH", "/usr/local/bin:/usr/bin:/bin");
    command.env("SHELL", &config.shell_path);
    command.env("TERM", "xterm-256color");
    command.env("HISTFILE", "/dev/null");
    command.umask(Some(0o077));

    let child = pair
        .slave
        .spawn_command(command)
        .map_err(|error| format!("failed to start terminal shell: {error}"))?;
    drop(pair.slave);

    let mut pty_reader = pair
        .master
        .try_clone_reader()
        .map_err(|error| format!("failed to read PTY: {error}"))?;
    let mut pty_writer = pair
        .master
        .take_writer()
        .map_err(|error| format!("failed to write PTY: {error}"))?;
    let socket_writer =
        Arc::new(Mutex::new(stream.try_clone().map_err(|error| {
            format!("failed to clone terminal socket: {error}")
        })?));
    let child = Arc::new(Mutex::new(child));

    write_json_frame(
        &socket_writer,
        SERVER_OPENED,
        &OpenedResponse {
            protocol_version: 1,
            session_id: &request.session_id,
        },
    )?;

    let output_socket = Arc::clone(&socket_writer);
    let output_thread = thread::spawn(move || {
        let mut buffer = vec![0_u8; 16 * 1024];
        loop {
            match pty_reader.read(&mut buffer) {
                Ok(0) => break,
                Ok(count) => {
                    if write_frame_locked(&output_socket, SERVER_STDOUT, &buffer[..count]).is_err()
                    {
                        break;
                    }
                }
                Err(error) if error.kind() == io::ErrorKind::Interrupted => continue,
                Err(_) => break,
            }
        }
        let _ = write_json_frame(
            &output_socket,
            SERVER_EXITED,
            &ExitResponse { exit_code: None },
        );
        if let Ok(socket) = output_socket.lock() {
            let _ = socket.shutdown(std::net::Shutdown::Both);
        }
    });

    let mut socket_reader = stream;
    loop {
        let (frame_type, payload) = match read_typed_frame(&mut socket_reader) {
            Ok(frame) => frame,
            Err(error)
                if matches!(
                    error.kind(),
                    io::ErrorKind::UnexpectedEof
                        | io::ErrorKind::ConnectionReset
                        | io::ErrorKind::BrokenPipe
                ) =>
            {
                break;
            }
            Err(error) => {
                let _ = write_json_frame(
                    &socket_writer,
                    SERVER_ERROR,
                    &ErrorResponse {
                        code: "terminal.invalid_frame",
                        message: "Terminal protocol frame is invalid",
                    },
                );
                return Err(format!("failed to read terminal frame: {error}"));
            }
        };

        match frame_type {
            CLIENT_STDIN => pty_writer
                .write_all(&payload)
                .map_err(|error| format!("failed to write terminal input: {error}"))?,
            CLIENT_RESIZE if payload.len() == 4 => {
                let columns = u16::from_be_bytes([payload[0], payload[1]]);
                let rows = u16::from_be_bytes([payload[2], payload[3]]);
                validate_size(columns, rows)?;
                pair.master
                    .resize(pty_size(columns, rows))
                    .map_err(|error| format!("failed to resize PTY: {error}"))?;
            }
            CLIENT_CLOSE if payload.is_empty() => break,
            _ => return Err("unknown or malformed terminal frame".to_owned()),
        }
    }

    drop(pty_writer);
    if let Ok(mut child) = child.lock() {
        let _ = child.kill();
        let _ = child.wait();
    }
    let _ = output_thread.join();
    info!(session_id = %request.session_id, "terminal session closed");
    Ok(())
}

fn pty_size(columns: u16, rows: u16) -> PtySize {
    PtySize {
        rows,
        cols: columns,
        pixel_width: 0,
        pixel_height: 0,
    }
}

fn read_sized_payload(stream: &mut UnixStream, max_bytes: usize) -> io::Result<Vec<u8>> {
    let mut length = [0_u8; 4];
    stream.read_exact(&mut length)?;
    let length = u32::from_be_bytes(length) as usize;
    if length == 0 || length > max_bytes {
        return Err(io::Error::new(
            io::ErrorKind::InvalidData,
            "frame length is outside the allowed range",
        ));
    }
    let mut payload = vec![0_u8; length];
    stream.read_exact(&mut payload)?;
    Ok(payload)
}

fn read_typed_frame(stream: &mut UnixStream) -> io::Result<(u8, Vec<u8>)> {
    let payload = read_sized_payload(stream, MAX_DATA_FRAME_BYTES + 1)?;
    Ok((payload[0], payload[1..].to_vec()))
}

fn write_json_frame<T: Serialize>(
    stream: &Arc<Mutex<UnixStream>>,
    frame_type: u8,
    value: &T,
) -> Result<(), String> {
    let payload = serde_json::to_vec(value).map_err(|error| error.to_string())?;
    write_frame_locked(stream, frame_type, &payload).map_err(|error| error.to_string())
}

fn write_frame_locked(
    stream: &Arc<Mutex<UnixStream>>,
    frame_type: u8,
    payload: &[u8],
) -> io::Result<()> {
    if payload.len() > MAX_DATA_FRAME_BYTES {
        return Err(io::Error::new(
            io::ErrorKind::InvalidInput,
            "frame exceeds terminal protocol limit",
        ));
    }
    let length = u32::try_from(payload.len() + 1)
        .map_err(|_| io::Error::new(io::ErrorKind::InvalidInput, "frame is too large"))?;
    let mut stream = stream
        .lock()
        .map_err(|_| io::Error::other("terminal socket lock is poisoned"))?;
    stream.write_all(&length.to_be_bytes())?;
    stream.write_all(&[frame_type])?;
    stream.write_all(payload)?;
    stream.flush()
}

#[cfg(test)]
mod tests {
    use super::*;

    fn request(columns: u16, rows: u16) -> OpenRequest {
        OpenRequest {
            protocol_version: 1,
            session_id: "0190f6f4-7de8-7000-8000-000000000001".to_owned(),
            profile: "maintenance".to_owned(),
            columns,
            rows,
        }
    }

    #[test]
    fn accepts_the_registered_profile_and_safe_dimensions() {
        assert!(validate_open_request(&request(120, 32)).is_ok());
    }

    #[test]
    fn rejects_dimensions_that_could_exhaust_the_terminal_renderer() {
        assert!(validate_open_request(&request(301, 32)).is_err());
        assert!(validate_open_request(&request(120, 121)).is_err());
    }

    #[test]
    fn rejects_unknown_profiles_and_protocol_versions() {
        let mut unknown_profile = request(120, 32);
        unknown_profile.profile = "root".to_owned();
        assert!(validate_open_request(&unknown_profile).is_err());

        let mut unknown_version = request(120, 32);
        unknown_version.protocol_version = 2;
        assert!(validate_open_request(&unknown_version).is_err());
    }

    #[test]
    fn relays_a_real_low_privilege_pty_session() {
        let (server, mut client) = UnixStream::pair().expect("create socket pair");
        client
            .set_read_timeout(Some(Duration::from_secs(5)))
            .expect("set timeout");
        let config = BrokerConfig {
            socket_path: PathBuf::from("/tmp/unused-terminal.sock"),
            allowed_uid: Uid::effective().as_raw(),
            shell_path: PathBuf::from(DEFAULT_SHELL_PATH),
            working_directory: PathBuf::from("/tmp"),
            max_sessions: 1,
        };
        let session = request(80, 24);
        let broker = thread::spawn(move || run_terminal_session(server, session, &config));

        let (opened_type, _) = read_typed_frame(&mut client).expect("read opened frame");
        assert_eq!(SERVER_OPENED, opened_type);

        let writer = Arc::new(Mutex::new(client.try_clone().expect("clone client")));
        write_frame_locked(
            &writer,
            CLIENT_STDIN,
            b"printf '__AMSEOKNAS_TERMINAL_OK__\\n'\r\nexit\r\n",
        )
        .expect("write terminal command");

        let mut output = Vec::new();
        loop {
            let (frame_type, payload) = read_typed_frame(&mut client).expect("read broker frame");
            match frame_type {
                SERVER_STDOUT => output.extend(payload),
                SERVER_EXITED => break,
                other => panic!("unexpected broker frame {other}"),
            }
        }

        assert!(
            String::from_utf8_lossy(&output).contains("__AMSEOKNAS_TERMINAL_OK__"),
            "PTY output did not contain the expected marker"
        );
        assert!(broker.join().expect("join broker session").is_ok());
    }
}
