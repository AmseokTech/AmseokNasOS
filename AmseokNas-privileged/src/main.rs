//--------------------------//
//--------只向登记的 API 身份开放受限系统查询与网络配置动作---------//
//--------Exposes constrained system queries and network configuration actions only to the registered API identity--------//
//-------------------------//
mod inventory;
mod network_write;
mod pending_changes;
mod protocol;

use std::env;
use std::fs;
use std::io;
use std::os::unix::fs::{FileTypeExt, MetadataExt, PermissionsExt};
use std::os::unix::net::{UnixListener, UnixStream};
use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::time::Duration;

use nix::sys::socket::{getsockopt, sockopt::PeerCredentials};
use tracing::{error, info, warn};
use tracing_subscriber::EnvFilter;

use network_write::NetworkWriteEnvironment;
use pending_changes::{PendingChangeRegistry, SharedPendingChangeRegistry};

const DEFAULT_SOCKET_PATH: &str = "/run/amseoknas/privileged.sock";
const CONNECTION_TIMEOUT: Duration = Duration::from_secs(5);

fn main() -> io::Result<()> {
    tracing_subscriber::fmt()
        .with_env_filter(
            EnvFilter::try_from_default_env().unwrap_or_else(|_| EnvFilter::new("info")),
        )
        .with_target(false)
        .init();

    let allowed_uid = required_allowed_uid()?;
    let socket_path = env::var_os("AMSEOKNAS_PRIVILEGED_SOCKET_PATH")
        .map(PathBuf::from)
        .unwrap_or_else(|| PathBuf::from(DEFAULT_SOCKET_PATH));
    let listener = bind_listener(&socket_path)?;

    // 待确认登记表与写入环境在启动时各建一份，随后被所有连接共享
    // 登记表必须只有一份：分散成多份会让看守线程看不到部分待确认改动
    let registry = PendingChangeRegistry::new_shared();
    let mut environment = NetworkWriteEnvironment::from_environment();

    // 看守线程起不来时只降级、不退出：
    // 只读查询本身仍然可用，直接中止会让整机连状态都查不到
    // 自动回滚失效时必须同时关闭新的应用动作，不能留下无人看守的生效配置
    match pending_changes::spawn_watcher(Arc::clone(&registry), environment.clone()) {
        Ok(_) => info!("pending network change watcher thread spawned"),
        Err(error) => {
            environment.disable_new_applications();
            error!(
                %error,
                "failed to spawn the pending network change watcher; \
                 read-only queries keep serving while new network applications are disabled"
            );
        }
    }

    info!(
        socket = %socket_path.display(),
        allowed_uid,
        "privileged query daemon started"
    );

    for connection in listener.incoming() {
        match connection {
            Ok(mut stream) => {
                if let Err(error) = handle_client(&mut stream, allowed_uid, &registry, &environment)
                {
                    warn!(%error, "privileged query request failed");
                }
            }
            Err(error) => error!(%error, "failed to accept privileged query connection"),
        }
    }
    Ok(())
}

fn handle_client(
    stream: &mut UnixStream,
    allowed_uid: u32,
    registry: &SharedPendingChangeRegistry,
    environment: &NetworkWriteEnvironment,
) -> io::Result<()> {
    stream.set_read_timeout(Some(CONNECTION_TIMEOUT))?;
    stream.set_write_timeout(Some(CONNECTION_TIMEOUT))?;
    let credentials = getsockopt(&*stream, PeerCredentials).map_err(io::Error::other)?;
    // 身份校验不通过必须在触及协议层之前返回，写入动作同样受这道闸门保护
    if credentials.uid() != allowed_uid {
        return Err(io::Error::new(
            io::ErrorKind::PermissionDenied,
            "unix peer uid is not allowed",
        ));
    }
    protocol::handle_connection(stream, registry, environment)
}

fn bind_listener(socket_path: &Path) -> io::Result<UnixListener> {
    let parent = socket_path.parent().ok_or_else(|| {
        io::Error::new(
            io::ErrorKind::InvalidInput,
            "socket path has no parent directory",
        )
    })?;
    let parent_metadata = fs::symlink_metadata(parent)?;
    if !parent_metadata.file_type().is_dir()
        || parent_metadata.file_type().is_symlink()
        || parent_metadata.uid() != nix::unistd::geteuid().as_raw()
        || parent_metadata.mode() & 0o022 != 0
    {
        return Err(io::Error::new(
            io::ErrorKind::PermissionDenied,
            "socket parent directory must be owned by the daemon and not be writable by other users",
        ));
    }

    if let Ok(metadata) = fs::symlink_metadata(socket_path) {
        if !metadata.file_type().is_socket() || metadata.uid() != nix::unistd::geteuid().as_raw() {
            return Err(io::Error::new(
                io::ErrorKind::AlreadyExists,
                "socket path is not an owned unix socket",
            ));
        }
        if UnixStream::connect(socket_path).is_ok() {
            return Err(io::Error::new(
                io::ErrorKind::AddrInUse,
                "another privileged daemon is already listening",
            ));
        }
        fs::remove_file(socket_path)?;
    }

    let listener = UnixListener::bind(socket_path)?;
    fs::set_permissions(socket_path, fs::Permissions::from_mode(0o660))?;
    Ok(listener)
}

fn required_allowed_uid() -> io::Result<u32> {
    parse_allowed_uid(env::var("AMSEOKNAS_PRIVILEGED_ALLOWED_UID").ok())
}

fn parse_allowed_uid(value: Option<String>) -> io::Result<u32> {
    let value = value.ok_or_else(|| {
        io::Error::new(
            io::ErrorKind::InvalidInput,
            "AMSEOKNAS_PRIVILEGED_ALLOWED_UID is required",
        )
    })?;
    value.parse::<u32>().map_err(|_| {
        io::Error::new(
            io::ErrorKind::InvalidInput,
            "AMSEOKNAS_PRIVILEGED_ALLOWED_UID must be an unsigned integer",
        )
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn rejects_a_missing_allowed_uid_instead_of_defaulting_to_everyone() {
        assert_eq!(
            parse_allowed_uid(None).unwrap_err().kind(),
            io::ErrorKind::InvalidInput
        );
    }

    #[test]
    fn accepts_an_explicit_numeric_allowed_uid() {
        assert_eq!(parse_allowed_uid(Some("1001".to_owned())).unwrap(), 1001);
    }
}
