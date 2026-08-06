//--------------------------//
//--------定义有界版本化的特权查询协议---------//
//--------Defines the bounded versioned privileged-query protocol--------//
//-------------------------//
use std::io::{self, Read, Write};
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

use serde::{Deserialize, Serialize};
use serde_json::Value;

use crate::inventory;
use crate::network_write::{
    self, NetworkWriteEnvironment, NetworkWriteError, NormalizedNetworkConfiguration,
};
use crate::pending_changes::{
    PendingChange, STATUS_AWAITING_CONFIRMATION, STATUS_CONFIRMED, STATUS_ROLLED_BACK,
    SharedPendingChangeRegistry,
};

pub const PROTOCOL_VERSION: u16 = 1;
pub const MAXIMUM_FRAME_BYTES: usize = 1024 * 1024;

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
pub struct RequestEnvelope {
    protocol_version: u16,
    request_id: String,
    action: String,
    deadline_unix_milliseconds: i64,
    parameters: Value,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct ResponseEnvelope {
    protocol_version: u16,
    request_id: String,
    success: bool,
    result: Option<Value>,
    error: Option<ProtocolError>,
    diagnostics: Diagnostics,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct ProtocolError {
    code: &'static str,
    message: String,
    retryable: bool,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct Diagnostics {
    duration_ms: u128,
    truncated: bool,
}

#[derive(Debug, Deserialize)]
#[serde(deny_unknown_fields)]
struct EmptyParameters {}

/// 应用网络配置的入参，字段与第一章契约一一对应；
/// 拒绝未知字段，避免调用方多写字段却以为已被采纳
#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct ApplyConfigurationParameters {
    operation_id: String,
    interface_id: String,
    mode: String,
    ip_address: Option<String>,
    prefix_length: Option<u32>,
    gateway: Option<String>,
    confirmation_deadline_unix_milliseconds: i64,
}

/// 确认与回滚的入参只有操作标识，与 C# 端两个方法的签名一致
#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct OperationParameters {
    operation_id: String,
}

/// 三个写入动作统一的成功返回体，状态取 pending_changes 中的固定字面量
#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct OperationResult {
    operation_id: String,
    status: &'static str,
    confirmation_deadline_unix_milliseconds: i64,
}

pub fn handle_connection(
    stream: &mut (impl Read + Write),
    registry: &SharedPendingChangeRegistry,
    environment: &NetworkWriteEnvironment,
) -> io::Result<()> {
    let started = Instant::now();
    let request_payload = read_frame(stream)?;
    let request = match serde_json::from_slice::<RequestEnvelope>(&request_payload) {
        Ok(request) => request,
        Err(_) => {
            return write_response(
                stream,
                ResponseEnvelope::failure(
                    "unknown".to_owned(),
                    "request.invalid",
                    "请求格式无效",
                    false,
                    started,
                ),
            );
        }
    };

    let response = process_request(request, registry, environment, started);
    write_response(stream, response)
}

fn process_request(
    request: RequestEnvelope,
    registry: &SharedPendingChangeRegistry,
    environment: &NetworkWriteEnvironment,
    started: Instant,
) -> ResponseEnvelope {
    if request.protocol_version != PROTOCOL_VERSION {
        return ResponseEnvelope::failure(
            request.request_id,
            "protocol.unsupported_version",
            "协议版本不受支持",
            false,
            started,
        );
    }
    if request.request_id.is_empty()
        || request.request_id.len() > 64
        || !request.request_id.is_ascii()
    {
        return ResponseEnvelope::failure(
            "unknown".to_owned(),
            "request.invalid",
            "请求标识无效",
            false,
            started,
        );
    }
    if request.deadline_unix_milliseconds <= current_unix_milliseconds() {
        return ResponseEnvelope::failure(
            request.request_id,
            "request.deadline_exceeded",
            "请求已超过期限",
            true,
            started,
        );
    }
    // 参数校验从"全局一刀切"改为"逐动作自校验"
    // 只读查询依旧一律不接受参数，写入动作要自己反序列化强类型参数
    // 因此空参数这道门必须下沉到各只读分支，不能再拦在动作分发之前
    let RequestEnvelope {
        request_id,
        action,
        parameters,
        ..
    } = request;

    let result = match action.as_str() {
        "system.getAbout" => match require_empty_parameters(&parameters, &request_id, started) {
            Err(failure) => return *failure,
            Ok(()) => inventory::system::get_about().and_then(to_json_value),
        },
        "network.inspectInterfaces" => {
            match require_empty_parameters(&parameters, &request_id, started) {
                Err(failure) => return *failure,
                Ok(()) => inventory::network::inspect_interfaces().and_then(to_json_value),
            }
        }
        "storage.inspectBlockDevices" => {
            match require_empty_parameters(&parameters, &request_id, started) {
                Err(failure) => return *failure,
                Ok(()) => inventory::storage::inspect_block_devices().and_then(to_json_value),
            }
        }
        "raid.inspectArrays" => match require_empty_parameters(&parameters, &request_id, started) {
            Err(failure) => return *failure,
            Ok(()) => inventory::raid::inspect_arrays().and_then(to_json_value),
        },
        // 三个写入动作各自反序列化强类型参数，绝不走只读的空参数守卫
        // 失败码一律取第一章约定的稳定错误码，不得混入 inventory.read_failed
        "network.applyConfiguration" => {
            return apply_configuration_action(
                &parameters,
                request_id,
                registry,
                environment,
                started,
            );
        }
        "network.confirmConfiguration" => {
            return confirm_configuration_action(&parameters, request_id, registry, started);
        }
        "network.rollbackConfiguration" => {
            return rollback_configuration_action(
                &parameters,
                request_id,
                registry,
                environment,
                started,
            );
        }
        _ => {
            return ResponseEnvelope::failure(
                request_id,
                "request.unknown_action",
                "请求动作未登记",
                false,
                started,
            );
        }
    };

    match result {
        Ok(result) => ResponseEnvelope::success(request_id, result, started),
        Err(error) => ResponseEnvelope::failure(
            request_id,
            "inventory.read_failed",
            error.to_string(),
            true,
            started,
        ),
    }
}

/// 只读动作的空参数守卫：只读查询不接受任何参数，
/// 携带参数一律视为请求无效，避免调用方误以为参数被采纳
fn require_empty_parameters(
    parameters: &Value,
    request_id: &str,
    started: Instant,
) -> Result<(), Box<ResponseEnvelope>> {
    match serde_json::from_value::<EmptyParameters>(parameters.clone()) {
        Ok(_) => Ok(()),
        Err(_) => Err(Box::new(ResponseEnvelope::failure(
            request_id.to_owned(),
            "request.invalid",
            "该查询不接受参数",
            false,
            started,
        ))),
    }
}

fn to_json_value<T: Serialize>(value: T) -> io::Result<Value> {
    serde_json::to_value(value).map_err(io::Error::other)
}

/// 把写入模块的稳定错误码原样搬到协议错误里：
/// 写入失败绝不能被折叠成只读失败码，否则调用方无法区分该重试还是该改参数
fn write_failure(
    request_id: String,
    error: NetworkWriteError,
    started: Instant,
) -> ResponseEnvelope {
    ResponseEnvelope::failure(
        request_id,
        error.code,
        error.message,
        error.retryable,
        started,
    )
}

/// 写入动作成功返回体的统一装配；序列化失败只可能是内存结构异常，
/// 归入应用失败并允许重试，绝不 unwrap
fn write_success(
    request_id: String,
    result: OperationResult,
    started: Instant,
) -> ResponseEnvelope {
    match to_json_value(result) {
        Ok(value) => ResponseEnvelope::success(request_id, value, started),
        Err(error) => ResponseEnvelope::failure(
            request_id,
            network_write::CODE_APPLY_FAILED,
            error.to_string(),
            true,
            started,
        ),
    }
}

/// 应用网络配置：参数不合法、同网卡已有待确认改动都在写盘之前被拦下
fn apply_configuration_action(
    parameters: &Value,
    request_id: String,
    registry: &SharedPendingChangeRegistry,
    environment: &NetworkWriteEnvironment,
    started: Instant,
) -> ResponseEnvelope {
    let parameters =
        match serde_json::from_value::<ApplyConfigurationParameters>(parameters.clone()) {
            Ok(parameters) => parameters,
            Err(error) => {
                return write_failure(
                    request_id,
                    NetworkWriteError::invalid_configuration(error.to_string()),
                    started,
                );
            }
        };

    // 操作标识重复同样必须在写盘前拒绝；若等到应用后登记才发现，
    // 会无谓地改写一次网络再回滚，扩大失联窗口
    if registry.find(&parameters.operation_id).is_some() {
        return write_failure(
            request_id,
            NetworkWriteError::operation_conflict("该操作标识已存在待确认记录"),
            started,
        );
    }

    // 同一网卡并发两次改动会互相覆盖备份，回滚基准会被污染，必须在入口就拒绝
    if registry.has_awaiting_change_for_interface(&parameters.interface_id) {
        return write_failure(
            request_id,
            NetworkWriteError::operation_conflict("该网卡已有待确认的改动"),
            started,
        );
    }

    let configuration = NormalizedNetworkConfiguration {
        mode: parameters.mode,
        ip_address: parameters.ip_address,
        prefix_length: parameters.prefix_length,
        gateway: parameters.gateway,
    };
    let backup = match network_write::apply_configuration(
        environment,
        &parameters.interface_id,
        &configuration,
    ) {
        Ok(backup) => backup,
        Err(error) => return write_failure(request_id, error, started),
    };

    // 登记失败意味着这份已生效的配置无人看守，超时也不会自动回滚
    // 因此必须立刻回滚，绝不允许留下一个没有看守的生效配置
    if let Err(error) = registry.register(PendingChange {
        operation_id: parameters.operation_id.clone(),
        interface_id: parameters.interface_id.clone(),
        backup: backup.clone(),
        confirmation_deadline_unix_milliseconds: parameters.confirmation_deadline_unix_milliseconds,
        status: STATUS_AWAITING_CONFIRMATION,
    }) {
        return match network_write::rollback_configuration(
            environment,
            &parameters.interface_id,
            &backup,
        ) {
            Ok(()) => write_failure(request_id, error, started),
            Err(rollback_error) => write_failure(request_id, rollback_error, started),
        };
    }

    write_success(
        request_id,
        OperationResult {
            operation_id: parameters.operation_id,
            status: STATUS_AWAITING_CONFIRMATION,
            confirmation_deadline_unix_milliseconds: parameters
                .confirmation_deadline_unix_milliseconds,
        },
        started,
    )
}

/// 确认网络配置：确认即从登记表移除，等于停掉这条改动的超时自动回滚
fn confirm_configuration_action(
    parameters: &Value,
    request_id: String,
    registry: &SharedPendingChangeRegistry,
    started: Instant,
) -> ResponseEnvelope {
    let parameters = match serde_json::from_value::<OperationParameters>(parameters.clone()) {
        Ok(parameters) => parameters,
        Err(error) => {
            return write_failure(
                request_id,
                NetworkWriteError::invalid_configuration(error.to_string()),
                started,
            );
        }
    };

    // 确认与移除在登记表的一次持锁操作内完成；若记录正被回滚占用，
    // 同样按不存在回复，绝不能在回滚已经开始后再假装确认成功
    match registry.confirm_and_remove(&parameters.operation_id) {
        Some(change) => write_success(
            request_id,
            OperationResult {
                operation_id: change.operation_id,
                status: STATUS_CONFIRMED,
                confirmation_deadline_unix_milliseconds: change
                    .confirmation_deadline_unix_milliseconds,
            },
            started,
        ),
        None => write_failure(
            request_id,
            NetworkWriteError::operation_not_found("该待确认改动已被超时回滚"),
            started,
        ),
    }
}

/// 显式回滚：先占用记录，成功后才移除；失败时保留记录，
/// 既避免看守线程重复回滚，也保住再次恢复所需的唯一备份
fn rollback_configuration_action(
    parameters: &Value,
    request_id: String,
    registry: &SharedPendingChangeRegistry,
    environment: &NetworkWriteEnvironment,
    started: Instant,
) -> ResponseEnvelope {
    let parameters = match serde_json::from_value::<OperationParameters>(parameters.clone()) {
        Ok(parameters) => parameters,
        Err(error) => {
            return write_failure(
                request_id,
                NetworkWriteError::invalid_configuration(error.to_string()),
                started,
            );
        }
    };

    let change = match registry.claim_rollback(&parameters.operation_id) {
        Some(change) => change,
        None => {
            return write_failure(
                request_id,
                NetworkWriteError::operation_not_found("未找到该操作标识对应的待确认改动"),
                started,
            );
        }
    };

    match network_write::rollback_configuration(environment, &change.interface_id, &change.backup) {
        Ok(()) => {
            registry.finish_rollback(&change.operation_id, true);
            write_success(
                request_id,
                OperationResult {
                    operation_id: change.operation_id,
                    status: STATUS_ROLLED_BACK,
                    confirmation_deadline_unix_milliseconds: change
                        .confirmation_deadline_unix_milliseconds,
                },
                started,
            )
        }
        Err(error) => {
            registry.finish_rollback(&change.operation_id, false);
            write_failure(request_id, error, started)
        }
    }
}

fn read_frame(stream: &mut impl Read) -> io::Result<Vec<u8>> {
    let mut header = [0_u8; 4];
    stream.read_exact(&mut header)?;
    let length = u32::from_be_bytes(header) as usize;
    if length == 0 || length > MAXIMUM_FRAME_BYTES {
        return Err(io::Error::new(
            io::ErrorKind::InvalidData,
            "privileged request frame length is invalid",
        ));
    }

    let mut payload = vec![0_u8; length];
    stream.read_exact(&mut payload)?;
    Ok(payload)
}

fn write_response(stream: &mut impl Write, response: ResponseEnvelope) -> io::Result<()> {
    let payload = serde_json::to_vec(&response).map_err(io::Error::other)?;
    if payload.len() > MAXIMUM_FRAME_BYTES {
        return Err(io::Error::new(
            io::ErrorKind::InvalidData,
            "privileged response exceeds the protocol limit",
        ));
    }
    stream.write_all(&(payload.len() as u32).to_be_bytes())?;
    stream.write_all(&payload)?;
    stream.flush()
}

fn current_unix_milliseconds() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or(Duration::ZERO)
        .as_millis()
        .try_into()
        .unwrap_or(i64::MAX)
}

impl ResponseEnvelope {
    fn success(request_id: String, result: Value, started: Instant) -> Self {
        Self {
            protocol_version: PROTOCOL_VERSION,
            request_id,
            success: true,
            result: Some(result),
            error: None,
            diagnostics: Diagnostics {
                duration_ms: started.elapsed().as_millis(),
                truncated: false,
            },
        }
    }

    fn failure(
        request_id: String,
        code: &'static str,
        message: impl Into<String>,
        retryable: bool,
        started: Instant,
    ) -> Self {
        Self {
            protocol_version: PROTOCOL_VERSION,
            request_id,
            success: false,
            result: None,
            error: Some(ProtocolError {
                code,
                message: message.into(),
                retryable,
            }),
            diagnostics: Diagnostics {
                duration_ms: started.elapsed().as_millis(),
                truncated: false,
            },
        }
    }
}

#[cfg(test)]
mod tests {
    use std::fs;
    use std::io::Cursor;

    use serde_json::json;

    use super::*;
    use crate::network_write::test_support;
    use crate::pending_changes::PendingChangeRegistry;

    const MAC: &str = "aa:bb:cc:dd:ee:ff";
    const INTERFACE_ID: &str = "mac:aa:bb:cc:dd:ee:ff";

    /// 只读测试用的空登记表与空写入环境：只读分支根本不会碰它们，
    /// 但签名要求必须传入，因此统一在这里造一份指向临时目录的环境
    fn read_only_context(tag: &str) -> (SharedPendingChangeRegistry, NetworkWriteEnvironment) {
        let configuration_directory = test_support::temporary_directory(&format!("{tag}-config"));
        let sysfs_directory = test_support::temporary_directory(&format!("{tag}-sysfs"));
        (
            PendingChangeRegistry::new_shared(),
            test_support::environment(&configuration_directory, &sysfs_directory),
        )
    }

    /// 走一遍完整的收发帧流程并取回响应，避免每个测试重复拼装长度前缀
    fn round_trip(
        request: Value,
        registry: &SharedPendingChangeRegistry,
        environment: &NetworkWriteEnvironment,
    ) -> Value {
        let payload = serde_json::to_vec(&request).unwrap();
        let mut input = Vec::new();
        input.extend_from_slice(&(payload.len() as u32).to_be_bytes());
        input.extend_from_slice(&payload);
        let mut stream = Cursor::new(input);

        handle_connection(&mut stream, registry, environment).unwrap();

        decode_response(stream.into_inner(), payload.len())
    }

    fn envelope(request_id: &str, action: &str, parameters: Value) -> Value {
        json!({
            "protocolVersion": 1,
            "requestId": request_id,
            "action": action,
            "deadlineUnixMilliseconds": current_unix_milliseconds() + 5_000,
            "parameters": parameters
        })
    }

    fn apply_parameters(operation_id: &str) -> Value {
        json!({
            "operationId": operation_id,
            "interfaceId": INTERFACE_ID,
            "mode": "staticIpv4",
            "ipAddress": "192.168.1.10",
            "prefixLength": 24,
            "gateway": "192.168.1.1",
            "confirmationDeadlineUnixMilliseconds": current_unix_milliseconds() + 120_000
        })
    }

    /// 从游标缓冲区中切出响应帧：请求与响应共用同一个游标，
    /// 响应紧跟在请求帧之后，因此必须按请求长度跳过前半段
    fn decode_response(bytes: Vec<u8>, request_payload_length: usize) -> Value {
        let response_offset = 4 + request_payload_length;
        let response_length = u32::from_be_bytes(
            bytes[response_offset..response_offset + 4]
                .try_into()
                .unwrap(),
        ) as usize;
        serde_json::from_slice(&bytes[response_offset + 4..response_offset + 4 + response_length])
            .unwrap()
    }

    #[test]
    fn rejects_unknown_actions_without_executing_a_command() {
        let (registry, environment) = read_only_context("protocol-unknown");

        let response = round_trip(
            envelope("test-request", "system.runCommand", json!({})),
            &registry,
            &environment,
        );

        assert_eq!(response["success"], false);
        assert_eq!(response["error"]["code"], "request.unknown_action");
    }

    #[test]
    fn rejects_a_read_only_action_that_carries_parameters() {
        let (registry, environment) = read_only_context("protocol-read-parameters");

        let response = round_trip(
            envelope(
                "test-read-parameters",
                "system.getAbout",
                json!({"unexpected": 1}),
            ),
            &registry,
            &environment,
        );

        assert_eq!(response["success"], false);
        assert_eq!(response["error"]["code"], "request.invalid");
    }

    #[test]
    fn rejects_oversized_frames_before_allocation() {
        let (registry, environment) = read_only_context("protocol-oversized");
        let mut stream = Cursor::new(((MAXIMUM_FRAME_BYTES + 1) as u32).to_be_bytes());

        let error = handle_connection(&mut stream, &registry, &environment).unwrap_err();

        assert_eq!(error.kind(), io::ErrorKind::InvalidData);
    }

    #[test]
    fn applies_a_configuration_and_then_confirms_it() {
        let configuration_directory = test_support::temporary_directory("protocol-apply-config");
        let sysfs_directory = test_support::temporary_directory("protocol-apply-sysfs");
        test_support::fake_interface(&sysfs_directory, "enp1s0", MAC);
        let environment = test_support::environment(&configuration_directory, &sysfs_directory);
        let registry = PendingChangeRegistry::new_shared();

        let applied = round_trip(
            envelope(
                "request-apply",
                "network.applyConfiguration",
                apply_parameters("operation-apply"),
            ),
            &registry,
            &environment,
        );

        assert_eq!(applied["success"], true);
        assert_eq!(applied["result"]["operationId"], "operation-apply");
        assert_eq!(applied["result"]["status"], STATUS_AWAITING_CONFIRMATION);

        let confirmed = round_trip(
            envelope(
                "request-confirm",
                "network.confirmConfiguration",
                json!({"operationId": "operation-apply"}),
            ),
            &registry,
            &environment,
        );

        assert_eq!(confirmed["success"], true);
        assert_eq!(confirmed["result"]["status"], STATUS_CONFIRMED);
    }

    #[test]
    fn rejects_a_second_apply_on_the_same_interface() {
        let configuration_directory = test_support::temporary_directory("protocol-conflict-config");
        let sysfs_directory = test_support::temporary_directory("protocol-conflict-sysfs");
        test_support::fake_interface(&sysfs_directory, "enp1s0", MAC);
        let environment = test_support::environment(&configuration_directory, &sysfs_directory);
        let registry = PendingChangeRegistry::new_shared();

        let first = round_trip(
            envelope(
                "request-first",
                "network.applyConfiguration",
                apply_parameters("operation-first"),
            ),
            &registry,
            &environment,
        );
        assert_eq!(first["success"], true);

        let second = round_trip(
            envelope(
                "request-second",
                "network.applyConfiguration",
                apply_parameters("operation-second"),
            ),
            &registry,
            &environment,
        );

        assert_eq!(second["success"], false);
        assert_eq!(
            second["error"]["code"],
            crate::network_write::CODE_OPERATION_CONFLICT
        );
    }

    #[test]
    fn reports_operation_not_found_for_an_unknown_operation_identifier() {
        let (registry, environment) = read_only_context("protocol-missing-operation");

        for action in [
            "network.confirmConfiguration",
            "network.rollbackConfiguration",
        ] {
            let response = round_trip(
                envelope(
                    "request-missing",
                    action,
                    json!({"operationId": "operation-missing"}),
                ),
                &registry,
                &environment,
            );

            assert_eq!(response["success"], false);
            assert_eq!(
                response["error"]["code"],
                crate::network_write::CODE_OPERATION_NOT_FOUND
            );
        }
    }

    #[test]
    fn rolls_back_an_awaiting_change_on_request() {
        let configuration_directory = test_support::temporary_directory("protocol-rollback-config");
        let sysfs_directory = test_support::temporary_directory("protocol-rollback-sysfs");
        test_support::fake_interface(&sysfs_directory, "enp1s0", MAC);
        let environment = test_support::environment(&configuration_directory, &sysfs_directory);
        let registry = PendingChangeRegistry::new_shared();

        let applied = round_trip(
            envelope(
                "request-apply",
                "network.applyConfiguration",
                apply_parameters("operation-rollback"),
            ),
            &registry,
            &environment,
        );
        assert_eq!(applied["success"], true);

        let rolled_back = round_trip(
            envelope(
                "request-rollback",
                "network.rollbackConfiguration",
                json!({"operationId": "operation-rollback"}),
            ),
            &registry,
            &environment,
        );

        assert_eq!(rolled_back["success"], true);
        assert_eq!(rolled_back["result"]["status"], STATUS_ROLLED_BACK);
        assert!(registry.find("operation-rollback").is_none());
    }

    #[test]
    fn rejects_an_apply_request_with_an_unknown_field() {
        let (registry, environment) = read_only_context("protocol-unknown-field");

        let response = round_trip(
            envelope(
                "request-unknown-field",
                "network.applyConfiguration",
                json!({
                    "operationId": "operation-unknown-field",
                    "interfaceId": INTERFACE_ID,
                    "mode": "dhcp",
                    "confirmationDeadlineUnixMilliseconds": 1,
                    "dnsServers": ["1.1.1.1"]
                }),
            ),
            &registry,
            &environment,
        );

        assert_eq!(response["success"], false);
        assert_eq!(
            response["error"]["code"],
            crate::network_write::CODE_INVALID_CONFIGURATION
        );
    }

    #[test]
    fn rejects_an_unknown_mode_before_interface_lookup() {
        let (registry, environment) = read_only_context("protocol-unknown-mode");

        let response = round_trip(
            envelope(
                "request-unknown-mode",
                "network.applyConfiguration",
                json!({
                    "operationId": "operation-unknown-mode",
                    "interfaceId": INTERFACE_ID,
                    "mode": "automatic",
                    "confirmationDeadlineUnixMilliseconds":
                        current_unix_milliseconds() + 120_000
                }),
            ),
            &registry,
            &environment,
        );

        assert_eq!(response["success"], false);
        assert_eq!(
            response["error"]["code"],
            crate::network_write::CODE_INVALID_CONFIGURATION
        );
    }

    #[test]
    fn disables_new_applications_when_the_timeout_watcher_is_unavailable() {
        let configuration_directory = test_support::temporary_directory("protocol-disabled-config");
        let sysfs_directory = test_support::temporary_directory("protocol-disabled-sysfs");
        test_support::fake_interface(&sysfs_directory, "enp1s0", MAC);
        let mut environment = test_support::environment(&configuration_directory, &sysfs_directory);
        environment.disable_new_applications();
        let registry = PendingChangeRegistry::new_shared();

        let response = round_trip(
            envelope(
                "request-disabled",
                "network.applyConfiguration",
                apply_parameters("operation-disabled"),
            ),
            &registry,
            &environment,
        );

        assert_eq!(response["success"], false);
        assert_eq!(
            response["error"]["code"],
            crate::network_write::CODE_APPLY_FAILED
        );
        assert!(
            !configuration_directory
                .join("70-amseoknas-aabbccddeeff.network")
                .exists()
        );
    }

    #[test]
    fn keeps_the_record_when_an_explicit_rollback_fails() {
        let root = test_support::temporary_directory("protocol-failed-rollback");
        let configuration_directory = root.join("network");
        let sysfs_directory = root.join("sysfs");
        fs::write(&configuration_directory, "该路径故意是普通文件").unwrap();
        let environment = test_support::environment(&configuration_directory, &sysfs_directory);
        let registry = PendingChangeRegistry::new_shared();
        registry
            .register(PendingChange {
                operation_id: "operation-failed-rollback".to_owned(),
                interface_id: INTERFACE_ID.to_owned(),
                backup: crate::network_write::ManagedFileBackup::Absent,
                confirmation_deadline_unix_milliseconds: current_unix_milliseconds() + 120_000,
                status: STATUS_AWAITING_CONFIRMATION,
            })
            .unwrap();

        let response = round_trip(
            envelope(
                "request-failed-rollback",
                "network.rollbackConfiguration",
                json!({"operationId": "operation-failed-rollback"}),
            ),
            &registry,
            &environment,
        );

        assert_eq!(response["success"], false);
        assert_eq!(
            response["error"]["code"],
            crate::network_write::CODE_ROLLBACK_FAILED
        );
        assert!(registry.find("operation-failed-rollback").is_some());
    }
}
