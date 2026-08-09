//--------------------------//
//--------提供可复用的桌面窗口标题栏与窗口控制---------//
//--------Provides a reusable desktop window title bar and window controls--------//
//-------------------------//
import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { CdkDragHandle } from '@angular/cdk/drag-drop';

import { TranslatePipe } from '../../../core/i18n';

export type WindowStatusTone = 'neutral' | 'busy' | 'online' | 'error';

@Component({
  selector: 'app-window-frame',
  imports: [CdkDragHandle, TranslatePipe],
  templateUrl: './window-frame.component.html',
  styleUrl: './window-frame.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class WindowFrameComponent {
  readonly title = input.required<string>();
  readonly subtitle = input('');
  readonly maximized = input(false);
  readonly showStatus = input(true);
  readonly statusTone = input<WindowStatusTone>('neutral');

  readonly minimized = output<void>();
  readonly maximizeToggled = output<void>();
  readonly closed = output<void>();

  handleTitleBarDoubleClick(event: MouseEvent): void {
    const target = event.target as HTMLElement;
    if (!target.closest('button')) {
      this.maximizeToggled.emit();
    }
  }
}
