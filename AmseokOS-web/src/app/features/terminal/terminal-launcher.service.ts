//--------------------------//
//--------串联管理员重新认证与受管终端窗口---------//
//--------Sequences administrator reauthentication and the managed terminal window--------//
//-------------------------//
import { inject, Injectable } from '@angular/core';

import { WindowManagerService } from '../../shell/window-manager/window-manager.service';

@Injectable({ providedIn: 'root' })
export class TerminalLauncherService {
  private readonly windowManager = inject(WindowManagerService);

  open(): void {
    if (this.windowManager.activate('terminal')) {
      return;
    }

    if (this.windowManager.activate('terminal-auth')) {
      return;
    }

    this.windowManager.open('terminal-auth');
  }

  reauthenticate(windowId: string): void {
    this.windowManager.close(windowId);
    this.open();
  }
}
