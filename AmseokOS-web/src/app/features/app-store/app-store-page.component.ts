//--------------------------//
//--------提供内置应用商店的只读探索界面---------//
//--------Provides the read-only built-in app store discovery view--------//
//-------------------------//
import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';

import { AppStoreDownloadCenterComponent } from './app-store-download-center.component';
import { AppStoreSubscriptionCenterComponent } from './app-store-subscription-center.component';

type AppStoreCategoryId = 'explore' | 'create' | 'work' | 'tools' | 'development';
type AppStoreView = 'catalog' | 'subscription' | 'downloads';

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
  readonly icon: 'photo' | 'sync' | 'monitor' | 'archive';
  readonly accent: 'blue' | 'teal' | 'coral' | 'slate';
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

const STORE_APPS: readonly StoreApp[] = [
  {
    id: 'photo-library',
    name: 'Photo Library',
    category: 'create',
    eyebrow: '媒体管理',
    description: '把家庭影像按时间、人物和相册清晰整理。',
    icon: 'photo',
    accent: 'coral'
  },
  {
    id: 'studio-sync',
    name: 'Studio Sync',
    category: 'work',
    eyebrow: '团队协作',
    description: '让素材、项目文件和成员进度始终保持同步。',
    icon: 'sync',
    accent: 'blue'
  },
  {
    id: 'screen-cast',
    name: 'Screen Cast',
    category: 'tools',
    eyebrow: '效率工具',
    description: '在可信设备之间轻松投送演示和媒体内容。',
    icon: 'monitor',
    accent: 'teal'
  },
  {
    id: 'backup-vault',
    name: 'Backup Vault',
    category: 'development',
    eyebrow: '开发工具',
    description: '为项目快照和构建产物预留统一归档入口。',
    icon: 'archive',
    accent: 'slate'
  }
];

@Component({
  selector: 'app-app-store-page',
  imports: [AppStoreDownloadCenterComponent, AppStoreSubscriptionCenterComponent],
  templateUrl: './app-store-page.component.html',
  styleUrl: './app-store-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AppStorePageComponent {
  readonly categories = CATEGORIES;
  readonly activeView = signal<AppStoreView>('catalog');
  readonly activeCategory = signal<AppStoreCategoryId>('explore');
  readonly searchTerm = signal('');
  readonly notice = signal('');
  readonly visibleApps = computed(() => {
    const term = this.searchTerm().trim().toLocaleLowerCase();
    const category = this.activeCategory();
    return STORE_APPS.filter((app) => {
      const inCategory = category === 'explore' || app.category === category;
      const inSearch = !term || [app.name, app.eyebrow, app.description]
        .join(' ')
        .toLocaleLowerCase()
        .includes(term);
      return inCategory && inSearch;
    });
  });

  selectCategory(category: AppStoreCategoryId): void {
    this.activeView.set('catalog');
    this.activeCategory.set(category);
  }

  selectView(view: AppStoreView): void {
    this.activeView.set(view);
  }

  setSearch(value: string): void {
    this.searchTerm.set(value);
  }

  showInstallNotice(appName: string): void {
    this.notice.set(`${appName} 仅用于展示。应用安装将在完成签名、权限和审计边界后开放。`);
  }
}
