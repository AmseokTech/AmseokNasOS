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
  iconUrl: '/assets/dock-icons/terminal.svg',
  launch: 'terminal'
};

const dashboardApp: DesktopApp = {
  id: 'dashboard',
  label: '概览',
  iconUrl: '/assets/dock-icons/dashboard.svg'
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
    const buttons = () =>
      [...fixture.nativeElement.querySelectorAll('.dock-item')] as HTMLButtonElement[];

    expect(buttons()[0].classList.contains('dock-item--selected')).toBe(true);
    expect(buttons()[0].getAttribute('aria-current')).toBe('page');
    expect(buttons()[0].getAttribute('aria-pressed')).toBeNull();
    expect(buttons()[1].classList.contains('dock-item--running')).toBe(false);
    const windowId = manager.open('terminal');
    fixture.detectChanges();
    expect(buttons()[1].classList.contains('dock-item--running')).toBe(true);
    expect(buttons()[1].classList.contains('dock-item--focused')).toBe(true);
    expect(buttons()[1].getAttribute('aria-pressed')).toBe('true');

    manager.minimize(windowId);
    fixture.detectChanges();
    expect(buttons()[1].classList.contains('dock-item--minimized')).toBe(true);
    expect(buttons()[1].classList.contains('dock-item--focused')).toBe(false);
    expect(buttons()[1].getAttribute('aria-pressed')).toBe('false');
    expect(buttons()[0].getAttribute('aria-current')).toBe('page');
  });
});
