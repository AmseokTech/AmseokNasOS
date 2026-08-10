import { TestBed } from '@angular/core/testing';
import { DateAdapter } from '@angular/material/core';

import { TopBarComponent } from './top-bar.component';

describe('TopBarComponent', () => {
  it('opens the local notification center and marks every notification as read', () => {
    const fixture = TestBed.createComponent(TopBarComponent);
    fixture.componentRef.setInput('activeApp', '概览');
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const trigger = compiled.querySelector<HTMLButtonElement>('.notification-trigger');

    trigger?.click();
    fixture.detectChanges();

    expect(compiled.querySelector('.notification-center')).not.toBeNull();
    expect(trigger?.getAttribute('aria-expanded')).toBe('true');
    expect(compiled.querySelector('.notification-center__header span')?.textContent).toContain('2 条未读');

    const markAllButton = Array.from(compiled.querySelectorAll<HTMLButtonElement>('button')).find(
      (button) => button.textContent?.includes('全部标为已读')
    );
    markAllButton?.click();
    fixture.detectChanges();

    expect(compiled.querySelector('.notification-center__header span')?.textContent).toContain('全部已读');
    expect(compiled.querySelector('.notification-badge')).toBeNull();
  });

  it('removes a notification without closing the center', () => {
    const fixture = TestBed.createComponent(TopBarComponent);
    fixture.componentRef.setInput('activeApp', '概览');
    fixture.detectChanges();

    fixture.componentInstance.toggleNotificationCenter();
    fixture.detectChanges();

    fixture.componentInstance.dismissNotification('app-store-demo');
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.notification-center')).not.toBeNull();
    expect(compiled.textContent).not.toContain('应用商店演示已准备就绪');
  });

  it('closes the notification center when the user clicks outside the top bar', () => {
    const fixture = TestBed.createComponent(TopBarComponent);
    fixture.componentRef.setInput('activeApp', '概览');
    fixture.detectChanges();

    fixture.componentInstance.toggleNotificationCenter();
    fixture.detectChanges();
    document.body.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.notification-center')).toBeNull();
  });

  it('opens the official Angular calendar and keeps it exclusive with notifications', () => {
    const fixture = TestBed.createComponent(TopBarComponent);
    fixture.componentRef.setInput('activeApp', '概览');
    fixture.detectChanges();

    fixture.componentInstance.toggleNotificationCenter();
    fixture.componentInstance.toggleDateTimePanel();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const trigger = compiled.querySelector<HTMLButtonElement>('.date-time-trigger');
    expect(compiled.querySelector('mat-calendar')).not.toBeNull();
    expect(compiled.querySelector('.notification-center')).toBeNull();
    expect(trigger?.getAttribute('aria-expanded')).toBe('true');

    fixture.componentInstance.toggleNotificationCenter();
    fixture.detectChanges();

    expect(compiled.querySelector('.date-time-panel')).toBeNull();
    expect(compiled.querySelector('.notification-center')).not.toBeNull();
  });

  it('updates the selected date shown below the calendar', () => {
    const fixture = TestBed.createComponent(TopBarComponent);
    fixture.componentRef.setInput('activeApp', '概览');
    fixture.detectChanges();

    fixture.componentInstance.toggleDateTimePanel();
    fixture.componentInstance.selectDate(new Date(2026, 7, 9));
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.date-time-panel__footer strong')?.textContent).toContain('2026年8月9日');
  });

  it('shows numeric day labels without the Chinese day suffix', () => {
    const fixture = TestBed.createComponent(TopBarComponent);
    fixture.componentRef.setInput('activeApp', '概览');
    fixture.detectChanges();

    const dateAdapter = fixture.debugElement.injector.get(DateAdapter<Date>);
    expect(dateAdapter.getDateNames()[19]).toBe('20');
    expect(dateAdapter.getDateNames()[20]).toBe('21');

    fixture.componentInstance.toggleDateTimePanel();
    fixture.detectChanges();

    const renderedDayLabels = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.mat-calendar-body-cell-content')
    ).map((cell) => cell.textContent?.trim());
    expect(renderedDayLabels).toContain('20');
    expect(renderedDayLabels).toContain('21');
    expect(renderedDayLabels.some((label) => label?.endsWith('日'))).toBe(false);
  });

  it('closes the date and time panel on outside click or Escape', () => {
    const fixture = TestBed.createComponent(TopBarComponent);
    fixture.componentRef.setInput('activeApp', '概览');
    fixture.detectChanges();

    fixture.componentInstance.toggleDateTimePanel();
    fixture.detectChanges();
    document.body.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('.date-time-panel')).toBeNull();

    fixture.componentInstance.toggleDateTimePanel();
    fixture.detectChanges();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('.date-time-panel')).toBeNull();
  });
});
