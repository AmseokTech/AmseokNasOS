//--------------------------//
//--------封装本机应用市场目录 API 查询---------//
//--------Encapsulates the local app-catalog API query--------//
//-------------------------//
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { catchError, Observable, throwError } from 'rxjs';

import type { AppCatalogResponse } from './app-store-catalog.models';

interface ProblemDetails {
  readonly detail?: string;
}

@Injectable({ providedIn: 'root' })
export class AppStoreCatalogService {
  private readonly http = inject(HttpClient);

  getCatalog(): Observable<AppCatalogResponse> {
    return this.http.get<AppCatalogResponse>('/api/app-store/catalog').pipe(
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
        return new Error('无法连接本机应用市场服务');
      }
      if (error.status === 401) {
        return new Error('登录状态已失效，请重新登录');
      }
      if (error.status === 403) {
        return new Error('当前账户没有查看应用市场的权限');
      }
    }

    return error instanceof Error ? error : new Error('应用目录加载失败');
  }
}
