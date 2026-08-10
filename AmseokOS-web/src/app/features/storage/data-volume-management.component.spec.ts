import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';

import { DataVolumeManagementComponent } from './data-volume-management.component';
import { ManagedVolume, StorageOperationRequest } from './storage-management.models';
import { StorageManagementService } from './storage-management.service';
import { RaidArray } from './storage-inventory.models';

function array(overrides: Partial<RaidArray> = {}): RaidArray {
  return {
    id: 'md:test', name: 'md0', path: '/dev/md0', uuid: 'array-uuid', level: 'raid1',
    state: 'clean', metadataVersion: '1.2', sizeBytes: 1024, configuredDeviceCount: 2,
    degradedDeviceCount: 0, syncAction: 'idle', syncCompletedSectors: null,
    syncTotalSectors: null, members: [], ...overrides
  };
}

function volume(overrides: Partial<ManagedVolume> = {}): ManagedVolume {
  return {
    id: 'volume:data', name: 'data', arrayId: 'md:test', arrayPath: '/dev/md0',
    fileSystemUuid: 'filesystem-uuid', fileSystemType: 'ext4',
    mountPath: '/srv/amseoknas/volumes/data', mounted: true, persistentMountEnabled: true,
    ownerName: 'root', groupName: 'amseoknas-data', directoryMode: '0770',
    readWriteVerified: true,
    smb: { enabled: false, shareName: null, readOnly: false, guestAccess: false, allowedNetwork: null },
    nfs: { enabled: false, clientNetwork: null, readOnly: false },
    ...overrides
  };
}

describe('DataVolumeManagementComponent', () => {
  let previewRequest: StorageOperationRequest | null;
  const storage = {
    getVolumes: () => of([]),
    preview: (request: StorageOperationRequest) => {
      previewRequest = request;
      return of({
        action: request.action, requested: { kind: 0 }, existingVolume: null, canExecute: true,
        previewToken: 'token', expiresAt: null, confirmationPhrase: '确认', blockingIssues: [], warnings: []
      });
    },
    execute: () => of({
      operationId: 'operation', action: 'verifyReadWrite', status: 'succeeded',
      resourceId: 'volume:data', volume: volume(), errorCode: null, retryable: false,
      createdAt: '2026-08-09T00:00:00Z', updatedAt: '2026-08-09T00:00:01Z'
    }),
    getOperation: () => of({})
  };

  beforeEach(async () => {
    previewRequest = null;
    await TestBed.configureTestingModule({
      imports: [DataVolumeManagementComponent],
      providers: [{ provide: StorageManagementService, useValue: storage }]
    }).compileComponents();
  });

  it('only offers healthy idle arrays that do not already back a managed volume', () => {
    const fixture = TestBed.createComponent(DataVolumeManagementComponent);
    fixture.componentRef.setInput('arrays', [
      array(),
      array({ id: 'md:sync', syncAction: 'resync' }),
      array({ id: 'md:degraded', degradedDeviceCount: 1 }),
      array({ id: 'md:free', name: 'md1', path: '/dev/md1' })
    ]);
    fixture.componentInstance.volumes.set([volume()]);

    expect(fixture.componentInstance.availableArrays().map((candidate) => candidate.id)).toEqual(['md:free']);
  });

  it('builds provisioning with ext4 directory and scoped share settings for backend preview', () => {
    const fixture = TestBed.createComponent(DataVolumeManagementComponent);
    fixture.componentRef.setInput('arrays', [array()]);
    const component = fixture.componentInstance;
    component.openProvision();
    component.smbEnabled.set(true);
    component.smbShareName.set('files');
    component.smbAllowedNetwork.set('192.168.188.0/24');
    component.nfsEnabled.set(true);
    component.nfsClientNetwork.set('192.168.188.0/24');
    component.password.set('password');

    component.createPreview();

    expect(previewRequest).toEqual({
      action: 'provisionVolume', arrayId: 'md:test', volumeId: null, volumeName: 'data',
      ownerName: 'root', groupName: 'amseoknas-data', directoryMode: '0770',
      smb: { enabled: true, shareName: 'files', readOnly: false, guestAccess: false, allowedNetwork: '192.168.188.0/24' },
      nfs: { enabled: true, clientNetwork: '192.168.188.0/24', readOnly: false }
    });
  });

  it('keeps verification separate from permission and share mutations', () => {
    const fixture = TestBed.createComponent(DataVolumeManagementComponent);
    fixture.componentRef.setInput('arrays', []);
    const component = fixture.componentInstance;
    component.openAction('verifyReadWrite', volume());
    component.password.set('password');

    component.createPreview();

    expect(previewRequest).toEqual({
      action: 'verifyReadWrite', arrayId: null, volumeId: 'volume:data', volumeName: null,
      ownerName: null, groupName: null, directoryMode: null, smb: null, nfs: null
    });
    expect(component.shareSummary(volume())).toBe('未启用');
    expect(component.operationLabel({
      operationId: 'operation', action: 'verifyReadWrite', status: 'succeeded',
      resourceId: 'volume:data', volume: null, errorCode: null, retryable: false,
      createdAt: '2026-08-09T00:00:00Z', updatedAt: '2026-08-09T00:00:01Z'
    })).toBe('操作已完成');
  });
});
