import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { routes } from '../../app.routes';
import { TerminalLauncherService } from '../../features/terminal/terminal-launcher.service';
import { DesktopComponent } from './desktop.component';

class TerminalLauncherStub {
  openCalls = 0;

  open(): void {
    this.openCalls += 1;
  }
}

describe('DesktopComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DesktopComponent],
      providers: [
        provideNoopAnimations(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter(routes),
        { provide: TerminalLauncherService, useClass: TerminalLauncherStub }
      ]
    }).compileComponents();
  });

  it('should restore the session and show the forced password-change reminder', () => {
    const fixture = TestBed.createComponent(DesktopComponent);
    const http = TestBed.inject(HttpTestingController);

    fixture.detectChanges();
    http.expectOne('/api/auth/session').flush({ userName: 'admin', mustChangePassword: true });
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.desktop-reminders')?.textContent).toContain('请修改初始密码');
    expect(compiled.querySelector<HTMLAnchorElement>('[reminder-actions]')?.getAttribute('href'))
      .toBe('/change-password');
    http.verify();
  });

  it('should open the terminal dialog flow without a dedicated terminal route', () => {
    const fixture = TestBed.createComponent(DesktopComponent);
    const http = TestBed.inject(HttpTestingController);
    const launcher = TestBed.inject(TerminalLauncherService) as unknown as TerminalLauncherStub;

    fixture.detectChanges();
    http.expectOne('/api/auth/session').flush({ userName: 'admin', mustChangePassword: false });
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const terminalButton = compiled.querySelector<HTMLButtonElement>('button[aria-label="终端"]');
    terminalButton?.click();

    expect(launcher.openCalls).toBe(1);
    expect(routes.some((route) => route.path === 'terminal')).toBe(false);
    http.verify();
  });
});
