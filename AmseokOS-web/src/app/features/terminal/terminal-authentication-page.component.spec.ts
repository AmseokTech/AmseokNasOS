import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { WindowManagerService } from '../../shell/window-manager/window-manager.service';
import { WINDOW_ID } from '../../shell/window-manager/window-state.model';
import { TerminalAuthenticationPageComponent } from './terminal-authentication-page.component';
import { TerminalSession, TerminalSessionService } from './terminal-session.service';

const session: TerminalSession = {
  sessionId: '0190f6f4-7de8-7000-8000-000000000001',
  expiresAt: '2026-07-22T12:00:00Z',
  webSocketPath: '/api/terminal/sessions/session/socket'
};

class TerminalSessionServiceStub {
  readonly create = vi.fn(() => of(session));
}

class WindowManagerStub {
  readonly close = vi.fn();
  readonly open = vi.fn();
}

describe('TerminalAuthenticationPageComponent', () => {
  let sessions: TerminalSessionServiceStub;
  let windowManager: WindowManagerStub;

  beforeEach(async () => {
    sessions = new TerminalSessionServiceStub();
    windowManager = new WindowManagerStub();
    await TestBed.configureTestingModule({
      imports: [TerminalAuthenticationPageComponent],
      providers: [
        provideNoopAnimations(),
        { provide: TerminalSessionService, useValue: sessions },
        { provide: WindowManagerService, useValue: windowManager },
        { provide: WINDOW_ID, useValue: 'terminal-auth-1' }
      ]
    }).compileComponents();
  });

  it('opens the managed terminal after successful reauthentication', () => {
    const fixture = TestBed.createComponent(TerminalAuthenticationPageComponent);
    fixture.componentInstance.form.controls.password.setValue('Admin-password1!');

    fixture.componentInstance.authenticate();

    expect(sessions.create).toHaveBeenCalledWith('Admin-password1!', 100, 30);
    expect(windowManager.close).toHaveBeenCalledWith('terminal-auth-1');
    expect(windowManager.open).toHaveBeenCalledWith('terminal', { data: session });
    expect(fixture.componentInstance.form.controls.password.value).toBe('');
  });

  it('keeps the managed window open and clears the password after a failed attempt', () => {
    const fixture = TestBed.createComponent(TerminalAuthenticationPageComponent);
    sessions.create.mockReturnValueOnce(throwError(() => new Error('管理员密码无效')));
    fixture.componentInstance.form.controls.password.setValue('wrong-password');

    fixture.componentInstance.authenticate();

    expect(windowManager.close).not.toHaveBeenCalled();
    expect(windowManager.open).not.toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.password.value).toBe('');
    expect(fixture.componentInstance.errorMessage()).toBe('管理员密码无效');
  });

  it('closes the authentication window when cancellation is requested', () => {
    const fixture = TestBed.createComponent(TerminalAuthenticationPageComponent);

    fixture.componentInstance.cancel();

    expect(windowManager.close).toHaveBeenCalledWith('terminal-auth-1');
  });
});
