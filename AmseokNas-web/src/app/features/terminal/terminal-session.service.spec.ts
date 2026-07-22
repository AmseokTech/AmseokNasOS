//--------------------------//
//--------验证终端会话创建会先准备 CSRF---------//
//--------Verifies terminal session creation prepares CSRF first--------//
//-------------------------//
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { TerminalSessionService } from './terminal-session.service';

describe('TerminalSessionService', () => {
  it('should prepare CSRF before creating a bounded terminal session', () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()]
    });
    const service = TestBed.inject(TerminalSessionService);
    const http = TestBed.inject(HttpTestingController);
    let sessionId = '';

    service.create('Admin-password1!', 120, 32).subscribe((session) => {
      sessionId = session.sessionId;
    });

    http.expectOne('/api/auth/csrf').flush(null);
    const request = http.expectOne('/api/terminal/sessions');
    expect(request.request.body).toEqual({
      password: 'Admin-password1!',
      columns: 120,
      rows: 32
    });
    request.flush({
      sessionId: '0190f6f4-7de8-7000-8000-000000000001',
      expiresAt: '2026-07-22T08:00:00Z',
      webSocketPath: '/api/terminal/sessions/0190f6f4-7de8-7000-8000-000000000001/socket'
    });

    expect(sessionId).toBe('0190f6f4-7de8-7000-8000-000000000001');
    http.verify();
  });
});
