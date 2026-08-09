//--------------------------//
//--------提供内置应用商店的只读探索界面---------//
//--------Provides the read-only built-in app store discovery view--------//
//-------------------------//
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';

import { LanguageService, TranslatePipe } from '../../core/i18n';
import { AppStoreDownloadCenterComponent } from './app-store-download-center.component';
import { AppStorePreviewCenterComponent } from './app-store-preview-center.component';
import { AppStoreSubscriptionCenterComponent } from './app-store-subscription-center.component';

type AppStoreCategoryId = 'explore' | 'create' | 'work' | 'tools' | 'development';
type AppStoreView = 'catalog' | 'detail' | 'preview' | 'subscription' | 'downloads';

interface AppStoreCategory {
  readonly id: AppStoreCategoryId;
  readonly label: string;
  readonly iconPath: string;
}

interface StoreApp {
  readonly id: string;
  readonly name: string;
  readonly category: AppStoreCategoryId;
  readonly eyebrow: string;
  readonly description: string;
  readonly overview: string;
  readonly features: readonly string[];
  readonly imagePath: string;
}

const CATEGORIES: readonly AppStoreCategory[] = [
  {
    id: 'explore',
    label: 'appStore.category.explore',
    iconPath: 'm12 2 2.2 6.3L21 10l-5.3 4.2 1.7 6.8-5.4-3.5-5.4 3.5 1.7-6.8L3 10l6.8-1.7L12 2Z'
  },
  {
    id: 'create',
    label: 'appStore.category.create',
    iconPath: 'm14.7 3.3 6 6-2.1 2.1-6-6 2.1-2.1ZM4 15.9l7.2-7.2 4.1 4.1L8.1 20H4v-4.1Z'
  },
  {
    id: 'work',
    label: 'appStore.category.work',
    iconPath: 'M4 5h16v14H4V5Zm2 2v10h12V7H6Zm2 2h8v2H8V9Zm0 4h5v2H8v-2Z'
  },
  {
    id: 'tools',
    label: 'appStore.category.tools',
    iconPath: 'm14.7 6.7 2.6-2.6 2.6 2.6-2.6 2.6-2.6-2.6ZM3 20.1l7.4-7.4 2.6 2.6-7.4 7.4H3v-2.5Zm9.9-11.7 2.6-2.6 2.6 2.6-2.6 2.6-2.6-2.6Z'
  },
  {
    id: 'development',
    label: 'appStore.category.development',
    iconPath: 'm8.2 7.1 1.4 1.4-3.5 3.5 3.5 3.5-1.4 1.4L3.3 12l4.9-4.9Zm7.6 0 4.9 4.9-4.9 4.9-1.4-1.4 3.5-3.5-3.5-3.5 1.4-1.4Z'
  }
];

const STORE_APPS: readonly StoreApp[] = [
  {
    id: 'photo-library',
    name: 'Photo Library',
    category: 'create',
    eyebrow: 'appStore.app.photo.eyebrow',
    description: 'appStore.app.photo.description',
    overview: 'appStore.app.photo.overview',
    features: [
      'appStore.app.photo.feature1',
      'appStore.app.photo.feature2',
      'appStore.app.photo.feature3'
    ],
    imagePath: '/assets/app-store/photo-library-card.jpg'
  },
  {
    id: 'studio-sync',
    name: 'Studio Sync',
    category: 'work',
    eyebrow: 'appStore.app.studio.eyebrow',
    description: 'appStore.app.studio.description',
    overview: 'appStore.app.studio.overview',
    features: [
      'appStore.app.studio.feature1',
      'appStore.app.studio.feature2',
      'appStore.app.studio.feature3'
    ],
    imagePath: '/assets/app-store/studio-sync-card.jpg'
  },
  {
    id: 'screen-cast',
    name: 'Screen Cast',
    category: 'tools',
    eyebrow: 'appStore.app.cast.eyebrow',
    description: 'appStore.app.cast.description',
    overview: 'appStore.app.cast.overview',
    features: [
      'appStore.app.cast.feature1',
      'appStore.app.cast.feature2',
      'appStore.app.cast.feature3'
    ],
    imagePath: '/assets/app-store/screen-cast-card.jpg'
  },
  {
    id: 'backup-vault',
    name: 'Backup Vault',
    category: 'development',
    eyebrow: 'appStore.app.backup.eyebrow',
    description: 'appStore.app.backup.description',
    overview: 'appStore.app.backup.overview',
    features: [
      'appStore.app.backup.feature1',
      'appStore.app.backup.feature2',
      'appStore.app.backup.feature3'
    ],
    imagePath: '/assets/app-store/backup-vault-card.jpg'
  }
];

@Component({
  selector: 'app-app-store-page',
  imports: [
    AppStoreDownloadCenterComponent,
    AppStorePreviewCenterComponent,
    AppStoreSubscriptionCenterComponent,
    TranslatePipe
  ],
  templateUrl: './app-store-page.component.html',
  styleUrl: './app-store-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AppStorePageComponent {
  private readonly languageService = inject(LanguageService);

  readonly categories = CATEGORIES;
  readonly activeView = signal<AppStoreView>('catalog');
  readonly activeCategory = signal<AppStoreCategoryId>('explore');
  readonly searchTerm = signal('');
  readonly selectedApp = signal<StoreApp | null>(null);
  readonly studioSyncPreviewJoined = signal(false);
  readonly visibleApps = computed(() => {
    const term = this.searchTerm().trim().toLocaleLowerCase();
    const category = this.activeCategory();
    return STORE_APPS.filter((app) => {
      const inCategory = category === 'explore' || app.category === category;
      const inSearch = !term || [
        app.name,
        this.languageService.translate(app.eyebrow),
        this.languageService.translate(app.description)
      ]
        .join(' ')
        .toLocaleLowerCase()
        .includes(term);
      return inCategory && inSearch;
    });
  });

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
