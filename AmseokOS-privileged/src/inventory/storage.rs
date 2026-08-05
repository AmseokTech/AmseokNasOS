//--------------------------//
//--------从内核与 udev 只读接口枚举物理块设备---------//
//--------Enumerates physical block devices from read-only kernel and udev interfaces--------//
//-------------------------//
use std::collections::{HashMap, HashSet, VecDeque};
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
    identity_conflict: bool,
    topology_complete: bool,
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
    in_use: bool,
    dependent_devices: Vec<BlockDependencyInformation>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct BlockPartitionInformation {
    name: String,
    path: String,
    size_bytes: u64,
    mount_points: Vec<String>,
    topology_complete: bool,
    system_device: bool,
    swap: bool,
    raid_member: bool,
    in_use: bool,
    dependent_devices: Vec<BlockDependencyInformation>,
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct BlockDependencyInformation {
    name: String,
    path: String,
    kind: String,
    mount_points: Vec<String>,
    swap: bool,
}

#[derive(Debug)]
struct BlockNode {
    name: String,
    kind: String,
    mount_points: Vec<String>,
    swap: bool,
    holders: Vec<String>,
}

#[derive(Debug)]
struct BlockSafetySummary {
    topology_complete: bool,
    system_device: bool,
    swap: bool,
    raid_member: bool,
    in_use: bool,
    dependent_devices: Vec<BlockDependencyInformation>,
}

#[derive(Debug)]
struct BlockTopology {
    nodes: HashMap<String, BlockNode>,
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
    let topology = BlockTopology::read(&entries, &mounts, &swap_names);
    let mut devices = Vec::new();

    for (name, path) in &entries {
        if path.join("partition").exists() || !is_physical_device(path) {
            continue;
        }

        let device_number = read_trimmed(path.join("dev")).unwrap_or_default();
        let properties = read_udev_properties(udev_data, &device_number);
        let (id, stable) = stable_device_id(&properties, &device_number);
        let partition_entries = entries
            .iter()
            .filter(|(_, child_path)| {
                partition_parent_name(child_path).as_deref() == Some(name.as_str())
            })
            .collect::<Vec<_>>();
        let mut partitions = partition_entries
            .iter()
            .map(|(partition_name, partition_path)| {
                let partition_number = read_trimmed(partition_path.join("dev")).unwrap_or_default();
                let mount_points = mounts.get(&partition_number).cloned().unwrap_or_default();
                let safety = topology.summarize(std::slice::from_ref(partition_name), false);
                BlockPartitionInformation {
                    name: (*partition_name).clone(),
                    path: format!("/dev/{partition_name}"),
                    size_bytes: block_size_bytes(partition_path),
                    mount_points,
                    topology_complete: safety.topology_complete,
                    system_device: safety.system_device,
                    swap: safety.swap,
                    raid_member: safety.raid_member,
                    in_use: safety.in_use,
                    dependent_devices: safety.dependent_devices,
                }
            })
            .collect::<Vec<_>>();
        partitions.sort_by(|left, right| left.name.cmp(&right.name));

        let mount_points = mounts.get(&device_number).cloned().unwrap_or_default();
        let roots = std::iter::once(name.clone())
            .chain(
                partition_entries
                    .iter()
                    .map(|(partition_name, _)| (*partition_name).clone()),
            )
            .collect::<Vec<_>>();
        let safety = topology.summarize(&roots, !partition_entries.is_empty());

        devices.push(BlockDeviceInformation {
            id,
            stable,
            identity_conflict: false,
            topology_complete: safety.topology_complete,
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
            system_device: safety.system_device,
            swap: safety.swap,
            raid_member: safety.raid_member,
            in_use: safety.in_use,
            dependent_devices: safety.dependent_devices,
        });
    }

    mark_identity_conflicts(&mut devices);
    devices.sort_by(|left, right| left.name.cmp(&right.name));
    Ok(devices)
}

impl BlockTopology {
    fn read(
        entries: &[(String, PathBuf)],
        mounts: &HashMap<String, Vec<String>>,
        swap_names: &[String],
    ) -> Self {
        let swap_names = swap_names
            .iter()
            .map(String::as_str)
            .collect::<HashSet<_>>();
        let nodes = entries
            .iter()
            .map(|(name, path)| {
                let device_number = read_trimmed(path.join("dev")).unwrap_or_default();
                let device_mapper_name = read_trimmed(path.join("dm/name"));
                let aliases = std::iter::once(name.as_str())
                    .chain(device_mapper_name.as_deref())
                    .collect::<Vec<_>>();
                let node = BlockNode {
                    name: name.clone(),
                    kind: block_device_kind(path),
                    mount_points: mounts.get(&device_number).cloned().unwrap_or_default(),
                    swap: aliases.iter().any(|alias| swap_names.contains(alias)),
                    holders: holder_names(path),
                };
                (name.clone(), node)
            })
            .collect();
        Self { nodes }
    }

    fn summarize(&self, roots: &[String], structurally_in_use: bool) -> BlockSafetySummary {
        let root_names = roots.iter().map(String::as_str).collect::<HashSet<_>>();
        let mut queue = roots.iter().cloned().collect::<VecDeque<_>>();
        let mut related_names = HashSet::new();
        let mut topology_complete = true;

        // holders 指向消费当前块设备的上层设备，必须传递遍历才能保护位于 MD、
        // dm-crypt 或 LVM 之下的系统盘；集合同时防止异常 sysfs 关系形成循环
        while let Some(name) = queue.pop_front() {
            if !related_names.insert(name.clone()) {
                continue;
            }
            if let Some(node) = self.nodes.get(&name) {
                queue.extend(node.holders.iter().cloned());
            } else {
                topology_complete = false;
            }
        }

        let related_nodes = related_names
            .iter()
            .filter_map(|name| self.nodes.get(name))
            .collect::<Vec<_>>();
        let system_device = related_nodes
            .iter()
            .flat_map(|node| &node.mount_points)
            .any(|mount_point| is_protected_mount(mount_point));
        let swap = related_nodes.iter().any(|node| node.swap);
        let mut dependent_devices = related_nodes
            .iter()
            .filter(|node| !root_names.contains(node.name.as_str()))
            .map(|node| BlockDependencyInformation {
                name: node.name.clone(),
                path: format!("/dev/{}", node.name),
                kind: node.kind.clone(),
                mount_points: node.mount_points.clone(),
                swap: node.swap,
            })
            .collect::<Vec<_>>();
        dependent_devices.sort_by(|left, right| left.name.cmp(&right.name));
        let raid_member = dependent_devices
            .iter()
            .any(|dependency| dependency.kind == "raid");
        let has_mount = related_nodes
            .iter()
            .any(|node| !node.mount_points.is_empty());

        BlockSafetySummary {
            topology_complete,
            system_device,
            swap,
            raid_member,
            in_use: structurally_in_use
                || !topology_complete
                || has_mount
                || swap
                || !dependent_devices.is_empty(),
            dependent_devices,
        }
    }
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

fn holder_names(path: &Path) -> Vec<String> {
    fs::read_dir(path.join("holders"))
        .ok()
        .into_iter()
        .flatten()
        .filter_map(Result::ok)
        .map(|entry| entry.file_name().to_string_lossy().into_owned())
        .collect()
}

fn block_device_kind(path: &Path) -> String {
    if path.join("md").is_dir() {
        return "raid".to_owned();
    }
    if path.join("dm").is_dir() {
        let uuid = read_trimmed(path.join("dm/uuid"))
            .unwrap_or_default()
            .to_ascii_uppercase();
        return device_mapper_kind(&uuid).to_owned();
    }
    if path.join("partition").exists() {
        return "partition".to_owned();
    }
    if path
        .file_name()
        .is_some_and(|name| name.to_string_lossy().starts_with("loop"))
    {
        return "loop".to_owned();
    }
    "block".to_owned()
}

fn device_mapper_kind(uuid: &str) -> &'static str {
    if uuid.starts_with("CRYPT-") {
        "encrypted"
    } else if uuid.starts_with("LVM-") {
        "lvm"
    } else {
        "device-mapper"
    }
}

fn is_protected_mount(mount_point: &str) -> bool {
    mount_point == "/"
        || mount_point == "/boot"
        || mount_point.starts_with("/boot/")
        || mount_point == "/var/lib/amseoknas"
        || mount_point.starts_with("/var/lib/amseoknas/")
        || mount_point == "/etc/amseoknas"
        || mount_point.starts_with("/etc/amseoknas/")
}

fn mark_identity_conflicts(devices: &mut [BlockDeviceInformation]) {
    let conflicts = stable_id_conflicts(
        devices
            .iter()
            .map(|device| (device.id.as_str(), device.stable)),
    );
    for device in devices {
        if conflicts.contains(device.id.as_str()) {
            device.stable = false;
            device.identity_conflict = true;
        }
    }
}

fn stable_id_conflicts<'a>(identities: impl Iterator<Item = (&'a str, bool)>) -> HashSet<String> {
    let mut counts = HashMap::new();
    for (id, _) in identities.filter(|(_, stable)| *stable) {
        *counts.entry(id.to_owned()).or_insert(0_u32) += 1;
    }
    counts
        .into_iter()
        .filter_map(|(id, count)| (count > 1).then_some(id))
        .collect()
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
    use std::os::unix::fs::symlink;
    use std::sync::atomic::{AtomicU64, Ordering};

    use super::*;

    static TEST_DIRECTORY_SEQUENCE: AtomicU64 = AtomicU64::new(0);

    struct TestDirectory(PathBuf);

    impl TestDirectory {
        fn new() -> Self {
            let sequence = TEST_DIRECTORY_SEQUENCE.fetch_add(1, Ordering::Relaxed);
            let path = std::env::temp_dir().join(format!(
                "amseoknas-storage-{}-{sequence}",
                std::process::id()
            ));
            fs::create_dir_all(&path).unwrap();
            Self(path)
        }

        fn path(&self) -> &Path {
            &self.0
        }
    }

    impl Drop for TestDirectory {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.0);
        }
    }

    fn write_file(path: impl AsRef<Path>, content: &str) {
        let path = path.as_ref();
        fs::create_dir_all(path.parent().unwrap()).unwrap();
        fs::write(path, content).unwrap();
    }

    fn node(
        name: &str,
        kind: &str,
        mount_points: &[&str],
        swap: bool,
        holders: &[&str],
    ) -> (String, BlockNode) {
        (
            name.to_owned(),
            BlockNode {
                name: name.to_owned(),
                kind: kind.to_owned(),
                mount_points: mount_points
                    .iter()
                    .map(|value| (*value).to_owned())
                    .collect(),
                swap,
                holders: holders.iter().map(|value| (*value).to_owned()).collect(),
            },
        )
    }

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

    #[test]
    fn protects_a_system_disk_through_encryption_and_lvm_holders() {
        let topology = BlockTopology {
            nodes: HashMap::from([
                node("sda2", "partition", &[], false, &["dm-0"]),
                node("dm-0", "encrypted", &[], false, &["dm-1"]),
                node("dm-1", "lvm", &["/"], false, &[]),
            ]),
        };

        let safety = topology.summarize(&["sda2".to_owned()], false);

        assert!(safety.system_device);
        assert!(safety.topology_complete);
        assert!(safety.in_use);
        assert!(!safety.raid_member);
        assert_eq!(
            safety
                .dependent_devices
                .iter()
                .map(|device| (device.name.as_str(), device.kind.as_str()))
                .collect::<Vec<_>>(),
            [("dm-0", "encrypted"), ("dm-1", "lvm")]
        );
    }

    #[test]
    fn follows_md_and_encryption_without_mislabeling_a_data_mount_as_system() {
        let topology = BlockTopology {
            nodes: HashMap::from([
                node("sdb1", "partition", &[], false, &["md0"]),
                node("md0", "raid", &[], false, &["dm-2"]),
                node("dm-2", "encrypted", &["/srv/data"], false, &[]),
            ]),
        };

        let safety = topology.summarize(&["sdb1".to_owned()], false);

        assert!(!safety.system_device);
        assert!(safety.topology_complete);
        assert!(safety.in_use);
        assert!(safety.raid_member);
    }

    #[test]
    fn propagates_swap_usage_through_a_device_mapper_holder() {
        let topology = BlockTopology {
            nodes: HashMap::from([
                node("sdc1", "partition", &[], false, &["dm-3"]),
                node("dm-3", "encrypted", &[], true, &[]),
            ]),
        };

        let safety = topology.summarize(&["sdc1".to_owned()], false);

        assert!(safety.swap);
        assert!(safety.topology_complete);
        assert!(safety.in_use);
    }

    #[test]
    fn classifies_known_device_mapper_owners() {
        assert_eq!(device_mapper_kind("CRYPT-LUKS2-TEST"), "encrypted");
        assert_eq!(device_mapper_kind("LVM-TEST"), "lvm");
        assert_eq!(device_mapper_kind("MPATH-TEST"), "device-mapper");
    }

    #[test]
    fn duplicate_hardware_identity_is_not_treated_as_stable() {
        let conflicts = stable_id_conflicts(
            [
                ("serial:duplicate", true),
                ("serial:duplicate", true),
                ("block:8:32", false),
            ]
            .into_iter(),
        );

        assert_eq!(conflicts, HashSet::from(["serial:duplicate".to_owned()]));
    }

    #[test]
    fn sysfs_inventory_protects_a_physical_disk_below_crypt_and_lvm() {
        let root = TestDirectory::new();
        let class_block = root.path().join("sys/class/block");
        let physical_disk = root.path().join("devices/pci0000/block/sda");
        let partition = physical_disk.join("sda2");
        let virtual_block = root.path().join("devices/virtual/block");
        let encrypted = virtual_block.join("dm-0");
        let logical_volume = virtual_block.join("dm-1");

        for path in [
            physical_disk.join("device"),
            physical_disk.join("holders"),
            partition.join("holders"),
            encrypted.join("holders"),
            encrypted.join("dm"),
            logical_volume.join("holders"),
            logical_volume.join("dm"),
            class_block.clone(),
        ] {
            fs::create_dir_all(path).unwrap();
        }
        symlink(&physical_disk, class_block.join("sda")).unwrap();
        symlink(&partition, class_block.join("sda2")).unwrap();
        symlink(&encrypted, class_block.join("dm-0")).unwrap();
        symlink(&logical_volume, class_block.join("dm-1")).unwrap();
        symlink(&encrypted, partition.join("holders/dm-0")).unwrap();
        symlink(&logical_volume, encrypted.join("holders/dm-1")).unwrap();

        write_file(physical_disk.join("dev"), "8:0\n");
        write_file(physical_disk.join("size"), "8192\n");
        write_file(physical_disk.join("queue/logical_block_size"), "512\n");
        write_file(physical_disk.join("queue/physical_block_size"), "4096\n");
        write_file(physical_disk.join("queue/rotational"), "1\n");
        write_file(physical_disk.join("removable"), "0\n");
        write_file(physical_disk.join("ro"), "0\n");
        write_file(physical_disk.join("device/model"), "Test Disk\n");
        write_file(partition.join("dev"), "8:2\n");
        write_file(partition.join("partition"), "2\n");
        write_file(partition.join("size"), "4096\n");
        write_file(encrypted.join("dev"), "253:0\n");
        write_file(encrypted.join("dm/name"), "cryptroot\n");
        write_file(encrypted.join("dm/uuid"), "CRYPT-LUKS2-TEST\n");
        write_file(logical_volume.join("dev"), "253:1\n");
        write_file(logical_volume.join("dm/name"), "root\n");
        write_file(logical_volume.join("dm/uuid"), "LVM-TEST\n");

        let udev_data = root.path().join("run/udev/data");
        write_file(udev_data.join("b8:0"), "E:ID_SERIAL_SHORT=DISK-1\n");
        let mount_info = root.path().join("proc/mountinfo");
        write_file(
            &mount_info,
            "36 25 253:1 / / rw,relatime - ext4 /dev/mapper/root rw\n",
        );
        let swaps = root.path().join("proc/swaps");
        write_file(&swaps, "Filename Type Size Used Priority\n");

        let devices =
            inspect_block_devices_at(&class_block, &udev_data, &mount_info, &swaps).unwrap();

        let device = devices.first().unwrap();
        assert_eq!(device.name, "sda");
        assert!(device.system_device);
        assert!(device.topology_complete);
        assert!(device.in_use);
        assert_eq!(
            device
                .dependent_devices
                .iter()
                .map(|dependency| dependency.kind.as_str())
                .collect::<Vec<_>>(),
            ["encrypted", "lvm"]
        );
        let partition = device.partitions.first().unwrap();
        assert!(partition.system_device);
        assert!(partition.topology_complete);
        assert!(partition.in_use);
    }

    #[test]
    fn missing_holder_node_fails_topology_completeness_closed() {
        let topology = BlockTopology {
            nodes: HashMap::from([node("sdd1", "partition", &[], false, &["missing-holder"])]),
        };

        let safety = topology.summarize(&["sdd1".to_owned()], false);

        assert!(!safety.topology_complete);
        assert!(safety.in_use);
    }
}
