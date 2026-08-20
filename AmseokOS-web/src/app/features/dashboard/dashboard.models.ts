//--------------------------//
//--------定义概览聚合状态而不复制底层领域模型---------//
//--------Defines dashboard aggregate state without duplicating domain models--------//
//-------------------------//

import type { HealthStatus } from '../../shared/models/health-status';
import type {
  NetworkInterfaceInformation,
  SystemAbout,
  SystemPerformanceSnapshot
} from '../settings';
import type {
  ManagedVolume,
  StorageInventory
} from '../storage';

export interface DashboardSection<T> {
  readonly value: T | null;
  readonly error: string | null;
}

export interface SmartSummary {
  readonly queried: number;
  readonly healthy: number;
  readonly warning: number;
  readonly failing: number;
  readonly unsupported: number;
  readonly unknown: number;
}

export interface DashboardSnapshot {
  readonly health: DashboardSection<HealthStatus>;
  readonly about: DashboardSection<SystemAbout>;
  readonly network: DashboardSection<readonly NetworkInterfaceInformation[]>;
  readonly storage: DashboardSection<StorageInventory>;
  readonly volumes: DashboardSection<readonly ManagedVolume[]>;
  readonly smart: DashboardSection<SmartSummary>;
}

export interface DashboardPerformanceSample {
  readonly capturedAtUnixMilliseconds: number;
  readonly cpu: DashboardCpuPerformance;
  readonly memory: DashboardMemoryPerformance;
  readonly disks: readonly DashboardDiskPerformance[];
  readonly networks: readonly DashboardNetworkPerformance[];
  readonly gpus: SystemPerformanceSnapshot['gpus'];
}

export type DashboardCpuPerformance = Omit<
  SystemPerformanceSnapshot['cpu'],
  'aggregate' | 'logicalProcessors'
> & {
  readonly utilizationPercent: number | null;
  readonly logicalProcessors: readonly DashboardCpuCorePerformance[];
};

export interface DashboardCpuCorePerformance {
  readonly id: string;
  readonly utilizationPercent: number | null;
}

export type DashboardMemoryPerformance = SystemPerformanceSnapshot['memory'] & {
  readonly utilizationPercent: number;
};

export type DashboardDiskPerformance = SystemPerformanceSnapshot['disks'][number] & {
  readonly readBytesPerSecond: number | null;
  readonly writtenBytesPerSecond: number | null;
  readonly activePercent: number | null;
};

export type DashboardNetworkPerformance = SystemPerformanceSnapshot['networks'][number] & {
  readonly receivedBytesPerSecond: number | null;
  readonly transmittedBytesPerSecond: number | null;
};
