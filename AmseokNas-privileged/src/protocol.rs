//--------------------------//
//--------定义有界版本化的特权查询协议---------//
//--------Defines the bounded versioned privileged-query protocol--------//
//-------------------------//
use std::io::{self, Read, Write};
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

use serde::{Deserialize, Serialize};
use serde_json::Value;

use crate::inventory;

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

pub fn handle_connection(stream: &mut (impl Read + Write)) -> io::Result<()> {
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

    let response = process_request(request, started);
    write_response(stream, response)
}

fn process_request(request: RequestEnvelope, started: Instant) -> ResponseEnvelope {
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
    if serde_json::from_value::<EmptyParameters>(request.parameters).is_err() {
        return ResponseEnvelope::failure(
            request.request_id,
            "request.invalid",
            "该查询不接受参数",
            false,
            started,
        );
    }

    let result = match request.action.as_str() {
        "system.getAbout" => inventory::system::get_about().and_then(to_json_value),
        "network.inspectInterfaces" => {
            inventory::network::inspect_interfaces().and_then(to_json_value)
        }
        _ => {
            return ResponseEnvelope::failure(
                request.request_id,
                "request.unknown_action",
                "请求动作未登记",
                false,
                started,
            );
        }
    };

    match result {
        Ok(result) => ResponseEnvelope::success(request.request_id, result, started),
        Err(error) => ResponseEnvelope::failure(
            request.request_id,
            "inventory.read_failed",
            error.to_string(),
            true,
            started,
        ),
    }
}

fn to_json_value<T: Serialize>(value: T) -> io::Result<Value> {
    serde_json::to_value(value).map_err(io::Error::other)
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
    use std::io::Cursor;

    use serde_json::json;

    use super::*;

    #[test]
    fn rejects_unknown_actions_without_executing_a_command() {
        let request = json!({
            "protocolVersion": 1,
            "requestId": "test-request",
            "action": "system.runCommand",
            "deadlineUnixMilliseconds": current_unix_milliseconds() + 5_000,
            "parameters": {}
        });
        let mut input = Vec::new();
        let payload = serde_json::to_vec(&request).unwrap();
        input.extend_from_slice(&(payload.len() as u32).to_be_bytes());
        input.extend_from_slice(&payload);
        let mut stream = Cursor::new(input);

        handle_connection(&mut stream).unwrap();

        let bytes = stream.into_inner();
        let response_offset = 4 + payload.len();
        let response_length = u32::from_be_bytes(
            bytes[response_offset..response_offset + 4]
                .try_into()
                .unwrap(),
        ) as usize;
        let response: Value = serde_json::from_slice(
            &bytes[response_offset + 4..response_offset + 4 + response_length],
        )
        .unwrap();
        assert_eq!(response["success"], false);
        assert_eq!(response["error"]["code"], "request.unknown_action");
    }

    #[test]
    fn rejects_oversized_frames_before_allocation() {
        let mut stream = Cursor::new(((MAXIMUM_FRAME_BYTES + 1) as u32).to_be_bytes());

        let error = handle_connection(&mut stream).unwrap_err();

        assert_eq!(error.kind(), io::ErrorKind::InvalidData);
    }
}
