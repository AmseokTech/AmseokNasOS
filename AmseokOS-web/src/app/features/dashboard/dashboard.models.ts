//--------------------------//
//--------定义概览聚合状态而不复制底层领域模型---------//
//--------Defines dashboard aggregate state without duplicating domain models--------//
//-------------------------//

import type { HealthStatus } from '../../shared/models/health-status';
import type {
  NetworkInterfaceInformation,
  SystemAbout
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
