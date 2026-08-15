import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import type { AppCatalogResponse } from './app-store-catalog.models';
import { AppStorePageComponent } from './app-store-page.component';

const catalog: AppCatalogResponse = {
  format: 'amseok-app-catalog-v1',
  revision: '2026.08.15.1',
  generatedAt: '2026-08-15T08:00:00Z',
  refreshedAt: '2026-08-15T08:01:00Z',
  isStale: false,
  apps: [
    {
      publisherId: 'amseok',
      id: 'photo-library',
      name: 'Photo Library',
      category: 'create',
      eyebrow: '媒体管理',
      description: '把家庭影像按时间、人物和相册清晰整理。',
      overview: '集中查看家庭照片和视频。',
      features: ['按时间线浏览', '相册管理'],
      imageUrl: 'https://download.amseok.cn/assets/photo-library-card.jpg'
    },
    {
      publisherId: 'amseok',
      id: 'studio-sync',
      name: 'Studio Sync',
      category: 'work',
      eyebrow: '团队协作',
      description: '让素材、项目文件和成员进度始终保持同步。',
      overview: '为创作素材和项目文件准备统一入口。',
      features: ['汇总协作状态', '保留工作上下文'],
      imageUrl: 'https://download.amseok.cn/assets/studio-sync-card.jpg'
    },
    {
      publisherId: 'amseok',
      id: 'screen-cast',
      name: 'Screen Cast',
      category: 'tools',
      eyebrow: '效率工具',
      description: '在可信设备之间轻松投送演示和媒体内容。',
      overview: '为可信设备之间的内容投送提供入口。',
      features: ['设备投送', '会议演示'],
      imageUrl: 'https://download.amseok.cn/assets/screen-cast-card.jpg'
    },
    {
      publisherId: 'amseok',
      id: 'backup-vault',
      name: 'Backup Vault',
      category: 'development',
      eyebrow: '开发工具',
      description: '为项目快照和构建产物预留统一归档入口。',
      overview: '为构建产物提供可追溯的归档视图。',
      features: ['构建产物归档', '快照管理'],
      imageUrl: 'https://download.amseok.cn/assets/backup-vault-card.jpg'
    }
  ]
};

describe('AppStorePageComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppStorePageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  function createLoadedFixture() {
    const fixture = TestBed.createComponent(AppStorePageComponent);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/app-store/catalog')
      .flush(catalog);
    fixture.detectChanges();
    return fixture;
  }

  it('filters apps and opens an image-backed application detail view', () => {
    const fixture = createLoadedFixture();

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
      .toBe('https://download.amseok.cn/assets/studio-sync-card.jpg');

    const back = [...compiled.querySelectorAll('button')]
      .find((button) => button.textContent?.includes('返回探索')) as HTMLButtonElement;
    back.click();
    fixture.detectChanges();
    expect(compiled.querySelectorAll('.app-store-card')).toHaveLength(1);
  });

  it('shows an empty state for unmatched searches and can clear the filters', () => {
    const fixture = createLoadedFixture();

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
    const fixture = createLoadedFixture();
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
    const fixture = createLoadedFixture();
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

  it('shows a retryable state when no verified catalog is available', () => {
    const fixture = TestBed.createComponent(AppStorePageComponent);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/app-store/catalog').flush(
      { detail: '暂时无法连接远端应用市场，请稍后重试' },
      { status: 503, statusText: 'Service Unavailable' }
    );
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('暂时无法连接远端应用市场，请稍后重试');
    const retry = [...compiled.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.includes('重新加载'));
    retry?.click();
    http.expectOne('/api/app-store/catalog').flush(catalog);
    fixture.detectChanges();

    expect(compiled.textContent).toContain('Photo Library');
  });

  it('labels a catalog served from the NAS fallback cache', () => {
    const fixture = TestBed.createComponent(AppStorePageComponent);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/app-store/catalog')
      .flush({ ...catalog, isStale: true });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent)
      .toContain('当前展示上次成功同步的目录');
  });
});
