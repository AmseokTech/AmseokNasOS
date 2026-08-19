//--------------------------//
//--------组合桌面背景、顶部任务栏与应用 Dock---------//
//--------Composes the desktop background, top bar, and application Dock--------//
//-------------------------//
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { Router, RouterLink } from '@angular/router';

import { AuthenticationService } from '../../core/auth/authentication.service';
import { TerminalLauncherService } from '../../features/terminal/terminal-launcher.service';
import { ReminderPopoverComponent } from '../../shared/components/reminder-popover/reminder-popover.component';
import { AppLauncherComponent } from '../app-launcher/app-launcher.component';
import { DockComponent } from '../dock/dock.component';
import { TopBarComponent } from '../top-bar/top-bar.component';
import { WindowHostComponent } from '../window-manager/window-host.component';
import { WindowManagerService } from '../window-manager/window-manager.service';
import { DESKTOP_APPS, DesktopApp } from './desktop-app.model';

@Component({
  selector: 'app-desktop',
  imports: [
    AppLauncherComponent,
    DockComponent,
    MatButtonModule,
    ReminderPopoverComponent,
    RouterLink,
    TopBarComponent,
    WindowHostComponent
  ],
  templateUrl: './desktop.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './desktop.component.scss'
})
export class DesktopComponent implements OnInit {
  private readonly authentication = inject(AuthenticationService);
  private readonly router = inject(Router);
  private readonly terminalLauncher = inject(TerminalLauncherService);
  private readonly windowManager = inject(WindowManagerService);

  readonly apps = DESKTOP_APPS;
  readonly launcherOpen = signal(false);
  readonly selectedApp = signal<DesktopApp>(DESKTOP_APPS[0]);
  readonly activeAppLabel = computed(
    () => this.launcherOpen()
      ? '启动台'
      : this.windowManager.focusedWindow()?.title ?? this.selectedApp().label
  );
  readonly session = this.authentication.session;

  ngOnInit(): void {
    if (this.session()) {
      return;
    }

    this.authentication.getSession().subscribe({
      error: () => void this.router.navigate(['/'])
    });
  }

  selectApp(app: DesktopApp): void {
    this.launcherOpen.set(false);

    if (app.kind === 'terminal') {
      if (this.session()?.mustChangePassword) {
        void this.router.navigate(['/change-password']);
        return;
      }

      this.terminalLauncher.open();
      return;
    }

    if (app.kind === 'route') {
      void this.router.navigate([app.route]);
      return;
    }

    this.selectedApp.set(app);
    this.windowManager.open(app.windowAppId);
  }

  toggleLauncher(): void {
    this.launcherOpen.update((open) => !open);
  }

  closeLauncher(): void {
    this.launcherOpen.set(false);
  }
}
