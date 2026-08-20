import { TestBed } from '@angular/core/testing';

import { WindowFrameComponent } from './window-frame.component';

describe('WindowFrameComponent', () => {
  it('should emit minimize, maximize, and close window commands', () => {
    const fixture = TestBed.createComponent(WindowFrameComponent);
    fixture.componentRef.setInput('title', '测试窗口');
    fixture.detectChanges();
    let minimized = 0;
    let maximizeToggled = 0;
    let closed = 0;
    fixture.componentInstance.minimized.subscribe(() => (minimized += 1));
    fixture.componentInstance.maximizeToggled.subscribe(() => (maximizeToggled += 1));
    fixture.componentInstance.closed.subscribe(() => (closed += 1));

    const compiled = fixture.nativeElement as HTMLElement;
    const controls = [
      ...compiled.querySelectorAll<HTMLButtonElement>('.window-frame__control')
    ];
    expect(controls.map((control) => control.getAttribute('aria-label'))).toEqual([
      '最小化窗口',
      '最大化窗口',
      '关闭窗口'
    ]);
    controls.forEach((control) => control.click());

    expect(minimized).toBe(1);
    expect(maximizeToggled).toBe(1);
    expect(closed).toBe(1);
  });

  it('should hide the optional status indicator', () => {
    const fixture = TestBed.createComponent(WindowFrameComponent);
    fixture.componentRef.setInput('title', 'terminal');
    fixture.componentRef.setInput('showStatus', false);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.window-frame__status')).toBeNull();
    expect(compiled.querySelector('.window-frame__identity')?.textContent?.trim()).toBe('terminal');
  });
});
