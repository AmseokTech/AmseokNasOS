//--------------------------//
//--------组合桌面背景、顶部任务栏与应用 Dock---------//
//--------Composes the desktop background, top bar, and application Dock--------//
//-------------------------//
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';

import { DockComponent } from '../dock/dock.component';
import { TopBarComponent } from '../top-bar/top-bar.component';
import { DESKTOP_APPS, DesktopApp } from './desktop-app.model';

@Component({
  selector: 'app-desktop',
  imports: [DockComponent, TopBarComponent],
  templateUrl: './desktop.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './desktop.component.scss'
})
export class DesktopComponent {
  readonly apps = DESKTOP_APPS;
  readonly activeApp = signal<DesktopApp>(DESKTOP_APPS[0]);

  selectApp(app: DesktopApp): void {
    this.activeApp.set(app);
  }
}
