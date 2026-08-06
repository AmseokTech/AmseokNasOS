import { TestBed } from '@angular/core/testing';

import { AppStorePageComponent } from './app-store-page.component';

describe('AppStorePageComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppStorePageComponent]
    }).compileComponents();
  });

  it('filters apps and opens an image-backed application detail view', () => {
    const fixture = TestBed.createComponent(AppStorePageComponent);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Photo Library');
    expect(compiled.querySelectorAll('.app-store-card')).toHaveLength(4);
    expect(compiled.querySelectorAll('.app-store-card__art img')).toHaveLength(4);

    const workButton = [...compiled.querySelectorAll('nav button')]
      .find((button) => button.textContent?.includes('工作')) as HTMLButtonElement;
    workButton.click();
    fixture.detectChanges();
    expect(compiled.querySelectorAll('.app-store-card')).toHaveLength(1);
    expect(compiled.textContent).toContain('Studio Sync');

    const action = compiled.querySelector<HTMLButtonElement>('.app-store-card button');
    action?.click();
    fixture.detectChanges();
    expect(compiled.textContent).toContain('应用概览');
    expect(compiled.textContent).toContain('安装功能暂未开放');
    expect(compiled.querySelector<HTMLImageElement>('.app-store-detail__hero > img')?.src)
      .toContain('/assets/app-store/studio-sync-card.png');

    const back = [...compiled.querySelectorAll('button')]
      .find((button) => button.textContent?.includes('返回探索')) as HTMLButtonElement;
    back.click();
    fixture.detectChanges();
    expect(compiled.querySelectorAll('.app-store-card')).toHaveLength(1);
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

  it('switches to the preview, subscription, and download centers without exposing installation', () => {
    const fixture = TestBed.createComponent(AppStorePageComponent);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;

    const preview = [...compiled.querySelectorAll('nav button')]
      .find((button) => button.textContent?.includes('应用尝鲜')) as HTMLButtonElement;
    preview.click();
    fixture.detectChanges();
    expect(compiled.textContent).toContain('Studio Sync Preview');

    const joinPreview = [...compiled.querySelectorAll('.preview-center__app button')]
      .find((button) => button.textContent?.includes('立即尝鲜')) as HTMLButtonElement;
    joinPreview.click();
    fixture.detectChanges();
    expect(compiled.textContent).toContain('该状态仅保留在当前应用商店窗口，不会安装应用。');

    const subscription = [...compiled.querySelectorAll('nav button')]
      .find((button) => button.textContent?.includes('产品订阅')) as HTMLButtonElement;
    subscription.click();
    fixture.detectChanges();
    expect(compiled.textContent).toContain('产品续费订阅中心');

    const downloads = [...compiled.querySelectorAll('nav button')]
      .find((button) => button.textContent?.includes('更新与下载')) as HTMLButtonElement;
    downloads.click();
    fixture.detectChanges();
    const demoDownload = compiled.querySelector<HTMLButtonElement>('.download-center__download');
    expect(demoDownload?.textContent).toContain('下载演示包');
    expect(demoDownload?.disabled).toBe(false);
  });
});
