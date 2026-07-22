import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import {
  APP_COMPONENT_REGISTRY,
  AppComponentDefinition
} from './app-component.registry';
import {
  WINDOW_LAYOUT_STORAGE,
  WINDOW_LAYOUT_STORAGE_KEY,
  WindowManagerService
} from './window-manager.service';

@Component({ template: '' })
class StubAppComponent {}

class MemoryStorage implements Storage {
  private readonly values = new Map<string, string>();

  get length(): number {
    return this.values.size;
  }

  clear(): void {
    this.values.clear();
  }

  getItem(key: string): string | null {
    return this.values.get(key) ?? null;
  }

  key(index: number): string | null {
    return [...this.values.keys()][index] ?? null;
  }

  removeItem(key: string): void {
    this.values.delete(key);
  }

  setItem(key: string, value: string): void {
    this.values.set(key, value);
  }
}

const definition = (appId: string, singleton: boolean): AppComponentDefinition => ({
  appId,
  title: appId,
  singleton,
  defaultWidth: 800,
  defaultHeight: 600,
  minWidth: 320,
  minHeight: 240,
  loadComponent: async () => StubAppComponent
});

const registry = new Map([
  ['terminal', definition('terminal', true)],
  ['files', definition('files', false)]
]);

describe('WindowManagerService', () => {
  let storage: MemoryStorage;
  let manager: WindowManagerService;

  beforeEach(() => {
    storage = new MemoryStorage();
    TestBed.configureTestingModule({
      providers: [
        { provide: APP_COMPONENT_REGISTRY, useValue: registry },
        { provide: WINDOW_LAYOUT_STORAGE, useValue: storage }
      ]
    });
    manager = TestBed.inject(WindowManagerService);
    manager.setWorkspaceBounds(1000, 700);
  });

  it('keeps singleton apps unique and activates an existing minimized window', () => {
    const firstId = manager.open('terminal', { data: { token: 'memory-only' } });
    manager.minimize(firstId);

    const secondId = manager.open('terminal', { data: { token: 'ignored' } });

    expect(secondId).toBe(firstId);
    expect(manager.windows()).toHaveLength(1);
    expect(manager.windows()[0].displayState).toBe('normal');
    expect(manager.windows()[0].data).toEqual({ token: 'memory-only' });
  });

  it('centralizes focus, minimize, restore, maximize, close, and normalized z-index state', () => {
    const terminalId = manager.open('terminal');
    const filesId = manager.open('files');

    expect(manager.focusedWindow()?.id).toBe(filesId);
    manager.focus(terminalId);
    expect(manager.focusedWindow()?.id).toBe(terminalId);
    expect(manager.windows().map(({ zIndex }) => zIndex).sort()).toEqual([1, 2]);

    manager.toggleMaximize(terminalId);
    expect(manager.windowForApp('terminal')?.displayState).toBe('maximized');
    manager.minimize(terminalId);
    expect(manager.focusedWindow()?.id).toBe(filesId);
    manager.restore(terminalId);
    expect(manager.windowForApp('terminal')?.displayState).toBe('maximized');

    manager.close(terminalId);
    expect(manager.windowForApp('terminal')).toBeUndefined();
    expect(manager.windows().map(({ zIndex }) => zIndex)).toEqual([1]);
  });

  it('clamps moves and resizes without overwriting normal bounds when maximized', () => {
    const id = manager.open('terminal');
    manager.move(id, 950, 680);
    manager.resize(id, 2_000, 2_000);

    const normal = manager.windowForApp('terminal')!;
    expect(normal.bounds.x).toBeGreaterThanOrEqual(0);
    expect(normal.bounds.y).toBeGreaterThanOrEqual(0);
    expect(normal.bounds.width).toBeLessThanOrEqual(1000);
    expect(normal.bounds.height).toBeLessThanOrEqual(700);

    manager.toggleMaximize(id);
    const normalBounds = manager.windowForApp('terminal')!.bounds;
    manager.setWorkspaceBounds(360, 240);
    expect(manager.renderBounds(manager.windowForApp('terminal')!)).toEqual({
      x: 0,
      y: 0,
      width: 360,
      height: 240
    });
    expect(manager.windowForApp('terminal')!.bounds).toEqual(normalBounds);
  });

  it('persists only validated, versioned app bounds and restores no running windows', () => {
    const id = manager.open('terminal', { data: { token: 'secret' } });
    manager.resize(id, 640, 480);
    const saved = JSON.parse(storage.getItem(WINDOW_LAYOUT_STORAGE_KEY)!) as Record<string, unknown>;

    expect(saved['version']).toBe(1);
    expect(JSON.stringify(saved)).not.toContain('secret');
    expect(JSON.stringify(saved)).not.toContain('token');

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        { provide: APP_COMPONENT_REGISTRY, useValue: registry },
        { provide: WINDOW_LAYOUT_STORAGE, useValue: storage }
      ]
    });
    const restoredManager = TestBed.inject(WindowManagerService);
    restoredManager.setWorkspaceBounds(1000, 700);
    expect(restoredManager.windows()).toHaveLength(0);
    restoredManager.open('terminal');
    expect(restoredManager.windowForApp('terminal')?.bounds.width).toBe(640);
    expect(restoredManager.windowForApp('terminal')?.bounds.height).toBe(480);
  });

  it('ignores malformed persistence and tolerates storage write failures', () => {
    storage.setItem(WINDOW_LAYOUT_STORAGE_KEY, '{broken');
    TestBed.resetTestingModule();
    const failingStorage = new MemoryStorage();
    failingStorage.setItem(WINDOW_LAYOUT_STORAGE_KEY, '{broken');
    vi.spyOn(failingStorage, 'setItem').mockImplementation(() => {
      throw new DOMException('Quota exceeded', 'QuotaExceededError');
    });
    TestBed.configureTestingModule({
      providers: [
        { provide: APP_COMPONENT_REGISTRY, useValue: registry },
        { provide: WINDOW_LAYOUT_STORAGE, useValue: failingStorage }
      ]
    });
    const safeManager = TestBed.inject(WindowManagerService);

    expect(() => {
      const id = safeManager.open('terminal');
      safeManager.move(id, 20, 30);
    }).not.toThrow();
  });
});
