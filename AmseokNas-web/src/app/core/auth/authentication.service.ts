//--------------------------//
//--------封装 Cookie 认证、CSRF 准备与密码修改请求---------//
//--------Encapsulates cookie authentication, CSRF preparation, and password-change requests--------//
//-------------------------//
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { catchError, concatMap, Observable, tap, throwError } from 'rxjs';

export interface AuthenticationSession {
  userName: string;
  mustChangePassword: boolean;
}

interface ProblemDetails {
  detail?: string;
}

@Injectable({ providedIn: 'root' })
export class AuthenticationService {
  private readonly http = inject(HttpClient);
  private readonly currentSession = signal<AuthenticationSession | null>(null);

  readonly session = this.currentSession.asReadonly();

  login(password: string): Observable<AuthenticationSession> {
    return this.withCsrf(() =>
      this.http.post<AuthenticationSession>('/api/auth/login', { password })
    ).pipe(tap((session) => this.currentSession.set(session)));
  }

  getSession(): Observable<AuthenticationSession> {
    return this.http.get<AuthenticationSession>('/api/auth/session').pipe(
      tap((session) => this.currentSession.set(session)),
      catchError((error: unknown) => throwError(() => this.normalizeError(error)))
    );
  }

  changePassword(currentPassword: string, newPassword: string): Observable<void> {
    return this.withCsrf(() =>
      this.http.post<void>('/api/auth/change-password', { currentPassword, newPassword })
    ).pipe(tap(() => this.currentSession.set(null)));
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
        return new Error('无法连接登录服务，请检查服务状态后重试');
      }

      if (error.status === 401) {
        return new Error('登录状态已失效，请重新登录');
      }

      if (error.status === 429) {
        return new Error('尝试次数过多，请稍后再试');
      }
    }

    return error instanceof Error ? error : new Error('请求失败，请稍后再试');
  }
}
