//--------------------------//
//--------在本机独立看守待确认的网络改动并在超时后自动回滚---------//
//--------Independently watches pending network changes on the local machine and rolls them back after the deadline--------//
//-------------------------//
//
// 为什么独立成一个文件，而不是并入 network_write：
// network_write 负责"一次改动怎么落盘、怎么校验、怎么恢复"，是无状态的事务动作
// 本模块负责"哪些改动还在等确认、谁到期了、谁该被回滚"，是有状态的看守
// 两者生命周期与并发要求完全不同，分开后各自的边界才能被单独审计
//
// 为什么回滚必须由本机后台线程独立完成：
// 网络地址一旦改错，调用方（浏览器或 C# 服务）可能再也连不上本机
// 若把回滚寄托在"调用方会再来一次请求"上，失联场景下就永远回不去
// 因此到期回滚由本进程内的后台线程按固定节拍自行触发，不依赖任何外部连接
//
// 已知限制（本期不处理，明确记录以免误以为已覆盖）：
// 待确认状态只保存在进程内存中，特权守护进程一旦重启，内存中的待确认记录即丢失
// 此时已写入的配置会失去看守，不会被自动回滚，需要人工确认或人工回滚
// 落盘持久化待确认状态属于下一期工作，不在本次改动范围内
use std::collections::{HashMap, HashSet};
use std::sync::{Arc, Mutex};
use std::thread;
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use tracing::{error, info, warn};

use crate::network_write::{
    ManagedFileBackup, NetworkWriteEnvironment, NetworkWriteError, rollback_configuration,
};

/// 看守线程的唤醒节拍：足够密以免超时后长时间不回滚，
/// 又足够稀以免空转占用 CPU
const WATCH_INTERVAL: Duration = Duration::from_secs(1);

/// 操作状态字面量，与 C# 端 NetworkConfigurationOperationStatus 一一对应
pub const STATUS_AWAITING_CONFIRMATION: &str = "awaitingConfirmation";
pub const STATUS_CONFIRMED: &str = "confirmed";
pub const STATUS_ROLLED_BACK: &str = "rolledBack";

/// 单条待确认改动：接口标识与备份合起来才够回滚，
/// 缺任何一个都无法把机器恢复到改动前状态
#[derive(Debug, Clone)]
pub struct PendingChange {
    pub operation_id: String,
    pub interface_id: String,
    pub backup: ManagedFileBackup,
    pub confirmation_deadline_unix_milliseconds: i64,
    pub status: &'static str,
}

/// 登记表内部状态把记录与“正在回滚”占用放在同一把锁下，
/// 避免确认、显式回滚和超时回滚同时处理同一个操作
#[derive(Debug, Default)]
struct PendingChangeRegistryState {
    entries: HashMap<String, PendingChange>,
    rollback_claims: HashSet<String>,
}

/// 以操作标识为索引的待确认登记表，跨连接线程与看守线程共享
#[derive(Debug, Default)]
pub struct PendingChangeRegistry {
    state: Mutex<PendingChangeRegistryState>,
}

/// 共享句柄类型：连接线程与看守线程持有同一份登记表
pub type SharedPendingChangeRegistry = Arc<PendingChangeRegistry>;

impl PendingChangeRegistry {
    pub fn new_shared() -> SharedPendingChangeRegistry {
        Arc::new(Self::default())
    }

    /// 锁中毒不能让服务直接崩掉：登记表本身只是普通数据，
    /// 取回内部值继续使用比让整个特权守护进程 panic 更安全
    fn locked(&self) -> std::sync::MutexGuard<'_, PendingChangeRegistryState> {
        match self.state.lock() {
            Ok(state) => state,
            Err(poisoned) => poisoned.into_inner(),
        }
    }

    /// 某网卡是否已有待确认改动：同一网卡并发两次改动会互相覆盖备份，
    /// 必须在入口就拒绝，否则回滚基准会被第二次改动污染
    pub fn has_awaiting_change_for_interface(&self, interface_id: &str) -> bool {
        self.locked().entries.values().any(|entry| {
            entry.interface_id == interface_id && entry.status == STATUS_AWAITING_CONFIRMATION
        })
    }

    /// 登记一条待确认改动；操作标识重复即视为冲突，绝不覆盖已有记录
    pub fn register(&self, change: PendingChange) -> Result<(), NetworkWriteError> {
        let mut state = self.locked();
        if state.entries.contains_key(&change.operation_id) {
            return Err(NetworkWriteError::operation_conflict(
                "该操作标识已存在待确认记录",
            ));
        }
        state.entries.insert(change.operation_id.clone(), change);
        Ok(())
    }

    pub fn find(&self, operation_id: &str) -> Option<PendingChange> {
        self.locked().entries.get(operation_id).cloned()
    }

    /// 确认并移除：确认成功后这条改动就不再需要看守，
    /// 移除即等于停掉它的超时自动回滚
    pub fn confirm_and_remove(&self, operation_id: &str) -> Option<PendingChange> {
        let mut state = self.locked();
        if state.rollback_claims.contains(operation_id) {
            return None;
        }
        match state.entries.remove(operation_id) {
            Some(mut change) => {
                change.status = STATUS_CONFIRMED;
                Some(change)
            }
            None => None,
        }
    }

    /// 显式回滚先占用记录但不移除；成功后才删除，失败则保留供再次处理
    /// 这样既能避免后台线程重复回滚，也不会在回滚失败时丢失唯一恢复依据
    pub fn claim_rollback(&self, operation_id: &str) -> Option<PendingChange> {
        let mut state = self.locked();
        if state.rollback_claims.contains(operation_id) {
            return None;
        }
        let change = match state.entries.get(operation_id) {
            Some(change) => change.clone(),
            None => return None,
        };
        state.rollback_claims.insert(operation_id.to_owned());
        Some(change)
    }

    /// 收集已过期且仍在等待确认的操作标识；只在锁内做纯内存筛选，
    /// 任何文件与外部程序操作都必须留到锁外，避免持锁做 IO 堵住连接线程
    fn collect_expired(&self, now_unix_milliseconds: i64) -> Vec<String> {
        self.locked()
            .entries
            .values()
            .filter(|entry| {
                entry.status == STATUS_AWAITING_CONFIRMATION
                    && entry.confirmation_deadline_unix_milliseconds <= now_unix_milliseconds
            })
            .map(|entry| entry.operation_id.clone())
            .collect()
    }

    /// 在真正执行回滚前占用记录；占用与复核期限在同一临界区完成，
    /// 一旦占用成功，确认和其他回滚路径都不能再同时处理它
    fn claim_expired_rollback(
        &self,
        operation_id: &str,
        now_unix_milliseconds: i64,
    ) -> Option<PendingChange> {
        let mut state = self.locked();
        if state.rollback_claims.contains(operation_id) {
            return None;
        }
        let change = match state.entries.get(operation_id) {
            Some(change)
                if change.status == STATUS_AWAITING_CONFIRMATION
                    && change.confirmation_deadline_unix_milliseconds <= now_unix_milliseconds =>
            {
                change.clone()
            }
            Some(_) | None => return None,
        };
        state.rollback_claims.insert(operation_id.to_owned());
        Some(change)
    }

    pub fn finish_rollback(&self, operation_id: &str, succeeded: bool) {
        let mut state = self.locked();
        state.rollback_claims.remove(operation_id);
        if succeeded {
            state.entries.remove(operation_id);
        }
    }

    #[cfg(test)]
    fn length(&self) -> usize {
        self.locked().entries.len()
    }
}

/// 扫描一轮到期记录并回滚，返回本轮实际回滚成功的条数
///
/// 拆成独立函数是为了让单元测试可以只跑一轮扫描，
/// 而不需要真的启动后台线程去等待时钟
pub fn sweep_expired_changes(
    registry: &PendingChangeRegistry,
    environment: &NetworkWriteEnvironment,
    now_unix_milliseconds: i64,
) -> usize {
    let expired_operation_ids = registry.collect_expired(now_unix_milliseconds);
    let mut rolled_back = 0;
    for operation_id in expired_operation_ids {
        let change = match registry.claim_expired_rollback(&operation_id, now_unix_milliseconds) {
            Some(change) => change,
            None => continue,
        };
        // 占用已写入登记表且锁已释放，这里的回滚不会与确认或另一条回滚并发
        match rollback_configuration(environment, &change.interface_id, &change.backup) {
            Ok(()) => {
                registry.finish_rollback(&change.operation_id, true);
                rolled_back += 1;
                warn!(
                    operation = %change.operation_id,
                    interface = %change.interface_id,
                    "pending network change exceeded its confirmation deadline and was rolled back"
                );
            }
            Err(failure) => {
                // 回滚失败是最严重情形：机器可能带着未确认的配置失联，
                // 记录必须保留并以最高级别日志暴露，等待人工介入
                registry.finish_rollback(&change.operation_id, false);
                error!(
                    operation = %change.operation_id,
                    interface = %change.interface_id,
                    code = failure.code,
                    "pending network change rollback failed and the record was kept for manual recovery"
                );
            }
        }
    }
    rolled_back
}

/// 启动后台看守线程；线程内部对任何单次失败都只记日志，
/// 绝不退出也绝不 panic，否则整机就再没有自动回滚这道保险
pub fn spawn_watcher(
    registry: SharedPendingChangeRegistry,
    environment: NetworkWriteEnvironment,
) -> std::io::Result<thread::JoinHandle<()>> {
    thread::Builder::new()
        .name("amseoknas-network-watch".to_owned())
        .spawn(move || {
            info!("pending network change watcher started");
            warn!(
                "pending network changes are stored in memory only and are lost after a daemon restart"
            );
            loop {
                thread::sleep(WATCH_INTERVAL);
                sweep_expired_changes(&registry, &environment, current_unix_milliseconds());
            }
        })
}

pub fn current_unix_milliseconds() -> i64 {
    let elapsed = match SystemTime::now().duration_since(UNIX_EPOCH) {
        Ok(elapsed) => elapsed,
        Err(_) => Duration::ZERO,
    };
    let milliseconds = elapsed.as_millis();
    if milliseconds > i64::MAX as u128 {
        i64::MAX
    } else {
        milliseconds as i64
    }
}

#[cfg(test)]
mod tests {
    use std::fs;

    use super::*;
    use crate::network_write::{
        NormalizedNetworkConfiguration, apply_configuration, managed_file_name, test_support,
    };

    const MAC: &str = "aa:bb:cc:dd:ee:ff";
    const INTERFACE_ID: &str = "mac:aa:bb:cc:dd:ee:ff";

    fn static_configuration() -> NormalizedNetworkConfiguration {
        NormalizedNetworkConfiguration {
            mode: crate::network_write::MODE_STATIC_IPV4.to_owned(),
            ip_address: Some("192.168.1.10".to_owned()),
            prefix_length: Some(24),
            gateway: Some("192.168.1.1".to_owned()),
        }
    }

    fn managed_path(directory: &std::path::Path) -> std::path::PathBuf {
        let name = match managed_file_name(INTERFACE_ID) {
            Some(name) => name,
            None => panic!("受管文件名生成失败"),
        };
        directory.join(name)
    }

    #[test]
    fn one_sweep_rolls_back_and_removes_an_expired_change() {
        let configuration_directory = test_support::temporary_directory("pending-expired-config");
        let sysfs_directory = test_support::temporary_directory("pending-expired-sysfs");
        test_support::fake_interface(&sysfs_directory, "enp1s0", MAC);
        let environment = test_support::environment(&configuration_directory, &sysfs_directory);
        let backup = apply_configuration(&environment, INTERFACE_ID, &static_configuration())
            .expect("应用应当成功");
        assert!(managed_path(&configuration_directory).exists());

        let registry = PendingChangeRegistry::default();
        registry
            .register(PendingChange {
                operation_id: "operation-expired".to_owned(),
                interface_id: INTERFACE_ID.to_owned(),
                backup,
                confirmation_deadline_unix_milliseconds: 1,
                status: STATUS_AWAITING_CONFIRMATION,
            })
            .expect("登记应当成功");

        let rolled_back = sweep_expired_changes(&registry, &environment, 1_000);

        assert_eq!(rolled_back, 1);
        assert_eq!(registry.length(), 0);
        // 改动前该受管文件不存在，回滚应当删除文件而不是留下空内容
        assert!(!managed_path(&configuration_directory).exists());
    }

    #[test]
    fn a_confirmed_change_is_never_rolled_back() {
        let configuration_directory = test_support::temporary_directory("pending-confirmed-config");
        let sysfs_directory = test_support::temporary_directory("pending-confirmed-sysfs");
        test_support::fake_interface(&sysfs_directory, "enp1s0", MAC);
        let environment = test_support::environment(&configuration_directory, &sysfs_directory);
        let backup = apply_configuration(&environment, INTERFACE_ID, &static_configuration())
            .expect("应用应当成功");

        let registry = PendingChangeRegistry::default();
        registry
            .register(PendingChange {
                operation_id: "operation-confirmed".to_owned(),
                interface_id: INTERFACE_ID.to_owned(),
                backup,
                confirmation_deadline_unix_milliseconds: 1,
                status: STATUS_AWAITING_CONFIRMATION,
            })
            .expect("登记应当成功");
        let confirmed = registry.confirm_and_remove("operation-confirmed");
        assert!(matches!(confirmed, Some(change) if change.status == STATUS_CONFIRMED));

        let rolled_back = sweep_expired_changes(&registry, &environment, 1_000);

        assert_eq!(rolled_back, 0);
        let content = fs::read_to_string(managed_path(&configuration_directory))
            .expect("确认后的配置应当保留");
        assert!(content.contains("Address=192.168.1.10/24"));
    }

    #[test]
    fn a_failed_timeout_rollback_keeps_the_pending_record() {
        let root = test_support::temporary_directory("pending-failed-config");
        let configuration_directory = root.join("network");
        let sysfs_directory = root.join("sysfs");
        fs::write(&configuration_directory, "该路径故意是普通文件").expect("测试夹具应当写入成功");
        let environment = test_support::environment(&configuration_directory, &sysfs_directory);
        let registry = PendingChangeRegistry::default();
        registry
            .register(PendingChange {
                operation_id: "operation-failed".to_owned(),
                interface_id: INTERFACE_ID.to_owned(),
                backup: ManagedFileBackup::Absent,
                confirmation_deadline_unix_milliseconds: 1,
                status: STATUS_AWAITING_CONFIRMATION,
            })
            .expect("登记应当成功");

        let rolled_back = sweep_expired_changes(&registry, &environment, 1_000);

        assert_eq!(rolled_back, 0);
        assert!(registry.find("operation-failed").is_some());
        assert!(registry.has_awaiting_change_for_interface(INTERFACE_ID));
    }

    #[test]
    fn rejects_a_duplicate_operation_identifier() {
        let registry = PendingChangeRegistry::default();
        let change = PendingChange {
            operation_id: "operation-duplicate".to_owned(),
            interface_id: INTERFACE_ID.to_owned(),
            backup: ManagedFileBackup::Absent,
            confirmation_deadline_unix_milliseconds: 1,
            status: STATUS_AWAITING_CONFIRMATION,
        };
        registry.register(change.clone()).expect("首次登记应当成功");

        let error = registry.register(change).expect_err("重复登记应当被拒绝");

        assert_eq!(error.code, crate::network_write::CODE_OPERATION_CONFLICT);
        assert_eq!(registry.length(), 1);
    }

    #[test]
    fn reports_an_awaiting_change_for_the_same_interface() {
        let registry = PendingChangeRegistry::default();
        registry
            .register(PendingChange {
                operation_id: "operation-awaiting".to_owned(),
                interface_id: INTERFACE_ID.to_owned(),
                backup: ManagedFileBackup::Absent,
                confirmation_deadline_unix_milliseconds: i64::MAX,
                status: STATUS_AWAITING_CONFIRMATION,
            })
            .expect("登记应当成功");

        assert!(registry.has_awaiting_change_for_interface(INTERFACE_ID));
        assert!(!registry.has_awaiting_change_for_interface("mac:00:11:22:33:44:55"));
    }
}
