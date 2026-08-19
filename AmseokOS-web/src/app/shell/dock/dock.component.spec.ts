import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import {
  APP_COMPONENT_REGISTRY,
  AppComponentDefinition
} from '../window-manager/app-component.registry';
import {
  WINDOW_LAYOUT_STORAGE,
  WindowManagerService
} from '../window-manager/window-manager.service';
import { DesktopApp } from '../desktop/desktop-app.model';
import { DockComponent } from './dock.component';

@Component({ template: '' })
class StubComponent {}

const terminalApp: DesktopApp = {
  id: 'terminal',
  label: '终端',
  kind: 'terminal',
  iconPath: 'M0 0',
  iconBackground: '#000'
};

const dashboardApp: DesktopApp = {
  id: 'dashboard',
  label: '概览',
  kind: 'window',
  iconPath: 'M0 0',
  iconBackground: '#00f',
  windowAppId: 'dashboard'
};

const definition: AppComponentDefinition = {
  appId: 'terminal',
  title: 'terminal',
  singleton: true,
  defaultWidth: 640,
  defaultHeight: 480,
  minWidth: 320,
  minHeight: 240,
  loadComponent: async () => StubComponent
};

describe('DockComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DockComponent],
      providers: [
        { provide: APP_COMPONENT_REGISTRY, useValue: new Map([['terminal', definition]]) },
        { provide: WINDOW_LAYOUT_STORAGE, useValue: null }
      ]
    }).compileComponents();
  });

  it('keeps selection separate from running, minimized, and focused window states', () => {
    const fixture = TestBed.createComponent(DockComponent);
    const manager = TestBed.inject(WindowManagerService);
    fixture.componentRef.setInput('apps', [dashboardApp, terminalApp]);
    fixture.componentRef.setInput('selectedAppId', 'dashboard');
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    const button = (label: string) => compiled.querySelector<HTMLButtonElement>(
      `button[aria-label="${label}"]`
    );

    expect(button('启动台')?.getAttribute('aria-pressed')).toBe('false');
    expect(button('概览')?.classList.contains('dock-item--selected')).toBe(true);
    expect(button('概览')?.getAttribute('aria-current')).toBe('page');
    expect(button('概览')?.getAttribute('aria-pressed')).toBeNull();
    expect(button('终端')?.classList.contains('dock-item--running')).toBe(false);
    const windowId = manager.open('terminal');
    fixture.detectChanges();
    expect(button('终端')?.classList.contains('dock-item--running')).toBe(true);
    expect(button('终端')?.classList.contains('dock-item--focused')).toBe(true);
    expect(button('终端')?.getAttribute('aria-pressed')).toBe('true');

    manager.minimize(windowId);
    fixture.detectChanges();
    expect(button('终端')?.classList.contains('dock-item--minimized')).toBe(true);
    expect(button('终端')?.classList.contains('dock-item--focused')).toBe(false);
    expect(button('终端')?.getAttribute('aria-pressed')).toBe('false');
    expect(button('概览')?.getAttribute('aria-current')).toBe('page');
  });

  it('renders the Launchpad first and reports toggle requests', () => {
    const fixture = TestBed.createComponent(DockComponent);
    const toggled = vi.fn();
    fixture.componentRef.setInput('apps', [dashboardApp, terminalApp]);
    fixture.componentRef.setInput('launcherOpen', true);
    fixture.componentRef.setInput('selectedAppId', 'dashboard');
    fixture.componentInstance.launcherToggled.subscribe(toggled);
    fixture.detectChanges();

    const buttons = [...fixture.nativeElement.querySelectorAll('.dock-item')] as HTMLButtonElement[];
    expect(buttons[0].getAttribute('aria-label')).toBe('启动台');
    expect(buttons[0].getAttribute('aria-pressed')).toBe('true');
    buttons[0].click();

    expect(toggled).toHaveBeenCalledOnce();
  });
});
