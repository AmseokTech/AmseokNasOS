//--------------------------//
//--------以备份、写入、重载、校验、失败即回滚的事务顺序改写网络配置---------//
//--------Rewrites network configuration through a backup, write, reload, verify, rollback-on-failure transaction--------//
//-------------------------//
//
// 为什么单独建立这个模块，而不是放进 inventory：
// inventory 目录的语义是"只读采集"，一旦在其中掺入写入能力，
// 读路径的安全审计边界就会被污染，后续无法再断言"读不会改机器"
// 因此写入能力一律落在这个独立模块，inventory 保持纯只读
//
// 为什么后端唯一选定 systemd-networkd：
// 读路径已经在 inventory/network.rs 里以 /run/systemd/netif/leases 判定 DHCP，
// 读与写必须共用同一事实来源，否则会出现"写进 A、读自 B"的状态撕裂
// 因此本模块只生成 systemd-networkd 的 .network 配置，不走 NetworkManager 路径
//
// 绝不允许跨越的边界：
// 1. 外部程序调用只使用固定绝对路径与固定动作；reconfigure 的接口名只能来自
//    sysfs 对稳定 MAC 身份的重新解析，严禁把请求里的接口标识、地址或网关直接拼进命令行
// 2. 只写受本系统管理的固定前缀文件，绝不改写第三方或发行版自带的网络配置
// 3. 重载或校验失败必须立即恢复备份，宁可回到旧状态也不留下半生效配置
use std::fmt;
use std::fs::{self, OpenOptions};
use std::io::{self, Write};
use std::net::Ipv4Addr;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::thread;
use std::time::{Duration, Instant};

use nix::ifaddrs::getifaddrs;
use serde::{Deserialize, Serialize};
use tracing::{error, warn};

/// 受管配置目录默认值，可由环境变量覆盖以便单元测试指向临时目录
const DEFAULT_CONFIGURATION_DIRECTORY: &str = "/etc/systemd/network";
const CONFIGURATION_DIRECTORY_VARIABLE: &str = "AMSEOKNAS_PRIVILEGED_NETWORK_CONFIG_DIRECTORY";

/// 网卡枚举根目录默认值，同样可覆盖，避免测试依赖真实硬件
const DEFAULT_SYSFS_DIRECTORY: &str = "/sys/class/net";
const SYSFS_DIRECTORY_VARIABLE: &str = "AMSEOKNAS_PRIVILEGED_NETWORK_SYSFS_DIRECTORY";

/// 受管文件固定前缀与后缀：前缀用于区分"本系统写的"与"别人写的"，
/// 数字前缀保证排序上覆盖发行版默认配置
const MANAGED_FILE_PREFIX: &str = "70-amseoknas-";
const MANAGED_FILE_SUFFIX: &str = ".network";

/// 重载程序候选路径为编译期固定常量，运行时不接受任何外部输入拼接
const RELOAD_PROGRAM_CANDIDATES: [&str; 3] = [
    "/usr/bin/networkctl",
    "/bin/networkctl",
    "/usr/sbin/networkctl",
];
const IP_PROGRAM_CANDIDATES: [&str; 3] = ["/usr/sbin/ip", "/usr/bin/ip", "/bin/ip"];
const RELOAD_ARGUMENT: &str = "reload";
const NETWORKCTL_TIMEOUT: Duration = Duration::from_secs(5);

/// 校验窗口：静态地址生效或 DHCP 取得地址都不是瞬时的，
/// 但也不能无限等待，否则会长期占住特权守护进程的连接线程
const VERIFICATION_ATTEMPTS: u32 = 10;
const VERIFICATION_INTERVAL: Duration = Duration::from_millis(500);

pub const MODE_DHCP: &str = "dhcp";
pub const MODE_STATIC_IPV4: &str = "staticIpv4";

/// 稳定错误码，与 C# 端 INetworkConfigurationExecutor 的失败枚举一一对应，
/// 不允许在此之外临时发明新码，否则跨语言契约会失效
pub const CODE_INTERFACE_NOT_FOUND: &str = "network.interface_not_found";
pub const CODE_INVALID_CONFIGURATION: &str = "network.invalid_configuration";
pub const CODE_APPLY_FAILED: &str = "network.apply_failed";
pub const CODE_VERIFICATION_FAILED: &str = "network.verification_failed";
pub const CODE_OPERATION_NOT_FOUND: &str = "network.operation_not_found";
pub const CODE_OPERATION_CONFLICT: &str = "network.operation_conflict";
pub const CODE_ROLLBACK_FAILED: &str = "network.rollback_failed";

/// 写入失败的统一表达：稳定码用于跨语言映射，可重试标记用于响应封装，
/// 诊断信息只用于日志与响应正文，不参与任何判定
#[derive(Debug, Clone)]
pub struct NetworkWriteError {
    pub code: &'static str,
    pub retryable: bool,
    pub message: String,
}

impl NetworkWriteError {
    fn new(code: &'static str, retryable: bool, message: impl Into<String>) -> Self {
        Self {
            code,
            retryable,
            message: message.into(),
        }
    }

    pub fn interface_not_found(message: impl Into<String>) -> Self {
        Self::new(CODE_INTERFACE_NOT_FOUND, false, message)
    }

    pub fn invalid_configuration(message: impl Into<String>) -> Self {
        Self::new(CODE_INVALID_CONFIGURATION, false, message)
    }

    pub fn apply_failed(message: impl Into<String>) -> Self {
        Self::new(CODE_APPLY_FAILED, true, message)
    }

    pub fn verification_failed(message: impl Into<String>) -> Self {
        Self::new(CODE_VERIFICATION_FAILED, true, message)
    }

    pub fn operation_not_found(message: impl Into<String>) -> Self {
        Self::new(CODE_OPERATION_NOT_FOUND, false, message)
    }

    pub fn operation_conflict(message: impl Into<String>) -> Self {
        Self::new(CODE_OPERATION_CONFLICT, false, message)
    }

    pub fn rollback_failed(message: impl Into<String>) -> Self {
        Self::new(CODE_ROLLBACK_FAILED, false, message)
    }
}

impl fmt::Display for NetworkWriteError {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(formatter, "{}: {}", self.code, self.message)
    }
}

/// 让文件与进程操作可以直接用问号运算符向上传播，避免在每个调用点写匹配分支
impl From<io::Error> for NetworkWriteError {
    fn from(error: io::Error) -> Self {
        Self::apply_failed(error.to_string())
    }
}

/// 归一化网络配置，字段与 C# 的 NormalizedNetworkConfiguration 语义对齐；
/// 契约中没有 DNS 字段，因此本期网络写入一律不设置 DNS
#[derive(Debug, Clone, Deserialize, Serialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct NormalizedNetworkConfiguration {
    pub mode: String,
    pub ip_address: Option<String>,
    pub prefix_length: Option<u32>,
    pub gateway: Option<String>,
}

/// 受管文件在改写前的原始状态，是回滚唯一依据：
/// 原文必须逐字节保留，绝不做任何格式化，否则回滚会引入新的差异
#[derive(Debug, Clone, PartialEq, Eq, Deserialize, Serialize)]
#[serde(rename_all = "camelCase", tag = "kind", content = "content")]
pub enum ManagedFileBackup {
    /// 改写前该受管文件不存在，回滚时应当删除文件而不是写回空内容
    Absent,
    Present(String),
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct AppliedNetworkConfiguration {
    pub backup: ManagedFileBackup,
    pub retained_addresses: Vec<String>,
}

/// 与系统交互的方式：生产路径真实重载并真实校验；
/// 模拟路径只在测试编译期存在，正式二进制里无法被任何请求触达
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SystemInteraction {
    Live,
    #[cfg(test)]
    Simulated,
}

/// 写入运行环境：目录可注入，使单元测试不需要真实系统目录与 root 权限
#[derive(Debug, Clone)]
pub struct NetworkWriteEnvironment {
    configuration_directory: PathBuf,
    sysfs_directory: PathBuf,
    interaction: SystemInteraction,
    writes_available: bool,
}

impl NetworkWriteEnvironment {
    pub fn from_environment() -> Self {
        Self {
            configuration_directory: configuration_directory_from_environment(),
            sysfs_directory: sysfs_directory_from_environment(),
            interaction: SystemInteraction::Live,
            writes_available: true,
        }
    }

    /// 看守线程不可用时只关闭新的应用动作；确认和回滚仍需保留，
    /// 否则已经登记的改动反而失去人工恢复入口
    pub fn disable_new_applications(&mut self) {
        self.writes_available = false;
    }
}

pub fn configuration_directory_from_environment() -> PathBuf {
    match std::env::var_os(CONFIGURATION_DIRECTORY_VARIABLE) {
        Some(directory) => PathBuf::from(directory),
        None => PathBuf::from(DEFAULT_CONFIGURATION_DIRECTORY),
    }
}

fn sysfs_directory_from_environment() -> PathBuf {
    match std::env::var_os(SYSFS_DIRECTORY_VARIABLE) {
        Some(directory) => PathBuf::from(directory),
        None => PathBuf::from(DEFAULT_SYSFS_DIRECTORY),
    }
}

/// 已定位到的目标网卡：名称用于地址校验，硬件地址用于写进 Match 段
#[derive(Debug, Clone)]
struct TargetInterface {
    name: String,
    mac_address: String,
}

/// 从接口标识解析出规范化的硬件地址；
/// 标识形如 mac:aa:bb:cc:dd:ee:ff，与 inventory/network.rs 生成的形式一致
fn normalize_interface_identity(interface_id: &str) -> Option<String> {
    let mac = interface_id.strip_prefix("mac:")?.to_ascii_lowercase();
    let segments: Vec<&str> = mac.split(':').collect();
    if segments.len() != 6 {
        return None;
    }
    if segments.iter().any(|segment| {
        segment.len() != 2
            || !segment
                .chars()
                .all(|character| character.is_ascii_hexdigit())
    }) {
        return None;
    }
    Some(mac)
}

/// 按接口标识定位物理网卡：只接受带 device 节点的物理网卡，
/// 排除回环与虚拟设备，避免把桥接、绑定、容器网卡当成可改写目标
fn locate_physical_interface(
    sysfs_directory: &Path,
    interface_id: &str,
) -> Result<TargetInterface, NetworkWriteError> {
    let mac_address = normalize_interface_identity(interface_id)
        .ok_or_else(|| NetworkWriteError::interface_not_found("接口标识格式无法解析"))?;

    let entries = fs::read_dir(sysfs_directory)
        .map_err(|error| NetworkWriteError::interface_not_found(error.to_string()))?;
    for entry in entries {
        let entry = match entry {
            Ok(entry) => entry,
            Err(_) => continue,
        };
        let name = entry.file_name().to_string_lossy().into_owned();
        if name == "lo" {
            continue;
        }
        let path = entry.path();
        if !path.join("device").exists() {
            continue;
        }
        let address = match fs::read_to_string(path.join("address")) {
            Ok(address) => address.trim().to_ascii_lowercase(),
            Err(_) => continue,
        };
        if address == mac_address {
            return Ok(TargetInterface {
                name,
                mac_address: address,
            });
        }
    }

    Err(NetworkWriteError::interface_not_found(
        "未找到与该接口标识匹配的物理网卡",
    ))
}

/// 受管文件名：固定前缀 + 硬件地址去分隔符，保证同一网卡稳定映射到同一文件，
/// 且文件名只由校验过的十六进制字符组成，不可能因入参穿越出目录
pub fn managed_file_name(interface_id: &str) -> Option<String> {
    let mac = normalize_interface_identity(interface_id)?;
    let compact: String = mac.chars().filter(|character| *character != ':').collect();
    Some(format!(
        "{MANAGED_FILE_PREFIX}{compact}{MANAGED_FILE_SUFFIX}"
    ))
}

fn managed_file_path(
    configuration_directory: &Path,
    interface_id: &str,
) -> Result<PathBuf, NetworkWriteError> {
    let name = managed_file_name(interface_id)
        .ok_or_else(|| NetworkWriteError::interface_not_found("接口标识格式无法解析"))?;
    Ok(configuration_directory.join(name))
}

/// 判断某网卡是否由本系统以静态方式接管，供只读侧回显模式使用；
/// 只认本系统前缀的受管文件，第三方配置一律不认领，避免误报
pub fn managed_static_declaration(configuration_directory: &Path, interface_id: &str) -> bool {
    let Some(name) = managed_file_name(interface_id) else {
        return false;
    };
    let Ok(content) = fs::read_to_string(configuration_directory.join(name)) else {
        return false;
    };
    content
        .lines()
        .map(str::trim)
        .any(|line| line.eq_ignore_ascii_case("DHCP=no"))
        && content
            .lines()
            .map(str::trim)
            .any(|line| line.starts_with("Address="))
}

fn parse_ipv4(value: &str, field: &str) -> Result<Ipv4Addr, NetworkWriteError> {
    value.parse::<Ipv4Addr>().map_err(|_| {
        NetworkWriteError::invalid_configuration(format!("{field} 不是合法的 IPv4 地址"))
    })
}

/// 前缀长度范围与 C# 端保持同一约定：1..30 闭区间
/// 31 与 32 在常规局域网语义下没有可用主机地址，直接拒绝而不是写进去再失败
fn validate_prefix_length(prefix_length: u32) -> Result<u32, NetworkWriteError> {
    if (1..=30).contains(&prefix_length) {
        Ok(prefix_length)
    } else {
        Err(NetworkWriteError::invalid_configuration(
            "前缀长度必须位于 1 到 30 之间",
        ))
    }
}

fn network_address(address: Ipv4Addr, prefix_length: u32) -> u32 {
    let mask = if prefix_length == 0 {
        0
    } else {
        u32::MAX << (32 - prefix_length)
    };
    u32::from(address) & mask
}

#[derive(Debug, Clone, Copy)]
enum ValidatedNetworkConfiguration {
    Dhcp,
    StaticIpv4 {
        address: Ipv4Addr,
        prefix_length: u32,
        gateway: Option<Ipv4Addr>,
    },
}

/// 所有配置语义必须在定位网卡和写盘之前完成验证，
/// 这样非法模式不会因机器当前硬件状态不同而漂移成“接口未找到”
fn validate_configuration(
    configuration: &NormalizedNetworkConfiguration,
) -> Result<ValidatedNetworkConfiguration, NetworkWriteError> {
    match configuration.mode.as_str() {
        MODE_DHCP => {
            if configuration.ip_address.is_some()
                || configuration.prefix_length.is_some()
                || configuration.gateway.is_some()
            {
                return Err(NetworkWriteError::invalid_configuration(
                    "DHCP 模式不能携带静态 IPv4 字段",
                ));
            }
            Ok(ValidatedNetworkConfiguration::Dhcp)
        }
        MODE_STATIC_IPV4 => {
            let address = configuration.ip_address.as_deref().ok_or_else(|| {
                NetworkWriteError::invalid_configuration("静态模式必须提供 IPv4 地址")
            })?;
            let prefix_length = configuration.prefix_length.ok_or_else(|| {
                NetworkWriteError::invalid_configuration("静态模式必须提供前缀长度")
            })?;
            let prefix_length = validate_prefix_length(prefix_length)?;
            let address = parse_ipv4(address, "IPv4 地址")?;
            let gateway = match configuration.gateway.as_deref() {
                Some(gateway) => {
                    let gateway = parse_ipv4(gateway, "网关")?;
                    // 网关必须与地址同子网，否则配置生效后本机会直接失去默认路由
                    // 这是最容易把机器改到失联的一步，必须在写盘之前拦下
                    if network_address(gateway, prefix_length)
                        != network_address(address, prefix_length)
                    {
                        return Err(NetworkWriteError::invalid_configuration(
                            "网关必须与 IPv4 地址处于同一子网",
                        ));
                    }
                    Some(gateway)
                }
                None => None,
            };
            Ok(ValidatedNetworkConfiguration::StaticIpv4 {
                address,
                prefix_length,
                gateway,
            })
        }
        _ => Err(NetworkWriteError::invalid_configuration(
            "网络模式只接受 dhcp 或 staticIpv4",
        )),
    }
}

/// 生成受管配置内容：Match 段按硬件地址匹配，
/// 因为网卡名可能随内核或固件变化而改名，硬件地址才是稳定身份
fn build_managed_content(
    interface: &TargetInterface,
    configuration: ValidatedNetworkConfiguration,
) -> String {
    let header = format!(
        "# 由 AmseokNas 特权守护进程生成，请勿手工编辑\n# Generated by the AmseokNas privileged daemon; do not edit by hand\n[Match]\nMACAddress={}\n\n[Network]\n",
        interface.mac_address
    );

    match configuration {
        ValidatedNetworkConfiguration::Dhcp => format!("{header}DHCP=ipv4\n"),
        ValidatedNetworkConfiguration::StaticIpv4 {
            address,
            prefix_length,
            gateway,
        } => {
            let mut content = format!("{header}DHCP=no\nAddress={address}/{prefix_length}\n");
            if let Some(gateway) = gateway {
                content.push_str(&format!("Gateway={gateway}\n"));
            }
            content
        }
    }
}

/// 原子替换受管文件：先写同目录临时文件再重命名，
/// 保证任何时刻 systemd-networkd 读到的都是完整内容，不会读到半截配置
fn write_managed_file(path: &Path, content: &str) -> Result<(), NetworkWriteError> {
    let parent = path
        .parent()
        .ok_or_else(|| NetworkWriteError::apply_failed("受管配置路径没有父目录"))?;
    fs::create_dir_all(parent)?;
    let temporary = path.with_extension("network.amseoknas-temporary");

    // 临时文件名固定且位于受管目录内，先显式移除上次崩溃可能遗留的节点
    // 随后使用 create_new，绝不能跟随攻击者预先放置的符号链接写到目录外
    match fs::remove_file(&temporary) {
        Ok(()) => {}
        Err(error) if error.kind() == io::ErrorKind::NotFound => {}
        Err(error) => return Err(NetworkWriteError::apply_failed(error.to_string())),
    }
    let write_result = OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(&temporary)
        .and_then(|mut file| file.write_all(content.as_bytes()));
    if let Err(error) = write_result {
        cleanup_temporary_file(&temporary);
        return Err(NetworkWriteError::apply_failed(error.to_string()));
    }
    match fs::rename(&temporary, path) {
        Ok(()) => Ok(()),
        Err(error) => {
            // 重命名失败时清掉临时文件，绝不在受管目录留下半成品
            cleanup_temporary_file(&temporary);
            Err(NetworkWriteError::apply_failed(error.to_string()))
        }
    }
}

/// 清理失败只能记录告警，不能覆盖真正的写入或重命名错误；
/// 但所有结果都必须显式处理，避免把遗留半成品静默吞掉
fn cleanup_temporary_file(path: &Path) {
    match fs::remove_file(path) {
        Ok(()) => {}
        Err(error) if error.kind() == io::ErrorKind::NotFound => {}
        Err(error) => warn!(
            path = %path.display(),
            %error,
            "failed to remove an incomplete managed network configuration file"
        ),
    }
}

fn restore_managed_file(path: &Path, backup: &ManagedFileBackup) -> Result<(), NetworkWriteError> {
    match backup {
        ManagedFileBackup::Present(content) => write_managed_file(path, content),
        ManagedFileBackup::Absent => match fs::remove_file(path) {
            Ok(()) => Ok(()),
            Err(error) if error.kind() == io::ErrorKind::NotFound => Ok(()),
            Err(error) => Err(NetworkWriteError::rollback_failed(error.to_string())),
        },
    }
}

/// 触发 systemd-networkd 重载：固定绝对路径 + 固定单一参数，
/// 清空环境变量以缩小外部影响面，且不接收任何来自请求的字符串
fn reload_network_backend(
    interaction: SystemInteraction,
    interface_name: &str,
) -> Result<(), NetworkWriteError> {
    match interaction {
        #[cfg(test)]
        SystemInteraction::Simulated => Ok(()),
        SystemInteraction::Live => {
            let mut last_error = String::from("未找到可用的 networkctl 可执行文件");
            for program in RELOAD_PROGRAM_CANDIDATES {
                if !Path::new(program).is_file() {
                    continue;
                }
                let reload = run_networkctl(program, &[RELOAD_ARGUMENT]);
                match reload {
                    Ok(status) if status.success() => {}
                    Ok(status) => {
                        last_error = format!("networkctl reload 返回非零状态 {status}");
                        continue;
                    }
                    Err(error) => {
                        last_error = error.to_string();
                        continue;
                    }
                }
                let reconfigure = run_networkctl(program, &["reconfigure", interface_name]);
                match reconfigure {
                    Ok(status) if status.success() => return Ok(()),
                    Ok(status) => {
                        last_error = format!("networkctl reconfigure 返回非零状态 {status}");
                    }
                    Err(error) => last_error = error.to_string(),
                }
            }
            Err(NetworkWriteError::apply_failed(last_error))
        }
    }
}

fn run_networkctl(program: &str, arguments: &[&str]) -> io::Result<std::process::ExitStatus> {
    run_bounded_command(program, arguments)
}

fn run_bounded_command(program: &str, arguments: &[&str]) -> io::Result<std::process::ExitStatus> {
    let mut child = Command::new(program)
        .args(arguments)
        .env_clear()
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .spawn()?;
    let started = Instant::now();
    loop {
        if let Some(status) = child.try_wait()? {
            return Ok(status);
        }
        if started.elapsed() >= NETWORKCTL_TIMEOUT {
            let _ = child.kill();
            let _ = child.wait();
            return Err(io::Error::new(
                io::ErrorKind::TimedOut,
                "networkctl exceeded the fixed execution timeout",
            ));
        }
        thread::sleep(Duration::from_millis(50));
    }
}

fn interface_ipv4_addresses(interface_name: &str) -> Vec<Ipv4Addr> {
    interface_ipv4_addresses_with_prefix(interface_name)
        .into_iter()
        .map(|(address, _)| address)
        .collect()
}

fn interface_ipv4_addresses_with_prefix(interface_name: &str) -> Vec<(Ipv4Addr, u32)> {
    let Ok(addresses) = getifaddrs() else {
        return Vec::new();
    };
    addresses
        .filter(|entry| entry.interface_name == interface_name)
        .filter_map(|entry| {
            let address = entry.address?.as_sockaddr_in()?.ip();
            let mask = entry.netmask?.as_sockaddr_in()?.ip();
            let bits = u32::from(mask);
            let prefix_length = bits.count_ones();
            let expected = if prefix_length == 0 {
                0
            } else {
                u32::MAX << (32 - prefix_length)
            };
            (bits == expected).then_some((address, prefix_length))
        })
        .collect()
}

fn retain_previous_addresses(
    interaction: SystemInteraction,
    interface: &TargetInterface,
    previous: &[(Ipv4Addr, u32)],
    configuration: &NormalizedNetworkConfiguration,
) -> Result<Vec<String>, NetworkWriteError> {
    #[cfg(test)]
    if interaction == SystemInteraction::Simulated {
        return Ok(Vec::new());
    }
    let _ = interaction;
    let desired = configuration
        .ip_address
        .as_deref()
        .and_then(|value| value.parse::<Ipv4Addr>().ok());
    let active = interface_ipv4_addresses(&interface.name);
    let mut retained = Vec::new();
    for (address, prefix_length) in previous {
        if Some(*address) == desired || address.is_link_local() || address.is_unspecified() {
            continue;
        }
        let cidr = format!("{address}/{prefix_length}");
        if !active.contains(address) {
            run_ip_address_action("add", &cidr, &interface.name)?;
        }
        retained.push(cidr);
    }
    Ok(retained)
}

fn run_ip_address_action(
    action: &str,
    cidr: &str,
    interface_name: &str,
) -> Result<(), NetworkWriteError> {
    let mut last_error = String::from("未找到可用的 ip 可执行文件");
    for program in IP_PROGRAM_CANDIDATES {
        if !Path::new(program).is_file() {
            continue;
        }
        match run_bounded_command(program, &["address", action, cidr, "dev", interface_name]) {
            Ok(status) if status.success() => return Ok(()),
            Ok(status) => last_error = format!("ip address {action} 返回非零状态 {status}"),
            Err(error) => last_error = error.to_string(),
        }
    }
    Err(NetworkWriteError::apply_failed(last_error))
}

pub fn release_retained_addresses(
    environment: &NetworkWriteEnvironment,
    interface_id: &str,
    retained_addresses: &[String],
) {
    if retained_addresses.is_empty() {
        return;
    }
    let Ok(interface) = locate_physical_interface(&environment.sysfs_directory, interface_id)
    else {
        warn!(
            interface = interface_id,
            "could not resolve interface while releasing retained addresses"
        );
        return;
    };
    #[cfg(test)]
    if environment.interaction == SystemInteraction::Simulated {
        return;
    }
    for address in retained_addresses {
        if let Err(failure) = run_ip_address_action("delete", address, &interface.name) {
            warn!(
                interface = %interface.name,
                address,
                code = failure.code,
                "confirmed network configuration kept a temporary previous address"
            );
        }
    }
}

/// 校验配置真正生效：静态模式要求目标地址出现，
/// DHCP 模式要求取得一个非链路本地地址；只看"写了文件"不算生效
fn verify_configuration(
    interaction: SystemInteraction,
    interface: &TargetInterface,
    configuration: &NormalizedNetworkConfiguration,
) -> Result<(), NetworkWriteError> {
    #[cfg(test)]
    if interaction == SystemInteraction::Simulated {
        return Ok(());
    }
    let _ = interaction;

    let expected = match configuration.mode.as_str() {
        MODE_STATIC_IPV4 => configuration
            .ip_address
            .as_deref()
            .and_then(|value| value.parse::<Ipv4Addr>().ok()),
        _ => None,
    };

    for attempt in 0..VERIFICATION_ATTEMPTS {
        let addresses = interface_ipv4_addresses(&interface.name);
        let satisfied = match expected {
            Some(expected) => addresses.contains(&expected),
            None => addresses
                .iter()
                .any(|address| !address.is_link_local() && !address.is_unspecified()),
        };
        if satisfied {
            return Ok(());
        }
        if attempt + 1 < VERIFICATION_ATTEMPTS {
            thread::sleep(VERIFICATION_INTERVAL);
        }
    }

    Err(NetworkWriteError::verification_failed(
        "配置写入后未在限定时间内观察到预期地址",
    ))
}

/// 应用网络配置，返回可用于回滚的原始状态备份
///
/// 顺序固定为：定位网卡 → 生成并校验内容 → 备份原文 → 原子写入 → 重载 → 校验
/// 任何一步在写盘之前失败都不会碰文件；重载或校验失败会立即恢复备份，
/// 并把原始失败码继续上抛，让调用方看到真正的失败原因
pub fn apply_configuration(
    environment: &NetworkWriteEnvironment,
    interface_id: &str,
    configuration: &NormalizedNetworkConfiguration,
) -> Result<AppliedNetworkConfiguration, NetworkWriteError> {
    if !environment.writes_available {
        return Err(NetworkWriteError::apply_failed(
            "超时自动回滚看守线程不可用，已拒绝新的网络配置应用",
        ));
    }
    let validated_configuration = validate_configuration(configuration)?;
    let interface = locate_physical_interface(&environment.sysfs_directory, interface_id)?;
    let content = build_managed_content(&interface, validated_configuration);
    let path = managed_file_path(&environment.configuration_directory, interface_id)?;
    let previous_addresses = interface_ipv4_addresses_with_prefix(&interface.name);

    let backup = match fs::read_to_string(&path) {
        Ok(existing) => ManagedFileBackup::Present(existing),
        Err(error) if error.kind() == io::ErrorKind::NotFound => ManagedFileBackup::Absent,
        Err(error) => return Err(NetworkWriteError::apply_failed(error.to_string())),
    };

    write_managed_file(&path, &content)?;

    let outcome = reload_network_backend(environment.interaction, &interface.name)
        .and_then(|()| verify_configuration(environment.interaction, &interface, configuration));

    match outcome {
        Ok(()) => match retain_previous_addresses(
            environment.interaction,
            &interface,
            &previous_addresses,
            configuration,
        ) {
            Ok(retained_addresses) => Ok(AppliedNetworkConfiguration {
                backup,
                retained_addresses,
            }),
            Err(failure) => {
                restore_managed_file(&path, &backup)
                    .and_then(|()| reload_network_backend(environment.interaction, &interface.name))
                    .map_err(|error| NetworkWriteError::rollback_failed(error.message))?;
                Err(failure)
            }
        },
        Err(failure) => {
            // 尽力恢复到改动前状态；恢复本身失败属于最严重情形，
            // 因为机器可能带着半生效配置失联，必须以最高级别日志暴露
            match restore_managed_file(&path, &backup)
                .and_then(|()| reload_network_backend(environment.interaction, &interface.name))
            {
                Ok(()) => {
                    warn!(
                        code = failure.code,
                        interface = %interface.name,
                        "network configuration apply failed and the previous configuration was restored"
                    );
                    Err(failure)
                }
                Err(restore_failure) => {
                    error!(
                        code = failure.code,
                        restore_code = restore_failure.code,
                        interface = %interface.name,
                        "network configuration apply failed and the restore also failed"
                    );
                    Err(NetworkWriteError::rollback_failed(format!(
                        "应用失败后恢复原配置同样失败：{} / {}",
                        failure.message, restore_failure.message
                    )))
                }
            }
        }
    }
}

/// 回滚到备份状态并重载，用于显式回滚与超时自动回滚两条路径
pub fn rollback_configuration(
    environment: &NetworkWriteEnvironment,
    interface_id: &str,
    backup: &ManagedFileBackup,
) -> Result<(), NetworkWriteError> {
    let interface = locate_physical_interface(&environment.sysfs_directory, interface_id)
        .map_err(|error| NetworkWriteError::rollback_failed(error.message))?;
    let path = managed_file_path(&environment.configuration_directory, interface_id)?;
    restore_managed_file(&path, backup)
        .map_err(|error| NetworkWriteError::rollback_failed(error.message))?;
    reload_network_backend(environment.interaction, &interface.name)
        .map_err(|error| NetworkWriteError::rollback_failed(error.message))
}

#[cfg(test)]
pub(crate) mod test_support {
    use super::*;

    /// 构造指向临时目录的写入环境，测试因此不需要真实系统目录与 root 权限
    pub fn environment(
        configuration_directory: &Path,
        sysfs_directory: &Path,
    ) -> NetworkWriteEnvironment {
        NetworkWriteEnvironment {
            configuration_directory: configuration_directory.to_path_buf(),
            sysfs_directory: sysfs_directory.to_path_buf(),
            interaction: SystemInteraction::Simulated,
            writes_available: true,
        }
    }

    /// 在临时目录里造一张假的物理网卡，含 device 节点与硬件地址
    pub fn fake_interface(sysfs_directory: &Path, name: &str, mac_address: &str) {
        let path = sysfs_directory.join(name);
        fs::create_dir_all(path.join("device")).unwrap();
        fs::write(path.join("address"), format!("{mac_address}\n")).unwrap();
    }

    pub fn temporary_directory(tag: &str) -> PathBuf {
        let path = std::env::temp_dir().join(format!("amseoknas-network-write-{tag}"));
        let _ = fs::remove_dir_all(&path);
        fs::create_dir_all(&path).unwrap();
        path
    }
}

#[cfg(test)]
mod tests {
    use super::test_support::*;
    use super::*;

    const MAC: &str = "aa:bb:cc:dd:ee:ff";
    const INTERFACE_ID: &str = "mac:aa:bb:cc:dd:ee:ff";

    fn configuration(
        mode: &str,
        ip_address: Option<&str>,
        prefix_length: Option<u32>,
        gateway: Option<&str>,
    ) -> NormalizedNetworkConfiguration {
        NormalizedNetworkConfiguration {
            mode: mode.to_owned(),
            ip_address: ip_address.map(str::to_owned),
            prefix_length,
            gateway: gateway.map(str::to_owned),
        }
    }

    #[test]
    fn writes_dhcp_content_matching_the_hardware_address() {
        let root = temporary_directory("dhcp");
        let config_directory = root.join("network");
        let sysfs = root.join("sys");
        fs::create_dir_all(&sysfs).unwrap();
        fake_interface(&sysfs, "enp1s0", MAC);
        let environment = environment(&config_directory, &sysfs);

        let backup = apply_configuration(
            &environment,
            INTERFACE_ID,
            &configuration(MODE_DHCP, None, None, None),
        )
        .unwrap();

        assert_eq!(backup.backup, ManagedFileBackup::Absent);
        let content =
            fs::read_to_string(config_directory.join("70-amseoknas-aabbccddeeff.network")).unwrap();
        assert!(content.contains("MACAddress=aa:bb:cc:dd:ee:ff"));
        assert!(content.contains("DHCP=ipv4"));
        assert!(!content.lines().any(|line| line.starts_with("Address=")));
    }

    #[test]
    fn writes_static_content_with_address_and_gateway() {
        let root = temporary_directory("static");
        let config_directory = root.join("network");
        let sysfs = root.join("sys");
        fs::create_dir_all(&sysfs).unwrap();
        fake_interface(&sysfs, "enp1s0", MAC);
        let environment = environment(&config_directory, &sysfs);

        apply_configuration(
            &environment,
            INTERFACE_ID,
            &configuration(
                MODE_STATIC_IPV4,
                Some("192.168.1.10"),
                Some(24),
                Some("192.168.1.1"),
            ),
        )
        .unwrap();

        let content =
            fs::read_to_string(config_directory.join("70-amseoknas-aabbccddeeff.network")).unwrap();
        assert!(content.contains("DHCP=no"));
        assert!(content.contains("Address=192.168.1.10/24"));
        assert!(content.contains("Gateway=192.168.1.1"));
        assert!(managed_static_declaration(&config_directory, INTERFACE_ID));
    }

    #[test]
    fn rejects_static_configuration_without_writing_any_file() {
        let root = temporary_directory("invalid");
        let config_directory = root.join("network");
        let sysfs = root.join("sys");
        fs::create_dir_all(&sysfs).unwrap();
        fake_interface(&sysfs, "enp1s0", MAC);
        let environment = environment(&config_directory, &sysfs);
        let rejected = [
            configuration(MODE_STATIC_IPV4, None, Some(24), None),
            configuration(MODE_STATIC_IPV4, Some("192.168.1.10"), Some(31), None),
            configuration(
                MODE_STATIC_IPV4,
                Some("192.168.1.10"),
                Some(24),
                Some("10.0.0.1"),
            ),
            configuration(MODE_STATIC_IPV4, Some("192.168.1.10"), None, None),
        ];

        for candidate in rejected {
            let error = apply_configuration(&environment, INTERFACE_ID, &candidate).unwrap_err();
            assert_eq!(error.code, CODE_INVALID_CONFIGURATION);
        }

        assert!(!config_directory.exists());
    }

    #[test]
    fn rejects_an_unmatched_interface_identity() {
        let root = temporary_directory("nomatch");
        let config_directory = root.join("network");
        let sysfs = root.join("sys");
        fs::create_dir_all(&sysfs).unwrap();
        let environment = environment(&config_directory, &sysfs);

        let error = apply_configuration(
            &environment,
            INTERFACE_ID,
            &configuration(MODE_DHCP, None, None, None),
        )
        .unwrap_err();

        assert_eq!(error.code, CODE_INTERFACE_NOT_FOUND);
        assert!(!config_directory.exists());
    }

    #[test]
    fn reports_an_unwritable_managed_directory_without_leaving_a_partial_file() {
        let root = temporary_directory("unwritable");
        let config_directory = root.join("network");
        let sysfs = root.join("sys");
        fs::create_dir_all(&sysfs).unwrap();
        fs::write(&config_directory, "该路径故意是普通文件").unwrap();
        fake_interface(&sysfs, "enp1s0", MAC);
        let environment = environment(&config_directory, &sysfs);

        let error = apply_configuration(
            &environment,
            INTERFACE_ID,
            &configuration(MODE_DHCP, None, None, None),
        )
        .unwrap_err();

        assert_eq!(error.code, CODE_APPLY_FAILED);
        assert!(!root.join("70-amseoknas-aabbccddeeff.network").exists());
        assert!(config_directory.is_file());
    }

    #[test]
    fn rollback_restores_the_previous_content_verbatim() {
        let root = temporary_directory("rollback");
        let config_directory = root.join("network");
        let sysfs = root.join("sys");
        fs::create_dir_all(&sysfs).unwrap();
        fs::create_dir_all(&config_directory).unwrap();
        fake_interface(&sysfs, "enp1s0", MAC);
        let path = config_directory.join("70-amseoknas-aabbccddeeff.network");
        fs::write(&path, "# 旧内容\n[Match]\nMACAddress=aa:bb:cc:dd:ee:ff\n").unwrap();
        let environment = environment(&config_directory, &sysfs);

        let backup = apply_configuration(
            &environment,
            INTERFACE_ID,
            &configuration(MODE_STATIC_IPV4, Some("10.0.0.5"), Some(24), None),
        )
        .unwrap();
        rollback_configuration(&environment, INTERFACE_ID, &backup.backup).unwrap();

        assert_eq!(
            fs::read_to_string(&path).unwrap(),
            "# 旧内容\n[Match]\nMACAddress=aa:bb:cc:dd:ee:ff\n"
        );
    }

    #[test]
    fn rollback_removes_a_file_that_did_not_exist_before() {
        let root = temporary_directory("rollback-absent");
        let config_directory = root.join("network");
        let sysfs = root.join("sys");
        fs::create_dir_all(&sysfs).unwrap();
        fake_interface(&sysfs, "enp1s0", MAC);
        let environment = environment(&config_directory, &sysfs);

        let backup = apply_configuration(
            &environment,
            INTERFACE_ID,
            &configuration(MODE_DHCP, None, None, None),
        )
        .unwrap();
        rollback_configuration(&environment, INTERFACE_ID, &backup.backup).unwrap();

        assert!(
            !config_directory
                .join("70-amseoknas-aabbccddeeff.network")
                .exists()
        );
    }

    #[test]
    fn managed_file_name_is_derived_only_from_validated_hexadecimal() {
        assert_eq!(
            managed_file_name(INTERFACE_ID).unwrap(),
            "70-amseoknas-aabbccddeeff.network"
        );
        assert!(managed_file_name("mac:../../etc/passwd").is_none());
        assert!(managed_file_name("name:enp1s0").is_none());
        assert!(managed_file_name("mac:aa:bb:cc:dd:ee").is_none());
    }
}
