//--------------------------//
//--------串联管理员重新认证与终端弹窗---------//
//--------Sequences administrator reauthentication and the terminal dialogs--------//
//-------------------------//
import { inject, Injectable } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { take } from 'rxjs';

import type { TerminalSession } from './terminal-session.service';

export type TerminalDialogResult = 'reauthenticate' | undefined;

@Injectable({ providedIn: 'root' })
export class TerminalLauncherService {
  private readonly dialog = inject(MatDialog);
  private active = false;
  private restoreTerminal: (() => void) | null = null;

  open(): void {
    if (this.restoreTerminal) {
      this.restoreTerminal();
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

            this.openTerminal(session);
          });
      })
      .catch(() => {
        this.active = false;
      });
  }

  private openTerminal(session: TerminalSession): void {
    void import('./terminal-page.component')
      .then(({ TerminalDialogComponent }) => {
        const reference = this.dialog.open(TerminalDialogComponent, {
          ariaLabel: 'AmseokNas Terminal',
          autoFocus: false,
          data: session,
          hasBackdrop: false,
          height: 'min(760px, calc(100vh - 24px))',
          maxHeight: 'none',
          maxWidth: 'none',
          panelClass: 'terminal-dialog-panel',
          restoreFocus: true,
          width: 'min(1120px, calc(100vw - 24px))'
        });
        this.restoreTerminal = () => reference.componentInstance.restore();
        reference
          .afterClosed()
          .pipe(take(1))
          .subscribe((result: TerminalDialogResult) => {
            this.restoreTerminal = null;
            this.active = false;
            if (result === 'reauthenticate') {
              this.open();
            }
          });
      })
      .catch(() => {
        this.restoreTerminal = null;
        this.active = false;
      });
  }
}
