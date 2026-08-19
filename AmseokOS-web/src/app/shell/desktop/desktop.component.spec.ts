import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { routes } from '../../app.routes';
import { TerminalLauncherService } from '../../features/terminal/terminal-launcher.service';
import { WindowManagerService } from '../window-manager/window-manager.service';
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

  it('should open the managed terminal flow without a dedicated terminal route', () => {
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

  it('should show the AmseokOS identity without the drawn desktop logo', () => {
    const fixture = TestBed.createComponent(DesktopComponent);
    const http = TestBed.inject(HttpTestingController);

    fixture.detectChanges();
    http.expectOne('/api/auth/session').flush({ userName: 'admin', mustChangePassword: false });
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const identity = compiled.querySelector('.desktop-identity');

    expect(identity?.textContent).toContain('AmseokOS');
    expect(identity?.textContent).toContain('更便捷地管理你的服务器');
    expect(identity?.querySelector('.desktop-identity__mark')).toBeNull();
    expect(compiled.querySelector('.desktop-workspace')?.getAttribute('aria-label'))
      .toBe('AmseokOS 桌面');
    http.verify();
  });

  it('should keep terminal reauthentication blocked until the initial password is changed', () => {
    const fixture = TestBed.createComponent(DesktopComponent);
    const http = TestBed.inject(HttpTestingController);
    const launcher = TestBed.inject(TerminalLauncherService) as unknown as TerminalLauncherStub;

    fixture.detectChanges();
    http.expectOne('/api/auth/session').flush({ userName: 'admin', mustChangePassword: true });
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const terminalButton = compiled.querySelector<HTMLButtonElement>(
      'button[aria-label="终端"]'
    );
    terminalButton?.click();

    expect(launcher.openCalls).toBe(0);
    http.verify();
  });

  it('opens the Dashboard as a singleton managed window', () => {
    const fixture = TestBed.createComponent(DesktopComponent);
    const http = TestBed.inject(HttpTestingController);
    const manager = TestBed.inject(WindowManagerService);

    fixture.detectChanges();
    http.expectOne('/api/auth/session').flush({ userName: 'admin', mustChangePassword: false });
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    compiled.querySelector<HTMLButtonElement>(
      'button[aria-label="概览"]'
    )?.click();

    expect(manager.windows()).toHaveLength(1);
    expect(manager.windows()[0].appId).toBe('dashboard');
    manager.open('dashboard');
    expect(manager.windows()).toHaveLength(1);
    http.verify();
  });

  it('opens Launchpad with only installed components and launches from its grid', () => {
    const fixture = TestBed.createComponent(DesktopComponent);
    const http = TestBed.inject(HttpTestingController);
    const launcher = TestBed.inject(TerminalLauncherService) as unknown as TerminalLauncherStub;

    fixture.detectChanges();
    http.expectOne('/api/auth/session').flush({ userName: 'admin', mustChangePassword: false });
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    compiled.querySelector<HTMLButtonElement>('button[aria-label="启动台"]')?.click();
    fixture.detectChanges();

    expect(fixture.componentInstance.launcherOpen()).toBe(true);
    expect(fixture.componentInstance.activeAppLabel()).toBe('启动台');
    expect(compiled.querySelectorAll('.app-launcher__app')).toHaveLength(4);
    expect(compiled.textContent).not.toContain('共享文件');
    expect(compiled.textContent).not.toContain('任务中心');

    compiled.querySelector<HTMLButtonElement>('button[aria-label="打开终端"]')?.click();
    fixture.detectChanges();

    expect(launcher.openCalls).toBe(1);
    expect(fixture.componentInstance.launcherOpen()).toBe(false);
    http.verify();
  });
});
