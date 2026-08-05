//--------------------------//
//--------封装终端重新认证与一次性会话创建---------//
//--------Encapsulates terminal reauthentication and one-time session creation--------//
//-------------------------//
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { catchError, concatMap, Observable, throwError } from 'rxjs';

export interface TerminalSession {
  sessionId: string;
  expiresAt: string;
  webSocketPath: string;
}

interface ProblemDetails {
  detail?: string;
}

@Injectable({ providedIn: 'root' })
export class TerminalSessionService {
  private readonly http = inject(HttpClient);

  create(password: string, columns: number, rows: number): Observable<TerminalSession> {
    return this.http.get<void>('/api/auth/csrf').pipe(
      concatMap(() =>
        this.http.post<TerminalSession>('/api/terminal/sessions', {
          password,
          columns,
          rows
        })
      ),
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
        return new Error('无法连接终端网关，请检查服务状态');
      }
      if (error.status === 401) {
        return new Error('管理员密码不正确或登录状态已失效');
      }
      if (error.status === 429) {
        return new Error('终端开启尝试过于频繁，请稍后再试');
      }
    }

    return error instanceof Error ? error : new Error('终端会话创建失败');
  }
}
