//--------------------------//
//--------定义桌面应用入口的展示契约---------//
//--------Defines the presentation contract for desktop app entries--------//
//-------------------------//

interface DesktopAppPresentation {
  readonly id: string;
  readonly label: string;
  readonly iconPath: string;
  readonly iconBackground: string;
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
    iconPath: 'M4 13h6V4H4v9Zm0 7h6v-5H4v5Zm10 0h6v-9h-6v9Zm0-16v5h6V4h-6Z',
    iconBackground: 'linear-gradient(145deg, #59b7ff, #176ee8)',
    windowAppId: 'dashboard'
  },
  {
    id: 'terminal',
    label: '终端',
    kind: 'terminal',
    iconPath: 'M4 5h16v14H4V5Zm2 2v10h12V7H6Zm1.5 2.2 2.3 2.3-2.3 2.3 1.4 1.4 3.7-3.7-3.7-3.7-1.4 1.4ZM12 14h4v-2h-4v2Z',
    iconBackground: 'linear-gradient(145deg, #36455b, #101722)'
  },
  {
    id: 'app-store',
    label: '应用商店',
    kind: 'window',
    iconPath: 'M7.1 2.5h9.8L20 6.1v13.4H4V6.1l3.1-3.6Zm.9 2L6.5 6.2h11L16 4.5H8Zm-.9 3.7v9.3h10V8.2H7Zm2.1 2.1h5.6v2H9.1v-2Zm0 3.5h5.6v2H9.1v-2Z',
    iconBackground: 'linear-gradient(145deg, #74c3ff, #2c78e6)',
    windowAppId: 'app-store'
  },
  {
    id: 'settings',
    label: '系统设置',
    kind: 'window',
    iconPath: 'm19.43 12.98.04-.98-.04-.98 2.11-1.65-2-3.46-2.49 1a7.6 7.6 0 0 0-1.69-.98L15 3.27h-4L10.64 5a7.6 7.6 0 0 0-1.69.98l-2.49-1-2 3.46 2.11 1.65-.04.98.04.98-2.11 1.65 2 3.46 2.49-1c.52.4 1.09.73 1.69.98L11 20.73h4l.36-2.66a7.6 7.6 0 0 0 1.69-.98l2.49 1 2-3.46-2.11-1.65ZM13 15.5a3.5 3.5 0 1 1 0-7 3.5 3.5 0 0 1 0 7Z',
    iconBackground: 'linear-gradient(145deg, #aeb7c2, #596574)',
    windowAppId: 'settings'
  }
];
