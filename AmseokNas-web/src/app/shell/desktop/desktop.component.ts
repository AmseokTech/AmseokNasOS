//--------------------------//
//--------组合桌面背景、顶部任务栏与应用 Dock---------//
//--------Composes the desktop background, top bar, and application Dock--------//
//-------------------------//
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { Router, RouterLink } from '@angular/router';

import { AuthenticationService } from '../../core/auth/authentication.service';
import { ReminderPopoverComponent } from '../../shared/components/reminder-popover/reminder-popover.component';
import { DockComponent } from '../dock/dock.component';
import { TopBarComponent } from '../top-bar/top-bar.component';
import { DESKTOP_APPS, DesktopApp } from './desktop-app.model';

@Component({
  selector: 'app-desktop',
  imports: [DockComponent, MatButtonModule, ReminderPopoverComponent, RouterLink, TopBarComponent],
  templateUrl: './desktop.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './desktop.component.scss'
})
export class DesktopComponent implements OnInit {
  private readonly authentication = inject(AuthenticationService);
  private readonly router = inject(Router);

  readonly apps = DESKTOP_APPS;
  readonly activeApp = signal<DesktopApp>(DESKTOP_APPS[0]);
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
    this.activeApp.set(app);
  }
}
