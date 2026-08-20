import { TestBed } from '@angular/core/testing';

import {
  APP_COMPONENT_REGISTRY,
  AppComponentDefinition
} from '../../shell/window-manager/app-component.registry';
import {
  WINDOW_LAYOUT_STORAGE,
  WindowManagerService
} from '../../shell/window-manager/window-manager.service';
import { TerminalLauncherService } from './terminal-launcher.service';
import type { TerminalSession } from './terminal-session.service';

const terminalDefinition: AppComponentDefinition = {
  appId: 'terminal',
  title: 'terminal',
  singleton: true,
  defaultWidth: 1120,
  defaultHeight: 760,
  minWidth: 480,
  minHeight: 320,
  loadComponent: async () => class {}
};

const terminalAuthenticationDefinition: AppComponentDefinition = {
  appId: 'terminal-auth',
  title: '使用 Terminal',
  singleton: true,
  defaultWidth: 520,
  defaultHeight: 390,
  minWidth: 420,
  minHeight: 330,
  loadComponent: async () => class {}
};

const session: TerminalSession = {
  sessionId: '0190f6f4-7de8-7000-8000-000000000001',
  expiresAt: '2026-07-22T12:00:00Z',
  webSocketPath: '/api/terminal/sessions/session/socket'
};

describe('TerminalLauncherService', () => {
  let launcher: TerminalLauncherService;
  let manager: WindowManagerService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: APP_COMPONENT_REGISTRY,
          useValue: new Map([
            ['terminal', terminalDefinition],
            ['terminal-auth', terminalAuthenticationDefinition]
          ])
        },
        { provide: WINDOW_LAYOUT_STORAGE, useValue: null }
      ]
    });
    launcher = TestBed.inject(TerminalLauncherService);
    manager = TestBed.inject(WindowManagerService);
  });

  it('opens one managed authentication window and restores it when reopened', () => {
    launcher.open();
    launcher.open();
    expect(manager.windows()).toHaveLength(1);
    expect(manager.windows()[0].appId).toBe('terminal-auth');

    manager.minimize(manager.windows()[0].id);
    launcher.open();
    expect(manager.windows()[0].displayState).toBe('normal');
  });

  it('restores an existing terminal without opening authentication again', () => {
    const windowId = manager.open('terminal', { data: session });
    manager.minimize(windowId);

    launcher.open();

    expect(manager.windows()).toHaveLength(1);
    expect(manager.windows()[0].appId).toBe('terminal');
    expect(manager.windows()[0].displayState).toBe('normal');
    expect(manager.windows()[0].data).toBe(session);
  });

  it('closes the old terminal before opening a managed reauthentication window', () => {
    const windowId = manager.open('terminal', { data: session });

    launcher.reauthenticate(windowId);

    expect(manager.windowForApp('terminal')).toBeUndefined();
    expect(manager.windows()).toHaveLength(1);
    expect(manager.windows()[0].appId).toBe('terminal-auth');
  });
});
