//--------------------------//
//--------定义桌面应用入口的展示契约---------//
//--------Defines the presentation contract for desktop app entries--------//
//-------------------------//

interface DesktopAppPresentation {
  readonly id: string;
  readonly label: string;
  readonly iconUrl: string;
  readonly route?: string;
  readonly launch?: 'terminal';
  readonly windowAppId?: string;
}

export type DesktopApp = DesktopAppPresentation & (
  | { readonly kind: 'route'; readonly route: string }
  | { readonly kind: 'terminal' }
  | { readonly kind: 'window'; readonly windowAppId: string }
);

export const DESKTOP_APPS: readonly DesktopApp[] = [
  {
    id: 'dashboard',
    label: '概览',
    iconUrl: '/assets/dock-icons/dashboard.svg',
    windowAppId: 'dashboard'
  },
  {
    id: 'storage',
    label: '存储空间',
    iconUrl: '/assets/dock-icons/storage.svg'
  },
  {
    id: 'shares',
    label: '共享文件',
    iconUrl: '/assets/dock-icons/shares.svg'
  },
  {
    id: 'users',
    label: '用户',
    iconUrl: '/assets/dock-icons/users.svg'
  },
  {
    id: 'operations',
    label: '任务中心',
    iconUrl: '/assets/dock-icons/operations.svg'
  },
  {
    id: 'terminal',
    label: '终端',
    iconUrl: '/assets/dock-icons/terminal.svg',
    launch: 'terminal'
  },
  {
    id: 'app-store',
    label: '应用商店',
    iconUrl: '/assets/dock-icons/app-store.svg',
    windowAppId: 'app-store'
  },
  {
    id: 'settings',
    label: '系统设置',
    iconUrl: '/assets/dock-icons/settings.svg',
    windowAppId: 'settings'
  }
];
