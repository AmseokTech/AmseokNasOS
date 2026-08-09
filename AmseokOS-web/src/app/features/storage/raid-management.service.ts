//--------------------------//
//--------封装 RAID 预检、确认执行和 Operation 查询---------//
//--------Encapsulates RAID previews, confirmed execution, and operation queries--------//
//-------------------------//
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { catchError, concatMap, Observable, throwError } from 'rxjs';

import {
  RaidOperation,
  RaidOperationPreview,
  RaidOperationRequest
} from './raid-management.models';

interface ProblemDetails {
  readonly detail?: string;
}

@Injectable({ providedIn: 'root' })
export class RaidManagementService {
  private readonly http = inject(HttpClient);

  preview(request: RaidOperationRequest, password: string): Observable<RaidOperationPreview> {
    return this.withCsrf(() => this.http.post<RaidOperationPreview>(
      '/api/raid/operation-previews',
      { ...request, password }
    ));
  }

  execute(
    previewToken: string,
    confirmationPhrase: string,
    idempotencyKey: string,
    password: string
  ): Observable<RaidOperation> {
    return this.withCsrf(() => this.http.post<RaidOperation>('/api/raid/operations', {
      previewToken,
      confirmationPhrase,
      idempotencyKey,
      password
    }));
  }

  getOperation(operationId: string): Observable<RaidOperation> {
    return this.http.get<RaidOperation>(`/api/raid/operations/${encodeURIComponent(operationId)}`).pipe(
      catchError((error: unknown) => throwError(() => this.normalizeError(error)))
    );
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
        return new Error('无法连接 RAID 管理服务');
      }
      if (error.status === 401) {
        return new Error('管理员密码不正确或登录状态已失效');
      }
      if (error.status === 403) {
        return new Error('当前账户没有修改 RAID 的权限');
      }
      if (error.status === 409) {
        return new Error('磁盘状态已经变化或资源正被其他操作占用，请刷新后重试');
      }
      if (error.status === 429) {
        return new Error('危险操作尝试过于频繁，请稍后再试');
      }
      if (error.status === 503) {
        return new Error('底层 RAID 执行服务暂不可用');
      }
    }
    return error instanceof Error ? error : new Error('RAID 操作失败');
  }
}
