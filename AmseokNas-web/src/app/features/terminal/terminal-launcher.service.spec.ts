import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { Subject } from 'rxjs';

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

class MatDialogStub {
  readonly closedResults: Subject<TerminalSession | undefined>[] = [];

  open(): { afterClosed: () => Subject<TerminalSession | undefined> } {
    const result = new Subject<TerminalSession | undefined>();
    this.closedResults.push(result);
    return { afterClosed: () => result };
  }
}

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

const session: TerminalSession = {
  sessionId: '0190f6f4-7de8-7000-8000-000000000001',
  expiresAt: '2026-07-22T12:00:00Z',
  webSocketPath: '/api/terminal/sessions/session/socket'
};

describe('TerminalLauncherService', () => {
  let dialog: MatDialogStub;
  let launcher: TerminalLauncherService;
  let manager: WindowManagerService;

  beforeEach(() => {
    dialog = new MatDialogStub();
    TestBed.configureTestingModule({
      providers: [
        { provide: MatDialog, useValue: dialog },
        {
          provide: APP_COMPONENT_REGISTRY,
          useValue: new Map([['terminal', terminalDefinition]])
        },
        { provide: WINDOW_LAYOUT_STORAGE, useValue: null }
      ]
    });
    launcher = TestBed.inject(TerminalLauncherService);
    manager = TestBed.inject(WindowManagerService);
  });

  it('suppresses repeated authentication and restores an existing terminal without reauthenticating', async () => {
    launcher.open();
    launcher.open();
    await vi.waitFor(() => expect(dialog.closedResults).toHaveLength(1));

    dialog.closedResults[0].next(session);
    dialog.closedResults[0].complete();
    expect(manager.windows()).toHaveLength(1);
    expect(manager.windows()[0].data).toBe(session);

    manager.minimize(manager.windows()[0].id);
    launcher.open();
    expect(manager.windows()[0].displayState).toBe('normal');
    expect(dialog.closedResults).toHaveLength(1);
  });

  it('unlocks after cancellation and closes the old window before reauthentication', async () => {
    launcher.open();
    await vi.waitFor(() => expect(dialog.closedResults).toHaveLength(1));
    dialog.closedResults[0].next(undefined);
    dialog.closedResults[0].complete();

    launcher.open();
    await vi.waitFor(() => expect(dialog.closedResults).toHaveLength(2));
    dialog.closedResults[1].next(session);
    dialog.closedResults[1].complete();
    const windowId = manager.windows()[0].id;

    launcher.reauthenticate(windowId);
    expect(manager.windows()).toHaveLength(0);
    await vi.waitFor(() => expect(dialog.closedResults).toHaveLength(3));
  });
});
