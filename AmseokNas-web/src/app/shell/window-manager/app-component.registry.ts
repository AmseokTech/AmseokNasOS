import { InjectionToken, Type } from '@angular/core';

export interface AppComponentDefinition {
  readonly appId: string;
  readonly title: string;
  readonly singleton: boolean;
  readonly defaultWidth: number;
  readonly defaultHeight: number;
  readonly minWidth: number;
  readonly minHeight: number;
  readonly loadComponent: () => Promise<Type<unknown>>;
}

const BUILT_IN_APP_COMPONENTS: ReadonlyMap<string, AppComponentDefinition> = new Map([
  [
    'terminal',
    {
      appId: 'terminal',
      title: 'terminal',
      singleton: true,
      defaultWidth: 1120,
      defaultHeight: 760,
      minWidth: 480,
      minHeight: 320,
      loadComponent: () =>
        import('../../features/terminal/terminal-page.component').then(
          ({ TerminalPageComponent }) => TerminalPageComponent
        )
    }
  ]
]);

export const APP_COMPONENT_REGISTRY = new InjectionToken<
  ReadonlyMap<string, AppComponentDefinition>
>('APP_COMPONENT_REGISTRY', {
  providedIn: 'root',
  factory: () => BUILT_IN_APP_COMPONENTS
});
