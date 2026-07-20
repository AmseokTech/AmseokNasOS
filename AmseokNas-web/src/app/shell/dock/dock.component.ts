//--------------------------//
//--------渲染桌面应用入口并报告当前选择---------//
//--------Renders desktop app entries and reports the current selection--------//
//-------------------------//
import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

import { DesktopApp } from '../desktop/desktop-app.model';

@Component({
  selector: 'app-dock',
  templateUrl: './dock.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './dock.component.scss'
})
export class DockComponent {
  readonly apps = input.required<readonly DesktopApp[]>();
  readonly activeAppId = input.required<string>();
  readonly appSelected = output<DesktopApp>();
}
