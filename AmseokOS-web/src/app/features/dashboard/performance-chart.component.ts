//--------------------------//
//--------绘制无依赖的轻量性能曲线---------//
//--------Draws lightweight dependency-free performance curves--------//
//-------------------------//

import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

@Component({
  selector: 'app-performance-chart',
  template: `
    <figure class="chart" [class.chart--compact]="compact()" [style.--chart-color]="color()">
      <figcaption>
        <span>{{ label() }}</span>
        <strong>{{ value() }}</strong>
      </figcaption>
      <div class="chart__plot" role="img" [attr.aria-label]="label() + '实时曲线'">
        <svg viewBox="0 0 100 40" preserveAspectRatio="none" aria-hidden="true">
          <polyline [attr.points]="points()"></polyline>
        </svg>
      </div>
    </figure>
  `,
  styles: `
    :host { display: block; min-width: 0; }
    .chart { margin: 0; min-width: 0; }
    figcaption { display: flex; justify-content: space-between; gap: 8px; margin-bottom: 7px; color: #536173; font-size: 12px; }
    figcaption strong { color: #1f2937; font-variant-numeric: tabular-nums; }
    .chart__plot { height: 118px; overflow: hidden; border: 1px solid color-mix(in srgb, var(--chart-color) 42%, #d8dee8); border-radius: 3px; background-color: #fff; background-image: linear-gradient(to right, #e9edf3 1px, transparent 1px), linear-gradient(to bottom, #e9edf3 1px, transparent 1px); background-size: 20% 25%; }
    svg { display: block; width: 100%; height: 100%; }
    polyline { fill: none; stroke: var(--chart-color); stroke-width: 1.25; vector-effect: non-scaling-stroke; stroke-linecap: round; stroke-linejoin: round; }
    .chart--compact figcaption { margin-bottom: 4px; font-size: 10px; }
    .chart--compact .chart__plot { height: 54px; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PerformanceChartComponent {
  readonly label = input.required<string>();
  readonly value = input('—');
  readonly series = input<readonly (number | null)[]>([]);
  readonly maximum = input<number | null>(100);
  readonly color = input('#2b7cd3');
  readonly compact = input(false);

  readonly points = computed(() => {
    const series = this.series();
    const finiteValues = series.filter((value): value is number =>
      value !== null && Number.isFinite(value)
    );
    if (finiteValues.length < 2) {
      return '';
    }
    const configuredMaximum = this.maximum();
    const maximum = configuredMaximum === null
      ? Math.max(...finiteValues, 1)
      : Math.max(configuredMaximum, 1);
    const offset = Math.max(0, 60 - series.length);

    return series.map((value, index) => {
      if (value === null || !Number.isFinite(value)) {
        return null;
      }
      const x = (offset + index) / 59 * 100;
      const y = 40 - Math.min(1, Math.max(0, value / maximum)) * 40;
      return `${x.toFixed(2)},${y.toFixed(2)}`;
    }).filter((point): point is string => point !== null).join(' ');
  });
}
