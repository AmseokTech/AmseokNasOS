import { InjectionToken, Signal } from '@angular/core';

export type WindowDisplayState = 'normal' | 'minimized' | 'maximized';
export type RestorableWindowDisplayState = Exclude<WindowDisplayState, 'minimized'>;

export interface WindowBounds {
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
}

export interface AppWindowState {
  readonly id: string;
  readonly appId: string;
  readonly title: string;
  readonly displayState: WindowDisplayState;
  readonly restoreState: RestorableWindowDisplayState;
  readonly bounds: WindowBounds;
  readonly zIndex: number;
  /** Runtime-only application data. WindowManager never persists this value. */
  readonly data: unknown;
}

export interface WindowOpenOptions<TData = unknown> {
  readonly data?: TData;
}

export const WINDOW_DATA = new InjectionToken<unknown>('WINDOW_DATA');
export const WINDOW_DISPLAY_STATE = new InjectionToken<Signal<WindowDisplayState>>(
  'WINDOW_DISPLAY_STATE'
);
export const WINDOW_ID = new InjectionToken<string>('WINDOW_ID');
