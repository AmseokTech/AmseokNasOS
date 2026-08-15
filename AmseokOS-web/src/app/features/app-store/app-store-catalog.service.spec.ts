import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import type { AppCatalogResponse } from './app-store-catalog.models';
import { AppStoreCatalogService } from './app-store-catalog.service';

describe('AppStoreCatalogService', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('reads the verified catalog only through the local API', () => {
    const service = TestBed.inject(AppStoreCatalogService);
    const http = TestBed.inject(HttpTestingController);
    const response: AppCatalogResponse = {
      format: 'amseok-app-catalog-v1',
      revision: 'revision-1',
      generatedAt: '2026-08-15T08:00:00Z',
      refreshedAt: '2026-08-15T08:01:00Z',
      isStale: false,
      apps: []
    };

    let received: AppCatalogResponse | undefined;
    service.getCatalog().subscribe((catalog) => received = catalog);
    http.expectOne('/api/app-store/catalog').flush(response);

    expect(received).toEqual(response);
  });

  it('uses the sanitized problem detail returned by the local API', () => {
    const service = TestBed.inject(AppStoreCatalogService);
    const http = TestBed.inject(HttpTestingController);
    let message: string | undefined;

    service.getCatalog().subscribe({
      error: (error: Error) => message = error.message
    });
    http.expectOne('/api/app-store/catalog').flush(
      { detail: '远端应用目录未通过安全校验' },
      { status: 503, statusText: 'Service Unavailable' }
    );

    expect(message).toBe('远端应用目录未通过安全校验');
  });
});
