import { TestBed } from '@angular/core/testing';

import { AppStoreDownloadCenterComponent } from './app-store-download-center.component';

describe('AppStoreDownloadCenterComponent', () => {
  afterEach(() => vi.useRealTimers());

  it('animates the demo package download and asks the browser to save the static file locally', () => {
    vi.useFakeTimers();
    const downloadClick = vi
      .spyOn(HTMLAnchorElement.prototype, 'click')
      .mockImplementation(() => undefined);
    const fixture = TestBed.createComponent(AppStoreDownloadCenterComponent);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const downloadButton = compiled.querySelector<HTMLButtonElement>('.download-center__download');
    downloadButton?.click();
    fixture.detectChanges();

    expect(downloadButton?.disabled).toBe(true);
    expect(compiled.querySelector('[role="progressbar"]')?.getAttribute('aria-valuenow')).toBe('0');

    vi.advanceTimersByTime(1000);
    fixture.detectChanges();

    expect(downloadClick).toHaveBeenCalledOnce();
    expect(compiled.textContent).toContain('将保存到浏览器默认下载目录。');
  });
});
