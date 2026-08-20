//--------------------------//
//--------展示已安装桌面组件并报告启动选择---------//
//--------Presents installed desktop components and reports launch selections--------//
//-------------------------//
import { A11yModule } from '@angular/cdk/a11y';
import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

import { DesktopApp } from '../desktop/desktop-app.model';

@Component({
  selector: 'app-launcher',
  imports: [A11yModule],
  templateUrl: './app-launcher.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './app-launcher.component.scss',
  host: {
    '(document:keydown.escape)': 'dismissed.emit()'
  }
})
export class AppLauncherComponent {
  readonly apps = input.required<readonly DesktopApp[]>();
  readonly appSelected = output<DesktopApp>();
  readonly dismissed = output<void>();

  launch(app: DesktopApp): void {
    this.appSelected.emit(app);
  }
}
