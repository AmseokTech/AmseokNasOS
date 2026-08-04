//--------------------------//
//--------从内核接口读取物理网络设备与地址信息---------//
//--------Reads physical network devices and addresses from kernel interfaces--------//
//-------------------------//
use std::collections::HashMap;
use std::fs;
use std::io;
use std::net::IpAddr;
use std::path::Path;

use nix::ifaddrs::getifaddrs;
use serde::Serialize;

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct NetworkInterfaceInformation {
    id: String,
    name: String,
    model: Option<String>,
    driver: Option<String>,
    mac_address: String,
    link_state: String,
    speed_mbps: Option<u64>,
    duplex: Option<String>,
    mtu: u64,
    configuration_mode: String,
    addresses: Vec<String>,
    gateway: Option<String>,
    dns_servers: Vec<String>,
}

pub fn inspect_interfaces() -> io::Result<Vec<NetworkInterfaceInformation>> {
    let addresses = interface_addresses()?;
    let gateways = ipv4_default_gateways();
    let dns_servers = dns_servers();
    let mut interfaces = Vec::new();

    for entry in fs::read_dir("/sys/class/net")? {
        let entry = entry?;
        let name = entry.file_name().to_string_lossy().into_owned();
        if name == "lo" || !is_physical_interface(&entry.path()) {
            continue;
        }

        let path = entry.path();
        let mac_address = read_trimmed(path.join("address")).unwrap_or_default();
        let if_index = read_trimmed(path.join("ifindex"))
            .and_then(|value| value.parse::<u32>().ok())
            .unwrap_or_default();
        let interface_addresses = addresses.get(&name).cloned().unwrap_or_default();
        let udev = read_udev_properties(if_index);
        let model = udev
            .get("ID_MODEL_FROM_DATABASE")
            .or_else(|| udev.get("ID_NET_NAME_PATH"))
            .cloned();
        let driver = fs::read_link(path.join("device/driver"))
            .ok()
            .and_then(|driver| {
                driver
                    .file_name()
                    .map(|name| name.to_string_lossy().into_owned())
            });

        interfaces.push(NetworkInterfaceInformation {
            id: format!("mac:{}", mac_address.to_ascii_lowercase()),
            name: name.clone(),
            model,
            driver,
            mac_address: mac_address.clone(),
            link_state: read_trimmed(path.join("operstate"))
                .unwrap_or_else(|| "unknown".to_owned()),
            speed_mbps: read_trimmed(path.join("speed")).and_then(|value| value.parse().ok()),
            duplex: read_trimmed(path.join("duplex")).map(|value| value.to_ascii_lowercase()),
            mtu: read_trimmed(path.join("mtu"))
                .and_then(|value| value.parse().ok())
                .unwrap_or_default(),
            configuration_mode: configuration_mode(
                if_index,
                &interface_addresses,
                &format!("mac:{}", mac_address.to_ascii_lowercase()),
                &crate::network_write::configuration_directory_from_environment(),
            ),
            addresses: interface_addresses,
            gateway: gateways.get(&name).cloned(),
            dns_servers: dns_servers.clone(),
        });
    }

    interfaces.sort_by(|left, right| left.name.cmp(&right.name));
    Ok(interfaces)
}

fn interface_addresses() -> io::Result<HashMap<String, Vec<String>>> {
    let mut interfaces: HashMap<String, Vec<String>> = HashMap::new();
    for address in getifaddrs().map_err(io::Error::other)? {
        let (Some(socket_address), Some(netmask)) = (address.address, address.netmask) else {
            continue;
        };
        let value = if let (Some(address), Some(mask)) =
            (socket_address.as_sockaddr_in(), netmask.as_sockaddr_in())
        {
            Some(format!(
                "{}/{}",
                address.ip(),
                ipv4_prefix_length(mask.ip().octets())
            ))
        } else if let (Some(address), Some(mask)) =
            (socket_address.as_sockaddr_in6(), netmask.as_sockaddr_in6())
        {
            Some(format!(
                "{}/{}",
                address.ip(),
                mask.ip()
                    .octets()
                    .iter()
                    .map(|octet| octet.count_ones())
                    .sum::<u32>()
            ))
        } else {
            None
        };
        if let Some(value) = value {
            interfaces
                .entry(address.interface_name)
                .or_default()
                .push(value);
        }
    }
    for addresses in interfaces.values_mut() {
        addresses.sort();
        addresses.dedup();
    }
    Ok(interfaces)
}

fn ipv4_prefix_length(mask: [u8; 4]) -> u32 {
    mask.iter().map(|octet| octet.count_ones()).sum()
}

fn ipv4_default_gateways() -> HashMap<String, String> {
    let routes = fs::read_to_string("/proc/net/route").unwrap_or_default();
    routes
        .lines()
        .skip(1)
        .filter_map(|line| {
            let fields: Vec<_> = line.split_whitespace().collect();
            if fields.len() < 4 || fields[1] != "00000000" {
                return None;
            }
            let flags = u16::from_str_radix(fields[3], 16).ok()?;
            if flags & 0x2 == 0 {
                return None;
            }
            let raw = u32::from_str_radix(fields[2], 16).ok()?;
            Some((
                fields[0].to_owned(),
                IpAddr::V4(raw.to_le_bytes().into()).to_string(),
            ))
        })
        .collect()
}

fn dns_servers() -> Vec<String> {
    fs::read_to_string("/etc/resolv.conf")
        .unwrap_or_default()
        .lines()
        .filter_map(|line| {
            let mut fields = line.split_whitespace();
            (fields.next()? == "nameserver")
                .then(|| fields.next().map(str::to_owned))
                .flatten()
        })
        .filter(|value| value.parse::<IpAddr>().is_ok())
        .collect()
}

fn configuration_mode(
    if_index: u32,
    addresses: &[String],
    interface_id: &str,
    managed_configuration_directory: &Path,
) -> String {
    if Path::new(&format!("/run/systemd/netif/leases/{if_index}")).is_file() {
        "dhcp".to_owned()
    } else if crate::network_write::managed_static_declaration(
        managed_configuration_directory,
        interface_id,
    ) {
        // 只认本系统按同一命名约定生成的静态文件，
        // 绝不能把 NetworkManager 或人工配置猜成静态模式
        "static".to_owned()
    } else if addresses.iter().any(|address| {
        !address.starts_with("169.254.") && !address.to_ascii_lowercase().starts_with("fe80:")
    }) {
        "unknown".to_owned()
    } else {
        "unconfigured".to_owned()
    }
}

fn is_physical_interface(path: &Path) -> bool {
    path.join("device").exists()
}

fn read_udev_properties(if_index: u32) -> HashMap<String, String> {
    fs::read_to_string(format!("/run/udev/data/n{if_index}"))
        .unwrap_or_default()
        .lines()
        .filter_map(|line| {
            let value = line.strip_prefix("E:")?;
            let (key, value) = value.split_once('=')?;
            Some((key.to_owned(), value.to_owned()))
        })
        .collect()
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
    fn converts_kernel_little_endian_default_gateway() {
        let raw = u32::from_str_radix("0101A8C0", 16).unwrap();

        assert_eq!(
            IpAddr::V4(raw.to_le_bytes().into()).to_string(),
            "192.168.1.1"
        );
    }

    #[test]
    fn counts_ipv4_prefix_bits() {
        assert_eq!(ipv4_prefix_length([255, 255, 255, 0]), 24);
    }

    #[test]
    fn reports_static_only_for_the_matching_managed_declaration() {
        let directory =
            crate::network_write::test_support::temporary_directory("inventory-network-static");
        let interface_id = "mac:aa:bb:cc:dd:ee:ff";
        let managed_name =
            crate::network_write::managed_file_name(interface_id).expect("受管文件名应当有效");
        fs::write(
            directory.join(managed_name),
            "[Match]\nMACAddress=aa:bb:cc:dd:ee:ff\n\n[Network]\nDHCP=no\nAddress=192.168.1.10/24\n",
        )
        .expect("测试受管文件应当写入成功");
        let addresses = vec!["192.168.1.10/24".to_owned()];

        assert_eq!(
            configuration_mode(u32::MAX, &addresses, interface_id, &directory),
            "static"
        );
        assert_eq!(
            configuration_mode(u32::MAX, &addresses, "mac:00:11:22:33:44:55", &directory,),
            "unknown"
        );
    }

    #[test]
    fn keeps_the_existing_mode_when_no_managed_file_exists() {
        let directory =
            crate::network_write::test_support::temporary_directory("inventory-network-unmanaged");

        assert_eq!(
            configuration_mode(
                u32::MAX,
                &["192.168.1.10/24".to_owned()],
                "mac:aa:bb:cc:dd:ee:ff",
                &directory,
            ),
            "unknown"
        );
        assert_eq!(
            configuration_mode(
                u32::MAX,
                &["169.254.1.5/16".to_owned()],
                "mac:aa:bb:cc:dd:ee:ff",
                &directory,
            ),
            "unconfigured"
        );
    }
}
