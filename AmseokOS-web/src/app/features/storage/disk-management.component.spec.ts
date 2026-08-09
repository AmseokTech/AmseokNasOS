import { TestBed } from '@angular/core/testing';

import { LanguageService } from '../../core/i18n';
import { DiskManagementComponent } from './disk-management.component';
import { RaidOperation, RaidOperationPreview } from './raid-management.models';
import { RaidManagementService } from './raid-management.service';
import { BlockDevice, BlockPartition, RaidArray } from './storage-inventory.models';
import { StorageInventoryService } from './storage-inventory.service';

const partition: BlockPartition = {
  name: 'sdb1',
  path: '/dev/sdb1',
  sizeBytes: 1024,
  mountPoints: [],
  topologyComplete: true,
  systemDevice: false,
  swap: false,
  raidMember: false,
  inUse: false,
  dependentDevices: []
};

function disk(overrides: Partial<BlockDevice> = {}): BlockDevice {
  return {
    id: 'wwn:eligible',
    stable: true,
    identityConflict: false,
    topologyComplete: true,
    name: 'sdb',
    path: '/dev/sdb',
    model: 'Test Disk',
    serialNumber: 'SERIAL',
    wwn: 'eligible',
    sizeBytes: 1024 ** 4,
    logicalSectorBytes: 512,
    physicalSectorBytes: 4096,
    rotational: false,
    removable: false,
    readOnly: false,
    partitions: [],
    mountPoints: [],
    systemDevice: false,
    swap: false,
    raidMember: false,
    inUse: false,
    dependentDevices: [],
    ...overrides
  };
}

function array(overrides: Partial<RaidArray> = {}): RaidArray {
  return {
    id: 'md:test',
    name: 'md0',
    path: '/dev/md0',
    uuid: 'test',
    level: 'raid1',
    state: 'clean',
    metadataVersion: '1.2',
    sizeBytes: 1024,
    configuredDeviceCount: 2,
    degradedDeviceCount: 0,
    syncAction: 'idle',
    syncCompletedSectors: null,
    syncTotalSectors: null,
    members: [],
    ...overrides
  };
}

function operation(overrides: Partial<RaidOperation> = {}): RaidOperation {
  return {
    operationId: 'operation-id',
    action: 'grow',
    status: 'succeeded',
    resourceId: 'array:md:test',
    arrayId: 'md:test',
    errorCode: null,
    retryable: false,
    progressPercentage: 100,
    createdAt: '2026-08-09T00:00:00Z',
    updatedAt: '2026-08-09T00:00:01Z',
    ...overrides
  };
}

describe('DiskManagementComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DiskManagementComponent],
      providers: [
        { provide: StorageInventoryService, useValue: {} },
        { provide: RaidManagementService, useValue: {} }
      ]
    }).compileComponents();
  });

  it('only offers stable, unused raw disks for destructive RAID operations', () => {
    const component = TestBed.createComponent(DiskManagementComponent).componentInstance;
    const candidates = [
      disk(),
      disk({ id: 'unstable', stable: false }),
      disk({ id: 'conflict', identityConflict: true }),
      disk({ id: 'incomplete', topologyComplete: false }),
      disk({ id: 'system', systemDevice: true }),
      disk({ id: 'swap', swap: true }),
      disk({ id: 'raid-member', raidMember: true }),
      disk({ id: 'in-use', inUse: true }),
      disk({ id: 'read-only', readOnly: true }),
      disk({ id: 'removable', removable: true }),
      disk({ id: 'partitioned', partitions: [partition] }),
      disk({ id: 'mounted', mountPoints: ['/mnt/data'] }),
      disk({
        id: 'dependent',
        dependentDevices: [{ name: 'dm-0', path: '/dev/dm-0', kind: 'crypt', mountPoints: [], swap: false }]
      })
    ];

    component.inventory.set({ disks: candidates, arrays: [] });

    expect(component.eligibleDisks().map((candidate) => candidate.id)).toEqual(['wwn:eligible']);
  });

  it('derives array state, progress, supported actions, and operation status labels', () => {
    const component = TestBed.createComponent(DiskManagementComponent).componentInstance;

    expect(component.formatBytes(Number.NaN)).toBe('0 B');
    expect(component.formatBytes(0)).toBe('0 B');
    expect(component.formatBytes(1024)).toContain('KB');
    expect(component.arrayStateLabel(array({ degradedDeviceCount: 1 }))).toContain('已降级');
    expect(component.arrayStateLabel(array({ state: 'active' }))).toBe('正常');
    expect(component.arrayStateLabel(array({ state: '' }))).toBe('未知');
    expect(component.degradedArrayCount([array(), array({ degradedDeviceCount: 2 })])).toBe(1);
    expect(component.syncPercentage(array())).toBeNull();
    expect(component.syncPercentage(array({ syncCompletedSectors: 1, syncTotalSectors: 0 }))).toBeNull();
    expect(component.syncPercentage(array({ syncCompletedSectors: 120, syncTotalSectors: 100 }))).toBe(100);
    expect(component.syncPercentage(array({ syncCompletedSectors: -1, syncTotalSectors: 100 }))).toBe(0);

    expect(component.actionsFor(array({ level: 'raid0' }))).toEqual(['grow', 'shrink', 'delete']);
    expect(component.actionsFor(array({ level: 'raid10' }))).toEqual([
      'addDevice',
      'removeDevice',
      'replaceDevice',
      'delete'
    ]);
    expect(component.usesNewDevices()).toBe(true);
    component.action.set('removeDevice');
    expect(component.usesNewDevices()).toBe(false);
    expect(component.usesSourceDevice()).toBe(true);
    expect(component.usesTargetCount()).toBe(false);
    component.action.set('shrink');
    expect(component.usesTargetCount()).toBe(true);
    expect(component.warningLabel('raid.reshape_backup_required')).toContain('不是用户数据备份');
    expect(component.warningLabel('custom-warning')).toBe('custom-warning');

    expect(component.operationLabel(operation())).toBe('操作已完成');
    expect(component.operationLabel(operation({ status: 'running', progressPercentage: null })))
      .toBe('操作正在后台执行');
    expect(component.operationLabel(operation({ status: 'running', progressPercentage: 25 })))
      .toContain('25%');
    expect(component.operationLabel(operation({ status: 'interrupted' }))).toContain('复核结果');
    expect(component.operationLabel(operation({ status: 'failed', errorCode: 'raid.failed' })))
      .toContain('raid.failed');
    expect(component.operationLabel(operation({ status: 'failed' }))).toBe('操作失败');
  });

  it('updates storage status and action labels immediately in English', () => {
    const component = TestBed.createComponent(DiskManagementComponent).componentInstance;

    TestBed.inject(LanguageService).setLanguage('en-US');

    expect(component.arrayStateLabel(array({ degradedDeviceCount: 1 }))).toContain('Degraded');
    expect(component.arrayStateLabel(array({ state: 'active' }))).toBe('Healthy');
    expect(component.actionLabel('replaceDevice')).toBe('Replace member');
    expect(component.operationLabel(operation({ status: 'running', progressPercentage: 42 })))
      .toContain('42%');
  });

  it('maps array members to stable disk identities and resets stale previews when inputs change', () => {
    const component = TestBed.createComponent(DiskManagementComponent).componentInstance;
    const directDisk = disk({ id: 'wwn:direct', path: '/dev/sdc', name: 'sdc', model: null });
    const partitionedDisk = disk({ id: 'wwn:partition', path: '/dev/sdd', name: 'sdd', partitions: [partition] });
    component.inventory.set({ disks: [directDisk, partitionedDisk], arrays: [] });
    const selectedArray = array({
      members: [
        { name: 'sdc', path: '/dev/sdc', state: 'in_sync', slot: 0 },
        { name: 'sdc-copy', path: '/dev/sdc', state: 'in_sync', slot: 1 },
        { name: 'sdb1', path: '/dev/sdb1', state: 'in_sync', slot: 2 },
        { name: 'missing', path: '/dev/missing', state: 'removed', slot: null }
      ]
    });

    expect(component.memberDevices(null)).toEqual([]);
    expect(component.memberDevices(selectedArray)).toEqual([
      { id: 'wwn:direct', label: 'sdc · /dev/sdc' },
      { id: 'wwn:partition', label: 'Test Disk · /dev/sdb1' }
    ]);

    component.openModify(selectedArray);
    expect(component.dialogOpen()).toBe(true);
    expect(component.action()).toBe('grow');
    component.selectAction('not-an-action');
    expect(component.action()).toBe('grow');
    const preview: RaidOperationPreview = {
      action: 'grow',
      arrayId: selectedArray.id,
      arrayName: null,
      level: null,
      deviceIds: [],
      sourceDeviceId: null,
      targetDeviceCount: 3,
      arrayDisplayName: selectedArray.name,
      expectedMemberDeviceIds: ['wwn:direct', 'wwn:partition'],
      canExecute: true,
      previewToken: 'preview-token',
      expiresAt: '2026-08-09T00:05:00Z',
      confirmationPhrase: '扩容 md0',
      blockingIssues: [],
      warnings: []
    };
    component.preview.set(preview);
    component.confirmationInput.set('确认');
    component.toggleDevice('wwn:direct', true);
    component.toggleDevice('wwn:partition', true);
    component.toggleDevice('wwn:direct', false);
    expect(component.selectedDeviceIds()).toEqual(['wwn:partition']);
    expect(component.preview()).toBeNull();
    expect(component.confirmationInput()).toBe('');
  });
});
