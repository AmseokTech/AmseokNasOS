import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting
} from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { routes } from '../../app.routes';
import { TerminalLauncherService } from '../../features/terminal/terminal-launcher.service';
import { WindowManagerService } from '../window-manager/window-manager.service';
import { DESKTOP_APPS } from './desktop-app.model';
import { DesktopComponent } from './desktop.component';

describe('Desktop settings launcher', () => {
  it('opens and renders one managed Settings window from the Dock', async () => {
    await TestBed.configureTestingModule({
      imports: [DesktopComponent],
      providers: [
        provideNoopAnimations(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter(routes),
        { provide: TerminalLauncherService, useValue: { open: vi.fn() } }
      ]
    }).compileComponents();
    const fixture = TestBed.createComponent(DesktopComponent);
    const http = TestBed.inject(HttpTestingController);
    const manager = TestBed.inject(WindowManagerService);
    fixture.detectChanges();
    http.expectOne('/api/auth/session').flush({
      userName: 'admin',
      mustChangePassword: false
    });

    const settings = DESKTOP_APPS.find(({ id }) => id === 'settings');
    expect(settings).toBeDefined();
    const compiled = fixture.nativeElement as HTMLElement;
    const settingsButton = compiled.querySelector<HTMLButtonElement>(
      'button[aria-label="系统设置"]'
    );
    expect(settingsButton).not.toBeNull();
    settingsButton!.click();
    fixture.detectChanges();

    expect(manager.windows()).toHaveLength(1);
    expect(manager.windows()[0].appId).toBe('settings');
    expect(fixture.componentInstance.activeAppLabel()).toBe('系统设置');

    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(compiled.querySelector('.settings-layout')).not.toBeNull();
    });
    http.expectOne('/api/system/about').flush(
      { detail: '测试环境未连接底层系统查询服务' },
      { status: 503, statusText: 'Service Unavailable' }
    );
    fixture.detectChanges();
    const managedWindow = compiled.querySelector<HTMLElement>('.managed-window');
    expect(managedWindow?.getAttribute('aria-label')).toBe('系统设置 窗口');

    settingsButton!.click();
    fixture.detectChanges();
    expect(manager.windows()).toHaveLength(1);
    http.verify();
  });
});
