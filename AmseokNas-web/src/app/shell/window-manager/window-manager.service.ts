import { DOCUMENT } from '@angular/common';
import { InjectionToken, computed, inject, Injectable, signal } from '@angular/core';

import {
  APP_COMPONENT_REGISTRY,
  AppComponentDefinition
} from './app-component.registry';
import {
  AppWindowState,
  RestorableWindowDisplayState,
  WindowBounds,
  WindowOpenOptions
} from './window-state.model';

interface PersistedWindowLayouts {
  readonly version: 1;
  readonly layouts: Readonly<Record<string, WindowBounds>>;
}

const LAYOUT_VERSION = 1;
export const WINDOW_LAYOUT_STORAGE_KEY = 'amseoknas.window-layouts';
const MAX_STORED_DIMENSION = 100_000;

export const WINDOW_LAYOUT_STORAGE = new InjectionToken<Storage | null>(
  'WINDOW_LAYOUT_STORAGE',
  {
    providedIn: 'root',
    factory: () => {
      try {
        return inject(DOCUMENT).defaultView?.localStorage ?? null;
      } catch {
        return null;
      }
    }
  }
);

@Injectable({ providedIn: 'root' })
export class WindowManagerService {
  private readonly registry = inject(APP_COMPONENT_REGISTRY);
  private readonly storage = inject(WINDOW_LAYOUT_STORAGE);
  private readonly windowStates = signal<readonly AppWindowState[]>([]);
  private readonly persistedLayouts = this.readPersistedLayouts();
  private readonly workspaceBounds = signal(this.initialWorkspaceBounds());
  private nextWindowId = 1;

  readonly windows = this.windowStates.asReadonly();
  readonly focusedWindow = computed(() => {
    const visible = this.windowStates().filter(
      (windowState) => windowState.displayState !== 'minimized'
    );
    return visible.reduce<AppWindowState | null>(
      (focused, current) => !focused || current.zIndex > focused.zIndex ? current : focused,
      null
    );
  });

  definition(appId: string): AppComponentDefinition | undefined {
    return this.registry.get(appId);
  }

  windowForApp(appId: string): AppWindowState | undefined {
    return this.windowStates().find((windowState) => windowState.appId === appId);
  }

  open<TData>(appId: string, options: WindowOpenOptions<TData> = {}): string {
    const definition = this.registry.get(appId);
    if (!definition) {
      throw new Error(`Unknown desktop application: ${appId}`);
    }

    if (definition.singleton) {
      const existing = this.windowForApp(appId);
      if (existing) {
        this.activate(appId);
        return existing.id;
      }
    }

    const id = `${appId}-${this.nextWindowId++}`;
    const bounds = this.persistedLayouts[appId] ?? this.createDefaultBounds(definition);
    const state: AppWindowState = {
      id,
      appId,
      title: definition.title,
      displayState: 'normal',
      restoreState: 'normal',
      bounds,
      zIndex: this.windowStates().length + 1,
      data: options.data
    };
    this.windowStates.update((windows) => this.normalizeZIndexes([...windows, state], id));
    return id;
  }

  activate(appId: string): boolean {
    const existing = this.windowForApp(appId);
    if (!existing) {
      return false;
    }
    if (existing.displayState === 'minimized') {
      this.restore(existing.id);
    } else {
      this.focus(existing.id);
    }
    return true;
  }

  close(windowId: string): void {
    this.windowStates.update((windows) =>
      this.normalizeZIndexes(windows.filter((windowState) => windowState.id !== windowId))
    );
  }

  focus(windowId: string): void {
    const target = this.findWindow(windowId);
    if (!target || target.displayState === 'minimized') {
      return;
    }
    this.windowStates.update((windows) => this.normalizeZIndexes(windows, windowId));
  }

  minimize(windowId: string): void {
    this.updateWindow(windowId, (windowState) => ({
      ...windowState,
      displayState: 'minimized',
      restoreState: this.restorableState(windowState)
    }));
  }

  restore(windowId: string): void {
    this.updateWindow(windowId, (windowState) => ({
      ...windowState,
      displayState: windowState.restoreState
    }));
    this.focus(windowId);
  }

  toggleMaximize(windowId: string): void {
    this.updateWindow(windowId, (windowState) => {
      const displayState: RestorableWindowDisplayState =
        windowState.displayState === 'maximized' ? 'normal' : 'maximized';
      return {
        ...windowState,
        displayState,
        restoreState: displayState
      };
    });
    this.focus(windowId);
  }

  move(windowId: string, x: number, y: number): void {
    this.updateNormalBounds(windowId, (bounds, definition) =>
      this.clampBounds({ ...bounds, x, y }, definition)
    );
    this.persistLayout(windowId);
  }

  resize(windowId: string, width: number, height: number, persist = true): void {
    this.updateNormalBounds(windowId, (bounds, definition) =>
      this.clampBounds({ ...bounds, width, height }, definition)
    );
    if (persist) {
      this.persistLayout(windowId);
    }
  }

  persistLayout(windowId: string): void {
    const windowState = this.findWindow(windowId);
    if (!windowState) {
      return;
    }
    this.persistedLayouts[windowState.appId] = windowState.bounds;
    this.writePersistedLayouts();
  }

  setWorkspaceBounds(width: number, height: number): void {
    if (!Number.isFinite(width) || !Number.isFinite(height) || width <= 0 || height <= 0) {
      return;
    }
    this.workspaceBounds.set({ width, height });
  }

  renderBounds(windowState: AppWindowState): WindowBounds {
    const workspace = this.workspaceBounds();
    if (windowState.displayState === 'maximized') {
      return { x: 0, y: 0, width: workspace.width, height: workspace.height };
    }
    const definition = this.registry.get(windowState.appId);
    return definition ? this.clampBounds(windowState.bounds, definition) : windowState.bounds;
  }

  private findWindow(windowId: string): AppWindowState | undefined {
    return this.windowStates().find((windowState) => windowState.id === windowId);
  }

  private updateWindow(
    windowId: string,
    update: (windowState: AppWindowState) => AppWindowState
  ): void {
    this.windowStates.update((windows) =>
      windows.map((windowState) => windowState.id === windowId ? update(windowState) : windowState)
    );
  }

  private updateNormalBounds(
    windowId: string,
    update: (
      bounds: WindowBounds,
      definition: AppComponentDefinition
    ) => WindowBounds
  ): void {
    const definition = this.findWindow(windowId);
    if (!definition || definition.displayState !== 'normal') {
      return;
    }
    const appDefinition = this.registry.get(definition.appId);
    if (!appDefinition) {
      return;
    }
    this.updateWindow(windowId, (windowState) => ({
      ...windowState,
      bounds: update(windowState.bounds, appDefinition)
    }));
  }

  private normalizeZIndexes(
    windows: readonly AppWindowState[],
    focusedWindowId?: string
  ): readonly AppWindowState[] {
    const ordered = [...windows].sort((left, right) => left.zIndex - right.zIndex);
    if (focusedWindowId) {
      const focusedIndex = ordered.findIndex((windowState) => windowState.id === focusedWindowId);
      if (focusedIndex >= 0) {
        ordered.push(...ordered.splice(focusedIndex, 1));
      }
    }
    return ordered.map((windowState, index) => ({ ...windowState, zIndex: index + 1 }));
  }

  private createDefaultBounds(definition: AppComponentDefinition): WindowBounds {
    const workspace = this.workspaceBounds();
    const offset = (this.windowStates().length % 6) * 24;
    const width = Math.min(definition.defaultWidth, workspace.width);
    const height = Math.min(definition.defaultHeight, workspace.height);
    return this.clampBounds(
      {
        x: Math.max(0, (workspace.width - width) / 2 + offset),
        y: Math.max(0, (workspace.height - height) / 2 + offset),
        width,
        height
      },
      definition
    );
  }

  private clampBounds(
    bounds: WindowBounds,
    definition: AppComponentDefinition
  ): WindowBounds {
    const workspace = this.workspaceBounds();
    const minWidth = Math.min(definition.minWidth, workspace.width);
    const minHeight = Math.min(definition.minHeight, workspace.height);
    const width = Math.min(Math.max(bounds.width, minWidth), workspace.width);
    const height = Math.min(Math.max(bounds.height, minHeight), workspace.height);
    return {
      x: Math.min(Math.max(bounds.x, 0), Math.max(0, workspace.width - width)),
      y: Math.min(Math.max(bounds.y, 0), Math.max(0, workspace.height - height)),
      width,
      height
    };
  }

  private restorableState(windowState: AppWindowState): RestorableWindowDisplayState {
    return windowState.displayState === 'maximized' ? 'maximized' : 'normal';
  }

  private initialWorkspaceBounds(): { width: number; height: number } {
    const view = inject(DOCUMENT).defaultView;
    return {
      width: Math.max(view?.innerWidth ?? 1280, 1),
      height: Math.max((view?.innerHeight ?? 800) - 40, 1)
    };
  }

  private readPersistedLayouts(): Record<string, WindowBounds> {
    if (!this.storage) {
      return {};
    }
    try {
      const raw = this.storage.getItem(WINDOW_LAYOUT_STORAGE_KEY);
      if (!raw) {
        return {};
      }
      const parsed = JSON.parse(raw) as unknown;
      if (!this.isPersistedWindowLayouts(parsed)) {
        return {};
      }
      return Object.fromEntries(
        Object.entries(parsed.layouts).filter(
          ([appId, bounds]) => this.registry.has(appId) && this.isWindowBounds(bounds)
        )
      );
    } catch {
      return {};
    }
  }

  private writePersistedLayouts(): void {
    if (!this.storage) {
      return;
    }
    const layouts = Object.fromEntries(
      Object.entries(this.persistedLayouts).filter(
        ([appId, bounds]) => this.registry.has(appId) && this.isWindowBounds(bounds)
      )
    );
    const persisted: PersistedWindowLayouts = { version: LAYOUT_VERSION, layouts };
    try {
      this.storage.setItem(WINDOW_LAYOUT_STORAGE_KEY, JSON.stringify(persisted));
    } catch {
      // Layout persistence is optional; quota and privacy-mode failures are non-fatal.
    }
  }

  private isPersistedWindowLayouts(value: unknown): value is PersistedWindowLayouts {
    if (!value || typeof value !== 'object') {
      return false;
    }
    const candidate = value as Partial<PersistedWindowLayouts>;
    return candidate.version === LAYOUT_VERSION &&
      !!candidate.layouts && typeof candidate.layouts === 'object' &&
      !Array.isArray(candidate.layouts);
  }

  private isWindowBounds(value: unknown): value is WindowBounds {
    if (!value || typeof value !== 'object') {
      return false;
    }
    const bounds = value as Partial<WindowBounds>;
    return this.isStoredNumber(bounds.x, true) &&
      this.isStoredNumber(bounds.y, true) &&
      this.isStoredNumber(bounds.width, false) &&
      this.isStoredNumber(bounds.height, false);
  }

  private isStoredNumber(value: unknown, allowZero: boolean): value is number {
    return typeof value === 'number' && Number.isFinite(value) &&
      value <= MAX_STORED_DIMENSION && (allowZero ? value >= 0 : value > 0);
  }
}
