import { TestBed } from '@angular/core/testing';

import {
  LANGUAGE_SELECTION_STORAGE_KEY,
  LANGUAGE_STORAGE_KEY,
  LanguageService
} from '../../../core/i18n';
import { WindowFrameComponent } from './window-frame.component';

describe('WindowFrameComponent', () => {
  beforeEach(() => {
    window.localStorage.removeItem(LANGUAGE_STORAGE_KEY);
    window.localStorage.removeItem(LANGUAGE_SELECTION_STORAGE_KEY);
  });

  afterEach(() => {
    window.localStorage.removeItem(LANGUAGE_STORAGE_KEY);
    window.localStorage.removeItem(LANGUAGE_SELECTION_STORAGE_KEY);
  });

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
    compiled.querySelector<HTMLButtonElement>('[aria-label="最小化窗口"]')?.click();
    compiled.querySelector<HTMLButtonElement>('[aria-label="最大化窗口"]')?.click();
    compiled.querySelector<HTMLButtonElement>('[aria-label="关闭窗口"]')?.click();

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

  it('updates a translated application title when the interface language changes', () => {
    const fixture = TestBed.createComponent(WindowFrameComponent);
    fixture.componentRef.setInput('title', '终端');
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.window-frame__identity')?.textContent).toContain('终端');

    TestBed.inject(LanguageService).setLanguage('en-US');
    TestBed.flushEffects();
    fixture.detectChanges();

    expect(compiled.querySelector('.window-frame__identity')?.textContent).toContain('Terminal');
  });
});
