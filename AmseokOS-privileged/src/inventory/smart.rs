//--------------------------//
//--------通过稳定磁盘身份执行有界 SMART 只读查询---------//
//--------Runs bounded read-only SMART queries resolved from stable disk identities--------//
//-------------------------//
use std::io::{self, Read};
use std::path::Path;
use std::process::{Command, Stdio};
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};
use std::thread;
use std::time::{Duration, Instant};

use serde::Serialize;
use serde_json::Value;

use super::storage::{self, BlockDeviceInformation};

const SMARTCTL_PATHS: [&str; 2] = ["/usr/sbin/smartctl", "/usr/bin/smartctl"];
const TOOL_TIMEOUT: Duration = Duration::from_secs(5);
const MAXIMUM_TOOL_OUTPUT_BYTES: usize = 512 * 1024;

pub const CODE_INVALID_DEVICE_ID: &str = "request.invalid";
pub const CODE_DEVICE_NOT_FOUND: &str = "resource.not_found";
pub const CODE_IDENTITY_UNSTABLE: &str = "resource.identity_unstable";
pub const CODE_INVENTORY_FAILED: &str = "inventory.read_failed";
pub const CODE_TOOL_NOT_AVAILABLE: &str = "smart.tool_not_available";
pub const CODE_TOOL_TIMEOUT: &str = "smart.tool_timeout";
pub const CODE_QUERY_FAILED: &str = "smart.query_failed";
pub const CODE_INVALID_OUTPUT: &str = "smart.invalid_output";

#[derive(Debug)]
pub struct SmartReadError {
    pub code: &'static str,
    pub message: String,
    pub retryable: bool,
}

impl SmartReadError {
    fn new(code: &'static str, message: impl Into<String>, retryable: bool) -> Self {
        Self {
            code,
            message: message.into(),
            retryable,
        }
    }
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DiskSmartInformation {
    device_id: String,
    supported: bool,
    enabled: bool,
    status: &'static str,
    passed: Option<bool>,
    temperature_celsius: Option<i64>,
    power_on_hours: Option<u64>,
    power_cycle_count: Option<u64>,
    reallocated_sector_count: Option<u64>,
    pending_sector_count: Option<u64>,
    offline_uncorrectable_sector_count: Option<u64>,
    media_error_count: Option<u64>,
    percentage_used: Option<u64>,
    critical_warning: Option<u64>,
}

struct ToolOutput {
    exit_status: i32,
    stdout: Vec<u8>,
}

pub fn inspect_device(device_id: &str) -> Result<DiskSmartInformation, SmartReadError> {
    validate_device_id(device_id)?;
    let devices = storage::inspect_block_devices()
        .map_err(|error| SmartReadError::new(CODE_INVENTORY_FAILED, error.to_string(), true))?;
    let device_path = resolve_stable_device_path(&devices, device_id)?;
    let smartctl_path = SMARTCTL_PATHS
        .iter()
        .map(Path::new)
        .find(|path| path.is_file())
        .map(Path::to_path_buf)
        .ok_or_else(|| {
            SmartReadError::new(
                CODE_TOOL_NOT_AVAILABLE,
                "smartctl is not installed at an approved path",
                false,
            )
        })?;
    let output = run_smartctl(&smartctl_path, Path::new(device_path))?;
    parse_smartctl_output(device_id, output.exit_status, &output.stdout)
}

fn validate_device_id(device_id: &str) -> Result<(), SmartReadError> {
    if device_id.is_empty() || device_id.len() > 256 || device_id.chars().any(char::is_control) {
        return Err(SmartReadError::new(
            CODE_INVALID_DEVICE_ID,
            "deviceId is invalid",
            false,
        ));
    }
    Ok(())
}

fn resolve_stable_device_path<'a>(
    devices: &'a [BlockDeviceInformation],
    device_id: &str,
) -> Result<&'a str, SmartReadError> {
    resolve_stable_path(
        devices.iter().map(|device| {
            (
                device.id.as_str(),
                device.stable,
                device.identity_conflict,
                device.path.as_str(),
            )
        }),
        device_id,
    )
}

fn resolve_stable_path<'a>(
    devices: impl Iterator<Item = (&'a str, bool, bool, &'a str)>,
    device_id: &str,
) -> Result<&'a str, SmartReadError> {
    let (_, stable, identity_conflict, path) = devices
        .into_iter()
        .find(|(id, _, _, _)| *id == device_id)
        .ok_or_else(|| {
            SmartReadError::new(
                CODE_DEVICE_NOT_FOUND,
                "the requested physical disk no longer exists",
                false,
            )
        })?;
    if !stable || identity_conflict {
        return Err(SmartReadError::new(
            CODE_IDENTITY_UNSTABLE,
            "the requested physical disk identity is not stable and unique",
            false,
        ));
    }
    Ok(path)
}

fn run_smartctl(smartctl_path: &Path, device_path: &Path) -> Result<ToolOutput, SmartReadError> {
    let mut child = Command::new(smartctl_path)
        .args(["--json=c", "--all"])
        .arg(device_path)
        .env_clear()
        .env("LC_ALL", "C")
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .map_err(|error| SmartReadError::new(CODE_QUERY_FAILED, error.to_string(), true))?;
    let exceeded = Arc::new(AtomicBool::new(false));
    let stdout_reader = read_limited(
        child.stdout.take().expect("piped stdout must exist"),
        Arc::clone(&exceeded),
    );
    let stderr_reader = read_limited(
        child.stderr.take().expect("piped stderr must exist"),
        Arc::clone(&exceeded),
    );
    let started = Instant::now();

    let status = loop {
        if exceeded.load(Ordering::Relaxed) {
            let _ = child.kill();
            let _ = child.wait();
            let _ = stdout_reader.join();
            let _ = stderr_reader.join();
            return Err(SmartReadError::new(
                CODE_INVALID_OUTPUT,
                "smartctl output exceeded the bounded response limit",
                false,
            ));
        }
        if let Some(status) = child
            .try_wait()
            .map_err(|error| SmartReadError::new(CODE_QUERY_FAILED, error.to_string(), true))?
        {
            break status;
        }
        if started.elapsed() >= TOOL_TIMEOUT {
            let _ = child.kill();
            let _ = child.wait();
            let _ = stdout_reader.join();
            let _ = stderr_reader.join();
            return Err(SmartReadError::new(
                CODE_TOOL_TIMEOUT,
                "smartctl did not finish within the allowed time",
                true,
            ));
        }
        thread::sleep(Duration::from_millis(10));
    };

    let stdout = join_reader(stdout_reader)?;
    let stderr = join_reader(stderr_reader)?;
    if stdout.is_empty() {
        let diagnostic = String::from_utf8_lossy(&stderr);
        return Err(SmartReadError::new(
            CODE_QUERY_FAILED,
            format!("smartctl returned no JSON output: {diagnostic}"),
            true,
        ));
    }
    Ok(ToolOutput {
        exit_status: status.code().unwrap_or(1),
        stdout,
    })
}

fn read_limited(
    mut reader: impl Read + Send + 'static,
    exceeded: Arc<AtomicBool>,
) -> thread::JoinHandle<io::Result<Vec<u8>>> {
    thread::spawn(move || {
        let mut output = Vec::new();
        let mut buffer = [0_u8; 8192];
        loop {
            let count = reader.read(&mut buffer)?;
            if count == 0 {
                return Ok(output);
            }
            if output.len().saturating_add(count) > MAXIMUM_TOOL_OUTPUT_BYTES {
                exceeded.store(true, Ordering::Relaxed);
                return Ok(output);
            }
            output.extend_from_slice(&buffer[..count]);
        }
    })
}

fn join_reader(reader: thread::JoinHandle<io::Result<Vec<u8>>>) -> Result<Vec<u8>, SmartReadError> {
    reader
        .join()
        .map_err(|_| {
            SmartReadError::new(CODE_QUERY_FAILED, "smartctl output reader panicked", true)
        })?
        .map_err(|error| SmartReadError::new(CODE_QUERY_FAILED, error.to_string(), true))
}

fn parse_smartctl_output(
    device_id: &str,
    process_exit_status: i32,
    output: &[u8],
) -> Result<DiskSmartInformation, SmartReadError> {
    let document: Value = serde_json::from_slice(output)
        .map_err(|error| SmartReadError::new(CODE_INVALID_OUTPUT, error.to_string(), false))?;
    let smartctl_exit_status = value_u64(&document["smartctl"]["exit_status"])
        .and_then(|value| u8::try_from(value).ok())
        .unwrap_or_else(|| u8::try_from(process_exit_status).unwrap_or(u8::MAX));
    let supported = document["smart_support"]["available"]
        .as_bool()
        .unwrap_or(false);
    let enabled = document["smart_support"]["enabled"]
        .as_bool()
        .unwrap_or(false);

    if !supported {
        if smartctl_exit_status & 0b0000_0011 != 0 {
            return Err(SmartReadError::new(
                CODE_QUERY_FAILED,
                "smartctl could not open or identify the resolved physical disk",
                true,
            ));
        }
        return Ok(DiskSmartInformation {
            device_id: device_id.to_owned(),
            supported: false,
            enabled: false,
            status: "unsupported",
            passed: None,
            temperature_celsius: None,
            power_on_hours: None,
            power_cycle_count: None,
            reallocated_sector_count: None,
            pending_sector_count: None,
            offline_uncorrectable_sector_count: None,
            media_error_count: None,
            percentage_used: None,
            critical_warning: None,
        });
    }
    if smartctl_exit_status & 0b0000_0111 != 0 {
        return Err(SmartReadError::new(
            CODE_QUERY_FAILED,
            "smartctl reported a command, device, or SMART command failure",
            true,
        ));
    }

    let passed = document["smart_status"]["passed"].as_bool();
    let critical_warning =
        value_u64(&document["nvme_smart_health_information_log"]["critical_warning"]);
    let status = smart_status(smartctl_exit_status, passed, critical_warning);

    Ok(DiskSmartInformation {
        device_id: device_id.to_owned(),
        supported,
        enabled,
        status,
        passed,
        temperature_celsius: value_i64(&document["temperature"]["current"]),
        power_on_hours: value_u64(&document["power_on_time"]["hours"]),
        power_cycle_count: value_u64(&document["power_cycle_count"]),
        reallocated_sector_count: ata_attribute_raw_value(&document, 5),
        pending_sector_count: ata_attribute_raw_value(&document, 197),
        offline_uncorrectable_sector_count: ata_attribute_raw_value(&document, 198),
        media_error_count: value_u64(
            &document["nvme_smart_health_information_log"]["media_errors"],
        ),
        percentage_used: value_u64(
            &document["nvme_smart_health_information_log"]["percentage_used"],
        ),
        critical_warning,
    })
}

fn smart_status(
    exit_status: u8,
    passed: Option<bool>,
    critical_warning: Option<u64>,
) -> &'static str {
    if passed == Some(false) || critical_warning.is_some_and(|warning| warning != 0) {
        return "failing";
    }
    if exit_status & 0b0001_1000 != 0 {
        return "failing";
    }
    if exit_status & 0b1110_0000 != 0 {
        return "warning";
    }
    if passed == Some(true) {
        return "healthy";
    }
    "unknown"
}

fn ata_attribute_raw_value(document: &Value, id: u64) -> Option<u64> {
    document["ata_smart_attributes"]["table"]
        .as_array()?
        .iter()
        .find(|attribute| value_u64(&attribute["id"]) == Some(id))
        .and_then(|attribute| value_u64(&attribute["raw"]["value"]))
}

fn value_u64(value: &Value) -> Option<u64> {
    value
        .as_u64()
        .or_else(|| value.as_str()?.parse::<u64>().ok())
}

fn value_i64(value: &Value) -> Option<i64> {
    value
        .as_i64()
        .or_else(|| value.as_str()?.parse::<i64>().ok())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn resolves_only_a_stable_unique_identity() {
        let devices = [("wwn:stable", true, false, "/dev/sda")];
        assert_eq!(
            resolve_stable_path(devices.into_iter(), "wwn:stable").unwrap(),
            "/dev/sda"
        );
        assert_eq!(
            resolve_stable_path(devices.into_iter(), "wwn:missing")
                .unwrap_err()
                .code,
            CODE_DEVICE_NOT_FOUND
        );

        let conflicted = [("wwn:duplicate", false, true, "/dev/sdb")];
        assert_eq!(
            resolve_stable_path(conflicted.into_iter(), "wwn:duplicate")
                .unwrap_err()
                .code,
            CODE_IDENTITY_UNSTABLE
        );
    }

    #[test]
    fn parses_ata_health_without_exposing_raw_vendor_output() {
        let output = br#"{
          "smartctl":{"exit_status":0},
          "smart_support":{"available":true,"enabled":true},
          "smart_status":{"passed":true},
          "temperature":{"current":34},
          "power_on_time":{"hours":1200},
          "power_cycle_count":42,
          "ata_smart_attributes":{"table":[
            {"id":5,"raw":{"value":1}},
            {"id":197,"raw":{"value":2}},
            {"id":198,"raw":{"value":0}}
          ]}
        }"#;

        let result = parse_smartctl_output("wwn:test", 0, output).unwrap();

        assert_eq!(result.status, "healthy");
        assert_eq!(result.temperature_celsius, Some(34));
        assert_eq!(result.power_on_hours, Some(1200));
        assert_eq!(result.reallocated_sector_count, Some(1));
        assert_eq!(result.pending_sector_count, Some(2));
    }

    #[test]
    fn parses_nvme_warning_as_a_failing_health_result() {
        let output = br#"{
          "smartctl":{"exit_status":8},
          "smart_support":{"available":true,"enabled":true},
          "smart_status":{"passed":false},
          "temperature":{"current":51},
          "power_on_time":{"hours":88},
          "power_cycle_count":9,
          "nvme_smart_health_information_log":{
            "critical_warning":4,
            "media_errors":3,
            "percentage_used":7
          }
        }"#;

        let result = parse_smartctl_output("serial:nvme", 8, output).unwrap();

        assert_eq!(result.status, "failing");
        assert_eq!(result.media_error_count, Some(3));
        assert_eq!(result.percentage_used, Some(7));
        assert_eq!(result.critical_warning, Some(4));
    }

    #[test]
    fn returns_unsupported_separately_from_a_query_failure() {
        let unsupported = br#"{
          "smartctl":{"exit_status":0},
          "smart_support":{"available":false,"enabled":false}
        }"#;
        assert_eq!(
            parse_smartctl_output("serial:virtual", 0, unsupported)
                .unwrap()
                .status,
            "unsupported"
        );

        let open_failed = br#"{"smartctl":{"exit_status":2}}"#;
        assert_eq!(
            parse_smartctl_output("wwn:missing", 2, open_failed)
                .unwrap_err()
                .code,
            CODE_QUERY_FAILED
        );
    }
}
