import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { RaidManagementService } from './raid-management.service';

describe('RaidManagementService', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
  });

  it('obtains CSRF protection before creating a destructive-operation preview', () => {
    const service = TestBed.inject(RaidManagementService);
    const http = TestBed.inject(HttpTestingController);
    let canExecute = false;

    service.preview({
      action: 'delete',
      arrayId: 'md:test',
      arrayName: null,
      level: null,
      deviceIds: [],
      sourceDeviceId: null,
      targetDeviceCount: null
    }, 'password').subscribe((preview) => canExecute = preview.canExecute);

    http.expectOne('/api/auth/csrf').flush(null);
    const request = http.expectOne('/api/raid/operation-previews');
    expect(request.request.method).toBe('POST');
    expect(request.request.body.password).toBe('password');
    request.flush({
      action: 'delete',
      arrayId: 'md:test',
      arrayName: null,
      level: null,
      deviceIds: [],
      sourceDeviceId: null,
      targetDeviceCount: null,
      arrayDisplayName: 'md0',
      expectedMemberDeviceIds: ['wwn:a', 'wwn:b'],
      canExecute: true,
      previewToken: 'one-time-token',
      expiresAt: '2026-08-09T00:00:00Z',
      confirmationPhrase: '删除 md0',
      blockingIssues: [],
      warnings: ['raid.all_array_data_will_be_destroyed']
    });

    expect(canExecute).toBe(true);
    http.verify();
  });

  it('uses a second CSRF-protected request to execute the one-time preview', () => {
    const service = TestBed.inject(RaidManagementService);
    const http = TestBed.inject(HttpTestingController);

    service.execute('token', '删除 md0', 'idempotency-key', 'password').subscribe();

    http.expectOne('/api/auth/csrf').flush(null);
    const request = http.expectOne('/api/raid/operations');
    expect(request.request.body).toEqual({
      previewToken: 'token',
      confirmationPhrase: '删除 md0',
      idempotencyKey: 'idempotency-key',
      password: 'password'
    });
    request.flush({
      operationId: 'operation-id',
      action: 'delete',
      status: 'succeeded',
      resourceId: 'array:md:test',
      arrayId: null,
      errorCode: null,
      retryable: false,
      progressPercentage: 100,
      createdAt: '2026-08-09T00:00:00Z',
      updatedAt: '2026-08-09T00:00:01Z'
    });
    http.verify();
  });
});
