//--------------------------//
//--------渲染桌面应用入口并报告当前选择---------//
//--------Renders desktop app entries and reports the current selection--------//
//-------------------------//
import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';

import { DesktopApp } from '../desktop/desktop-app.model';
import { WindowManagerService } from '../window-manager/window-manager.service';
import type { AppWindowState } from '../window-manager/window-state.model';

@Component({
  selector: 'app-dock',
  templateUrl: './dock.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './dock.component.scss'
})
export class DockComponent {
  private readonly windowManager = inject(WindowManagerService);

  readonly apps = input.required<readonly DesktopApp[]>();
  readonly launcherOpen = input(false);
  readonly selectedAppId = input.required<string>();
  readonly appSelected = output<DesktopApp>();
  readonly launcherToggled = output<void>();

  windowState(appId: string): AppWindowState | undefined {
    return this.windowManager.windowForApp(appId);
  }

  isFocused(windowState: AppWindowState | undefined): boolean {
    return !!windowState && this.windowManager.focusedWindow()?.id === windowState.id;
  }
}
