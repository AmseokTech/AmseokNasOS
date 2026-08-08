//--------------------------//
//--------原子持久化 RAID 幂等结果与 fencing 状态---------//
//--------Atomically persists RAID idempotency results and fencing state--------//
//-------------------------//
use std::collections::BTreeMap;
use std::fs::{self, OpenOptions};
use std::io::{self, Write};
use std::os::unix::fs::OpenOptionsExt;
use std::path::PathBuf;
use std::sync::Mutex;

use serde::{Deserialize, Serialize};

use crate::raid_write::{
    CODE_RECONCILIATION_REQUIRED, RaidExecutionParameters, RaidExecutionResult, RaidWriteError,
};

#[derive(Debug, Default, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct RegistryState {
    highest_fencing_token: i64,
    operations: BTreeMap<String, RegistryEntry>,
}

#[derive(Clone, Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct RegistryEntry {
    idempotency_key: String,
    snapshot_fingerprint: String,
    state: RegistryEntryState,
    result: Option<RaidExecutionResult>,
}

#[derive(Clone, Debug, Deserialize, Serialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
enum RegistryEntryState {
    Executing,
    Completed,
}

pub(crate) struct RaidOperationRegistry {
    path: PathBuf,
    state: Mutex<RegistryState>,
}

impl RaidOperationRegistry {
    pub(crate) fn open(path: PathBuf) -> io::Result<Self> {
        if let Some(parent) = path.parent() {
            fs::create_dir_all(parent)?;
        }
        if path.exists() && fs::symlink_metadata(&path)?.file_type().is_symlink() {
            return Err(io::Error::new(
                io::ErrorKind::PermissionDenied,
                "RAID registry cannot be a symlink",
            ));
        }
        let state = if path.exists() {
            serde_json::from_slice(&fs::read(&path)?).map_err(io::Error::other)?
        } else {
            RegistryState::default()
        };
        let registry = Self {
            path,
            state: Mutex::new(state),
        };
        if !registry.path.exists() {
            let state = registry
                .state
                .lock()
                .map_err(|_| io::Error::other("registry lock poisoned"))?;
            registry.persist(&state)?;
        }
        Ok(registry)
    }

    pub(crate) fn replay(
        &self,
        parameters: &RaidExecutionParameters,
    ) -> Result<Option<RaidExecutionResult>, RaidWriteError> {
        let state = self
            .state
            .lock()
            .map_err(|_| RaidWriteError::unavailable("RAID 操作登记表锁失效"))?;
        let Some(entry) = state.operations.get(&parameters.operation_id) else {
            return Ok(None);
        };
        if entry.idempotency_key != parameters.idempotency_key
            || entry.snapshot_fingerprint != parameters.snapshot_fingerprint
        {
            return Err(RaidWriteError::new(
                "operation.conflict",
                "操作标识与原请求不一致",
                false,
            ));
        }
        match entry.state {
            RegistryEntryState::Completed => Ok(entry.result.clone()),
            RegistryEntryState::Executing => Err(RaidWriteError::new(
                CODE_RECONCILIATION_REQUIRED,
                "此前的 RAID 操作结果尚未复核",
                true,
            )),
        }
    }

    pub(crate) fn begin(&self, parameters: &RaidExecutionParameters) -> Result<(), RaidWriteError> {
        let mut state = self
            .state
            .lock()
            .map_err(|_| RaidWriteError::unavailable("RAID 操作登记表锁失效"))?;
        if parameters.fencing_token <= state.highest_fencing_token {
            return Err(RaidWriteError::new(
                "operation.stale_fencing_token",
                "fencing token 已失效",
                false,
            ));
        }
        let previous_fencing_token = state.highest_fencing_token;
        state.highest_fencing_token = parameters.fencing_token;
        state.operations.insert(
            parameters.operation_id.clone(),
            RegistryEntry {
                idempotency_key: parameters.idempotency_key.clone(),
                snapshot_fingerprint: parameters.snapshot_fingerprint.clone(),
                state: RegistryEntryState::Executing,
                result: None,
            },
        );
        if let Err(error) = self.persist(&state) {
            state.operations.remove(&parameters.operation_id);
            state.highest_fencing_token = previous_fencing_token;
            return Err(RaidWriteError::unavailable(error.to_string()));
        }
        Ok(())
    }

    pub(crate) fn complete(
        &self,
        parameters: &RaidExecutionParameters,
        result: &RaidExecutionResult,
    ) -> Result<(), RaidWriteError> {
        let mut state = self
            .state
            .lock()
            .map_err(|_| RaidWriteError::unavailable("RAID 操作登记表锁失效"))?;
        let entry = state
            .operations
            .get_mut(&parameters.operation_id)
            .ok_or_else(|| RaidWriteError::unavailable("RAID 操作登记记录丢失"))?;
        entry.state = RegistryEntryState::Completed;
        entry.result = Some(result.clone());
        self.persist(&state)
            .map_err(|error| RaidWriteError::unavailable(error.to_string()))
    }

    fn persist(&self, state: &RegistryState) -> io::Result<()> {
        let temporary = self.path.with_extension("tmp");
        let payload = serde_json::to_vec(state).map_err(io::Error::other)?;
        let mut file = OpenOptions::new()
            .create(true)
            .truncate(true)
            .write(true)
            .mode(0o600)
            .open(&temporary)?;
        file.write_all(&payload)?;
        file.sync_all()?;
        fs::rename(&temporary, &self.path)?;
        if let Some(parent) = self.path.parent() {
            OpenOptions::new().read(true).open(parent)?.sync_all()?;
        }
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn parameters(operation_id: &str, fencing_token: i64) -> RaidExecutionParameters {
        serde_json::from_value(serde_json::json!({
            "operationId": operation_id,
            "idempotencyKey": format!("key-{operation_id}"),
            "fencingToken": fencing_token,
            "arrayId": null,
            "arrayName": "data",
            "level": "raid1",
            "deviceIds": ["wwn:a", "wwn:b"],
            "sourceDeviceId": null,
            "targetDeviceCount": null,
            "expectedMemberDeviceIds": [],
            "snapshotFingerprint": "a".repeat(64)
        }))
        .unwrap()
    }

    fn temporary_registry(tag: &str) -> RaidOperationRegistry {
        let path = std::env::temp_dir().join(format!(
            "amseoknas-raid-{tag}-{}-{}.json",
            std::process::id(),
            current_test_nonce()
        ));
        RaidOperationRegistry::open(path).unwrap()
    }

    fn current_test_nonce() -> u128 {
        std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap()
            .as_nanos()
    }

    #[test]
    fn completed_operation_is_replayed_without_a_second_command() {
        let registry = temporary_registry("replay");
        let parameters = parameters("00000000-0000-0000-0000-000000000001", 1);
        let result = RaidExecutionResult {
            array_id: Some("md:test".to_owned()),
            in_progress: false,
            progress_percentage: Some(100),
        };

        registry.begin(&parameters).unwrap();
        registry.complete(&parameters, &result).unwrap();

        assert_eq!(
            registry.replay(&parameters).unwrap().unwrap().array_id,
            Some("md:test".to_owned())
        );
    }

    #[test]
    fn executing_duplicate_requires_reconciliation_and_old_fencing_is_rejected() {
        let registry = temporary_registry("fencing");
        let first = parameters("00000000-0000-0000-0000-000000000001", 2);
        registry.begin(&first).unwrap();

        assert_eq!(
            registry.replay(&first).unwrap_err().code,
            CODE_RECONCILIATION_REQUIRED
        );
        let stale = parameters("00000000-0000-0000-0000-000000000002", 1);
        assert_eq!(
            registry.begin(&stale).unwrap_err().code,
            "operation.stale_fencing_token"
        );
    }
}
