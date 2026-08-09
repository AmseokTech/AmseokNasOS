//--------------------------//
//--------封装数据卷供应、权限、校验与共享管理 HTTP 边界---------//
//--------Encapsulates volume, permission, verification, and share-management HTTP boundaries--------//
//-------------------------//
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { catchError, concatMap, Observable, throwError } from 'rxjs';

import {
  ManagedVolume,
  StorageOperation,
  StorageOperationPreview,
  StorageOperationRequest
} from './storage-management.models';

interface ProblemDetails {
  readonly detail?: string;
}

@Injectable({ providedIn: 'root' })
export class StorageManagementService {
  private readonly http = inject(HttpClient);

  getVolumes(): Observable<readonly ManagedVolume[]> {
    return this.http.get<readonly ManagedVolume[]>('/api/storage-management/volumes').pipe(
      catchError((error: unknown) => throwError(() => this.normalizeError(error)))
    );
  }

  preview(request: StorageOperationRequest, password: string): Observable<StorageOperationPreview> {
    return this.withCsrf(() => this.http.post<StorageOperationPreview>(
      '/api/storage-management/operation-previews',
      { ...request, password }
    ));
  }

  execute(
    previewToken: string,
    confirmationPhrase: string,
    idempotencyKey: string,
    password: string
  ): Observable<StorageOperation> {
    return this.withCsrf(() => this.http.post<StorageOperation>('/api/storage-management/operations', {
      previewToken,
      confirmationPhrase,
      idempotencyKey,
      password
    }));
  }

  getOperation(operationId: string): Observable<StorageOperation> {
    return this.http.get<StorageOperation>(
      `/api/storage-management/operations/${encodeURIComponent(operationId)}`
    ).pipe(catchError((error: unknown) => throwError(() => this.normalizeError(error))));
  }

  private withCsrf<T>(request: () => Observable<T>): Observable<T> {
    return this.http.get<void>('/api/auth/csrf').pipe(
      concatMap(request),
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
        return new Error('无法连接数据卷与共享管理服务');
      }
      if (error.status === 401) {
        return new Error('管理员密码不正确或登录状态已失效');
      }
      if (error.status === 403) {
        return new Error('当前账户没有管理数据卷和共享的权限');
      }
      if (error.status === 409) {
        return new Error('阵列或数据卷状态已经变化，或资源正被其他操作占用');
      }
      if (error.status === 429) {
        return new Error('存储操作尝试过于频繁，请稍后再试');
      }
      if (error.status === 503) {
        return new Error('底层数据卷与共享服务尚不可用');
      }
    }
    return error instanceof Error ? error : new Error('数据卷操作失败');
  }
}
