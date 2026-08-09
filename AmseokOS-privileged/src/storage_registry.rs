//--------------------------//
//--------原子持久化数据卷操作的幂等结果与 fencing 状态---------//
//--------Atomically persists volume-operation idempotency and fencing state--------//
//-------------------------//
use std::collections::BTreeMap;
use std::fs::{self, OpenOptions};
use std::io::{self, Write};
use std::os::unix::fs::OpenOptionsExt;
use std::path::PathBuf;
use std::sync::Mutex;

use serde::{Deserialize, Serialize};

use crate::storage_write::{
    ManagedVolumeInformation, StorageExecutionParameters, StorageWriteError,
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
    executing: bool,
    result: Option<ManagedVolumeInformation>,
}

pub(crate) struct StorageOperationRegistry {
    path: PathBuf,
    state: Mutex<RegistryState>,
}

impl StorageOperationRegistry {
    pub(crate) fn open(path: PathBuf) -> io::Result<Self> {
        if let Some(parent) = path.parent() {
            fs::create_dir_all(parent)?;
        }
        if path.exists() && fs::symlink_metadata(&path)?.file_type().is_symlink() {
            return Err(io::Error::new(
                io::ErrorKind::PermissionDenied,
                "storage registry cannot be a symlink",
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
        parameters: &StorageExecutionParameters,
    ) -> Result<Option<ManagedVolumeInformation>, StorageWriteError> {
        let state = self
            .state
            .lock()
            .map_err(|_| StorageWriteError::unavailable("数据卷操作登记表锁失效"))?;
        let Some(entry) = state.operations.get(&parameters.operation_id) else {
            return Ok(None);
        };
        if entry.idempotency_key != parameters.idempotency_key
            || entry.snapshot_fingerprint != parameters.snapshot_fingerprint
        {
            return Err(StorageWriteError::new(
                "operation.conflict",
                "操作标识与原请求不一致",
                false,
            ));
        }
        if entry.executing {
            return Err(StorageWriteError::new(
                "operation.duplicate_requires_reconciliation",
                "此前的数据卷操作结果尚未复核",
                true,
            ));
        }
        Ok(entry.result.clone())
    }

    pub(crate) fn begin(
        &self,
        parameters: &StorageExecutionParameters,
    ) -> Result<(), StorageWriteError> {
        let mut state = self
            .state
            .lock()
            .map_err(|_| StorageWriteError::unavailable("数据卷操作登记表锁失效"))?;
        if parameters.fencing_token <= state.highest_fencing_token {
            return Err(StorageWriteError::new(
                "operation.stale_fencing_token",
                "fencing token 已失效",
                false,
            ));
        }
        let previous = state.highest_fencing_token;
        state.highest_fencing_token = parameters.fencing_token;
        state.operations.insert(
            parameters.operation_id.clone(),
            RegistryEntry {
                idempotency_key: parameters.idempotency_key.clone(),
                snapshot_fingerprint: parameters.snapshot_fingerprint.clone(),
                executing: true,
                result: None,
            },
        );
        if let Err(error) = self.persist(&state) {
            state.operations.remove(&parameters.operation_id);
            state.highest_fencing_token = previous;
            return Err(StorageWriteError::unavailable(error.to_string()));
        }
        Ok(())
    }

    pub(crate) fn complete(
        &self,
        parameters: &StorageExecutionParameters,
        result: &ManagedVolumeInformation,
    ) -> Result<(), StorageWriteError> {
        let mut state = self
            .state
            .lock()
            .map_err(|_| StorageWriteError::unavailable("数据卷操作登记表锁失效"))?;
        let entry = state
            .operations
            .get_mut(&parameters.operation_id)
            .ok_or_else(|| StorageWriteError::unavailable("数据卷操作登记记录丢失"))?;
        entry.executing = false;
        entry.result = Some(result.clone());
        self.persist(&state)
            .map_err(|error| StorageWriteError::unavailable(error.to_string()))
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
