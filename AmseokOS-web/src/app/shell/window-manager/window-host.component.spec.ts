import { Component, OnDestroy } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import {
  APP_COMPONENT_REGISTRY,
  AppComponentDefinition
} from './app-component.registry';
import { WINDOW_LAYOUT_STORAGE, WindowManagerService } from './window-manager.service';
import { WindowHostComponent } from './window-host.component';

let destroyCalls = 0;

@Component({ template: '<p class="stub-content">Window content</p>' })
class LifecycleStubComponent implements OnDestroy {
  ngOnDestroy(): void {
    destroyCalls += 1;
  }
}

const definition: AppComponentDefinition = {
  appId: 'test-app',
  title: 'Test app',
  singleton: true,
  defaultWidth: 640,
  defaultHeight: 480,
  minWidth: 320,
  minHeight: 240,
  loadComponent: async () => LifecycleStubComponent
};

describe('WindowHostComponent', () => {
  beforeEach(async () => {
    destroyCalls = 0;
    await TestBed.configureTestingModule({
      imports: [WindowHostComponent],
      providers: [
        { provide: APP_COMPONENT_REGISTRY, useValue: new Map([['test-app', definition]]) },
        { provide: WINDOW_LAYOUT_STORAGE, useValue: null }
      ]
    }).compileComponents();
  });

  it('keeps minimized content alive and destroys it when the managed window closes', async () => {
    const fixture = TestBed.createComponent(WindowHostComponent);
    const manager = TestBed.inject(WindowManagerService);
    const windowId = manager.open('test-app');

    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.stub-content')).not.toBeNull();
    expect(
      fixture.nativeElement.querySelector('.managed-window')?.getAttribute('data-app-id')
    ).toBe('test-app');

    manager.minimize(windowId);
    fixture.detectChanges();
    expect(destroyCalls).toBe(0);
    expect(fixture.nativeElement.querySelector('.managed-window--minimized')).not.toBeNull();

    manager.close(windowId);
    fixture.detectChanges();
    expect(destroyCalls).toBe(1);
    expect(fixture.nativeElement.querySelector('.managed-window')).toBeNull();
  });

  it('resizes a normal window with arrow keys and uses a larger Shift step', async () => {
    const fixture = TestBed.createComponent(WindowHostComponent);
    const manager = TestBed.inject(WindowManagerService);
    manager.setWorkspaceBounds(1000, 700);
    manager.open('test-app');

    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const handles = fixture.nativeElement.querySelectorAll(
      '.managed-window__resize-handle'
    ) as NodeListOf<HTMLButtonElement>;
    expect(handles).toHaveLength(8);
    const handle = fixture.nativeElement.querySelector(
      '.managed-window__resize-handle--bottom-right'
    ) as HTMLButtonElement;
    const initialBounds = manager.windowForApp('test-app')!.bounds;
    const right = new KeyboardEvent('keydown', { key: 'ArrowRight', cancelable: true });
    handle.dispatchEvent(right);
    fixture.detectChanges();

    expect(right.defaultPrevented).toBe(true);
    expect(manager.windowForApp('test-app')!.bounds.width).toBe(initialBounds.width + 10);

    const down = new KeyboardEvent('keydown', {
      key: 'ArrowDown',
      shiftKey: true,
      cancelable: true
    });
    handle.dispatchEvent(down);
    fixture.detectChanges();
    expect(manager.windowForApp('test-app')!.bounds.height).toBe(initialBounds.height + 50);
  });
});
