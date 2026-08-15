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

describe('Desktop app store launcher', () => {
  it('opens one managed App Store window from the Dock', async () => {
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

    expect(DESKTOP_APPS.find(({ id }) => id === 'app-store')).toBeDefined();
    const compiled = fixture.nativeElement as HTMLElement;
    const appStoreButton = compiled.querySelector<HTMLButtonElement>(
      'button[aria-label="应用商店"]'
    );
    appStoreButton?.click();
    fixture.detectChanges();

    expect(manager.windows()).toHaveLength(1);
    expect(manager.windows()[0].appId).toBe('app-store');
    expect(fixture.componentInstance.activeAppLabel()).toBe('应用商店');

    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(compiled.querySelector('.app-store')).not.toBeNull();
    });
    http.expectOne('/api/app-store/catalog').flush({
      format: 'amseok-app-catalog-v1',
      revision: 'revision-1',
      generatedAt: '2026-08-15T08:00:00Z',
      refreshedAt: '2026-08-15T08:01:00Z',
      isStale: false,
      apps: []
    });
    fixture.detectChanges();
    expect(compiled.querySelector('.managed-window')?.getAttribute('aria-label')).toBe(
      '应用商店 窗口'
    );

    appStoreButton?.click();
    fixture.detectChanges();
    expect(manager.windows()).toHaveLength(1);
    http.verify();
  });
});
