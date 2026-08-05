import { TestBed } from '@angular/core/testing';

import { AppStorePageComponent } from './app-store-page.component';

describe('AppStorePageComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppStorePageComponent]
    }).compileComponents();
  });

  it('filters apps and makes the unavailable installation boundary explicit', () => {
    const fixture = TestBed.createComponent(AppStorePageComponent);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Photo Library');
    expect(compiled.querySelectorAll('.app-store-card')).toHaveLength(4);

    const workButton = [...compiled.querySelectorAll('nav button')]
      .find((button) => button.textContent?.includes('工作')) as HTMLButtonElement;
    workButton.click();
    fixture.detectChanges();
    expect(compiled.querySelectorAll('.app-store-card')).toHaveLength(1);
    expect(compiled.textContent).toContain('Studio Sync');

    const action = compiled.querySelector<HTMLButtonElement>('.app-store-card button');
    action?.click();
    fixture.detectChanges();
    expect(compiled.textContent).toContain('应用安装将在完成签名、权限和审计边界后开放。');
  });

  it('shows an empty state for unmatched searches and can clear the filters', () => {
    const fixture = TestBed.createComponent(AppStorePageComponent);
    fixture.detectChanges();

    fixture.componentInstance.setSearch('no-match');
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('没有找到匹配的应用。');

    const reset = [...compiled.querySelectorAll('button')]
      .find((button) => button.textContent?.includes('清除筛选')) as HTMLButtonElement;
    reset.click();
    fixture.detectChanges();
    expect(compiled.querySelectorAll('.app-store-card')).toHaveLength(4);
  });

  it('switches to the subscription and download centers without exposing installation', () => {
    const fixture = TestBed.createComponent(AppStorePageComponent);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;

    const subscription = [...compiled.querySelectorAll('nav button')]
      .find((button) => button.textContent?.includes('产品订阅')) as HTMLButtonElement;
    subscription.click();
    fixture.detectChanges();
    expect(compiled.textContent).toContain('产品续费订阅中心');

    const downloads = [...compiled.querySelectorAll('nav button')]
      .find((button) => button.textContent?.includes('更新与下载')) as HTMLButtonElement;
    downloads.click();
    fixture.detectChanges();
    const demoDownload = compiled.querySelector<HTMLAnchorElement>('.download-center__download');
    expect(demoDownload?.download).toBe('studio-sync-demo.txt');
    expect(demoDownload?.getAttribute('href')).toBe('/downloads/studio-sync-demo.txt');
  });
});
