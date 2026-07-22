//--------------------------//
//--------串联管理员重新认证与受管终端窗口---------//
//--------Sequences administrator reauthentication and the managed terminal window--------//
//-------------------------//
import { inject, Injectable } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { take } from 'rxjs';

import { WindowManagerService } from '../../shell/window-manager/window-manager.service';
import type { TerminalSession } from './terminal-session.service';

@Injectable({ providedIn: 'root' })
export class TerminalLauncherService {
  private readonly dialog = inject(MatDialog);
  private readonly windowManager = inject(WindowManagerService);
  private active = false;

  open(): void {
    if (this.windowManager.activate('terminal')) {
      return;
    }

    if (this.active) {
      return;
    }

    this.active = true;
    void import('./terminal-authentication-dialog.component')
      .then(({ TerminalAuthenticationDialogComponent }) => {
        const reference = this.dialog.open(TerminalAuthenticationDialogComponent, {
          ariaLabel: '验证 Web 管理员密码',
          autoFocus: 'first-tabbable',
          maxWidth: 'calc(100vw - 32px)',
          restoreFocus: true,
          width: '440px'
        });
        reference
          .afterClosed()
          .pipe(take(1))
          .subscribe((session: TerminalSession | undefined) => {
            if (!session) {
              this.active = false;
              return;
            }

            this.windowManager.open('terminal', { data: session });
            this.active = false;
          });
      })
      .catch(() => {
        this.active = false;
      });
  }

  reauthenticate(windowId: string): void {
    this.windowManager.close(windowId);
    this.active = false;
    this.open();
  }
}
