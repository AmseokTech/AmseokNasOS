import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { StorageManagementService } from './storage-management.service';

describe('StorageManagementService', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
  });

  it('loads the managed ext4 volume inventory', () => {
    const service = TestBed.inject(StorageManagementService);
    const http = TestBed.inject(HttpTestingController);
    let count = 0;

    service.getVolumes().subscribe((volumes) => count = volumes.length);

    const request = http.expectOne('/api/storage-management/volumes');
    expect(request.request.method).toBe('GET');
    request.flush([{
      id: 'volume:data',
      name: 'data',
      arrayId: 'md:test',
      arrayPath: '/dev/md0',
      fileSystemUuid: 'uuid',
      fileSystemType: 'ext4',
      mountPath: '/srv/amseoknas/volumes/data',
      mounted: true,
      persistentMountEnabled: true,
      ownerName: 'root',
      groupName: 'amseoknas-data',
      directoryMode: '0770',
      readWriteVerified: true,
      smb: { enabled: false, shareName: null, readOnly: false, guestAccess: false, allowedNetwork: null },
      nfs: { enabled: false, clientNetwork: null, readOnly: false }
    }]);

    expect(count).toBe(1);
    http.verify();
  });

  it('obtains CSRF protection before preview and confirmed execution', () => {
    const service = TestBed.inject(StorageManagementService);
    const http = TestBed.inject(HttpTestingController);
    const request = {
      action: 'verifyReadWrite' as const,
      arrayId: null,
      volumeId: 'volume:data',
      volumeName: null,
      ownerName: null,
      groupName: null,
      directoryMode: null,
      smb: null,
      nfs: null
    };

    service.preview(request, 'first-password').subscribe();
    http.expectOne('/api/auth/csrf').flush(null);
    const preview = http.expectOne('/api/storage-management/operation-previews');
    expect(preview.request.method).toBe('POST');
    expect(preview.request.body).toEqual({ ...request, password: 'first-password' });
    preview.flush({
      action: 'verifyReadWrite', requested: {}, existingVolume: null, canExecute: true,
      previewToken: 'token', expiresAt: null, confirmationPhrase: '校验 data', blockingIssues: [], warnings: []
    });

    service.execute('token', '校验 data', 'idempotency', 'second-password').subscribe();
    http.expectOne('/api/auth/csrf').flush(null);
    const execute = http.expectOne('/api/storage-management/operations');
    expect(execute.request.body).toEqual({
      previewToken: 'token',
      confirmationPhrase: '校验 data',
      idempotencyKey: 'idempotency',
      password: 'second-password'
    });
    execute.flush({
      operationId: 'operation', action: 'verifyReadWrite', status: 'succeeded',
      resourceId: 'volume:data', volume: null, errorCode: null, retryable: false,
      createdAt: '2026-08-09T00:00:00Z', updatedAt: '2026-08-09T00:00:01Z'
    });
    http.verify();
  });
});
