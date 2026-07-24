//--------------------------//
//--------从受信任的 proc、sys 与文件系统接口读取本机信息---------//
//--------Reads host information from trusted proc, sys, and filesystem interfaces--------//
//-------------------------//
use std::collections::HashSet;
use std::fs;
use std::io;
use std::path::{Path, PathBuf};

use nix::sys::statvfs::statvfs;
use serde::Serialize;

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct SystemAbout {
    host_name: String,
    operating_system: String,
    kernel_version: String,
    uptime_seconds: u64,
    cpu: CpuInformation,
    memory: MemoryInformation,
    system_storage: SystemStorageInformation,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct CpuInformation {
    model: String,
    physical_core_count: usize,
    logical_processor_count: usize,
    current_frequency_mhz: Option<u64>,
    maximum_frequency_mhz: Option<u64>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct MemoryInformation {
    total_bytes: u64,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct SystemStorageInformation {
    source: String,
    stable_id: Option<String>,
    model: Option<String>,
    total_bytes: u64,
    used_bytes: u64,
    available_bytes: u64,
}

pub fn get_about() -> io::Result<SystemAbout> {
    let cpu_info = fs::read_to_string("/proc/cpuinfo")?;
    let memory_info = fs::read_to_string("/proc/meminfo")?;
    let uptime = fs::read_to_string("/proc/uptime")?;
    let root_source = root_mount_source().unwrap_or_else(|| "unknown".to_owned());

    Ok(SystemAbout {
        host_name: read_trimmed("/proc/sys/kernel/hostname")?,
        operating_system: operating_system_name(),
        kernel_version: read_trimmed("/proc/sys/kernel/osrelease")?,
        uptime_seconds: uptime
            .split_whitespace()
            .next()
            .and_then(|value| value.parse::<f64>().ok())
            .unwrap_or_default() as u64,
        cpu: parse_cpu_information(&cpu_info),
        memory: MemoryInformation {
            total_bytes: parse_memory_total_bytes(&memory_info).unwrap_or_default(),
        },
        system_storage: system_storage_information(root_source)?,
    })
}

fn parse_cpu_information(cpu_info: &str) -> CpuInformation {
    let mut model = None;
    let mut logical_processor_count = 0;
    let mut physical_cores = HashSet::new();
    let mut physical_id = "0";
    let mut core_id = None;
    let mut fallback_frequency_mhz = None;

    for block in cpu_info.split("\n\n") {
        if block.trim().is_empty() {
            continue;
        }
        logical_processor_count += 1;
        for line in block.lines() {
            let Some((key, value)) = line.split_once(':') else {
                continue;
            };
            match key.trim() {
                "model name" | "Hardware" if model.is_none() => {
                    model = Some(value.trim().to_owned());
                }
                "physical id" => physical_id = value.trim(),
                "core id" => core_id = Some(value.trim()),
                "cpu MHz" if fallback_frequency_mhz.is_none() => {
                    fallback_frequency_mhz =
                        value.trim().parse::<f64>().ok().map(|value| value as u64);
                }
                _ => {}
            }
        }
        if let Some(core_id) = core_id.take() {
            physical_cores.insert((physical_id.to_owned(), core_id.to_owned()));
        }
    }

    let current_frequency_mhz =
        average_cpu_frequency("scaling_cur_freq").or(fallback_frequency_mhz);
    let maximum_frequency_mhz = average_cpu_frequency("cpuinfo_max_freq")
        .or_else(|| average_cpu_frequency("scaling_max_freq"));

    CpuInformation {
        model: model.unwrap_or_else(|| "Unknown CPU".to_owned()),
        physical_core_count: if physical_cores.is_empty() {
            logical_processor_count
        } else {
            physical_cores.len()
        },
        logical_processor_count,
        current_frequency_mhz,
        maximum_frequency_mhz,
    }
}

fn average_cpu_frequency(file_name: &str) -> Option<u64> {
    let entries = fs::read_dir("/sys/devices/system/cpu").ok()?;
    let frequencies: Vec<u64> = entries
        .filter_map(Result::ok)
        .filter(|entry| {
            entry
                .file_name()
                .to_string_lossy()
                .strip_prefix("cpu")
                .is_some_and(|suffix| suffix.chars().all(|character| character.is_ascii_digit()))
        })
        .filter_map(|entry| {
            fs::read_to_string(entry.path().join("cpufreq").join(file_name))
                .ok()?
                .trim()
                .parse::<u64>()
                .ok()
        })
        .collect();
    (!frequencies.is_empty())
        .then(|| frequencies.iter().sum::<u64>() / frequencies.len() as u64 / 1_000)
}

fn parse_memory_total_bytes(memory_info: &str) -> Option<u64> {
    memory_info.lines().find_map(|line| {
        let value = line.strip_prefix("MemTotal:")?.split_whitespace().next()?;
        value.parse::<u64>().ok().map(|kilobytes| kilobytes * 1024)
    })
}

fn system_storage_information(source: String) -> io::Result<SystemStorageInformation> {
    let statistics = statvfs(Path::new("/")).map_err(io::Error::other)?;
    let block_size = statistics.fragment_size();
    let total_bytes = statistics.blocks().saturating_mul(block_size);
    let available_bytes = statistics.blocks_available().saturating_mul(block_size);
    let free_bytes = statistics.blocks_free().saturating_mul(block_size);
    let used_bytes = total_bytes.saturating_sub(free_bytes);
    let block_name = source
        .strip_prefix("/dev/")
        .map(Path::new)
        .and_then(Path::file_name)
        .map(|name| name.to_string_lossy().into_owned());

    Ok(SystemStorageInformation {
        stable_id: stable_block_id(&source),
        model: block_name.as_deref().and_then(block_model),
        source,
        total_bytes,
        used_bytes,
        available_bytes,
    })
}

fn root_mount_source() -> Option<String> {
    fs::read_to_string("/proc/self/mountinfo")
        .ok()?
        .lines()
        .find_map(|line| {
            let (mount, filesystem) = line.split_once(" - ")?;
            (mount.split_whitespace().nth(4)? == "/")
                .then(|| filesystem.split_whitespace().nth(1).map(str::to_owned))
                .flatten()
        })
}

fn stable_block_id(source: &str) -> Option<String> {
    let canonical_source = fs::canonicalize(source).ok()?;
    fs::read_dir("/dev/disk/by-id")
        .ok()?
        .filter_map(Result::ok)
        .find_map(|entry| {
            let target = fs::canonicalize(entry.path()).ok()?;
            (target == canonical_source).then(|| entry.file_name().to_string_lossy().into_owned())
        })
}

fn block_model(block_name: &str) -> Option<String> {
    let mut path = fs::canonicalize(PathBuf::from("/sys/class/block").join(block_name)).ok()?;
    if PathBuf::from("/sys/class/block")
        .join(block_name)
        .join("partition")
        .exists()
    {
        path = path.parent()?.to_owned();
    }
    fs::read_to_string(path.join("device/model"))
        .ok()
        .map(|value| value.trim().to_owned())
        .filter(|value| !value.is_empty())
}

fn operating_system_name() -> String {
    let content = fs::read_to_string("/etc/os-release").unwrap_or_default();
    content
        .lines()
        .find_map(|line| {
            line.strip_prefix("PRETTY_NAME=")
                .map(|value| value.trim_matches('"').to_owned())
        })
        .unwrap_or_else(|| "Linux".to_owned())
}

fn read_trimmed(path: &str) -> io::Result<String> {
    Ok(fs::read_to_string(path)?.trim().to_owned())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_memory_total_without_exposing_other_proc_fields() {
        let input = "MemTotal:       16384 kB\nMemFree:         1024 kB\n";

        assert_eq!(parse_memory_total_bytes(input), Some(16 * 1024 * 1024));
    }

    #[test]
    fn parses_cpu_topology_and_frequency() {
        let input = "processor: 0\nmodel name: Test CPU\nphysical id: 0\ncore id: 0\ncpu MHz: 2400.000\n\nprocessor: 1\nmodel name: Test CPU\nphysical id: 0\ncore id: 0\n";

        let result = parse_cpu_information(input);

        assert_eq!(result.model, "Test CPU");
        assert_eq!(result.physical_core_count, 1);
        assert_eq!(result.logical_processor_count, 2);
    }
}
