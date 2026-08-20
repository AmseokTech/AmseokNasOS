//--------------------------//
//--------从内核计数器采集无状态实时性能快照---------//
//--------Collects stateless performance snapshots from kernel counters--------//
//-------------------------//
use std::collections::HashMap;
use std::fs;
use std::io;
use std::path::Path;
use std::time::{SystemTime, UNIX_EPOCH};

use serde::Serialize;

use super::system::{average_cpu_frequency, parse_cpu_information};

const KERNEL_SECTOR_BYTES: u64 = 512;

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct SystemPerformanceSnapshot {
    captured_at_unix_milliseconds: u64,
    cpu: CpuPerformanceSnapshot,
    memory: MemoryPerformanceSnapshot,
    disks: Vec<DiskPerformanceSnapshot>,
    networks: Vec<NetworkPerformanceSnapshot>,
    gpus: Vec<GpuPerformanceSnapshot>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct CpuPerformanceSnapshot {
    model: String,
    physical_core_count: usize,
    logical_processor_count: usize,
    current_frequency_mhz: Option<u64>,
    maximum_frequency_mhz: Option<u64>,
    l1_cache_bytes: Option<u64>,
    l2_cache_bytes: Option<u64>,
    l3_cache_bytes: Option<u64>,
    aggregate: CpuTimeCounter,
    logical_processors: Vec<CpuTimeCounter>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct CpuTimeCounter {
    id: String,
    total_ticks: u64,
    idle_ticks: u64,
}

#[derive(Debug, Default, Serialize)]
#[serde(rename_all = "camelCase")]
struct MemoryPerformanceSnapshot {
    total_bytes: u64,
    used_bytes: u64,
    available_bytes: u64,
    cached_bytes: u64,
    swap_total_bytes: u64,
    swap_used_bytes: u64,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct DiskPerformanceSnapshot {
    id: String,
    name: String,
    model: Option<String>,
    total_bytes: u64,
    read_bytes: u64,
    written_bytes: u64,
    busy_milliseconds: u64,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct NetworkPerformanceSnapshot {
    id: String,
    name: String,
    model: Option<String>,
    speed_mbps: Option<u64>,
    received_bytes: u64,
    transmitted_bytes: u64,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct GpuPerformanceSnapshot {
    id: String,
    name: String,
    driver: Option<String>,
    memory_total_bytes: Option<u64>,
    memory_used_bytes: Option<u64>,
    core_utilization_percent: Option<f64>,
    two_d_utilization_percent: Option<f64>,
    three_d_utilization_percent: Option<f64>,
    current_frequency_mhz: Option<u64>,
    maximum_frequency_mhz: Option<u64>,
}

pub fn inspect_performance() -> io::Result<SystemPerformanceSnapshot> {
    let cpu_info = fs::read_to_string("/proc/cpuinfo")?;
    let cpu_static = parse_cpu_information(&cpu_info);
    let mut cpu_times = parse_cpu_time_counters(&fs::read_to_string("/proc/stat")?);
    if cpu_times.is_empty() {
        return Err(io::Error::new(
            io::ErrorKind::InvalidData,
            "/proc/stat does not contain CPU counters",
        ));
    }
    let aggregate = cpu_times.remove(0);
    let caches = cpu_cache_sizes();

    Ok(SystemPerformanceSnapshot {
        captured_at_unix_milliseconds: SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .map_err(io::Error::other)?
            .as_millis()
            .try_into()
            .unwrap_or(u64::MAX),
        cpu: CpuPerformanceSnapshot {
            model: cpu_static.model,
            physical_core_count: cpu_static.physical_core_count,
            logical_processor_count: cpu_static.logical_processor_count,
            current_frequency_mhz: average_cpu_frequency("scaling_cur_freq")
                .or(cpu_static.current_frequency_mhz),
            maximum_frequency_mhz: cpu_static.maximum_frequency_mhz,
            l1_cache_bytes: caches.get(&1).copied(),
            l2_cache_bytes: caches.get(&2).copied(),
            l3_cache_bytes: caches.get(&3).copied(),
            aggregate,
            logical_processors: cpu_times,
        },
        memory: parse_memory_performance(&fs::read_to_string("/proc/meminfo")?),
        disks: inspect_disks().unwrap_or_default(),
        networks: inspect_networks().unwrap_or_default(),
        gpus: inspect_gpus().unwrap_or_default(),
    })
}

fn parse_cpu_time_counters(content: &str) -> Vec<CpuTimeCounter> {
    content
        .lines()
        .take_while(|line| line.starts_with("cpu"))
        .filter_map(|line| {
            let mut fields = line.split_whitespace();
            let id = fields.next()?.to_owned();
            let values: Vec<u64> = fields.filter_map(|value| value.parse().ok()).collect();
            (values.len() >= 4).then(|| CpuTimeCounter {
                id,
                total_ticks: values.iter().copied().sum(),
                idle_ticks: values[3].saturating_add(values.get(4).copied().unwrap_or_default()),
            })
        })
        .collect()
}

fn parse_memory_performance(content: &str) -> MemoryPerformanceSnapshot {
    let values: HashMap<&str, u64> = content
        .lines()
        .filter_map(|line| {
            let (key, value) = line.split_once(':')?;
            let kilobytes = value.split_whitespace().next()?.parse::<u64>().ok()?;
            Some((key, kilobytes.saturating_mul(1024)))
        })
        .collect();
    let total = values.get("MemTotal").copied().unwrap_or_default();
    let available = values
        .get("MemAvailable")
        .or_else(|| values.get("MemFree"))
        .copied()
        .unwrap_or_default();
    let cached = values
        .get("Cached")
        .copied()
        .unwrap_or_default()
        .saturating_add(values.get("SReclaimable").copied().unwrap_or_default());
    let swap_total = values.get("SwapTotal").copied().unwrap_or_default();
    let swap_free = values.get("SwapFree").copied().unwrap_or_default();

    MemoryPerformanceSnapshot {
        total_bytes: total,
        used_bytes: total.saturating_sub(available),
        available_bytes: available,
        cached_bytes: cached,
        swap_total_bytes: swap_total,
        swap_used_bytes: swap_total.saturating_sub(swap_free),
    }
}

fn cpu_cache_sizes() -> HashMap<u8, u64> {
    let mut sizes = HashMap::new();
    let Ok(entries) = fs::read_dir("/sys/devices/system/cpu/cpu0/cache") else {
        return sizes;
    };
    for entry in entries.filter_map(Result::ok) {
        let level =
            read_trimmed(entry.path().join("level")).and_then(|value| value.parse::<u8>().ok());
        let size = read_trimmed(entry.path().join("size"))
            .and_then(|value| parse_cache_size_bytes(&value));
        if let (Some(level), Some(size)) = (level, size) {
            sizes
                .entry(level)
                .and_modify(|total: &mut u64| *total = total.saturating_add(size))
                .or_insert(size);
        }
    }
    sizes
}

fn parse_cache_size_bytes(value: &str) -> Option<u64> {
    let split = value.find(|character: char| !character.is_ascii_digit())?;
    let amount = value[..split].parse::<u64>().ok()?;
    match value[split..].trim().to_ascii_uppercase().as_str() {
        "K" | "KB" => Some(amount.saturating_mul(1024)),
        "M" | "MB" => Some(amount.saturating_mul(1024 * 1024)),
        "G" | "GB" => Some(amount.saturating_mul(1024 * 1024 * 1024)),
        _ => None,
    }
}

fn inspect_disks() -> io::Result<Vec<DiskPerformanceSnapshot>> {
    let mut disks = Vec::new();
    for entry in fs::read_dir("/sys/class/block")? {
        let entry = entry?;
        let path = entry.path();
        if path.join("partition").exists() || !path.join("device").exists() {
            continue;
        }
        let name = entry.file_name().to_string_lossy().into_owned();
        let fields: Vec<u64> = read_trimmed(path.join("stat"))
            .into_iter()
            .flat_map(|value| {
                value
                    .split_whitespace()
                    .filter_map(|field| field.parse::<u64>().ok())
                    .collect::<Vec<_>>()
            })
            .collect();
        if fields.len() < 10 {
            continue;
        }
        disks.push(DiskPerformanceSnapshot {
            id: format!("block:{name}"),
            name,
            model: read_trimmed(path.join("device/model")),
            total_bytes: read_u64(path.join("size"))
                .unwrap_or_default()
                .saturating_mul(KERNEL_SECTOR_BYTES),
            read_bytes: fields[2].saturating_mul(KERNEL_SECTOR_BYTES),
            written_bytes: fields[6].saturating_mul(KERNEL_SECTOR_BYTES),
            busy_milliseconds: fields[9],
        });
    }
    disks.sort_by(|left, right| left.name.cmp(&right.name));
    Ok(disks)
}

fn inspect_networks() -> io::Result<Vec<NetworkPerformanceSnapshot>> {
    let mut networks = Vec::new();
    for entry in fs::read_dir("/sys/class/net")? {
        let entry = entry?;
        let path = entry.path();
        let name = entry.file_name().to_string_lossy().into_owned();
        if name == "lo" || !path.join("device").exists() {
            continue;
        }
        let mac_address = read_trimmed(path.join("address")).unwrap_or_default();
        let driver = fs::read_link(path.join("device/driver"))
            .ok()
            .and_then(|path| {
                path.file_name()
                    .map(|value| value.to_string_lossy().into_owned())
            });
        networks.push(NetworkPerformanceSnapshot {
            id: format!("mac:{}", mac_address.to_ascii_lowercase()),
            name,
            model: driver,
            speed_mbps: read_u64(path.join("speed")),
            received_bytes: read_u64(path.join("statistics/rx_bytes")).unwrap_or_default(),
            transmitted_bytes: read_u64(path.join("statistics/tx_bytes")).unwrap_or_default(),
        });
    }
    networks.sort_by(|left, right| left.name.cmp(&right.name));
    Ok(networks)
}

fn inspect_gpus() -> io::Result<Vec<GpuPerformanceSnapshot>> {
    let mut gpus = Vec::new();
    for entry in fs::read_dir("/sys/class/drm")? {
        let entry = entry?;
        let card_name = entry.file_name().to_string_lossy().into_owned();
        let Some(suffix) = card_name.strip_prefix("card") else {
            continue;
        };
        if suffix.is_empty() || !suffix.chars().all(|character| character.is_ascii_digit()) {
            continue;
        }
        let path = entry.path();
        let device_path = path.join("device");
        if !device_path.exists() {
            continue;
        }
        let driver = fs::read_link(device_path.join("driver"))
            .ok()
            .and_then(|path| {
                path.file_name()
                    .map(|value| value.to_string_lossy().into_owned())
            });
        let vendor =
            read_trimmed(device_path.join("vendor")).unwrap_or_else(|| "unknown".to_owned());
        let device =
            read_trimmed(device_path.join("device")).unwrap_or_else(|| "unknown".to_owned());
        let busy =
            read_u64(device_path.join("gpu_busy_percent")).map(|value| value.min(100) as f64);
        let (amd_current, amd_maximum) = amd_clock_frequencies(&device_path);
        let current_frequency_mhz = read_u64(path.join("gt_cur_freq_mhz"))
            .or_else(|| read_u64(device_path.join("gt_cur_freq_mhz")))
            .or(amd_current);
        let maximum_frequency_mhz = read_u64(path.join("gt_max_freq_mhz"))
            .or_else(|| read_u64(device_path.join("gt_max_freq_mhz")))
            .or(amd_maximum);

        gpus.push(GpuPerformanceSnapshot {
            id: format!("drm:{card_name}"),
            name: format!(
                "{} GPU ({vendor}:{device})",
                driver.as_deref().unwrap_or("Linux")
            ),
            driver,
            memory_total_bytes: read_u64(device_path.join("mem_info_vram_total")),
            memory_used_bytes: read_u64(device_path.join("mem_info_vram_used")),
            core_utilization_percent: busy,
            // Linux DRM does not expose portable 2D/3D engine counters. Unsupported fields stay null.
            two_d_utilization_percent: None,
            three_d_utilization_percent: None,
            current_frequency_mhz,
            maximum_frequency_mhz,
        });
    }
    gpus.sort_by(|left, right| left.id.cmp(&right.id));
    Ok(gpus)
}

fn amd_clock_frequencies(device_path: &Path) -> (Option<u64>, Option<u64>) {
    let Some(content) = fs::read_to_string(device_path.join("pp_dpm_sclk")).ok() else {
        return (None, None);
    };
    let frequencies: Vec<(u64, bool)> = content
        .lines()
        .filter_map(|line| {
            let value = line.split_whitespace().find_map(|field| {
                field
                    .to_ascii_lowercase()
                    .strip_suffix("mhz")?
                    .parse::<u64>()
                    .ok()
            })?;
            Some((value, line.contains('*')))
        })
        .collect();
    (
        frequencies
            .iter()
            .find(|(_, current)| *current)
            .map(|(value, _)| *value),
        frequencies.iter().map(|(value, _)| *value).max(),
    )
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
    fn parses_aggregate_and_per_processor_cpu_counters() {
        let counters = parse_cpu_time_counters(
            "cpu  10 2 3 20 5 1 1 0 0 0\ncpu0 5 1 1 10 2 1 0 0 0 0\ncpu1 5 1 2 10 3 0 1 0 0 0\nintr 1\n",
        );

        assert_eq!(counters.len(), 3);
        assert_eq!(counters[0].id, "cpu");
        assert_eq!(counters[0].total_ticks, 42);
        assert_eq!(counters[0].idle_ticks, 25);
        assert_eq!(counters[1].id, "cpu0");
    }

    #[test]
    fn parses_memory_usage_from_available_memory() {
        let memory = parse_memory_performance(
            "MemTotal: 1000 kB\nMemAvailable: 250 kB\nCached: 100 kB\nSReclaimable: 20 kB\nSwapTotal: 200 kB\nSwapFree: 50 kB\n",
        );

        assert_eq!(memory.total_bytes, 1000 * 1024);
        assert_eq!(memory.used_bytes, 750 * 1024);
        assert_eq!(memory.cached_bytes, 120 * 1024);
        assert_eq!(memory.swap_used_bytes, 150 * 1024);
    }

    #[test]
    fn parses_kernel_cache_units_without_guessing_unknown_suffixes() {
        assert_eq!(parse_cache_size_bytes("32K"), Some(32 * 1024));
        assert_eq!(parse_cache_size_bytes("2M"), Some(2 * 1024 * 1024));
        assert_eq!(parse_cache_size_bytes("1T"), None);
    }
}
