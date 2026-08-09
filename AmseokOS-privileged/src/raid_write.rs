//--------------------------//
//--------以稳定设备身份和固定 mdadm 参数执行受限 RAID 生命周期动作---------//
//--------Executes constrained RAID lifecycle actions with stable identities and fixed mdadm arguments--------//
//-------------------------//
use std::collections::BTreeSet;
use std::env;
use std::fs;
use std::io;
use std::os::unix::fs::PermissionsExt;
use std::path::{Path, PathBuf};
use std::process::{Command, ExitStatus, Stdio};
use std::thread;
use std::time::{Duration, Instant};

use serde::{Deserialize, Serialize};

use crate::inventory::raid::{self, RaidArrayInformation};
use crate::inventory::storage::{self, BlockDeviceInformation};
use crate::raid_registry::RaidOperationRegistry;

const DEFAULT_REGISTRY_PATH: &str = "/var/lib/amseoknas/raid-operations.json";
const DEFAULT_BACKUP_DIRECTORY: &str = "/var/lib/amseoknas/raid-reshape";
const TOOL_TIMEOUT: Duration = Duration::from_secs(45);
pub(crate) const CODE_RECONCILIATION_REQUIRED: &str = "operation.duplicate_requires_reconciliation";

#[derive(Clone, Copy, Debug)]
pub enum RaidAction {
    Create,
    Delete,
    AddDevice,
    RemoveDevice,
    ReplaceDevice,
    Grow,
    Shrink,
}

#[derive(Clone, Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct RaidExecutionParameters {
    pub(crate) operation_id: String,
    pub(crate) idempotency_key: String,
    pub(crate) fencing_token: i64,
    array_id: Option<String>,
    array_name: Option<String>,
    level: Option<String>,
    device_ids: Vec<String>,
    source_device_id: Option<String>,
    target_device_count: Option<i32>,
    expected_member_device_ids: Vec<String>,
    pub(crate) snapshot_fingerprint: String,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct RaidExecutionResult {
    pub array_id: Option<String>,
    pub in_progress: bool,
    pub progress_percentage: Option<i32>,
}

#[derive(Debug)]
pub struct RaidWriteError {
    pub code: &'static str,
    pub message: String,
    pub retryable: bool,
}

impl RaidWriteError {
    pub(crate) fn new(code: &'static str, message: impl Into<String>, retryable: bool) -> Self {
        Self {
            code,
            message: message.into(),
            retryable,
        }
    }

    pub fn unavailable(message: impl Into<String>) -> Self {
        Self::new("raid.write_unavailable", message, true)
    }
}

pub struct RaidWriteContext {
    mdadm_path: PathBuf,
    blkid_path: Option<PathBuf>,
    backup_directory: PathBuf,
    registry: RaidOperationRegistry,
}

impl RaidWriteContext {
    pub fn from_environment() -> Result<Self, RaidWriteError> {
        let mdadm_path = find_tool("AMSEOKNAS_MDADM_PATH", &["/usr/sbin/mdadm", "/sbin/mdadm"])
            .ok_or_else(|| RaidWriteError::unavailable("mdadm 工具不存在"))?;
        let blkid_path = find_tool("AMSEOKNAS_BLKID_PATH", &["/usr/sbin/blkid", "/sbin/blkid"]);
        let registry_path = env::var_os("AMSEOKNAS_RAID_OPERATION_REGISTRY_PATH")
            .map(PathBuf::from)
            .unwrap_or_else(|| PathBuf::from(DEFAULT_REGISTRY_PATH));
        let backup_directory = env::var_os("AMSEOKNAS_RAID_BACKUP_DIRECTORY")
            .map(PathBuf::from)
            .unwrap_or_else(|| PathBuf::from(DEFAULT_BACKUP_DIRECTORY));
        let registry = RaidOperationRegistry::open(registry_path)
            .map_err(|error| RaidWriteError::unavailable(error.to_string()))?;
        Ok(Self {
            mdadm_path,
            blkid_path,
            backup_directory,
            registry,
        })
    }

    pub fn execute(
        &self,
        action: RaidAction,
        parameters: RaidExecutionParameters,
    ) -> Result<RaidExecutionResult, RaidWriteError> {
        validate_common(&parameters)?;
        if let Some(result) = self.registry.replay(&parameters)? {
            return Ok(result);
        }

        let devices = storage::inspect_block_devices().map_err(|error| {
            RaidWriteError::new("inventory.read_failed", error.to_string(), true)
        })?;
        let arrays = raid::inspect_arrays().map_err(|error| {
            RaidWriteError::new("inventory.read_failed", error.to_string(), true)
        })?;
        let prepared = prepare(action, &parameters, &devices, &arrays, self)?;

        self.registry.begin(&parameters)?;
        let result = match self.perform(prepared, &parameters) {
            Ok(result) => result,
            Err(error) => {
                return Err(RaidWriteError::new(
                    CODE_RECONCILIATION_REQUIRED,
                    format!("RAID 命令已开始但结果需要复核：{}", error.message),
                    true,
                ));
            }
        };
        if let Err(error) = self.registry.complete(&parameters, &result) {
            return Err(RaidWriteError::new(
                CODE_RECONCILIATION_REQUIRED,
                format!("RAID 命令已完成但结果登记失败：{}", error.message),
                true,
            ));
        }
        Ok(result)
    }

    fn perform(
        &self,
        prepared: PreparedAction,
        parameters: &RaidExecutionParameters,
    ) -> Result<RaidExecutionResult, RaidWriteError> {
        match prepared {
            PreparedAction::Create { name, level, paths } => {
                let array_path = format!("/dev/md/{name}");
                let mut arguments = vec![
                    "--create".to_owned(),
                    array_path,
                    "--metadata=1.2".to_owned(),
                    format!("--level={level}"),
                    format!("--raid-devices={}", paths.len()),
                    "--run".to_owned(),
                ];
                arguments.extend(paths);
                run_tool(&self.mdadm_path, &arguments)?;
                let array = find_array_by_members(&parameters.device_ids)?;
                Ok(result_for_array(&array))
            }
            PreparedAction::Delete {
                array,
                member_paths,
            } => {
                run_tool(&self.mdadm_path, &["--stop".to_owned(), array.path.clone()])?;
                for path in member_paths {
                    run_tool(
                        &self.mdadm_path,
                        &["--zero-superblock".to_owned(), "--force".to_owned(), path],
                    )?;
                }
                if raid::inspect_arrays()
                    .map_err(inventory_error)?
                    .iter()
                    .any(|candidate| candidate.id == array.id)
                {
                    return Err(verification_error("阵列停止后仍然存在"));
                }
                Ok(RaidExecutionResult {
                    array_id: None,
                    in_progress: false,
                    progress_percentage: Some(100),
                })
            }
            PreparedAction::Add { array, new_path } => {
                run_tool(
                    &self.mdadm_path,
                    &[
                        "--manage".to_owned(),
                        array.path,
                        "--add".to_owned(),
                        new_path,
                    ],
                )?;
                verify_array_member(parameters.array_id.as_deref(), &parameters.device_ids[0])
            }
            PreparedAction::Remove {
                array,
                source_path,
                fail_first,
            } => {
                if fail_first {
                    run_tool(
                        &self.mdadm_path,
                        &[
                            "--manage".to_owned(),
                            array.path.clone(),
                            "--fail".to_owned(),
                            source_path.clone(),
                        ],
                    )?;
                }
                run_tool(
                    &self.mdadm_path,
                    &[
                        "--manage".to_owned(),
                        array.path,
                        "--remove".to_owned(),
                        source_path,
                    ],
                )?;
                verify_array_member_absent(
                    parameters.array_id.as_deref(),
                    parameters.source_device_id.as_deref(),
                )
            }
            PreparedAction::Replace {
                array,
                source_path,
                new_path,
            } => {
                run_tool(
                    &self.mdadm_path,
                    &[
                        "--manage".to_owned(),
                        array.path.clone(),
                        "--fail".to_owned(),
                        source_path.clone(),
                    ],
                )?;
                run_tool(
                    &self.mdadm_path,
                    &[
                        "--manage".to_owned(),
                        array.path.clone(),
                        "--remove".to_owned(),
                        source_path,
                    ],
                )?;
                run_tool(
                    &self.mdadm_path,
                    &[
                        "--manage".to_owned(),
                        array.path,
                        "--add".to_owned(),
                        new_path,
                    ],
                )?;
                let mut result =
                    verify_array_member(parameters.array_id.as_deref(), &parameters.device_ids[0])?;
                verify_array_member_absent(
                    parameters.array_id.as_deref(),
                    parameters.source_device_id.as_deref(),
                )?;
                result.in_progress = true;
                result.progress_percentage = None;
                Ok(result)
            }
            PreparedAction::Resize {
                array,
                new_paths,
                target,
            } => {
                for path in new_paths {
                    run_tool(
                        &self.mdadm_path,
                        &[
                            "--manage".to_owned(),
                            array.path.clone(),
                            "--add".to_owned(),
                            path,
                        ],
                    )?;
                }
                let backup_path = self
                    .backup_directory
                    .join(format!("{}.backup", parameters.operation_id));
                let arguments = vec![
                    "--grow".to_owned(),
                    array.path,
                    format!("--raid-devices={target}"),
                    format!("--backup-file={}", backup_path.display()),
                ];
                run_tool(&self.mdadm_path, &arguments)?;
                let current = find_array(parameters.array_id.as_deref())?;
                if current.configured_device_count != target {
                    return Err(verification_error("阵列成员数未更新为目标值"));
                }
                Ok(result_for_array(&current))
            }
        }
    }
}

#[derive(Debug)]
enum PreparedAction {
    Create {
        name: String,
        level: String,
        paths: Vec<String>,
    },
    Delete {
        array: ArraySnapshot,
        member_paths: Vec<String>,
    },
    Add {
        array: ArraySnapshot,
        new_path: String,
    },
    Remove {
        array: ArraySnapshot,
        source_path: String,
        fail_first: bool,
    },
    Replace {
        array: ArraySnapshot,
        source_path: String,
        new_path: String,
    },
    Resize {
        array: ArraySnapshot,
        new_paths: Vec<String>,
        target: u64,
    },
}

#[derive(Clone, Debug)]
struct ArraySnapshot {
    id: String,
    path: String,
}

fn prepare(
    action: RaidAction,
    parameters: &RaidExecutionParameters,
    devices: &[BlockDeviceInformation],
    arrays: &[RaidArrayInformation],
    context: &RaidWriteContext,
) -> Result<PreparedAction, RaidWriteError> {
    match action {
        RaidAction::Create => {
            if !parameters.expected_member_device_ids.is_empty() {
                return Err(identity_error("新建阵列的预期成员快照必须为空"));
            }
            let name = parameters
                .array_name
                .as_deref()
                .ok_or_else(invalid_request)?;
            validate_array_name(name)?;
            let level =
                normalized_level(parameters.level.as_deref()).ok_or_else(invalid_request)?;
            validate_device_count(level, parameters.device_ids.len())?;
            if arrays.iter().any(|array| array.name == name) {
                return Err(RaidWriteError::new(
                    "resource.busy",
                    "阵列名称已被占用",
                    false,
                ));
            }
            let paths = resolve_new_devices(&parameters.device_ids, devices)?;
            Ok(PreparedAction::Create {
                name: name.to_owned(),
                level: level.to_owned(),
                paths,
            })
        }
        RaidAction::Delete => {
            let array = require_array(parameters, arrays, devices)?;
            ensure_array_idle(array)?;
            ensure_array_not_busy(array)?;
            Ok(PreparedAction::Delete {
                array: snapshot(array),
                member_paths: array
                    .members
                    .iter()
                    .map(|member| member.path.clone())
                    .collect(),
            })
        }
        RaidAction::AddDevice => {
            let array = require_manageable_array(parameters, arrays, devices)?;
            if array.configured_device_count >= 64 {
                return Err(invalid_request());
            }
            let paths = resolve_exact_new_devices(&parameters.device_ids, devices, 1)?;
            Ok(PreparedAction::Add {
                array: snapshot(array),
                new_path: paths[0].clone(),
            })
        }
        RaidAction::RemoveDevice => {
            let array = require_manageable_array(parameters, arrays, devices)?;
            let source = require_source_member(parameters, array, devices)?;
            let fail_first = !source.state.contains("faulty") && !source.state.contains("spare");
            Ok(PreparedAction::Remove {
                array: snapshot(array),
                source_path: source.path.clone(),
                fail_first,
            })
        }
        RaidAction::ReplaceDevice => {
            let array = require_manageable_array(parameters, arrays, devices)?;
            let source = require_source_member(parameters, array, devices)?;
            let paths = resolve_exact_new_devices(&parameters.device_ids, devices, 1)?;
            Ok(PreparedAction::Replace {
                array: snapshot(array),
                source_path: source.path.clone(),
                new_path: paths[0].clone(),
            })
        }
        RaidAction::Grow | RaidAction::Shrink => {
            let array = require_array(parameters, arrays, devices)?;
            ensure_array_idle(array)?;
            let level = normalized_level(Some(&array.level)).ok_or_else(invalid_request)?;
            if !matches!(level, "raid0" | "raid1" | "raid5" | "raid6") {
                return Err(RaidWriteError::new(
                    "raid.reshape_level_unsupported",
                    "当前 RAID 级别不支持调整成员数量",
                    false,
                ));
            }
            let target = parameters
                .target_device_count
                .and_then(|value| u64::try_from(value).ok())
                .ok_or_else(invalid_request)?;
            validate_device_count(
                level,
                usize::try_from(target).map_err(|_| invalid_request())?,
            )?;
            let current = array.configured_device_count;
            let new_paths = if matches!(action, RaidAction::Grow) {
                if target <= current {
                    return Err(invalid_request());
                }
                resolve_exact_new_devices(
                    &parameters.device_ids,
                    devices,
                    usize::try_from(target - current).map_err(|_| invalid_request())?,
                )?
            } else {
                if target >= current || !parameters.device_ids.is_empty() {
                    return Err(invalid_request());
                }
                ensure_array_not_busy(array)?;
                ensure_no_filesystem_signature(context, &array.path)?;
                Vec::new()
            };
            ensure_backup_directory(&context.backup_directory)?;
            Ok(PreparedAction::Resize {
                array: snapshot(array),
                new_paths,
                target,
            })
        }
    }
}

fn validate_common(parameters: &RaidExecutionParameters) -> Result<(), RaidWriteError> {
    if !is_uuid(&parameters.operation_id)
        || parameters.idempotency_key.is_empty()
        || parameters.idempotency_key.len() > 200
        || parameters.fencing_token <= 0
        || parameters.snapshot_fingerprint.len() != 64
        || !parameters
            .snapshot_fingerprint
            .bytes()
            .all(|byte| byte.is_ascii_hexdigit())
        || parameters.device_ids.len() > 64
        || parameters.expected_member_device_ids.len() > 64
        || parameters
            .device_ids
            .iter()
            .chain(&parameters.expected_member_device_ids)
            .any(|id| id.is_empty() || id.len() > 300)
    {
        return Err(invalid_request());
    }
    Ok(())
}

fn is_uuid(value: &str) -> bool {
    value.len() == 36
        && value.bytes().enumerate().all(|(index, byte)| match index {
            8 | 13 | 18 | 23 => byte == b'-',
            _ => byte.is_ascii_hexdigit(),
        })
}

fn require_array<'a>(
    parameters: &RaidExecutionParameters,
    arrays: &'a [RaidArrayInformation],
    devices: &[BlockDeviceInformation],
) -> Result<&'a RaidArrayInformation, RaidWriteError> {
    let id = parameters.array_id.as_deref().ok_or_else(invalid_request)?;
    if !id.starts_with("md:") {
        return Err(identity_error("阵列没有稳定 UUID"));
    }
    let matches = arrays
        .iter()
        .filter(|array| array.id == id)
        .collect::<Vec<_>>();
    if matches.len() != 1 || matches[0].uuid.is_none() {
        return Err(RaidWriteError::new(
            "resource.not_found",
            "目标阵列不存在",
            false,
        ));
    }
    let array = matches[0];
    let current = member_device_ids(array, devices)?;
    if current != as_set(&parameters.expected_member_device_ids) {
        return Err(identity_error("阵列成员身份已发生变化"));
    }
    if devices
        .iter()
        .any(|device| current.contains(&device.id) && device.system_device)
    {
        return Err(RaidWriteError::new(
            "resource.system_disk",
            "包含系统盘的阵列禁止执行写操作",
            false,
        ));
    }
    Ok(array)
}

fn require_manageable_array<'a>(
    parameters: &RaidExecutionParameters,
    arrays: &'a [RaidArrayInformation],
    devices: &[BlockDeviceInformation],
) -> Result<&'a RaidArrayInformation, RaidWriteError> {
    let array = require_array(parameters, arrays, devices)?;
    ensure_array_idle(array)?;
    if normalized_level(Some(&array.level)) == Some("raid0") {
        return Err(RaidWriteError::new(
            "raid0.member_management_unsupported",
            "RAID0 不支持成员管理",
            false,
        ));
    }
    Ok(array)
}

fn resolve_new_devices(
    ids: &[String],
    devices: &[BlockDeviceInformation],
) -> Result<Vec<String>, RaidWriteError> {
    let mut result = Vec::with_capacity(ids.len());
    for id in ids {
        let matches = devices
            .iter()
            .filter(|device| device.id == *id)
            .collect::<Vec<_>>();
        if matches.len() != 1 {
            return Err(RaidWriteError::new(
                "resource.not_found",
                "目标磁盘不存在或身份冲突",
                false,
            ));
        }
        let device = matches[0];
        if !device.stable || device.identity_conflict || !device.topology_complete {
            return Err(identity_error("目标磁盘身份或拓扑不可靠"));
        }
        if device.system_device {
            return Err(RaidWriteError::new(
                "resource.system_disk",
                "系统盘禁止用于 RAID",
                false,
            ));
        }
        if device.swap
            || device.raid_member
            || device.in_use
            || device.read_only
            || device.removable
            || !device.partitions.is_empty()
            || !device.mount_points.is_empty()
            || !device.dependent_devices.is_empty()
        {
            return Err(RaidWriteError::new(
                "resource.busy",
                "目标磁盘已占用或不可写",
                false,
            ));
        }
        result.push(device.path.clone());
    }
    Ok(result)
}

fn resolve_exact_new_devices(
    ids: &[String],
    devices: &[BlockDeviceInformation],
    expected: usize,
) -> Result<Vec<String>, RaidWriteError> {
    if ids.len() != expected {
        return Err(invalid_request());
    }
    resolve_new_devices(ids, devices)
}

fn member_device_ids(
    array: &RaidArrayInformation,
    devices: &[BlockDeviceInformation],
) -> Result<BTreeSet<String>, RaidWriteError> {
    array
        .members
        .iter()
        .map(|member| physical_id_for_path(&member.path, devices))
        .collect()
}

fn physical_id_for_path(
    path: &str,
    devices: &[BlockDeviceInformation],
) -> Result<String, RaidWriteError> {
    let matches = devices
        .iter()
        .filter(|device| {
            device.path == path
                || device
                    .partitions
                    .iter()
                    .any(|partition| partition.path == path)
        })
        .collect::<Vec<_>>();
    if matches.len() != 1 || !matches[0].stable || matches[0].identity_conflict {
        return Err(identity_error("无法把阵列成员映射到唯一稳定物理磁盘"));
    }
    Ok(matches[0].id.clone())
}

fn require_source_member<'a>(
    parameters: &RaidExecutionParameters,
    array: &'a RaidArrayInformation,
    devices: &[BlockDeviceInformation],
) -> Result<&'a crate::inventory::raid::RaidMemberInformation, RaidWriteError> {
    let source_id = parameters
        .source_device_id
        .as_deref()
        .ok_or_else(invalid_request)?;
    let matches = array
        .members
        .iter()
        .filter(|member| {
            physical_id_for_path(&member.path, devices).ok().as_deref() == Some(source_id)
        })
        .collect::<Vec<_>>();
    if matches.len() != 1 {
        return Err(RaidWriteError::new(
            "resource.not_found",
            "源成员磁盘不存在",
            false,
        ));
    }
    Ok(matches[0])
}

fn ensure_array_idle(array: &RaidArrayInformation) -> Result<(), RaidWriteError> {
    if array.sync_action != "idle" {
        return Err(RaidWriteError::new(
            "resource.busy",
            "阵列正在同步或重塑",
            false,
        ));
    }
    Ok(())
}

fn ensure_array_not_busy(array: &RaidArrayInformation) -> Result<(), RaidWriteError> {
    let sys_path = Path::new("/sys/class/block").join(&array.name);
    if fs::read_dir(sys_path.join("holders"))
        .map(|mut entries| entries.next().is_some())
        .unwrap_or(true)
    {
        return Err(RaidWriteError::new(
            "resource.busy",
            "阵列仍被上层块设备占用",
            false,
        ));
    }
    let device_number = fs::read_to_string(sys_path.join("dev"))
        .map(|value| value.trim().to_owned())
        .map_err(|_| identity_error("无法确认阵列设备号"))?;
    let mounted = fs::read_to_string("/proc/self/mountinfo")
        .unwrap_or_default()
        .lines()
        .any(|line| line.split_whitespace().nth(2) == Some(device_number.as_str()));
    let swapped = fs::read_to_string("/proc/swaps")
        .unwrap_or_default()
        .lines()
        .skip(1)
        .any(|line| line.split_whitespace().next() == Some(array.path.as_str()));
    if mounted || swapped {
        return Err(RaidWriteError::new(
            "resource.busy",
            "阵列已挂载或被用作交换空间",
            false,
        ));
    }
    Ok(())
}

fn ensure_no_filesystem_signature(
    context: &RaidWriteContext,
    array_path: &str,
) -> Result<(), RaidWriteError> {
    let blkid = context
        .blkid_path
        .as_ref()
        .ok_or_else(|| RaidWriteError::unavailable("blkid 工具不存在，不能安全缩容"))?;
    let status = run_probe(blkid, &["-p", array_path])?;
    if status.success() {
        return Err(RaidWriteError::new(
            "resource.busy",
            "阵列包含文件系统或其他数据签名；请先在系统外安全缩小上层数据结构",
            false,
        ));
    }
    if status.code() != Some(2) {
        return Err(RaidWriteError::new(
            "tool.failed",
            "blkid 无法确认阵列为空签名",
            true,
        ));
    }
    Ok(())
}

fn run_tool(path: &Path, arguments: &[String]) -> Result<(), RaidWriteError> {
    let mut child = Command::new(path)
        .args(arguments)
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .spawn()
        .map_err(tool_io_error)?;
    let started = Instant::now();
    loop {
        if let Some(status) = child.try_wait().map_err(tool_io_error)? {
            return if status.success() {
                Ok(())
            } else {
                Err(RaidWriteError::new(
                    "tool.failed",
                    "mdadm 返回失败状态",
                    false,
                ))
            };
        }
        if started.elapsed() >= TOOL_TIMEOUT {
            let _ = child.kill();
            let _ = child.wait();
            return Err(RaidWriteError::new("tool.timeout", "mdadm 执行超时", true));
        }
        thread::sleep(Duration::from_millis(50));
    }
}

fn run_probe(path: &Path, arguments: &[&str]) -> Result<ExitStatus, RaidWriteError> {
    let mut child = Command::new(path)
        .args(arguments)
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .spawn()
        .map_err(tool_io_error)?;
    let started = Instant::now();
    loop {
        if let Some(status) = child.try_wait().map_err(tool_io_error)? {
            return Ok(status);
        }
        if started.elapsed() >= TOOL_TIMEOUT {
            let _ = child.kill();
            let _ = child.wait();
            return Err(RaidWriteError::new("tool.timeout", "blkid 执行超时", true));
        }
        thread::sleep(Duration::from_millis(50));
    }
}

fn ensure_backup_directory(path: &Path) -> Result<(), RaidWriteError> {
    fs::create_dir_all(path).map_err(tool_io_error)?;
    let metadata = fs::symlink_metadata(path).map_err(tool_io_error)?;
    if metadata.file_type().is_symlink() || !metadata.is_dir() {
        return Err(RaidWriteError::new(
            "resource.identity_changed",
            "RAID 重塑备份目录不安全",
            false,
        ));
    }
    fs::set_permissions(path, fs::Permissions::from_mode(0o700)).map_err(tool_io_error)
}

fn find_array(id: Option<&str>) -> Result<RaidArrayInformation, RaidWriteError> {
    let id = id.ok_or_else(invalid_request)?;
    raid::inspect_arrays()
        .map_err(inventory_error)?
        .into_iter()
        .find(|array| array.id == id)
        .ok_or_else(|| RaidWriteError::new("resource.not_found", "目标阵列不存在", false))
}

fn find_array_by_members(ids: &[String]) -> Result<RaidArrayInformation, RaidWriteError> {
    let expected = as_set(ids);
    let devices = storage::inspect_block_devices().map_err(inventory_error)?;
    let matches = raid::inspect_arrays()
        .map_err(inventory_error)?
        .into_iter()
        .filter(|array| member_device_ids(array, &devices).ok().as_ref() == Some(&expected))
        .collect::<Vec<_>>();
    if matches.len() != 1 {
        return Err(verification_error("无法按成员身份唯一确认新阵列"));
    }
    Ok(matches.into_iter().next().expect("length checked"))
}

fn verify_array_member(
    array_id: Option<&str>,
    device_id: &str,
) -> Result<RaidExecutionResult, RaidWriteError> {
    let array = find_array(array_id)?;
    let devices = storage::inspect_block_devices().map_err(inventory_error)?;
    if !member_device_ids(&array, &devices)?.contains(device_id) {
        return Err(verification_error("新磁盘未出现在阵列成员中"));
    }
    Ok(result_for_array(&array))
}

fn verify_array_member_absent(
    array_id: Option<&str>,
    device_id: Option<&str>,
) -> Result<RaidExecutionResult, RaidWriteError> {
    let device_id = device_id.ok_or_else(invalid_request)?;
    let array = find_array(array_id)?;
    let devices = storage::inspect_block_devices().map_err(inventory_error)?;
    if member_device_ids(&array, &devices)?.contains(device_id) {
        return Err(verification_error("源磁盘仍在阵列成员中"));
    }
    Ok(result_for_array(&array))
}

fn result_for_array(array: &RaidArrayInformation) -> RaidExecutionResult {
    RaidExecutionResult {
        array_id: Some(array.id.clone()),
        in_progress: array.sync_action != "idle",
        progress_percentage: (array.sync_action == "idle").then_some(100),
    }
}

fn snapshot(array: &RaidArrayInformation) -> ArraySnapshot {
    ArraySnapshot {
        id: array.id.clone(),
        path: array.path.clone(),
    }
}

fn validate_array_name(name: &str) -> Result<(), RaidWriteError> {
    if name.is_empty()
        || name.len() > 32
        || !name.as_bytes()[0].is_ascii_alphabetic()
        || !name
            .bytes()
            .all(|byte| byte.is_ascii_alphanumeric() || byte == b'-')
    {
        return Err(invalid_request());
    }
    Ok(())
}

fn normalized_level(level: Option<&str>) -> Option<&'static str> {
    match level {
        Some("raid0" | "0") => Some("raid0"),
        Some("raid1" | "1") => Some("raid1"),
        Some("raid5" | "5") => Some("raid5"),
        Some("raid6" | "6") => Some("raid6"),
        Some("raid10" | "10") => Some("raid10"),
        _ => None,
    }
}

fn validate_device_count(level: &str, count: usize) -> Result<(), RaidWriteError> {
    let minimum = match level {
        "raid0" | "raid1" => 2,
        "raid5" => 3,
        "raid6" | "raid10" => 4,
        _ => return Err(invalid_request()),
    };
    if count < minimum || level == "raid10" && count & 1 != 0 || count > 64 {
        return Err(invalid_request());
    }
    Ok(())
}

fn as_set(values: &[String]) -> BTreeSet<String> {
    values.iter().cloned().collect()
}

fn find_tool(variable: &str, candidates: &[&str]) -> Option<PathBuf> {
    env::var_os(variable)
        .map(PathBuf::from)
        .filter(|path| path.is_file())
        .or_else(|| {
            candidates
                .iter()
                .map(PathBuf::from)
                .find(|path| path.is_file())
        })
}

fn invalid_request() -> RaidWriteError {
    RaidWriteError::new("request.invalid", "RAID 操作参数无效", false)
}

fn identity_error(message: impl Into<String>) -> RaidWriteError {
    RaidWriteError::new("resource.identity_changed", message, false)
}

fn inventory_error(error: io::Error) -> RaidWriteError {
    RaidWriteError::new("inventory.read_failed", error.to_string(), true)
}

fn tool_io_error(error: io::Error) -> RaidWriteError {
    RaidWriteError::new("tool.failed", error.to_string(), true)
}

fn verification_error(message: impl Into<String>) -> RaidWriteError {
    RaidWriteError::new("result.verification_failed", message, false)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn parameters(operation_id: &str, fencing_token: i64) -> RaidExecutionParameters {
        RaidExecutionParameters {
            operation_id: operation_id.to_owned(),
            idempotency_key: format!("key-{operation_id}"),
            fencing_token,
            array_id: None,
            array_name: Some("data".to_owned()),
            level: Some("raid1".to_owned()),
            device_ids: vec!["wwn:a".to_owned(), "wwn:b".to_owned()],
            source_device_id: None,
            target_device_count: None,
            expected_member_device_ids: Vec::new(),
            snapshot_fingerprint: "a".repeat(64),
        }
    }

    #[test]
    fn validates_supported_raid_member_counts() {
        assert!(validate_device_count("raid5", 3).is_ok());
        assert!(validate_device_count("raid6", 3).is_err());
        assert!(validate_device_count("raid10", 5).is_err());
    }

    #[test]
    fn rejects_unsafe_array_names() {
        assert!(validate_array_name("data-01").is_ok());
        assert!(validate_array_name("../md0").is_err());
        assert!(validate_array_name("1data").is_err());
    }

    #[test]
    fn rejects_an_operation_identifier_that_could_escape_the_backup_directory() {
        let mut parameters = parameters("../../../../../../../../tmp/escape-1", 1);

        assert_eq!(
            validate_common(&parameters).unwrap_err().code,
            "request.invalid"
        );
        parameters.operation_id = "00000000-0000-0000-0000-000000000001".to_owned();
        assert!(validate_common(&parameters).is_ok());
    }
}
