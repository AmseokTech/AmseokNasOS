//--------------------------//
//--------公开存储管理视图入口---------//
//--------Exposes the storage management view entry point--------//
//-------------------------//

export { DiskManagementComponent } from './disk-management.component';
export { StorageInventoryService } from './storage-inventory.service';
export type {
  BlockDevice,
  DiskSmartInformation,
  DiskSmartStatus,
  RaidArray,
  StorageInventory
} from './storage-inventory.models';
export { StorageManagementService } from './storage-management.service';
export type { ManagedVolume } from './storage-management.models';
