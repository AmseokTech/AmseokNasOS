import { TestBed } from '@angular/core/testing';

import {
  LANGUAGE_SELECTION_STORAGE_KEY,
  LANGUAGE_STORAGE_KEY,
  LanguageService
} from '../../core/i18n';
import { AppStorePageComponent } from './app-store-page.component';

describe('AppStorePageComponent', () => {
  beforeEach(async () => {
    window.localStorage.removeItem(LANGUAGE_STORAGE_KEY);
    window.localStorage.removeItem(LANGUAGE_SELECTION_STORAGE_KEY);
    await TestBed.configureTestingModule({
      imports: [AppStorePageComponent]
    }).compileComponents();
  });

  afterEach(() => {
    window.localStorage.removeItem(LANGUAGE_STORAGE_KEY);
    window.localStorage.removeItem(LANGUAGE_SELECTION_STORAGE_KEY);
  });

  it('updates catalog copy and search matching immediately in English', () => {
    const fixture = TestBed.createComponent(AppStorePageComponent);
    fixture.detectChanges();

    TestBed.inject(LanguageService).setLanguage('en-US');
    TestBed.flushEffects();
    fixture.detectChanges();
    fixture.componentInstance.setSearch('collaboration');
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector<HTMLInputElement>('input[type="search"]')?.placeholder)
      .toBe('Search apps');
    expect(compiled.textContent).toContain('Team Collaboration');
    expect(compiled.querySelectorAll('.app-store-card')).toHaveLength(1);
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
    expect(compiled.querySelector<HTMLImageElement>('.app-store-detail__art > img')?.src)
      .toContain('/assets/app-store/studio-sync-card.jpg');

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

  it('keeps search available in service views and returns to matching catalog results', () => {
    const fixture = TestBed.createComponent(AppStorePageComponent);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;

    const serviceViews = [
      { label: '应用尝鲜', content: 'Studio Sync Preview' },
      { label: '产品订阅', content: '产品续费订阅中心' },
      { label: '更新与下载', content: '更新与下载中心' }
    ] as const;

    for (const view of serviceViews) {
      const button = [...compiled.querySelectorAll<HTMLButtonElement>('.app-store__nav button')]
        .find((candidate) => candidate.textContent?.includes(view.label));
      expect(button).toBeDefined();
      button!.click();
      fixture.detectChanges();

      expect(compiled.textContent).toContain(view.content);
      expect(compiled.querySelector<HTMLInputElement>('.app-store__search input')).not.toBeNull();
    }

    const search = compiled.querySelector<HTMLInputElement>('.app-store__search input')!;
    search.value = 'Photo';
    search.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(fixture.componentInstance.activeView()).toBe('catalog');
    expect(compiled.querySelectorAll('.app-store-card')).toHaveLength(1);
    expect(compiled.textContent).toContain('Photo Library');
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

    preview.click();
    fixture.detectChanges();
    expect(compiled.textContent).toContain('已加入体验');
    expect(compiled.querySelector<HTMLButtonElement>('.preview-center__app button')?.disabled).toBe(true);

    const downloads = [...compiled.querySelectorAll('nav button')]
      .find((button) => button.textContent?.includes('更新与下载')) as HTMLButtonElement;
    downloads.click();
    fixture.detectChanges();
    const demoDownload = compiled.querySelector<HTMLButtonElement>('.download-center__download');
    expect(demoDownload?.textContent).toContain('下载演示包');
    expect(demoDownload?.disabled).toBe(false);
  });
});
