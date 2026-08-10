//--------------------------//
//--------通过公开功能入口聚合只读概览数据---------//
//--------Aggregates read-only dashboard data through public feature entry points--------//
//-------------------------//

import { inject, Injectable } from '@angular/core';
import { catchError, forkJoin, map, Observable, of, switchMap } from 'rxjs';

import { ApiHealthService } from '../../core/services/api-health.service';
import { SystemSettingsService } from '../settings';
import {
  StorageInventoryService,
  StorageManagementService
} from '../storage';
import type { DiskSmartStatus, StorageInventory } from '../storage';
import {
  DashboardSection,
  DashboardSnapshot,
  SmartSummary
} from './dashboard.models';

@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly health = inject(ApiHealthService);
  private readonly settings = inject(SystemSettingsService);
  private readonly storage = inject(StorageInventoryService);
  private readonly storageManagement = inject(StorageManagementService);

  load(): Observable<DashboardSnapshot> {
    return forkJoin({
      health: this.capture(this.health.getHealth()),
      about: this.capture(this.settings.getAbout()),
      network: this.capture(this.settings.getNetworkInterfaces()),
      storage: this.capture(this.storage.getInventory()),
      volumes: this.capture(this.storageManagement.getVolumes())
    }).pipe(
      switchMap((base) => this.loadSmartSummary(base.storage).pipe(
        map((smart) => ({ ...base, smart }))
      ))
    );
  }

  private loadSmartSummary(
    storage: DashboardSection<StorageInventory>
  ): Observable<DashboardSection<SmartSummary>> {
    const eligibleDisks = storage.value?.disks.filter(
      ({ stable, identityConflict }) => stable && !identityConflict
    ) ?? [];
    if (eligibleDisks.length === 0) {
      return of({
        value: this.summarize([]),
        error: storage.error
      });
    }

    return forkJoin(eligibleDisks.map(({ id }) => this.storage.getSmart(id).pipe(
      map(({ status }) => status),
      catchError(() => of<DiskSmartStatus>('unknown'))
    ))).pipe(
      map((statuses) => ({ value: this.summarize(statuses), error: null }))
    );
  }

  private summarize(statuses: readonly DiskSmartStatus[]): SmartSummary {
    const count = (status: DiskSmartStatus): number =>
      statuses.filter((value) => value === status).length;
    return {
      queried: statuses.length,
      healthy: count('healthy'),
      warning: count('warning'),
      failing: count('failing'),
      unsupported: count('unsupported'),
      unknown: count('unknown')
    };
  }

  private capture<T>(source: Observable<T>): Observable<DashboardSection<T>> {
    return source.pipe(
      map((value) => ({ value, error: null })),
      catchError((error: unknown) => of({
        value: null,
        error: error instanceof Error ? error.message : '数据加载失败'
      }))
    );
  }
}
