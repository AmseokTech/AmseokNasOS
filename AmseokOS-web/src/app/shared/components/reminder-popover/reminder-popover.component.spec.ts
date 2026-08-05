import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { ReminderPopoverComponent } from './reminder-popover.component';

@Component({
  imports: [ReminderPopoverComponent],
  template: `
    <app-reminder-popover title="测试提醒" [dismissible]="true" (dismissed)="dismissed = true">
      <p>任意提醒内容</p>
      <button reminder-actions type="button">执行操作</button>
    </app-reminder-popover>
  `
})
class ReminderPopoverTestHostComponent {
  dismissed = false;
}

describe('ReminderPopoverComponent', () => {
  it('should project content and emit dismissal', () => {
    const fixture = TestBed.createComponent(ReminderPopoverTestHostComponent);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('任意提醒内容');
    expect(compiled.textContent).toContain('执行操作');

    compiled.querySelector<HTMLButtonElement>('.dismiss-button')?.click();
    expect(fixture.componentInstance.dismissed).toBe(true);
  });
});
