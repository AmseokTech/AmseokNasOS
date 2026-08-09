//--------------------------//
//--------从 md 内核接口只读枚举软件 RAID 阵列---------//
//--------Enumerates software RAID arrays from read-only MD kernel interfaces--------//
//-------------------------//
use std::fs;
use std::io;
use std::path::Path;

use serde::Serialize;

const SYS_CLASS_BLOCK: &str = "/sys/class/block";

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct RaidArrayInformation {
    pub(crate) id: String,
    pub(crate) name: String,
    pub(crate) path: String,
    pub(crate) uuid: Option<String>,
    pub(crate) level: String,
    state: String,
    metadata_version: Option<String>,
    size_bytes: u64,
    pub(crate) configured_device_count: u64,
    pub(crate) degraded_device_count: u64,
    pub(crate) sync_action: String,
    sync_completed_sectors: Option<u64>,
    sync_total_sectors: Option<u64>,
    pub(crate) members: Vec<RaidMemberInformation>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub(crate) struct RaidMemberInformation {
    pub(crate) name: String,
    pub(crate) path: String,
    pub(crate) state: String,
    pub(crate) slot: Option<i32>,
}

pub fn inspect_arrays() -> io::Result<Vec<RaidArrayInformation>> {
    inspect_arrays_at(Path::new(SYS_CLASS_BLOCK))
}

fn inspect_arrays_at(sys_class_block: &Path) -> io::Result<Vec<RaidArrayInformation>> {
    let mut arrays = Vec::new();
    for entry in fs::read_dir(sys_class_block)? {
        let entry = entry?;
        let path = entry.path();
        let md_path = path.join("md");
        if !md_path.is_dir() {
            continue;
        }

        let name = entry.file_name().to_string_lossy().into_owned();
        let uuid = read_trimmed(md_path.join("uuid"));
        let (sync_completed_sectors, sync_total_sectors) =
            read_trimmed(md_path.join("sync_completed"))
                .as_deref()
                .and_then(parse_sync_completed)
                .map_or((None, None), |(completed, total)| {
                    (Some(completed), Some(total))
                });
        let mut members = inspect_members(&md_path);
        members.sort_by(|left, right| {
            left.slot
                .cmp(&right.slot)
                .then_with(|| left.name.cmp(&right.name))
        });

        arrays.push(RaidArrayInformation {
            id: uuid
                .as_ref()
                .map_or_else(|| format!("md-name:{name}"), |value| format!("md:{value}")),
            name: name.clone(),
            path: format!("/dev/{name}"),
            uuid,
            level: read_trimmed(md_path.join("level")).unwrap_or_else(|| "unknown".to_owned()),
            state: read_trimmed(md_path.join("array_state"))
                .unwrap_or_else(|| "unknown".to_owned()),
            metadata_version: read_trimmed(md_path.join("metadata_version")),
            size_bytes: read_u64(path.join("size"))
                .unwrap_or_default()
                .saturating_mul(512),
            configured_device_count: read_u64(md_path.join("raid_disks")).unwrap_or_default(),
            degraded_device_count: read_u64(md_path.join("degraded")).unwrap_or_default(),
            sync_action: read_trimmed(md_path.join("sync_action"))
                .unwrap_or_else(|| "idle".to_owned()),
            sync_completed_sectors,
            sync_total_sectors,
            members,
        });
    }

    arrays.sort_by(|left, right| left.name.cmp(&right.name));
    Ok(arrays)
}

fn inspect_members(md_path: &Path) -> Vec<RaidMemberInformation> {
    fs::read_dir(md_path)
        .ok()
        .into_iter()
        .flatten()
        .filter_map(Result::ok)
        .filter(|entry| entry.file_name().to_string_lossy().starts_with("dev-"))
        .filter_map(|entry| {
            let path = entry.path();
            let name = fs::read_link(path.join("block"))
                .ok()
                .and_then(|value| value.file_name().map(|name| name.to_owned()))
                .map(|name| name.to_string_lossy().into_owned())
                .or_else(|| {
                    entry
                        .file_name()
                        .to_string_lossy()
                        .strip_prefix("dev-")
                        .map(str::to_owned)
                })?;
            Some(RaidMemberInformation {
                path: format!("/dev/{name}"),
                name,
                state: read_trimmed(path.join("state")).unwrap_or_else(|| "unknown".to_owned()),
                slot: read_trimmed(path.join("slot")).and_then(|value| value.parse().ok()),
            })
        })
        .collect()
}

fn parse_sync_completed(value: &str) -> Option<(u64, u64)> {
    let (completed, total) = value.split_once('/')?;
    Some((completed.trim().parse().ok()?, total.trim().parse().ok()?))
}

fn read_u64(path: impl AsRef<Path>) -> Option<u64> {
    read_trimmed(path)?.parse().ok()
}

fn read_trimmed(path: impl AsRef<Path>) -> Option<String> {
    fs::read_to_string(path)
        .ok()
        .map(|value| value.trim().to_owned())
        .filter(|value| !value.is_empty())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_kernel_sync_progress_without_guessing_percentages() {
        assert_eq!(parse_sync_completed("1024 / 4096"), Some((1024, 4096)));
        assert_eq!(parse_sync_completed("none"), None);
    }
}
