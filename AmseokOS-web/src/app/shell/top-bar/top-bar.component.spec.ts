import { TestBed } from '@angular/core/testing';
import { DateAdapter } from '@angular/material/core';

import { TopBarComponent } from './top-bar.component';

describe('TopBarComponent', () => {
  beforeEach(() => {
    window.localStorage.clear();
    document.documentElement.removeAttribute('data-theme');
    document.documentElement.style.removeProperty('color-scheme');
  });

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

  it('opens the Wi-Fi panel, selects a network, and keeps system panels exclusive', () => {
    const fixture = TestBed.createComponent(TopBarComponent);
    fixture.componentRef.setInput('activeApp', '概览');
    fixture.detectChanges();

    fixture.componentInstance.toggleWifiPanel();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const trigger = compiled.querySelector<HTMLButtonElement>('.network-status');
    expect(compiled.querySelector('.wifi-panel')).not.toBeNull();
    expect(trigger?.getAttribute('aria-expanded')).toBe('true');

    fixture.componentInstance.selectWifiNetwork('office');
    fixture.detectChanges();
    expect(compiled.querySelector('.wifi-network--selected')?.textContent).toContain('Amseok-Office');

    fixture.componentInstance.toggleNotificationCenter();
    fixture.detectChanges();
    expect(compiled.querySelector('.wifi-panel')).toBeNull();
    expect(compiled.querySelector('.notification-center')).not.toBeNull();
  });

  it('disables network selection when Wi-Fi is switched off', () => {
    const fixture = TestBed.createComponent(TopBarComponent);
    fixture.componentRef.setInput('activeApp', '概览');
    fixture.componentInstance.toggleWifiPanel();
    fixture.componentInstance.setWifiEnabled(false);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.wifi-panel__networks--disabled')).not.toBeNull();
    expect(compiled.querySelector<HTMLButtonElement>('.wifi-network')?.disabled).toBe(true);
    expect(fixture.componentInstance.selectedWifiId()).toBe('');
  });

  it('opens the control center and keeps it exclusive with the existing system panels', () => {
    const fixture = TestBed.createComponent(TopBarComponent);
    fixture.componentRef.setInput('activeApp', '概览');
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    compiled.querySelector<HTMLButtonElement>('.control-center-trigger')?.click();
    fixture.detectChanges();

    expect(compiled.querySelector('.control-center')).not.toBeNull();
    expect(compiled.querySelector('.control-center-trigger')?.getAttribute('aria-expanded')).toBe('true');

    fixture.componentInstance.toggleWifiPanel();
    fixture.detectChanges();

    expect(compiled.querySelector('.control-center')).toBeNull();
    expect(compiled.querySelector('.wifi-panel')).not.toBeNull();
  });

  it('switches the shared system panel theme and persists the choice', () => {
    const fixture = TestBed.createComponent(TopBarComponent);
    fixture.componentRef.setInput('activeApp', '概览');
    fixture.componentInstance.toggleControlCenter();
    fixture.detectChanges();

    const themeControl = (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      '.setting-control--theme'
    );
    expect(themeControl?.getAttribute('aria-pressed')).toBe('true');
    expect(document.documentElement.dataset['theme']).toBe('dark');

    themeControl?.click();
    fixture.detectChanges();

    expect(themeControl?.getAttribute('aria-pressed')).toBe('false');
    expect(document.documentElement.dataset['theme']).toBe('light');
    expect(window.localStorage.getItem('amseokos-color-theme')).toBe('light');
  });

  it('keeps the reduced control set functional and editable', () => {
    const fixture = TestBed.createComponent(TopBarComponent);
    fixture.componentRef.setInput('activeApp', '概览');
    fixture.componentInstance.toggleControlCenter();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const sliders = compiled.querySelectorAll<HTMLInputElement>('input[type="range"]');
    sliders[0].value = '42';
    sliders[0].dispatchEvent(new Event('input'));
    sliders[1].value = '16';
    sliders[1].dispatchEvent(new Event('input'));
    compiled.querySelectorAll<HTMLButtonElement>('.quick-control')[1].click();
    compiled.querySelector<HTMLButtonElement>('.low-power-control')?.click();
    fixture.detectChanges();

    expect(sliders[0].getAttribute('aria-valuetext')).toBe('42%');
    expect(sliders[1].getAttribute('aria-valuetext')).toBe('16%');
    expect(compiled.querySelectorAll<HTMLButtonElement>('.quick-control')[1].getAttribute('aria-pressed')).toBe(
      'false'
    );
    expect(compiled.querySelectorAll('.quick-control').length).toBe(2);
    expect(compiled.querySelector('.network-speed-control')?.textContent).toMatch(/下载.*MB\/s.*上传.*MB\/s/s);
    expect(compiled.querySelector('.low-power-control')?.getAttribute('aria-pressed')).toBe('true');
    expect(compiled.querySelector('.control-center')?.textContent).not.toMatch(
      /隔空投送|专注模式|台前调度|屏幕镜像|夜览/
    );

    compiled.querySelector<HTMLButtonElement>('.edit-controls')?.click();
    fixture.detectChanges();
    expect(compiled.querySelector('.edit-actions')).not.toBeNull();
    expect(compiled.querySelector('input[type="checkbox"]')).toBeNull();

    compiled.querySelector<HTMLButtonElement>('[aria-label="移除显示"]')?.click();
    fixture.detectChanges();
    expect(compiled.querySelector('[aria-labelledby="display-controls-title"]')).toBeNull();

    compiled.querySelector<HTMLButtonElement>('.add-controls')?.click();
    fixture.detectChanges();
    const restoreDisplay = Array.from(
      compiled.querySelectorAll<HTMLButtonElement>('.control-picker button')
    ).find((button) => button.textContent?.includes('显示'));
    restoreDisplay?.click();
    fixture.detectChanges();

    expect(compiled.querySelector('[aria-labelledby="display-controls-title"]')).not.toBeNull();
    expect(compiled.querySelector('.control-picker')).toBeNull();
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
