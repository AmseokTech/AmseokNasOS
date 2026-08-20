import { TestBed } from '@angular/core/testing';
import { firstValueFrom, of, throwError } from 'rxjs';

import { ApiHealthService } from '../../core/services/api-health.service';
import { SystemSettingsService } from '../settings';
import type { SystemPerformanceSnapshot } from '../settings';
import { StorageInventoryService, StorageManagementService } from '../storage';
import { DashboardService } from './dashboard.service';

const firstPerformance: SystemPerformanceSnapshot = {
  capturedAtUnixMilliseconds: 1_000,
  cpu: {
    model: 'Test CPU', physicalCoreCount: 1, logicalProcessorCount: 2,
    currentFrequencyMhz: 2200, maximumFrequencyMhz: 3000,
    l1CacheBytes: 64, l2CacheBytes: 1024, l3CacheBytes: 4096,
    aggregate: { id: 'cpu', totalTicks: 100, idleTicks: 40 },
    logicalProcessors: [
      { id: 'cpu0', totalTicks: 50, idleTicks: 20 },
      { id: 'cpu1', totalTicks: 50, idleTicks: 20 }
    ]
  },
  memory: {
    totalBytes: 1000, usedBytes: 400, availableBytes: 600, cachedBytes: 100,
    swapTotalBytes: 200, swapUsedBytes: 50
  },
  disks: [{
    id: 'disk0', name: 'sda', model: 'Disk', totalBytes: 10_000,
    readBytes: 1000, writtenBytes: 2000, busyMilliseconds: 100
  }],
  networks: [{
    id: 'net0', name: 'eth0', model: 'Ethernet', speedMbps: 1000,
    receivedBytes: 5000, transmittedBytes: 6000
  }],
  gpus: []
};

describe('DashboardService', () => {
  const health = { getHealth: vi.fn(() => of({ status: 'Healthy' })) };
  const settings = {
    getAbout: vi.fn(() => of({
      hostName: 'nas', operatingSystem: 'Debian', kernelVersion: '6.12', uptimeSeconds: 60,
      cpu: { model: 'CPU', physicalCoreCount: 2, logicalProcessorCount: 4, currentFrequencyMhz: null, maximumFrequencyMhz: null },
      memory: { totalBytes: 1024 },
      systemStorage: { source: '/dev/sda1', stableId: null, model: null, totalBytes: 100, usedBytes: 25, availableBytes: 75 }
    })),
    getNetworkInterfaces: vi.fn(() => of([])),
    getPerformance: vi.fn(() => of(firstPerformance))
  };
  const inventory = {
    getInventory: vi.fn(() => of({
      disks: [
        { id: 'stable', stable: true, identityConflict: false },
        { id: 'unstable', stable: false, identityConflict: false },
        { id: 'conflict', stable: true, identityConflict: true }
      ],
      arrays: []
    })),
    getSmart: vi.fn(() => of({ status: 'unsupported' }))
  };
  const volumes = { getVolumes: vi.fn(() => of([])) };

  beforeEach(() => {
    vi.clearAllMocks();
    TestBed.configureTestingModule({
      providers: [
        DashboardService,
        { provide: ApiHealthService, useValue: health },
        { provide: SystemSettingsService, useValue: settings },
        { provide: StorageInventoryService, useValue: inventory },
        { provide: StorageManagementService, useValue: volumes }
      ]
    });
  });

  it('aggregates sections and only queries SMART for stable unique identities', async () => {
    const snapshot = await firstValueFrom(TestBed.inject(DashboardService).load());

    expect(snapshot.health.value?.status).toBe('Healthy');
    expect(snapshot.smart.value).toEqual({
      queried: 1, healthy: 0, warning: 0, failing: 0, unsupported: 1, unknown: 0
    });
    expect(inventory.getSmart).toHaveBeenCalledOnce();
    expect(inventory.getSmart).toHaveBeenCalledWith('stable');
  });

  it('keeps other overview sections available when one endpoint is disconnected', async () => {
    settings.getNetworkInterfaces.mockReturnValueOnce(
      throwError(() => new Error('网络查询暂不可用'))
    );

    const snapshot = await firstValueFrom(TestBed.inject(DashboardService).load());

    expect(snapshot.network.value).toBeNull();
    expect(snapshot.network.error).toBe('网络查询暂不可用');
    expect(snapshot.storage.value).not.toBeNull();
  });

  it('derives utilization and transfer rates from consecutive kernel counters', async () => {
    const service = TestBed.inject(DashboardService);
    const first = await firstValueFrom(service.samplePerformance());
    settings.getPerformance.mockReturnValueOnce(of({
      ...firstPerformance,
      capturedAtUnixMilliseconds: 2_000,
      cpu: {
        ...firstPerformance.cpu,
        aggregate: { id: 'cpu', totalTicks: 200, idleTicks: 70 },
        logicalProcessors: [
          { id: 'cpu0', totalTicks: 100, idleTicks: 30 },
          { id: 'cpu1', totalTicks: 100, idleTicks: 40 }
        ]
      },
      disks: [{
        ...firstPerformance.disks[0], readBytes: 3048, writtenBytes: 6096,
        busyMilliseconds: 350
      }],
      networks: [{
        ...firstPerformance.networks[0], receivedBytes: 8072, transmittedBytes: 7024
      }]
    }));

    const second = await firstValueFrom(service.samplePerformance());

    expect(first.cpu.utilizationPercent).toBeNull();
    expect(second.cpu.utilizationPercent).toBe(70);
    expect(second.cpu.logicalProcessors.map(({ utilizationPercent }) =>
      utilizationPercent
    )).toEqual([80, 60]);
    expect(second.memory.utilizationPercent).toBe(40);
    expect(second.disks[0]).toMatchObject({
      readBytesPerSecond: 2048,
      writtenBytesPerSecond: 4096,
      activePercent: 25
    });
    expect(second.networks[0]).toMatchObject({
      receivedBytesPerSecond: 3072,
      transmittedBytesPerSecond: 1024
    });
  });
});
