//--------------------------//
//--------定义桌面应用入口的展示契约---------//
//--------Defines the presentation contract for desktop app entries--------//
//-------------------------//

export interface DesktopApp {
  readonly id: string;
  readonly label: string;
  readonly iconPath: string;
  readonly iconBackground: string;
}

export const DESKTOP_APPS: readonly DesktopApp[] = [
  {
    id: 'dashboard',
    label: '概览',
    iconPath: 'M4 13h6V4H4v9Zm0 7h6v-5H4v5Zm10 0h6v-9h-6v9Zm0-16v5h6V4h-6Z',
    iconBackground: 'linear-gradient(145deg, #59b7ff, #176ee8)'
  },
  {
    id: 'storage',
    label: '存储空间',
    iconPath: 'M5 5.5C5 4.12 8.13 3 12 3s7 1.12 7 2.5S15.87 8 12 8 5 6.88 5 5.5Zm0 4C5 10.88 8.13 12 12 12s7-1.12 7-2.5v4c0 1.38-3.13 2.5-7 2.5s-7-1.12-7-2.5v-4Zm0 8C5 18.88 8.13 20 12 20s7-1.12 7-2.5v-2c-1.5 1-4.06 1.5-7 1.5s-5.5-.5-7-1.5v2Z',
    iconBackground: 'linear-gradient(145deg, #8d9aaa, #4f5d6d)'
  },
  {
    id: 'shares',
    label: '共享文件',
    iconPath: 'M3.5 6.5A2.5 2.5 0 0 1 6 4h4l2 2h6a2.5 2.5 0 0 1 2.5 2.5v8A2.5 2.5 0 0 1 18 19H6a2.5 2.5 0 0 1-2.5-2.5v-10Z',
    iconBackground: 'linear-gradient(145deg, #70d6ff, #2877d4)'
  },
  {
    id: 'users',
    label: '用户',
    iconPath: 'M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8Zm-7 8a7 7 0 0 1 14 0H5Z',
    iconBackground: 'linear-gradient(145deg, #b981ff, #7045d6)'
  },
  {
    id: 'operations',
    label: '任务中心',
    iconPath: 'M12 3a9 9 0 1 0 9 9h-2a7 7 0 1 1-2.05-4.95L14 10h7V3l-2.64 2.64A8.96 8.96 0 0 0 12 3Zm-1 4v6l4.5 2.6 1-1.73-3.5-2.02V7h-2Z',
    iconBackground: 'linear-gradient(145deg, #ffb25b, #ec6b2d)'
  },
  {
    id: 'settings',
    label: '系统设置',
    iconPath: 'm19.43 12.98.04-.98-.04-.98 2.11-1.65-2-3.46-2.49 1a7.6 7.6 0 0 0-1.69-.98L15 3.27h-4L10.64 5a7.6 7.6 0 0 0-1.69.98l-2.49-1-2 3.46 2.11 1.65-.04.98.04.98-2.11 1.65 2 3.46 2.49-1c.52.4 1.09.73 1.69.98L11 20.73h4l.36-2.66a7.6 7.6 0 0 0 1.69-.98l2.49 1 2-3.46-2.11-1.65ZM13 15.5a3.5 3.5 0 1 1 0-7 3.5 3.5 0 0 1 0 7Z',
    iconBackground: 'linear-gradient(145deg, #aeb7c2, #596574)'
  }
];
