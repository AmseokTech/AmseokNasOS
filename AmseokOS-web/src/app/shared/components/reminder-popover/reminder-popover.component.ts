//--------------------------//
//--------提供可投影正文与操作区的通用提醒弹窗---------//
//--------Provides a reusable reminder popover with projected content and actions--------//
//-------------------------//
import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

import { TranslatePipe } from '../../../core/i18n';

export type ReminderTone = 'info' | 'warning';

@Component({
  selector: 'app-reminder-popover',
  imports: [TranslatePipe],
  templateUrl: './reminder-popover.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './reminder-popover.component.scss'
})
export class ReminderPopoverComponent {
  readonly title = input.required<string>();
  readonly tone = input<ReminderTone>('info');
  readonly dismissible = input(false);
  readonly dismissed = output<void>();
}
