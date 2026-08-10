import { TestBed } from '@angular/core/testing';
import { firstValueFrom, of, throwError } from 'rxjs';

import { ApiHealthService } from '../../core/services/api-health.service';
import { SystemSettingsService } from '../settings';
import { StorageInventoryService, StorageManagementService } from '../storage';
import { DashboardService } from './dashboard.service';

describe('DashboardService', () => {
  const health = { getHealth: vi.fn(() => of({ status: 'Healthy' })) };
  const settings = {
    getAbout: vi.fn(() => of({
      hostName: 'nas', operatingSystem: 'Debian', kernelVersion: '6.12', uptimeSeconds: 60,
      cpu: { model: 'CPU', physicalCoreCount: 2, logicalProcessorCount: 4, currentFrequencyMhz: null, maximumFrequencyMhz: null },
      memory: { totalBytes: 1024 },
      systemStorage: { source: '/dev/sda1', stableId: null, model: null, totalBytes: 100, usedBytes: 25, availableBytes: 75 }
    })),
    getNetworkInterfaces: vi.fn(() => of([]))
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
});
