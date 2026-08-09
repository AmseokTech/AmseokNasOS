//--------------------------//
//--------执行受限 ext4 数据卷、UUID 挂载、权限、校验与 SMB/NFS 配置---------//
//--------Executes constrained ext4 volumes, UUID mounts, permissions, verification, and shares--------//
//-------------------------//
use std::env;
use std::fs::{self, File, OpenOptions};
use std::io::{self, Read, Write};
use std::os::unix::fs::{OpenOptionsExt, PermissionsExt};
use std::path::{Path, PathBuf};
use std::process::{Command, ExitStatus, Stdio};
use std::thread;
use std::time::{Duration, Instant};

use nix::unistd::{Gid, Group, Uid, User, chown};
use serde::{Deserialize, Serialize};

use crate::inventory::raid;
use crate::storage_registry::StorageOperationRegistry;

const DEFAULT_REGISTRY_PATH: &str = "/var/lib/amseoknas/storage/operations.json";
const DEFAULT_DESCRIPTOR_DIRECTORY: &str = "/var/lib/amseoknas/storage/volumes";
const DEFAULT_VOLUME_ROOT: &str = "/srv/amseoknas/volumes";
const DEFAULT_UNIT_DIRECTORY: &str = "/etc/systemd/system";
const DEFAULT_SAMBA_CONFIG: &str = "/etc/samba/smb.conf";
const DEFAULT_SAMBA_INCLUDE_DIRECTORY: &str = "/etc/samba/smb.conf.d";
const DEFAULT_NFS_EXPORT_DIRECTORY: &str = "/etc/exports.d";
const TOOL_TIMEOUT: Duration = Duration::from_secs(50);
const MAXIMUM_TOOL_OUTPUT: usize = 64 * 1024;

#[derive(Clone, Copy, Debug)]
pub enum StorageAction {
    ProvisionVolume,
    UpdatePermissions,
    ConfigureShares,
    VerifyReadWrite,
}

#[derive(Clone, Debug, Deserialize, Serialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct SmbShareSettings {
    pub enabled: bool,
    pub share_name: Option<String>,
    pub read_only: bool,
    pub guest_access: bool,
    pub allowed_network: Option<String>,
}

impl Default for SmbShareSettings {
    fn default() -> Self {
        Self {
            enabled: false,
            share_name: None,
            read_only: true,
            guest_access: false,
            allowed_network: None,
        }
    }
}

#[derive(Clone, Debug, Deserialize, Serialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct NfsShareSettings {
    pub enabled: bool,
    pub client_network: Option<String>,
    pub read_only: bool,
}

impl Default for NfsShareSettings {
    fn default() -> Self {
        Self {
            enabled: false,
            client_network: None,
            read_only: true,
        }
    }
}

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct StorageExecutionParameters {
    pub(crate) operation_id: String,
    pub(crate) idempotency_key: String,
    pub(crate) fencing_token: i64,
    array_id: Option<String>,
    volume_id: Option<String>,
    volume_name: Option<String>,
    owner_name: Option<String>,
    group_name: Option<String>,
    directory_mode: Option<String>,
    smb: Option<SmbShareSettings>,
    nfs: Option<NfsShareSettings>,
    pub(crate) snapshot_fingerprint: String,
}

#[derive(Clone, Debug, Deserialize, Serialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub struct ManagedVolumeInformation {
    pub id: String,
    pub name: String,
    pub array_id: String,
    pub array_path: String,
    pub file_system_uuid: String,
    pub file_system_type: String,
    pub mount_path: String,
    pub mounted: bool,
    pub persistent_mount_enabled: bool,
    pub owner_name: String,
    pub group_name: String,
    pub directory_mode: String,
    pub read_write_verified: bool,
    pub smb: SmbShareSettings,
    pub nfs: NfsShareSettings,
}

#[derive(Debug)]
pub struct StorageWriteError {
    pub code: &'static str,
    pub message: String,
    pub retryable: bool,
}

impl StorageWriteError {
    pub(crate) fn new(code: &'static str, message: impl Into<String>, retryable: bool) -> Self {
        Self {
            code,
            message: message.into(),
            retryable,
        }
    }

    pub fn unavailable(message: impl Into<String>) -> Self {
        Self::new("storage.write_unavailable", message, true)
    }
}

pub struct StorageWriteContext {
    mkfs_ext4_path: PathBuf,
    blkid_path: PathBuf,
    systemctl_path: PathBuf,
    systemd_analyze_path: PathBuf,
    testparm_path: PathBuf,
    exportfs_path: PathBuf,
    descriptor_directory: PathBuf,
    volume_root: PathBuf,
    unit_directory: PathBuf,
    samba_config: PathBuf,
    samba_include_directory: PathBuf,
    nfs_export_directory: PathBuf,
    registry: StorageOperationRegistry,
}

impl StorageWriteContext {
    pub fn from_environment() -> Result<Self, StorageWriteError> {
        let required = |variable, candidates: &[&str]| {
            find_tool(variable, candidates)
                .ok_or_else(|| StorageWriteError::unavailable(format!("缺少受支持工具 {variable}")))
        };
        let descriptor_directory = environment_path(
            "AMSEOKNAS_STORAGE_DESCRIPTOR_DIRECTORY",
            DEFAULT_DESCRIPTOR_DIRECTORY,
        );
        let registry = StorageOperationRegistry::open(environment_path(
            "AMSEOKNAS_STORAGE_OPERATION_REGISTRY_PATH",
            DEFAULT_REGISTRY_PATH,
        ))
        .map_err(|error| StorageWriteError::unavailable(error.to_string()))?;
        Ok(Self {
            mkfs_ext4_path: required(
                "AMSEOKNAS_MKFS_EXT4_PATH",
                &["/usr/sbin/mkfs.ext4", "/sbin/mkfs.ext4"],
            )?,
            blkid_path: required("AMSEOKNAS_BLKID_PATH", &["/usr/sbin/blkid", "/sbin/blkid"])?,
            systemctl_path: required(
                "AMSEOKNAS_SYSTEMCTL_PATH",
                &["/usr/bin/systemctl", "/bin/systemctl"],
            )?,
            systemd_analyze_path: required(
                "AMSEOKNAS_SYSTEMD_ANALYZE_PATH",
                &["/usr/bin/systemd-analyze", "/bin/systemd-analyze"],
            )?,
            testparm_path: required(
                "AMSEOKNAS_TESTPARM_PATH",
                &["/usr/bin/testparm", "/usr/sbin/testparm"],
            )?,
            exportfs_path: required(
                "AMSEOKNAS_EXPORTFS_PATH",
                &["/usr/sbin/exportfs", "/sbin/exportfs"],
            )?,
            descriptor_directory,
            volume_root: environment_path("AMSEOKNAS_VOLUME_ROOT", DEFAULT_VOLUME_ROOT),
            unit_directory: environment_path(
                "AMSEOKNAS_MOUNT_UNIT_DIRECTORY",
                DEFAULT_UNIT_DIRECTORY,
            ),
            samba_config: environment_path("AMSEOKNAS_SAMBA_CONFIG", DEFAULT_SAMBA_CONFIG),
            samba_include_directory: environment_path(
                "AMSEOKNAS_SAMBA_INCLUDE_DIRECTORY",
                DEFAULT_SAMBA_INCLUDE_DIRECTORY,
            ),
            nfs_export_directory: environment_path(
                "AMSEOKNAS_NFS_EXPORT_DIRECTORY",
                DEFAULT_NFS_EXPORT_DIRECTORY,
            ),
            registry,
        })
    }

    pub fn execute(
        &self,
        action: StorageAction,
        parameters: StorageExecutionParameters,
    ) -> Result<ManagedVolumeInformation, StorageWriteError> {
        validate_common(action, &parameters)?;
        if let Some(result) = self.registry.replay(&parameters)? {
            return Ok(result);
        }
        let prepared = self.prepare(action, &parameters)?;
        self.registry.begin(&parameters)?;
        let result = self.perform(prepared, &parameters).map_err(|error| {
            StorageWriteError::new(
                "operation.duplicate_requires_reconciliation",
                format!("数据卷命令已开始但结果需要复核：{}", error.message),
                true,
            )
        })?;
        self.registry
            .complete(&parameters, &result)
            .map_err(|error| {
                StorageWriteError::new(
                    "operation.duplicate_requires_reconciliation",
                    format!("数据卷命令已完成但结果登记失败：{}", error.message),
                    true,
                )
            })?;
        Ok(result)
    }

    fn prepare(
        &self,
        action: StorageAction,
        parameters: &StorageExecutionParameters,
    ) -> Result<PreparedAction, StorageWriteError> {
        match action {
            StorageAction::ProvisionVolume => {
                let array_id = parameters.array_id.as_deref().ok_or_else(invalid_request)?;
                let arrays = raid::inspect_arrays().map_err(inventory_error)?;
                let array = arrays
                    .into_iter()
                    .find(|candidate| candidate.id == array_id)
                    .ok_or_else(|| {
                        StorageWriteError::new("resource.not_found", "目标 RAID 阵列不存在", false)
                    })?;
                if array.uuid.is_none()
                    || array.degraded_device_count != 0
                    || array.sync_action != "idle"
                {
                    return Err(StorageWriteError::new(
                        "resource.busy",
                        "阵列身份不稳定、已降级或仍在同步",
                        false,
                    ));
                }
                ensure_no_signature(&self.blkid_path, &array.path)?;
                if self
                    .read_descriptors()?
                    .iter()
                    .any(|volume| volume.array_id == array.id)
                {
                    return Err(StorageWriteError::new(
                        "storage.array_already_managed",
                        "阵列已经属于受管数据卷",
                        false,
                    ));
                }
                Ok(PreparedAction::Provision {
                    array_id: array.id,
                    array_path: array.path,
                })
            }
            StorageAction::UpdatePermissions
            | StorageAction::ConfigureShares
            | StorageAction::VerifyReadWrite => {
                let volume_id = parameters
                    .volume_id
                    .as_deref()
                    .ok_or_else(invalid_request)?;
                let volume = self
                    .read_descriptors()?
                    .into_iter()
                    .find(|candidate| candidate.id == volume_id)
                    .ok_or_else(|| {
                        StorageWriteError::new("resource.not_found", "目标受管数据卷不存在", false)
                    })?;
                if !is_mounted(Path::new(&volume.mount_path))? {
                    return Err(StorageWriteError::new(
                        "storage.mount_failed",
                        "目标数据卷当前未挂载",
                        true,
                    ));
                }
                Ok(PreparedAction::Existing {
                    volume: Box::new(volume),
                })
            }
        }
    }

    fn perform(
        &self,
        prepared: PreparedAction,
        parameters: &StorageExecutionParameters,
    ) -> Result<ManagedVolumeInformation, StorageWriteError> {
        match prepared {
            PreparedAction::Provision {
                array_id,
                array_path,
            } => self.provision(array_id, array_path, parameters),
            PreparedAction::Existing { volume } => {
                let mut volume = *volume;
                if parameters.owner_name.is_some() {
                    self.apply_permissions(&mut volume, parameters)?;
                }
                if parameters.smb.is_some() || parameters.nfs.is_some() {
                    self.apply_shares(&mut volume, parameters)?;
                }
                if parameters.owner_name.is_none()
                    && parameters.smb.is_none()
                    && parameters.nfs.is_none()
                {
                    verify_read_write(Path::new(&volume.mount_path), &parameters.operation_id)?;
                    volume.read_write_verified = true;
                }
                self.persist_descriptor(&volume)?;
                Ok(self.refresh_volume(volume)?)
            }
        }
    }

    fn provision(
        &self,
        array_id: String,
        array_path: String,
        parameters: &StorageExecutionParameters,
    ) -> Result<ManagedVolumeInformation, StorageWriteError> {
        let name = parameters
            .volume_name
            .as_deref()
            .ok_or_else(invalid_request)?;
        run_tool(
            &self.mkfs_ext4_path,
            &[
                "-F".to_owned(),
                "-t".to_owned(),
                "ext4".to_owned(),
                "-L".to_owned(),
                format!("amseok-{name}"),
                array_path.clone(),
            ],
        )?;
        let uuid = tool_output(
            &self.blkid_path,
            &[
                "-s".to_owned(),
                "UUID".to_owned(),
                "-o".to_owned(),
                "value".to_owned(),
                array_path.clone(),
            ],
        )?
        .trim()
        .to_ascii_lowercase();
        if !valid_uuid(&uuid) {
            return Err(StorageWriteError::new(
                "storage.verification_failed",
                "ext4 UUID 复核失败",
                false,
            ));
        }
        let mount_path = self.volume_root.join(name);
        create_secure_directory(&mount_path, 0o700)?;
        self.install_mount_unit(name, &uuid, &mount_path)?;
        if !is_mounted(&mount_path)? {
            return Err(StorageWriteError::new(
                "storage.mount_failed",
                "systemd 返回成功但数据卷未挂载",
                true,
            ));
        }
        let mut volume = ManagedVolumeInformation {
            id: format!("volume:{uuid}"),
            name: name.to_owned(),
            array_id,
            array_path,
            file_system_uuid: uuid,
            file_system_type: "ext4".to_owned(),
            mount_path: mount_path.to_string_lossy().into_owned(),
            mounted: true,
            persistent_mount_enabled: true,
            owner_name: parameters.owner_name.clone().ok_or_else(invalid_request)?,
            group_name: parameters.group_name.clone().ok_or_else(invalid_request)?,
            directory_mode: parameters
                .directory_mode
                .clone()
                .ok_or_else(invalid_request)?,
            read_write_verified: false,
            smb: SmbShareSettings::default(),
            nfs: NfsShareSettings::default(),
        };
        self.set_permissions(&volume)?;
        verify_read_write(&mount_path, &parameters.operation_id)?;
        volume.read_write_verified = true;
        self.persist_descriptor(&volume)?;
        self.apply_shares(&mut volume, parameters)?;
        self.persist_descriptor(&volume)?;
        self.refresh_volume(volume)
    }

    fn apply_permissions(
        &self,
        volume: &mut ManagedVolumeInformation,
        parameters: &StorageExecutionParameters,
    ) -> Result<(), StorageWriteError> {
        volume.owner_name = parameters.owner_name.clone().ok_or_else(invalid_request)?;
        volume.group_name = parameters.group_name.clone().ok_or_else(invalid_request)?;
        volume.directory_mode = parameters
            .directory_mode
            .clone()
            .ok_or_else(invalid_request)?;
        self.set_permissions(volume)?;
        verify_read_write(Path::new(&volume.mount_path), &parameters.operation_id)?;
        volume.read_write_verified = true;
        Ok(())
    }

    fn set_permissions(&self, volume: &ManagedVolumeInformation) -> Result<(), StorageWriteError> {
        let user = User::from_name(&volume.owner_name)
            .map_err(io_error)?
            .ok_or_else(|| {
                StorageWriteError::new("storage.permission_failed", "目录所有者不存在", false)
            })?;
        let group = Group::from_name(&volume.group_name)
            .map_err(io_error)?
            .ok_or_else(|| {
                StorageWriteError::new("storage.permission_failed", "目录组不存在", false)
            })?;
        let mode = u32::from_str_radix(&volume.directory_mode, 8).map_err(|_| invalid_request())?;
        let path = Path::new(&volume.mount_path);
        reject_symlink(path)?;
        chown(
            path,
            Some(Uid::from_raw(user.uid.as_raw())),
            Some(Gid::from_raw(group.gid.as_raw())),
        )
        .map_err(|error| {
            StorageWriteError::new("storage.permission_failed", error.to_string(), false)
        })?;
        fs::set_permissions(path, fs::Permissions::from_mode(mode)).map_err(|error| {
            StorageWriteError::new("storage.permission_failed", error.to_string(), false)
        })
    }

    fn install_mount_unit(
        &self,
        name: &str,
        uuid: &str,
        mount_path: &Path,
    ) -> Result<(), StorageWriteError> {
        fs::create_dir_all(&self.unit_directory).map_err(io_error)?;
        let unit_name = mount_unit_name(name);
        let unit_path = self.unit_directory.join(&unit_name);
        let content = mount_unit_content(name, uuid, mount_path);
        atomic_write(&unit_path, content.as_bytes(), 0o644)?;
        run_tool(
            &self.systemd_analyze_path,
            &[
                "verify".to_owned(),
                unit_path.to_string_lossy().into_owned(),
            ],
        )?;
        run_tool(&self.systemctl_path, &["daemon-reload".to_owned()])?;
        run_tool(
            &self.systemctl_path,
            &["enable".to_owned(), "--now".to_owned(), unit_name],
        )
        .map_err(|error| StorageWriteError::new("storage.mount_failed", error.message, true))
    }

    fn apply_shares(
        &self,
        volume: &mut ManagedVolumeInformation,
        parameters: &StorageExecutionParameters,
    ) -> Result<(), StorageWriteError> {
        let smb = parameters.smb.clone().unwrap_or_default();
        let nfs = parameters.nfs.clone().unwrap_or_default();
        validate_share_settings(&smb, &nfs)?;
        if smb.enabled && smb.guest_access && !smb.read_only && volume.directory_mode != "0777" {
            return Err(StorageWriteError::new(
                "request.invalid",
                "匿名 SMB 写入要求目录权限为 0777",
                false,
            ));
        }
        let smb_path = self
            .samba_include_directory
            .join(format!("amseokos-{}.conf", volume.name));
        let nfs_path = self
            .nfs_export_directory
            .join(format!("amseokos-{}.exports", volume.name));
        let previous_main = read_optional(&self.samba_config)?;
        let previous_smb = read_optional(&smb_path)?;
        let previous_nfs = read_optional(&nfs_path)?;

        let result = (|| {
            if smb.enabled {
                fs::create_dir_all(&self.samba_include_directory).map_err(io_error)?;
                ensure_samba_include(&self.samba_config, &smb_path)?;
                atomic_write(&smb_path, smb_config(volume, &smb).as_bytes(), 0o640)?;
                run_tool(
                    &self.testparm_path,
                    &[
                        "-s".to_owned(),
                        self.samba_config.to_string_lossy().into_owned(),
                    ],
                )
                .map_err(share_validation_error)?;
                run_tool(
                    &self.systemctl_path,
                    &[
                        "enable".to_owned(),
                        "--now".to_owned(),
                        "smbd.service".to_owned(),
                    ],
                )?;
                run_tool(
                    &self.systemctl_path,
                    &["reload".to_owned(), "smbd.service".to_owned()],
                )?;
            } else {
                remove_regular_file_if_exists(&smb_path)?;
                if self.samba_config.exists() {
                    remove_samba_include(&self.samba_config, &smb_path)?;
                    run_tool(
                        &self.testparm_path,
                        &[
                            "-s".to_owned(),
                            self.samba_config.to_string_lossy().into_owned(),
                        ],
                    )
                    .map_err(share_validation_error)?;
                    let _ = run_tool(
                        &self.systemctl_path,
                        &["reload".to_owned(), "smbd.service".to_owned()],
                    );
                }
            }

            fs::create_dir_all(&self.nfs_export_directory).map_err(io_error)?;
            if nfs.enabled {
                atomic_write(&nfs_path, nfs_config(volume, &nfs).as_bytes(), 0o640)?;
                run_tool(
                    &self.systemctl_path,
                    &[
                        "enable".to_owned(),
                        "--now".to_owned(),
                        "nfs-server.service".to_owned(),
                    ],
                )?;
            } else {
                remove_regular_file_if_exists(&nfs_path)?;
            }
            run_tool(&self.exportfs_path, &["-ra".to_owned()]).map_err(share_validation_error)?;
            Ok(())
        })();

        if let Err(error) = result {
            let _ = restore_optional(&self.samba_config, previous_main.as_deref(), 0o644);
            let _ = restore_optional(&smb_path, previous_smb.as_deref(), 0o640);
            let _ = restore_optional(&nfs_path, previous_nfs.as_deref(), 0o640);
            let _ = run_tool(&self.exportfs_path, &["-ra".to_owned()]);
            let _ = run_tool(
                &self.systemctl_path,
                &["reload".to_owned(), "smbd.service".to_owned()],
            );
            return Err(error);
        }
        volume.smb = smb;
        volume.nfs = nfs;
        Ok(())
    }

    fn persist_descriptor(
        &self,
        volume: &ManagedVolumeInformation,
    ) -> Result<(), StorageWriteError> {
        fs::create_dir_all(&self.descriptor_directory).map_err(io_error)?;
        let path = self
            .descriptor_directory
            .join(format!("{}.json", volume.name));
        let payload = serde_json::to_vec_pretty(volume).map_err(|error| {
            StorageWriteError::unavailable(format!("序列化数据卷状态失败：{error}"))
        })?;
        atomic_write(&path, &payload, 0o600)
    }

    fn read_descriptors(&self) -> Result<Vec<ManagedVolumeInformation>, StorageWriteError> {
        read_descriptors_from(&self.descriptor_directory)
    }

    fn refresh_volume(
        &self,
        mut volume: ManagedVolumeInformation,
    ) -> Result<ManagedVolumeInformation, StorageWriteError> {
        volume.mounted = is_mounted(Path::new(&volume.mount_path))?;
        volume.persistent_mount_enabled = self
            .unit_directory
            .join(mount_unit_name(&volume.name))
            .is_file();
        Ok(volume)
    }
}

enum PreparedAction {
    Provision {
        array_id: String,
        array_path: String,
    },
    Existing {
        volume: Box<ManagedVolumeInformation>,
    },
}

pub fn inspect_managed_volumes() -> io::Result<Vec<ManagedVolumeInformation>> {
    let descriptor_directory = environment_path(
        "AMSEOKNAS_STORAGE_DESCRIPTOR_DIRECTORY",
        DEFAULT_DESCRIPTOR_DIRECTORY,
    );
    let unit_directory = environment_path("AMSEOKNAS_MOUNT_UNIT_DIRECTORY", DEFAULT_UNIT_DIRECTORY);
    let mut volumes = read_descriptors_from(&descriptor_directory)
        .map_err(|error| io::Error::other(error.message))?;
    for volume in &mut volumes {
        volume.mounted = is_mounted(Path::new(&volume.mount_path))
            .map_err(|error| io::Error::other(error.message))?;
        volume.persistent_mount_enabled =
            unit_directory.join(mount_unit_name(&volume.name)).is_file();
    }
    volumes.sort_by(|left, right| left.name.cmp(&right.name));
    Ok(volumes)
}

fn validate_common(
    action: StorageAction,
    parameters: &StorageExecutionParameters,
) -> Result<(), StorageWriteError> {
    if !valid_uuid(&parameters.operation_id)
        || parameters.idempotency_key.is_empty()
        || parameters.idempotency_key.len() > 200
        || parameters.fencing_token <= 0
        || parameters.snapshot_fingerprint.len() != 64
        || !parameters
            .snapshot_fingerprint
            .bytes()
            .all(|value| value.is_ascii_hexdigit())
    {
        return Err(invalid_request());
    }
    match action {
        StorageAction::ProvisionVolume => {
            if parameters.array_id.as_deref().is_none_or(str::is_empty)
                || !valid_name(parameters.volume_name.as_deref())
                || !valid_account(parameters.owner_name.as_deref())
                || !valid_account(parameters.group_name.as_deref())
                || !valid_mode(parameters.directory_mode.as_deref())
            {
                return Err(invalid_request());
            }
        }
        StorageAction::UpdatePermissions => {
            if parameters.volume_id.as_deref().is_none_or(str::is_empty)
                || !valid_account(parameters.owner_name.as_deref())
                || !valid_account(parameters.group_name.as_deref())
                || !valid_mode(parameters.directory_mode.as_deref())
                || parameters.smb.is_some()
                || parameters.nfs.is_some()
            {
                return Err(invalid_request());
            }
        }
        StorageAction::ConfigureShares => {
            if parameters.volume_id.as_deref().is_none_or(str::is_empty)
                || parameters.owner_name.is_some()
                || parameters.group_name.is_some()
                || parameters.directory_mode.is_some()
                || parameters.smb.is_none()
                || parameters.nfs.is_none()
            {
                return Err(invalid_request());
            }
        }
        StorageAction::VerifyReadWrite => {
            if parameters.volume_id.as_deref().is_none_or(str::is_empty)
                || parameters.owner_name.is_some()
                || parameters.group_name.is_some()
                || parameters.directory_mode.is_some()
                || parameters.smb.is_some()
                || parameters.nfs.is_some()
            {
                return Err(invalid_request());
            }
        }
    }
    validate_share_settings(
        parameters
            .smb
            .as_ref()
            .unwrap_or(&SmbShareSettings::default()),
        parameters
            .nfs
            .as_ref()
            .unwrap_or(&NfsShareSettings::default()),
    )
}

fn validate_share_settings(
    smb: &SmbShareSettings,
    nfs: &NfsShareSettings,
) -> Result<(), StorageWriteError> {
    if smb.enabled
        && (!valid_name(smb.share_name.as_deref())
            || !valid_ipv4_cidr(smb.allowed_network.as_deref()))
    {
        return Err(invalid_request());
    }
    if nfs.enabled && !valid_ipv4_cidr(nfs.client_network.as_deref()) {
        return Err(invalid_request());
    }
    Ok(())
}

fn ensure_no_signature(blkid: &Path, device: &str) -> Result<(), StorageWriteError> {
    let output = Command::new(blkid)
        .args(["-p", device])
        .env_clear()
        .env("PATH", "/usr/sbin:/usr/bin:/sbin:/bin")
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .output()
        .map_err(io_error)?;
    match output.status.code() {
        Some(2) => Ok(()),
        Some(0) => Err(StorageWriteError::new(
            "storage.filesystem_exists",
            "目标阵列已存在文件系统或其他数据签名",
            false,
        )),
        _ => Err(StorageWriteError::new(
            "storage.verification_failed",
            bounded_output(&output.stderr),
            true,
        )),
    }
}

fn verify_read_write(path: &Path, operation_id: &str) -> Result<(), StorageWriteError> {
    reject_symlink(path)?;
    let test_path = path.join(format!(".amseokos-write-test-{operation_id}"));
    if test_path.exists() {
        return Err(StorageWriteError::new(
            "storage.verification_failed",
            "读写校验文件已存在",
            false,
        ));
    }
    let payload = format!("AmseokOS storage verification {operation_id}\n").into_bytes();
    let mut file = OpenOptions::new()
        .create_new(true)
        .write(true)
        .mode(0o600)
        .open(&test_path)
        .map_err(verification_io)?;
    file.write_all(&payload).map_err(verification_io)?;
    file.sync_all().map_err(verification_io)?;
    drop(file);
    let mut actual = Vec::new();
    File::open(&test_path)
        .and_then(|mut value| value.read_to_end(&mut actual))
        .map_err(verification_io)?;
    if actual != payload {
        return Err(StorageWriteError::new(
            "storage.verification_failed",
            "写入后读取内容不一致",
            false,
        ));
    }
    fs::remove_file(&test_path).map_err(verification_io)?;
    File::open(path)
        .and_then(|directory| directory.sync_all())
        .map_err(verification_io)?;
    Ok(())
}

fn ensure_samba_include(config_path: &Path, share_path: &Path) -> Result<(), StorageWriteError> {
    let content = fs::read_to_string(config_path).map_err(io_error)?;
    let include = format!("include = {}", share_path.display());
    let output = samba_include_content(&content, &include, true)?;
    atomic_write(config_path, output.as_bytes(), 0o644)
}

fn remove_samba_include(config_path: &Path, share_path: &Path) -> Result<(), StorageWriteError> {
    let content = fs::read_to_string(config_path).map_err(io_error)?;
    let include = format!("include = {}", share_path.display());
    let output = samba_include_content(&content, &include, false)?;
    atomic_write(config_path, output.as_bytes(), 0o644)
}

fn samba_include_content(
    content: &str,
    include: &str,
    enabled: bool,
) -> Result<String, StorageWriteError> {
    const LEGACY_WILDCARD_INCLUDE: &str = "include = /etc/samba/smb.conf.d/amseokos-*.conf";
    let mut output = String::new();
    let mut inserted = false;
    for line in content.lines() {
        let trimmed = line.trim();
        if trimmed == LEGACY_WILDCARD_INCLUDE || trimmed == include {
            if enabled && trimmed == include && !inserted {
                output.push_str(include);
                output.push('\n');
                inserted = true;
            }
            continue;
        }
        output.push_str(line);
        output.push('\n');
        if enabled && !inserted && trimmed.eq_ignore_ascii_case("[global]") {
            output.push_str("# AmseokOS managed share include\n");
            output.push_str(include);
            output.push('\n');
            inserted = true;
        }
    }
    if enabled && !inserted {
        return Err(StorageWriteError::new(
            "storage.share_validation_failed",
            "Samba 主配置缺少 [global] 段",
            false,
        ));
    }
    Ok(output)
}

fn smb_config(volume: &ManagedVolumeInformation, settings: &SmbShareSettings) -> String {
    let name = settings.share_name.as_deref().unwrap_or(&volume.name);
    let network = settings
        .allowed_network
        .as_deref()
        .unwrap_or("127.0.0.1/32");
    let mut content = format!(
        "[{name}]\n path = {}\n browseable = yes\n read only = {}\n guest ok = {}\n force group = {}\n create mask = 0660\n directory mask = 0770\n hosts allow = {network}\n hosts deny = 0.0.0.0/0\n",
        volume.mount_path,
        if settings.read_only { "yes" } else { "no" },
        if settings.guest_access { "yes" } else { "no" },
        volume.group_name,
    );
    if !settings.guest_access {
        content.push_str(&format!(" valid users = @{}\n", volume.group_name));
    }
    content
}

fn nfs_config(volume: &ManagedVolumeInformation, settings: &NfsShareSettings) -> String {
    let access = if settings.read_only { "ro" } else { "rw" };
    format!(
        "{} {}({access},sync,root_squash,no_subtree_check)\n",
        volume.mount_path,
        settings.client_network.as_deref().unwrap_or("127.0.0.1/32")
    )
}

fn mount_unit_content(name: &str, uuid: &str, mount_path: &Path) -> String {
    format!(
        "[Unit]\nDescription=AmseokOS data volume {name}\nBefore=smbd.service nfs-server.service\n\n[Mount]\nWhat=UUID={uuid}\nWhere={}\nType=ext4\nOptions=defaults,noatime,nodev,nosuid\nTimeoutSec=30\n\n[Install]\nWantedBy=multi-user.target\n",
        mount_path.display()
    )
}

fn mount_unit_name(name: &str) -> String {
    format!("srv-amseoknas-volumes-{name}.mount")
}

fn read_descriptors_from(
    directory: &Path,
) -> Result<Vec<ManagedVolumeInformation>, StorageWriteError> {
    if !directory.exists() {
        return Ok(Vec::new());
    }
    reject_symlink(directory)?;
    let mut volumes = Vec::new();
    for entry in fs::read_dir(directory).map_err(io_error)? {
        let entry = entry.map_err(io_error)?;
        let path = entry.path();
        if path.extension().and_then(|value| value.to_str()) != Some("json") {
            continue;
        }
        reject_symlink(&path)?;
        let volume = serde_json::from_slice(&fs::read(&path).map_err(io_error)?)
            .map_err(|error| StorageWriteError::unavailable(error.to_string()))?;
        volumes.push(volume);
    }
    Ok(volumes)
}

fn is_mounted(path: &Path) -> Result<bool, StorageWriteError> {
    let target = path.to_string_lossy();
    let mountinfo = fs::read_to_string("/proc/self/mountinfo").map_err(io_error)?;
    Ok(mountinfo
        .lines()
        .any(|line| line.split_whitespace().nth(4) == Some(target.as_ref())))
}

fn atomic_write(path: &Path, payload: &[u8], mode: u32) -> Result<(), StorageWriteError> {
    if path.exists() {
        reject_symlink(path)?;
    }
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent).map_err(io_error)?;
        reject_symlink(parent)?;
    }
    let temporary = path.with_extension("amseokos.tmp");
    let mut file = OpenOptions::new()
        .create(true)
        .truncate(true)
        .write(true)
        .mode(mode)
        .open(&temporary)
        .map_err(io_error)?;
    file.write_all(payload).map_err(io_error)?;
    file.sync_all().map_err(io_error)?;
    fs::set_permissions(&temporary, fs::Permissions::from_mode(mode)).map_err(io_error)?;
    fs::rename(&temporary, path).map_err(io_error)?;
    if let Some(parent) = path.parent() {
        File::open(parent)
            .and_then(|value| value.sync_all())
            .map_err(io_error)?;
    }
    Ok(())
}

fn read_optional(path: &Path) -> Result<Option<Vec<u8>>, StorageWriteError> {
    if !path.exists() {
        return Ok(None);
    }
    reject_symlink(path)?;
    fs::read(path).map(Some).map_err(io_error)
}

fn restore_optional(
    path: &Path,
    content: Option<&[u8]>,
    mode: u32,
) -> Result<(), StorageWriteError> {
    match content {
        Some(content) => atomic_write(path, content, mode),
        None => remove_regular_file_if_exists(path),
    }
}

fn remove_regular_file_if_exists(path: &Path) -> Result<(), StorageWriteError> {
    if !path.exists() {
        return Ok(());
    }
    reject_symlink(path)?;
    if !fs::symlink_metadata(path)
        .map_err(io_error)?
        .file_type()
        .is_file()
    {
        return Err(StorageWriteError::new(
            "storage.share_validation_failed",
            "受管配置目标不是普通文件",
            false,
        ));
    }
    fs::remove_file(path).map_err(io_error)
}

fn create_secure_directory(path: &Path, mode: u32) -> Result<(), StorageWriteError> {
    fs::create_dir_all(path).map_err(io_error)?;
    reject_symlink(path)?;
    fs::set_permissions(path, fs::Permissions::from_mode(mode)).map_err(io_error)
}

fn reject_symlink(path: &Path) -> Result<(), StorageWriteError> {
    let metadata = fs::symlink_metadata(path).map_err(io_error)?;
    if metadata.file_type().is_symlink() {
        return Err(StorageWriteError::new(
            "storage.path_unsafe",
            format!("拒绝符号链接路径：{}", path.display()),
            false,
        ));
    }
    Ok(())
}

fn run_tool(path: &Path, arguments: &[String]) -> Result<(), StorageWriteError> {
    let output = run_tool_output(path, arguments)?;
    if output.status.success() {
        return Ok(());
    }
    Err(StorageWriteError::new(
        "tool.failed",
        bounded_output(&output.stderr),
        true,
    ))
}

fn tool_output(path: &Path, arguments: &[String]) -> Result<String, StorageWriteError> {
    let output = run_tool_output(path, arguments)?;
    if !output.status.success() {
        return Err(StorageWriteError::new(
            "tool.failed",
            bounded_output(&output.stderr),
            true,
        ));
    }
    Ok(bounded_output(&output.stdout))
}

fn run_tool_output(path: &Path, arguments: &[String]) -> Result<ToolOutput, StorageWriteError> {
    let mut child = Command::new(path)
        .args(arguments)
        .env_clear()
        .env("PATH", "/usr/sbin:/usr/bin:/sbin:/bin")
        .env("LANG", "C")
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .map_err(io_error)?;
    let started = Instant::now();
    loop {
        match child.try_wait().map_err(io_error)? {
            Some(status) => {
                let output = child.wait_with_output().map_err(io_error)?;
                return Ok(ToolOutput {
                    status,
                    stdout: output.stdout,
                    stderr: output.stderr,
                });
            }
            None if started.elapsed() >= TOOL_TIMEOUT => {
                let _ = child.kill();
                let _ = child.wait();
                return Err(StorageWriteError::new(
                    "tool.timeout",
                    "受限存储工具执行超时",
                    true,
                ));
            }
            None => thread::sleep(Duration::from_millis(50)),
        }
    }
}

struct ToolOutput {
    status: ExitStatus,
    stdout: Vec<u8>,
    stderr: Vec<u8>,
}

fn bounded_output(value: &[u8]) -> String {
    let length = value.len().min(MAXIMUM_TOOL_OUTPUT);
    String::from_utf8_lossy(&value[..length]).trim().to_owned()
}

fn environment_path(variable: &str, default: &str) -> PathBuf {
    env::var_os(variable)
        .map(PathBuf::from)
        .unwrap_or_else(|| PathBuf::from(default))
}

fn find_tool(variable: &str, candidates: &[&str]) -> Option<PathBuf> {
    if let Some(value) = env::var_os(variable) {
        let path = PathBuf::from(value);
        return path.is_file().then_some(path);
    }
    candidates
        .iter()
        .map(PathBuf::from)
        .find(|path| path.is_file())
}

fn valid_name(value: Option<&str>) -> bool {
    value.is_some_and(|value| {
        (1..=32).contains(&value.len())
            && value.as_bytes()[0].is_ascii_lowercase()
            && value
                .bytes()
                .all(|byte| byte.is_ascii_lowercase() || byte.is_ascii_digit() || byte == b'-')
    })
}

fn valid_account(value: Option<&str>) -> bool {
    value.is_some_and(|value| {
        (1..=32).contains(&value.len())
            && (value.as_bytes()[0].is_ascii_alphabetic() || value.as_bytes()[0] == b'_')
            && value
                .bytes()
                .all(|byte| byte.is_ascii_alphanumeric() || byte == b'_' || byte == b'-')
    })
}

fn valid_mode(value: Option<&str>) -> bool {
    matches!(value, Some("0750" | "0770" | "0775" | "0777"))
}

fn valid_uuid(value: &str) -> bool {
    value.len() == 36
        && value.bytes().enumerate().all(|(index, byte)| {
            if matches!(index, 8 | 13 | 18 | 23) {
                byte == b'-'
            } else {
                byte.is_ascii_hexdigit()
            }
        })
}

fn valid_ipv4_cidr(value: Option<&str>) -> bool {
    let Some((address, prefix)) = value.and_then(|item| item.split_once('/')) else {
        return false;
    };
    address.parse::<std::net::Ipv4Addr>().is_ok()
        && prefix
            .parse::<u8>()
            .is_ok_and(|value| (1..=32).contains(&value))
}

fn invalid_request() -> StorageWriteError {
    StorageWriteError::new("request.invalid", "存储操作参数无效", false)
}

fn inventory_error(error: io::Error) -> StorageWriteError {
    StorageWriteError::new("inventory.read_failed", error.to_string(), true)
}

fn io_error(error: impl ToString) -> StorageWriteError {
    StorageWriteError::unavailable(error.to_string())
}

fn verification_io(error: io::Error) -> StorageWriteError {
    StorageWriteError::new("storage.verification_failed", error.to_string(), false)
}

fn share_validation_error(error: StorageWriteError) -> StorageWriteError {
    StorageWriteError::new("storage.share_validation_failed", error.message, false)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn volume() -> ManagedVolumeInformation {
        ManagedVolumeInformation {
            id: "volume:01234567-89ab-cdef-0123-456789abcdef".to_owned(),
            name: "data".to_owned(),
            array_id: "md:test".to_owned(),
            array_path: "/dev/md0".to_owned(),
            file_system_uuid: "01234567-89ab-cdef-0123-456789abcdef".to_owned(),
            file_system_type: "ext4".to_owned(),
            mount_path: "/srv/amseoknas/volumes/data".to_owned(),
            mounted: true,
            persistent_mount_enabled: true,
            owner_name: "root".to_owned(),
            group_name: "amseoknas-data".to_owned(),
            directory_mode: "0770".to_owned(),
            read_write_verified: true,
            smb: SmbShareSettings::default(),
            nfs: NfsShareSettings::default(),
        }
    }

    #[test]
    fn mount_unit_uses_uuid_and_a_fixed_managed_path() {
        let content = mount_unit_content(
            "data",
            "01234567-89ab-cdef-0123-456789abcdef",
            Path::new("/srv/amseoknas/volumes/data"),
        );
        assert!(content.contains("What=UUID=01234567-89ab-cdef-0123-456789abcdef"));
        assert!(content.contains("Where=/srv/amseoknas/volumes/data"));
        assert_eq!(mount_unit_name("data"), "srv-amseoknas-volumes-data.mount");
    }

    #[test]
    fn share_configs_are_network_scoped_and_root_squashed() {
        let smb = SmbShareSettings {
            enabled: true,
            share_name: Some("data".to_owned()),
            read_only: false,
            guest_access: false,
            allowed_network: Some("192.168.188.0/24".to_owned()),
        };
        let nfs = NfsShareSettings {
            enabled: true,
            client_network: Some("192.168.188.0/24".to_owned()),
            read_only: false,
        };
        let volume = volume();
        let samba = smb_config(&volume, &smb);
        let exports = nfs_config(&volume, &nfs);
        assert!(samba.contains("hosts allow = 192.168.188.0/24"));
        assert!(samba.contains("valid users = @amseoknas-data"));
        assert!(exports.contains("rw,sync,root_squash,no_subtree_check"));
    }

    #[test]
    fn validators_reject_paths_and_unscoped_networks() {
        assert!(valid_name(Some("data-1")));
        assert!(!valid_name(Some("../data")));
        assert!(valid_ipv4_cidr(Some("192.168.188.0/24")));
        assert!(!valid_ipv4_cidr(Some("0.0.0.0")));
        assert!(!valid_ipv4_cidr(Some("0.0.0.0/0")));
        assert!(!valid_mode(Some("0776")));
    }

    #[test]
    fn samba_main_config_uses_exact_includes_instead_of_an_unsupported_glob() {
        let original =
            "[global]\n workgroup = WORKGROUP\ninclude = /etc/samba/smb.conf.d/amseokos-*.conf\n";
        let exact = "include = /etc/samba/smb.conf.d/amseokos-data.conf";

        let enabled = samba_include_content(original, exact, true).expect("enable include");
        assert!(enabled.contains(exact));
        assert!(!enabled.contains("amseokos-*.conf"));

        let disabled = samba_include_content(&enabled, exact, false).expect("disable include");
        assert!(!disabled.contains(exact));
    }
}
