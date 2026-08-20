//--------------------------//
//--------定义系统设置只读视图的前端契约---------//
//--------Defines read-only frontend contracts for system settings--------//
//-------------------------//

export interface SystemAbout {
  readonly hostName: string;
  readonly operatingSystem: string;
  readonly kernelVersion: string;
  readonly uptimeSeconds: number;
  readonly cpu: CpuInformation;
  readonly memory: MemoryInformation;
  readonly systemStorage: SystemStorageInformation;
}

export interface CpuInformation {
  readonly model: string;
  readonly physicalCoreCount: number;
  readonly logicalProcessorCount: number;
  readonly currentFrequencyMhz: number | null;
  readonly maximumFrequencyMhz: number | null;
}

export interface MemoryInformation {
  readonly totalBytes: number;
}

export interface SystemPerformanceSnapshot {
  readonly capturedAtUnixMilliseconds: number;
  readonly cpu: CpuPerformanceInformation;
  readonly memory: MemoryPerformanceInformation;
  readonly disks: readonly DiskPerformanceInformation[];
  readonly networks: readonly NetworkPerformanceInformation[];
  readonly gpus: readonly GpuPerformanceInformation[];
}

export interface CpuPerformanceInformation {
  readonly model: string;
  readonly physicalCoreCount: number;
  readonly logicalProcessorCount: number;
  readonly currentFrequencyMhz: number | null;
  readonly maximumFrequencyMhz: number | null;
  readonly l1CacheBytes: number | null;
  readonly l2CacheBytes: number | null;
  readonly l3CacheBytes: number | null;
  readonly aggregate: CpuTimeCounterInformation;
  readonly logicalProcessors: readonly CpuTimeCounterInformation[];
}

export interface CpuTimeCounterInformation {
  readonly id: string;
  readonly totalTicks: number;
  readonly idleTicks: number;
}

export interface MemoryPerformanceInformation {
  readonly totalBytes: number;
  readonly usedBytes: number;
  readonly availableBytes: number;
  readonly cachedBytes: number;
  readonly swapTotalBytes: number;
  readonly swapUsedBytes: number;
}

export interface DiskPerformanceInformation {
  readonly id: string;
  readonly name: string;
  readonly model: string | null;
  readonly totalBytes: number;
  readonly readBytes: number;
  readonly writtenBytes: number;
  readonly busyMilliseconds: number;
}

export interface NetworkPerformanceInformation {
  readonly id: string;
  readonly name: string;
  readonly model: string | null;
  readonly speedMbps: number | null;
  readonly receivedBytes: number;
  readonly transmittedBytes: number;
}

export interface GpuPerformanceInformation {
  readonly id: string;
  readonly name: string;
  readonly driver: string | null;
  readonly memoryTotalBytes: number | null;
  readonly memoryUsedBytes: number | null;
  readonly coreUtilizationPercent: number | null;
  readonly twoDUtilizationPercent: number | null;
  readonly threeDUtilizationPercent: number | null;
  readonly currentFrequencyMhz: number | null;
  readonly maximumFrequencyMhz: number | null;
}

export interface SystemStorageInformation {
  readonly source: string;
  readonly stableId: string | null;
  readonly model: string | null;
  readonly totalBytes: number;
  readonly usedBytes: number;
  readonly availableBytes: number;
}

export type NetworkConfigurationMode = 'dhcp' | 'static' | 'unknown' | 'unconfigured';

export interface NetworkInterfaceInformation {
  readonly id: string;
  readonly name: string;
  readonly model: string | null;
  readonly driver: string | null;
  readonly macAddress: string;
  readonly linkState: string;
  readonly speedMbps: number | null;
  readonly duplex: string | null;
  readonly mtu: number;
  readonly configurationMode: NetworkConfigurationMode;
  readonly addresses: readonly string[];
  readonly gateway: string | null;
  readonly dnsServers: readonly string[];
}
