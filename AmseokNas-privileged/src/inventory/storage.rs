//--------------------------//
//--------从内核与 udev 只读接口枚举物理块设备---------//
//--------Enumerates physical block devices from read-only kernel and udev interfaces--------//
//-------------------------//
use std::collections::HashMap;
use std::fs;
use std::io;
use std::path::{Path, PathBuf};

use serde::Serialize;

const SYS_CLASS_BLOCK: &str = "/sys/class/block";
const UDEV_DATA: &str = "/run/udev/data";
const MOUNT_INFO: &str = "/proc/self/mountinfo";
const SWAPS: &str = "/proc/swaps";

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct BlockDeviceInformation {
    id: String,
    stable: bool,
    name: String,
    path: String,
    model: Option<String>,
    serial_number: Option<String>,
    wwn: Option<String>,
    size_bytes: u64,
    logical_sector_bytes: u64,
    physical_sector_bytes: u64,
    rotational: bool,
    removable: bool,
    read_only: bool,
    partitions: Vec<BlockPartitionInformation>,
    mount_points: Vec<String>,
    system_device: bool,
    swap: bool,
    raid_member: bool,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct BlockPartitionInformation {
    name: String,
    path: String,
    size_bytes: u64,
    mount_points: Vec<String>,
    swap: bool,
    raid_member: bool,
}

pub fn inspect_block_devices() -> io::Result<Vec<BlockDeviceInformation>> {
    inspect_block_devices_at(
        Path::new(SYS_CLASS_BLOCK),
        Path::new(UDEV_DATA),
        Path::new(MOUNT_INFO),
        Path::new(SWAPS),
    )
}

fn inspect_block_devices_at(
    sys_class_block: &Path,
    udev_data: &Path,
    mount_info: &Path,
    swaps: &Path,
) -> io::Result<Vec<BlockDeviceInformation>> {
    let mounts = parse_mount_info(&fs::read_to_string(mount_info).unwrap_or_default());
    let swap_names = parse_swap_names(&fs::read_to_string(swaps).unwrap_or_default());
    let entries = block_entries(sys_class_block)?;
    let mut devices = Vec::new();

    for (name, path) in &entries {
        if path.join("partition").exists() || !is_physical_device(path) {
            continue;
        }

        let device_number = read_trimmed(path.join("dev")).unwrap_or_default();
        let properties = read_udev_properties(udev_data, &device_number);
        let (id, stable) = stable_device_id(&properties, &device_number);
        let mut partitions = entries
            .iter()
            .filter(|(_, child_path)| {
                partition_parent_name(child_path).as_deref() == Some(name.as_str())
            })
            .map(|(partition_name, partition_path)| {
                let partition_number = read_trimmed(partition_path.join("dev")).unwrap_or_default();
                let mount_points = mounts.get(&partition_number).cloned().unwrap_or_default();
                BlockPartitionInformation {
                    name: partition_name.clone(),
                    path: format!("/dev/{partition_name}"),
                    size_bytes: block_size_bytes(partition_path),
                    mount_points,
                    swap: swap_names.iter().any(|value| value == partition_name),
                    raid_member: has_md_holder(partition_path),
                }
            })
            .collect::<Vec<_>>();
        partitions.sort_by(|left, right| left.name.cmp(&right.name));

        let mount_points = mounts.get(&device_number).cloned().unwrap_or_default();
        let system_device = mount_points
            .iter()
            .chain(
                partitions
                    .iter()
                    .flat_map(|partition| &partition.mount_points),
            )
            .any(|mount_point| mount_point == "/" || mount_point.starts_with("/boot"));
        let swap = swap_names.iter().any(|value| value == name)
            || partitions.iter().any(|partition| partition.swap);
        let raid_member =
            has_md_holder(path) || partitions.iter().any(|partition| partition.raid_member);

        devices.push(BlockDeviceInformation {
            id,
            stable,
            name: name.clone(),
            path: format!("/dev/{name}"),
            model: property(&properties, "ID_MODEL")
                .or_else(|| read_trimmed(path.join("device/model"))),
            serial_number: property(&properties, "ID_SERIAL_SHORT")
                .or_else(|| property(&properties, "ID_SERIAL")),
            wwn: property(&properties, "ID_WWN_WITH_EXTENSION")
                .or_else(|| property(&properties, "ID_WWN")),
            size_bytes: block_size_bytes(path),
            logical_sector_bytes: read_u64(path.join("queue/logical_block_size")).unwrap_or(512),
            physical_sector_bytes: read_u64(path.join("queue/physical_block_size")).unwrap_or(512),
            rotational: read_u64(path.join("queue/rotational")) == Some(1),
            removable: read_u64(path.join("removable")) == Some(1),
            read_only: read_u64(path.join("ro")) == Some(1),
            partitions,
            mount_points,
            system_device,
            swap,
            raid_member,
        });
    }

    devices.sort_by(|left, right| left.name.cmp(&right.name));
    Ok(devices)
}

fn block_entries(sys_class_block: &Path) -> io::Result<Vec<(String, PathBuf)>> {
    fs::read_dir(sys_class_block)?
        .map(|entry| {
            let entry = entry?;
            Ok((
                entry.file_name().to_string_lossy().into_owned(),
                entry.path(),
            ))
        })
        .collect()
}

fn is_physical_device(path: &Path) -> bool {
    let Ok(canonical_path) = fs::canonicalize(path) else {
        return false;
    };
    path.join("device").exists()
        && !canonical_path
            .components()
            .any(|component| component.as_os_str() == "virtual")
}

fn partition_parent_name(path: &Path) -> Option<String> {
    path.join("partition").exists().then_some(())?;
    fs::canonicalize(path)
        .ok()?
        .parent()?
        .file_name()
        .map(|name| name.to_string_lossy().into_owned())
}

fn block_size_bytes(path: &Path) -> u64 {
    read_u64(path.join("size"))
        .unwrap_or_default()
        .saturating_mul(512)
}

fn has_md_holder(path: &Path) -> bool {
    fs::read_dir(path.join("holders"))
        .ok()
        .into_iter()
        .flatten()
        .filter_map(Result::ok)
        .any(|entry| entry.file_name().to_string_lossy().starts_with("md"))
}

fn read_udev_properties(path: &Path, device_number: &str) -> HashMap<String, String> {
    fs::read_to_string(path.join(format!("b{device_number}")))
        .unwrap_or_default()
        .lines()
        .filter_map(|line| {
            let value = line.strip_prefix("E:")?;
            let (key, value) = value.split_once('=')?;
            Some((key.to_owned(), value.to_owned()))
        })
        .collect()
}

fn stable_device_id(properties: &HashMap<String, String>, device_number: &str) -> (String, bool) {
    for (key, prefix) in [
        ("ID_WWN_WITH_EXTENSION", "wwn"),
        ("ID_WWN", "wwn"),
        ("ID_SERIAL_SHORT", "serial"),
        ("ID_SERIAL", "serial"),
    ] {
        if let Some(value) = property(properties, key) {
            return (format!("{prefix}:{}", value.to_ascii_lowercase()), true);
        }
    }
    if let Some(value) = property(properties, "ID_PATH") {
        return (format!("path:{}", value.to_ascii_lowercase()), false);
    }
    (format!("block:{device_number}"), false)
}

fn property(properties: &HashMap<String, String>, name: &str) -> Option<String> {
    properties
        .get(name)
        .map(|value| value.trim().to_owned())
        .filter(|value| !value.is_empty())
}

fn parse_mount_info(content: &str) -> HashMap<String, Vec<String>> {
    let mut mounts: HashMap<String, Vec<String>> = HashMap::new();
    for line in content.lines() {
        let fields = line.split_whitespace().collect::<Vec<_>>();
        if fields.len() < 5 || !fields[2].contains(':') {
            continue;
        }
        mounts
            .entry(fields[2].to_owned())
            .or_default()
            .push(decode_mount_field(fields[4]));
    }
    mounts
}

fn decode_mount_field(value: &str) -> String {
    value
        .replace("\\040", " ")
        .replace("\\011", "\t")
        .replace("\\012", "\n")
        .replace("\\134", "\\")
}

fn parse_swap_names(content: &str) -> Vec<String> {
    content
        .lines()
        .skip(1)
        .filter_map(|line| {
            Path::new(line.split_whitespace().next()?)
                .file_name()
                .map(|name| name.to_string_lossy().into_owned())
        })
        .collect()
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
    fn prefers_wwn_over_serial_for_stable_ids() {
        let properties = HashMap::from([
            ("ID_SERIAL_SHORT".to_owned(), "SERIAL-1".to_owned()),
            ("ID_WWN".to_owned(), "0x5000CCA123".to_owned()),
        ]);

        assert_eq!(
            stable_device_id(&properties, "8:0"),
            ("wwn:0x5000cca123".to_owned(), true)
        );
    }

    #[test]
    fn parses_mount_points_by_kernel_device_number() {
        let mounts = parse_mount_info(
            "36 25 8:2 / / rw,relatime - ext4 /dev/sda2 rw\n\
             37 25 8:1 / /boot\\040efi rw,relatime - vfat /dev/sda1 rw\n",
        );

        assert_eq!(mounts["8:2"], vec!["/".to_owned()]);
        assert_eq!(mounts["8:1"], vec!["/boot efi".to_owned()]);
    }

    #[test]
    fn device_path_is_descriptive_but_not_a_stable_identity() {
        let properties =
            HashMap::from([("ID_PATH".to_owned(), "pci-0000:01:00.0-ata-1".to_owned())]);

        assert_eq!(
            stable_device_id(&properties, "8:0"),
            ("path:pci-0000:01:00.0-ata-1".to_owned(), false)
        );
    }

    #[test]
    fn unstable_fallback_never_claims_a_kernel_number_is_stable() {
        assert_eq!(
            stable_device_id(&HashMap::new(), "8:16"),
            ("block:8:16".to_owned(), false)
        );
    }
}
