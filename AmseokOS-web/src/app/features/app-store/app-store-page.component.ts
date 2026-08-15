//--------------------------//
//--------展示由本机 API 校验的远端应用目录---------//
//--------Displays the remote app catalog validated by the local API--------//
//-------------------------//
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, EMPTY, exhaustMap, interval, merge, of, Subject } from 'rxjs';

import type { AppStoreCategoryId, StoreApp } from './app-store-catalog.models';
import { AppStoreCatalogService } from './app-store-catalog.service';
import { AppStoreDownloadCenterComponent } from './app-store-download-center.component';
import { AppStorePreviewCenterComponent } from './app-store-preview-center.component';
import { AppStoreSubscriptionCenterComponent } from './app-store-subscription-center.component';

type AppStoreView = 'catalog' | 'detail' | 'preview' | 'subscription' | 'downloads';

interface AppStoreCategory {
  readonly id: AppStoreCategoryId;
  readonly label: string;
  readonly iconPath: string;
}

const CATEGORIES: readonly AppStoreCategory[] = [
  {
    id: 'explore',
    label: '探索',
    iconPath: 'm12 2 2.2 6.3L21 10l-5.3 4.2 1.7 6.8-5.4-3.5-5.4 3.5 1.7-6.8L3 10l6.8-1.7L12 2Z'
  },
  {
    id: 'create',
    label: '创作',
    iconPath: 'm14.7 3.3 6 6-2.1 2.1-6-6 2.1-2.1ZM4 15.9l7.2-7.2 4.1 4.1L8.1 20H4v-4.1Z'
  },
  {
    id: 'work',
    label: '工作',
    iconPath: 'M4 5h16v14H4V5Zm2 2v10h12V7H6Zm2 2h8v2H8V9Zm0 4h5v2H8v-2Z'
  },
  {
    id: 'tools',
    label: '效率工具',
    iconPath: 'm14.7 6.7 2.6-2.6 2.6 2.6-2.6 2.6-2.6-2.6ZM3 20.1l7.4-7.4 2.6 2.6-7.4 7.4H3v-2.5Zm9.9-11.7 2.6-2.6 2.6 2.6-2.6 2.6-2.6-2.6Z'
  },
  {
    id: 'development',
    label: '开发',
    iconPath: 'm8.2 7.1 1.4 1.4-3.5 3.5 3.5 3.5-1.4 1.4L3.3 12l4.9-4.9Zm7.6 0 4.9 4.9-4.9 4.9-1.4-1.4 3.5-3.5-3.5-3.5 1.4-1.4Z'
  }
];

@Component({
  selector: 'app-app-store-page',
  imports: [
    AppStoreDownloadCenterComponent,
    AppStorePreviewCenterComponent,
    AppStoreSubscriptionCenterComponent
  ],
  templateUrl: './app-store-page.component.html',
  styleUrl: './app-store-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AppStorePageComponent {
  private readonly catalogService = inject(AppStoreCatalogService);
  private readonly refreshRequests = new Subject<void>();

  readonly categories = CATEGORIES;
  readonly apps = signal<readonly StoreApp[]>([]);
  readonly catalogRevision = signal<string | null>(null);
  readonly catalogRefreshedAt = signal<string | null>(null);
  readonly catalogIsStale = signal(false);
  readonly catalogLoading = signal(true);
  readonly catalogError = signal<string | null>(null);
  readonly activeView = signal<AppStoreView>('catalog');
  readonly activeCategory = signal<AppStoreCategoryId>('explore');
  readonly searchTerm = signal('');
  readonly selectedApp = signal<StoreApp | null>(null);
  readonly studioSyncPreviewJoined = signal(false);
  readonly visibleApps = computed(() => {
    const term = this.searchTerm().trim().toLocaleLowerCase();
    const category = this.activeCategory();
    return this.apps().filter((app) => {
      const inCategory = category === 'explore' || app.category === category;
      const inSearch = !term || [app.name, app.eyebrow, app.description]
        .join(' ')
        .toLocaleLowerCase()
        .includes(term);
      return inCategory && inSearch;
    });
  });

  constructor() {
    merge(of(undefined), interval(60_000), this.refreshRequests)
      .pipe(
        exhaustMap(() => {
          if (this.apps().length === 0) {
            this.catalogLoading.set(true);
          }
          this.catalogError.set(null);
          return this.catalogService.getCatalog().pipe(
            catchError((error: unknown) => {
              this.catalogError.set(
                error instanceof Error ? error.message : '应用目录加载失败'
              );
              this.catalogLoading.set(false);
              return EMPTY;
            })
          );
        }),
        takeUntilDestroyed()
      )
      .subscribe((catalog) => {
        this.apps.set(catalog.apps);
        this.catalogRevision.set(catalog.revision);
        this.catalogRefreshedAt.set(catalog.refreshedAt);
        this.catalogIsStale.set(catalog.isStale);
        this.catalogLoading.set(false);

        const selected = this.selectedApp();
        if (selected) {
          const current = catalog.apps.find((app) =>
            app.publisherId === selected.publisherId && app.id === selected.id
          );
          if (current) {
            this.selectedApp.set(current);
          } else {
            this.returnToCatalog();
          }
        }
      });
  }

  refreshCatalog(): void {
    this.refreshRequests.next();
  }

  selectCategory(category: AppStoreCategoryId): void {
    this.activeView.set('catalog');
    this.activeCategory.set(category);
    this.selectedApp.set(null);
  }

  selectView(view: AppStoreView): void {
    this.activeView.set(view);
    if (view !== 'detail') {
      this.selectedApp.set(null);
    }
  }

  setSearch(value: string): void {
    this.searchTerm.set(value);

    if (value.trim()) {
      this.activeView.set('catalog');
      this.activeCategory.set('explore');
      this.selectedApp.set(null);
    }
  }

  openAppDetail(app: StoreApp): void {
    this.selectedApp.set(app);
    this.activeView.set('detail');
  }

  returnToCatalog(): void {
    this.selectedApp.set(null);
    this.activeView.set('catalog');
  }

  joinStudioSyncPreview(): void {
    this.studioSyncPreviewJoined.set(true);
  }
}
