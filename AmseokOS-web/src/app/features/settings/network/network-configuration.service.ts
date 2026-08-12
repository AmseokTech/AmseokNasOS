import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { catchError, Observable, throwError } from 'rxjs';

import {
  NetworkConfigurationOperation,
  NetworkConfigurationPreview,
  NetworkConfigurationRequest
} from './network-configuration.models';

interface ProblemDetails {
  readonly detail?: string;
  readonly title?: string;
}

@Injectable({ providedIn: 'root' })
export class NetworkConfigurationService {
  private readonly http = inject(HttpClient);

  preview(request: NetworkConfigurationRequest): Observable<NetworkConfigurationPreview> {
    return this.http.post<NetworkConfigurationPreview>(
      '/api/network/configuration-previews',
      request
    ).pipe(catchError((error: unknown) => this.fail(error)));
  }

  apply(request: NetworkConfigurationRequest): Observable<NetworkConfigurationOperation> {
    return this.http.post<NetworkConfigurationOperation>(
      '/api/network/configuration-operations',
      request
    ).pipe(catchError((error: unknown) => this.fail(error)));
  }

  confirm(operationId: string): Observable<NetworkConfigurationOperation> {
    return this.http.post<NetworkConfigurationOperation>(
      `/api/network/configuration-operations/${operationId}/confirm`,
      {}
    ).pipe(catchError((error: unknown) => this.fail(error)));
  }

  rollback(operationId: string): Observable<NetworkConfigurationOperation> {
    return this.http.post<NetworkConfigurationOperation>(
      `/api/network/configuration-operations/${operationId}/rollback`,
      {}
    ).pipe(catchError((error: unknown) => this.fail(error)));
  }

  private fail(error: unknown): Observable<never> {
    if (error instanceof HttpErrorResponse) {
      const problem = error.error as ProblemDetails | null;
      if (problem?.detail || problem?.title) {
        return throwError(() => new Error(problem.detail ?? problem.title));
      }
      if (error.status === 0) {
        return throwError(() => new Error(
          '连接可能因 IP 变更中断；请等待自动回滚，或尝试使用新地址访问。'
        ));
      }
      if (error.status === 401) {
        return throwError(() => new Error('管理员密码错误或登录状态已失效'));
      }
      if (error.status === 403) {
        return throwError(() => new Error('当前账户没有修改网络配置的权限'));
      }
    }
    return throwError(() => error instanceof Error
      ? error
      : new Error('网络配置操作失败'));
  }
}
