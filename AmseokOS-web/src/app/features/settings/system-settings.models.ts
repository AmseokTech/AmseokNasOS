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
