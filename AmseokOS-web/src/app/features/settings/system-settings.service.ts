//--------------------------//
//--------封装关于本机与网络只读查询---------//
//--------Encapsulates read-only About and Network queries--------//
//-------------------------//
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { catchError, Observable, throwError } from 'rxjs';

import {
  NetworkInterfaceInformation,
  SystemAbout
} from './system-settings.models';

interface ProblemDetails {
  readonly detail?: string;
}

@Injectable({ providedIn: 'root' })
export class SystemSettingsService {
  private readonly http = inject(HttpClient);

  getAbout(): Observable<SystemAbout> {
    return this.http.get<SystemAbout>('/api/system/about').pipe(
      catchError((error: unknown) => throwError(() => this.normalizeError(error)))
    );
  }

  getNetworkInterfaces(): Observable<readonly NetworkInterfaceInformation[]> {
    return this.http.get<readonly NetworkInterfaceInformation[]>(
      '/api/network/interfaces'
    ).pipe(
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
        return new Error('无法连接系统信息服务，请检查服务状态');
      }
      if (error.status === 401) {
        return new Error('登录状态已失效，请重新登录');
      }
      if (error.status === 403) {
        return new Error('当前账户没有查看系统信息的权限');
      }
      if (error.status === 503) {
        return new Error('底层系统信息服务暂不可用');
      }
    }

    return error instanceof Error ? error : new Error('系统信息加载失败');
  }
}
