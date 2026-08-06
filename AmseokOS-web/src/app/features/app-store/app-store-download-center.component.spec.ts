import { TestBed } from '@angular/core/testing';

import { AppStoreDownloadCenterComponent } from './app-store-download-center.component';

describe('AppStoreDownloadCenterComponent', () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('checks the static file before asking the browser to save it locally', async () => {
    vi.useFakeTimers();
    const fetchRequest = vi.fn().mockResolvedValue(new Response('demo package', { status: 200 }));
    const createObjectUrl = vi.fn().mockReturnValue('blob:studio-sync-demo');
    const revokeObjectUrl = vi.fn();
    vi.stubGlobal('fetch', fetchRequest);
    vi.stubGlobal('URL', { createObjectURL: createObjectUrl, revokeObjectURL: revokeObjectUrl });
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

    await vi.advanceTimersByTimeAsync(1000);
    fixture.detectChanges();

    expect(fetchRequest).toHaveBeenCalledWith('/downloads/studio-sync-demo.txt', { credentials: 'same-origin' });
    expect(downloadClick).toHaveBeenCalledOnce();
    expect(createObjectUrl).toHaveBeenCalledOnce();
    expect(revokeObjectUrl).toHaveBeenCalledWith('blob:studio-sync-demo');
    expect(compiled.textContent).toContain('已开始保存到浏览器默认下载目录。');
  });

  it('reports a failed download when the static file cannot be fetched', async () => {
    vi.useFakeTimers();
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 404 })));
    const downloadClick = vi
      .spyOn(HTMLAnchorElement.prototype, 'click')
      .mockImplementation(() => undefined);
    const fixture = TestBed.createComponent(AppStoreDownloadCenterComponent);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;

    compiled.querySelector<HTMLButtonElement>('.download-center__download')?.click();
    await vi.advanceTimersByTimeAsync(1000);
    fixture.detectChanges();

    expect(downloadClick).not.toHaveBeenCalled();
    expect(compiled.textContent).toContain('演示包下载失败，请稍后重试。');
  });
});
