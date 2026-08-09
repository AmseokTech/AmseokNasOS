import { TestBed } from '@angular/core/testing';

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
});
