//--------------------------//
//--------定义磁盘与 RAID 只读清单契约---------//
//--------Defines read-only disk and RAID inventory contracts--------//
//-------------------------//

export interface StorageInventory {
  readonly disks: readonly BlockDevice[];
  readonly arrays: readonly RaidArray[];
}

export interface BlockDevice {
  readonly id: string;
  readonly stable: boolean;
  readonly identityConflict: boolean;
  readonly topologyComplete: boolean;
  readonly name: string;
  readonly path: string;
  readonly model: string | null;
  readonly serialNumber: string | null;
  readonly wwn: string | null;
  readonly sizeBytes: number;
  readonly logicalSectorBytes: number;
  readonly physicalSectorBytes: number;
  readonly rotational: boolean;
  readonly removable: boolean;
  readonly readOnly: boolean;
  readonly partitions: readonly BlockPartition[];
  readonly mountPoints: readonly string[];
  readonly systemDevice: boolean;
  readonly swap: boolean;
  readonly raidMember: boolean;
  readonly inUse: boolean;
  readonly dependentDevices: readonly BlockDependency[];
}

export interface BlockPartition {
  readonly name: string;
  readonly path: string;
  readonly sizeBytes: number;
  readonly mountPoints: readonly string[];
  readonly topologyComplete: boolean;
  readonly systemDevice: boolean;
  readonly swap: boolean;
  readonly raidMember: boolean;
  readonly inUse: boolean;
  readonly dependentDevices: readonly BlockDependency[];
}

export interface BlockDependency {
  readonly name: string;
  readonly path: string;
  readonly kind: string;
  readonly mountPoints: readonly string[];
  readonly swap: boolean;
}

export type DiskSmartStatus = 'healthy' | 'warning' | 'failing' | 'unsupported' | 'unknown';

export interface DiskSmartInformation {
  readonly deviceId: string;
  readonly supported: boolean;
  readonly enabled: boolean;
  readonly status: DiskSmartStatus;
  readonly passed: boolean | null;
  readonly temperatureCelsius: number | null;
  readonly powerOnHours: number | null;
  readonly powerCycleCount: number | null;
  readonly reallocatedSectorCount: number | null;
  readonly pendingSectorCount: number | null;
  readonly offlineUncorrectableSectorCount: number | null;
  readonly mediaErrorCount: number | null;
  readonly percentageUsed: number | null;
  readonly criticalWarning: number | null;
}

export interface RaidArray {
  readonly id: string;
  readonly name: string;
  readonly path: string;
  readonly uuid: string | null;
  readonly level: string;
  readonly state: string;
  readonly metadataVersion: string | null;
  readonly sizeBytes: number;
  readonly configuredDeviceCount: number;
  readonly degradedDeviceCount: number;
  readonly syncAction: string;
  readonly syncCompletedSectors: number | null;
  readonly syncTotalSectors: number | null;
  readonly members: readonly RaidMember[];
}

export interface RaidMember {
  readonly name: string;
  readonly path: string;
  readonly state: string;
  readonly slot: number | null;
}
