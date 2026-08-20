//--------------------------//
//--------呈现可选择的性能资源列表---------//
//--------Presents the selectable performance resource list--------//
//-------------------------//

import {
  ChangeDetectionStrategy,
  Component,
  input,
  output
} from '@angular/core';

export type PerformanceResourceKind = 'cpu' | 'memory' | 'disk' | 'network' | 'gpu';

export interface PerformanceResource {
  readonly key: string;
  readonly kind: PerformanceResourceKind;
  readonly index: number;
  readonly title: string;
  readonly subtitle: string;
  readonly metric: string;
}

@Component({
  selector: 'app-performance-resource-list',
  template: `
    <nav class="resource-list" aria-label="性能资源">
      @for (resource of resources(); track resource.key) {
        <button
          type="button"
          class="resource-item"
          [class.resource-item--selected]="selectedKey() === resource.key"
          [class]="'resource-item resource-item--' + resource.kind"
          (click)="resourceSelected.emit(resource.key)"
        >
          <span class="resource-item__icon">
            @switch (resource.kind) {
              @case ('cpu') { CPU }
              @case ('memory') { 内 }
              @case ('disk') { 磁 }
              @case ('network') { 网 }
              @case ('gpu') { GPU }
            }
          </span>
          <span class="resource-item__copy">
            <strong>{{ resource.title }}</strong>
            <small>{{ resource.subtitle }}</small>
          </span>
          <span class="resource-item__metric">{{ resource.metric }}</span>
        </button>
      }
    </nav>
  `,
  styles: `
    :host { display: block; min-width: 0; min-height: 0; }
    .resource-list { height: 100%; box-sizing: border-box; overflow-y: auto; border-right: 1px solid #e1e4e8; background: #f8f9fb; padding: 8px; }
    .resource-item { display: grid; grid-template-columns: 38px minmax(0, 1fr) auto; align-items: center; width: 100%; min-height: 62px; gap: 9px; border: 1px solid transparent; border-radius: 8px; background: transparent; padding: 8px; color: inherit; text-align: left; cursor: pointer; }
    .resource-item:hover { background: #eef1f5; }
    .resource-item--selected { border-color: #cbd4df; background: #e8edf3; }
    .resource-item__icon { display: grid; place-items: center; width: 36px; height: 36px; box-sizing: border-box; border: 1px solid currentColor; border-radius: 5px; color: #2b7cd3; font-size: 9px; font-weight: 800; }
    .resource-item--memory .resource-item__icon { color: #8b5cc7; }
    .resource-item--disk .resource-item__icon { color: #4a9b63; }
    .resource-item--network .resource-item__icon { color: #b56b27; }
    .resource-item--gpu .resource-item__icon { color: #6f62d8; }
    .resource-item__copy { display: grid; min-width: 0; gap: 2px; }
    .resource-item__copy strong { font-size: 13px; font-weight: 650; }
    .resource-item__copy small { overflow: hidden; color: #6f7885; font-size: 10px; text-overflow: ellipsis; white-space: nowrap; }
    .resource-item__metric { color: #394555; font-size: 10px; font-variant-numeric: tabular-nums; white-space: nowrap; }
    @media (max-width: 820px) {
      .resource-item { grid-template-columns: 34px minmax(0, 1fr); }
      .resource-item__metric { display: none; }
    }
    @media (max-width: 600px) {
      .resource-list { display: flex; gap: 6px; overflow: auto; border-right: 0; border-bottom: 1px solid #e1e4e8; }
      .resource-item { flex: 0 0 134px; grid-template-columns: 30px minmax(0, 1fr); min-height: 50px; }
      .resource-item__icon { width: 29px; height: 29px; }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PerformanceResourceListComponent {
  readonly resources = input.required<readonly PerformanceResource[]>();
  readonly selectedKey = input.required<string>();
  readonly resourceSelected = output<string>();
}
