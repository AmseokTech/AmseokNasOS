//--------------------------//
//--------封装磁盘与 RAID 只读清单查询---------//
//--------Encapsulates read-only disk and RAID inventory queries--------//
//-------------------------//
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { catchError, forkJoin, Observable, throwError } from 'rxjs';

import {
  BlockDevice,
  DiskSmartInformation,
  RaidArray,
  StorageInventory
} from './storage-inventory.models';

interface ProblemDetails {
  readonly detail?: string;
}

@Injectable({ providedIn: 'root' })
export class StorageInventoryService {
  private readonly http = inject(HttpClient);

  getInventory(): Observable<StorageInventory> {
    return forkJoin({
      disks: this.http.get<readonly BlockDevice[]>('/api/storage/disks'),
      arrays: this.http.get<readonly RaidArray[]>('/api/raid/arrays')
    }).pipe(
      catchError((error: unknown) => throwError(() => this.normalizeError(error)))
    );
  }

  getSmart(deviceId: string): Observable<DiskSmartInformation> {
    const params = new HttpParams().set('deviceId', deviceId);
    return this.http.get<DiskSmartInformation>('/api/storage/disks/smart', { params }).pipe(
      catchError((error: unknown) => throwError(() => this.normalizeError(error)))
    );
  }

  private normalizeError(error: unknown): Error {
    if (error instanceof HttpErrorResponse) {
      const problem = error.error as ProblemDetails | null;
      if (problem?.detail) {
        return new Error(problem.detail);
      }
      if (error.status === 0) {
        return new Error('无法连接存储管理服务，请检查服务状态');
      }
      if (error.status === 401) {
        return new Error('登录状态已失效，请重新登录');
      }
      if (error.status === 403) {
        return new Error('当前账户没有查看磁盘和阵列的权限');
      }
      if (error.status === 503) {
        return new Error('底层存储查询服务暂不可用');
      }
    }

    return error instanceof Error ? error : new Error('磁盘信息加载失败');
  }
}
