//--------------------------//
//--------定义桌面应用入口的展示契约---------//
//--------Defines the presentation contract for desktop app entries--------//
//-------------------------//

interface DesktopAppPresentation {
  readonly id: string;
  readonly label: string;
  readonly iconUrl: string;
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
    kind: 'window',
    iconUrl: '/assets/dock-icons/dashboard.svg',
    windowAppId: 'dashboard'
  },
  {
    id: 'terminal',
    label: '终端',
    kind: 'terminal',
    iconUrl: '/assets/dock-icons/terminal.svg'
  },
  {
    id: 'app-store',
    label: '应用商店',
    kind: 'window',
    iconUrl: '/assets/dock-icons/app-store.svg',
    windowAppId: 'app-store'
  },
  {
    id: 'settings',
    label: '系统设置',
    kind: 'window',
    iconUrl: '/assets/dock-icons/settings.svg',
    windowAppId: 'settings'
  }
];
