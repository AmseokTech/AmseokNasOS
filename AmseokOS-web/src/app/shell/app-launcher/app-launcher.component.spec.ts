import { TestBed } from '@angular/core/testing';

import { DesktopApp } from '../desktop/desktop-app.model';
import { AppLauncherComponent } from './app-launcher.component';

const installedApps: readonly DesktopApp[] = [
  {
    id: 'dashboard',
    label: '概览',
    kind: 'window',
    iconUrl: '/assets/dock-icons/dashboard.svg',
    windowAppId: 'dashboard'
  },
  {
    id: 'settings',
    label: '系统设置',
    kind: 'window',
    iconUrl: '/assets/dock-icons/settings.svg',
    windowAppId: 'settings'
  }
];

describe('AppLauncherComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppLauncherComponent]
    }).compileComponents();
  });

  it('renders installed components and emits the selected app', () => {
    const fixture = TestBed.createComponent(AppLauncherComponent);
    const selected = vi.fn();
    fixture.componentRef.setInput('apps', installedApps);
    fixture.componentInstance.appSelected.subscribe(selected);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const dialog = compiled.querySelector('[role="dialog"]');
    expect(dialog?.getAttribute('aria-modal')).toBe('true');
    expect(dialog?.getAttribute('aria-label')).toBe('启动台');
    expect(compiled.querySelector('.app-launcher__header')).toBeNull();
    expect(compiled.textContent).not.toContain('AMSEOKOS');
    expect(compiled.textContent).not.toContain('个已安装组件');
    expect(
      compiled.querySelector<HTMLImageElement>('[data-app-id="dashboard"] img')?.getAttribute('src')
    ).toBe('/assets/dock-icons/dashboard.svg');
    compiled.querySelector<HTMLButtonElement>('button[aria-label="打开系统设置"]')?.click();

    expect(selected).toHaveBeenCalledWith(installedApps[1]);
  });

  it('can be dismissed with Escape', () => {
    const fixture = TestBed.createComponent(AppLauncherComponent);
    const dismissed = vi.fn();
    fixture.componentRef.setInput('apps', installedApps);
    fixture.componentInstance.dismissed.subscribe(dismissed);
    fixture.detectChanges();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));

    expect(dismissed).toHaveBeenCalledOnce();
  });
});
