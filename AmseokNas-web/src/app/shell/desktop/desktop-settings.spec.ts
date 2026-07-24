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
  it('opens one managed Settings window and restores that singleton', async () => {
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
    fixture.componentInstance.selectApp(settings!);
    fixture.componentInstance.selectApp(settings!);

    expect(manager.windows()).toHaveLength(1);
    expect(manager.windows()[0].appId).toBe('settings');
    expect(fixture.componentInstance.activeAppLabel()).toBe('系统设置');
    http.verify();
  });
});
