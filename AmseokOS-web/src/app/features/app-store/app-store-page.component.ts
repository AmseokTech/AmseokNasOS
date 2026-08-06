//--------------------------//
//--------提供内置应用商店的只读探索界面---------//
//--------Provides the read-only built-in app store discovery view--------//
//-------------------------//
import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';

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
    overview: '集中查看家庭照片和视频，为按时间整理、相册管理及日后的设备同步预留清晰入口。',
    features: ['按时间线和相册浏览影像', '为家庭资料建立可识别的归档入口', '后续同步能力将遵循权限边界开放'],
    imagePath: '/assets/app-store/photo-library-card.png'
  },
  {
    id: 'studio-sync',
    name: 'Studio Sync',
    category: 'work',
    eyebrow: '团队协作',
    description: '让素材、项目文件和成员进度始终保持同步。',
    overview: '为创作素材、项目文件与团队进度准备统一入口，减少分散文件带来的协作成本。',
    features: ['汇总项目资料的协作状态', '为成员同步保留清晰的工作上下文', '当前仅展示产品规划，不会修改本地文件'],
    imagePath: '/assets/app-store/studio-sync-card.png'
  },
  {
    id: 'screen-cast',
    name: 'Screen Cast',
    category: 'tools',
    eyebrow: '效率工具',
    description: '在可信设备之间轻松投送演示和媒体内容。',
    overview: '将演示内容和媒体资料投送到可信设备，适合会议展示与日常协作场景。',
    features: ['为可信设备间的内容投送提供入口', '保留演示与媒体资料的使用场景', '设备发现和投送操作尚未开放'],
    imagePath: '/assets/app-store/screen-cast-card.png'
  },
  {
    id: 'backup-vault',
    name: 'Backup Vault',
    category: 'development',
    eyebrow: '开发工具',
    description: '为项目快照和构建产物预留统一归档入口。',
    overview: '为项目快照和构建产物提供可追溯的归档视图，便于后续管理备份策略。',
    features: ['为构建产物与快照保留统一视图', '突出可靠归档和恢复的产品方向', '备份任务和存储写入均保持关闭'],
    imagePath: '/assets/app-store/backup-vault-card.png'
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
  readonly categories = CATEGORIES;
  readonly activeView = signal<AppStoreView>('catalog');
  readonly activeCategory = signal<AppStoreCategoryId>('explore');
  readonly searchTerm = signal('');
  readonly selectedApp = signal<StoreApp | null>(null);
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
  }

  openAppDetail(app: StoreApp): void {
    this.selectedApp.set(app);
    this.activeView.set('detail');
  }

  returnToCatalog(): void {
    this.selectedApp.set(null);
    this.activeView.set('catalog');
  }
}
