//--------------------------//
//--------呈现任务管理器式实时系统性能视图---------//
//--------Presents a task-manager-style real-time system performance view--------//
//-------------------------//

import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  signal
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { catchError, exhaustMap, finalize, merge, of, Subject, timer } from 'rxjs';

import { formatBytes } from '../settings';
import type {
  DashboardDiskPerformance,
  DashboardNetworkPerformance,
  DashboardPerformanceSample,
  DashboardSnapshot
} from './dashboard.models';
import { DashboardService } from './dashboard.service';
import { PerformanceChartComponent } from './performance-chart.component';
import { PerformanceResourceListComponent } from './performance-resource-list.component';
import type { PerformanceResource } from './performance-resource-list.component';

interface CpuCoreView {
  readonly id: string;
  readonly label: string;
  readonly utilizationPercent: number | null;
}

@Component({
  selector: 'app-dashboard-page',
  imports: [
    MatButtonModule,
    PerformanceChartComponent,
    PerformanceResourceListComponent
  ],
  templateUrl: './dashboard-page.component.html',
  styleUrl: './dashboard-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DashboardPageComponent implements OnInit {
  private readonly dashboard = inject(DashboardService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly refreshRequested = new Subject<void>();

  readonly snapshot = signal<DashboardSnapshot | null>(null);
  readonly performance = signal<DashboardPerformanceSample | null>(null);
  readonly performanceHistory = signal<readonly DashboardPerformanceSample[]>([]);
  readonly selectedKey = signal('cpu');
  readonly loading = signal(false);
  readonly lastUpdated = signal<Date | null>(null);
  readonly unexpectedError = signal<string | null>(null);
  readonly performanceError = signal<string | null>(null);

  readonly errors = computed(() => {
    const snapshot = this.snapshot();
    return [
      snapshot?.health.error,
      snapshot?.about.error,
      snapshot?.network.error,
      snapshot?.storage.error,
      snapshot?.volumes.error,
      snapshot?.smart.error,
      this.unexpectedError()
    ].filter((error): error is string => Boolean(error));
  });

  readonly resources = computed<readonly PerformanceResource[]>(() => {
    const performance = this.performance();
    if (performance) {
      return [
        {
          key: 'cpu', kind: 'cpu', index: 0, title: 'CPU',
          subtitle: performance.cpu.model,
          metric: this.formatPercent(performance.cpu.utilizationPercent)
        },
        {
          key: 'memory', kind: 'memory', index: 0, title: '内存',
          subtitle: `${formatBytes(performance.memory.usedBytes)} / ${formatBytes(performance.memory.totalBytes)}`,
          metric: this.formatPercent(performance.memory.utilizationPercent)
        },
        ...performance.disks.map((disk, index) => ({
          key: `disk:${disk.id}`, kind: 'disk' as const, index,
          title: `磁盘 ${index}`, subtitle: disk.model ?? disk.name,
          metric: this.formatPercent(disk.activePercent)
        })),
        ...performance.networks.map((network, index) => ({
          key: `network:${network.id}`, kind: 'network' as const, index,
          title: '网络', subtitle: network.model ?? network.name,
          metric: `收 ${this.formatRate(network.receivedBytesPerSecond)}`
        })),
        ...this.gpuResources(performance)
      ];
    }

    return this.fallbackResources();
  });

  readonly selectedResource = computed(() => {
    const resources = this.resources();
    return resources.find(({ key }) => key === this.selectedKey()) ?? resources[0];
  });

  readonly selectedDisk = computed<DashboardDiskPerformance | null>(() => {
    const selected = this.selectedResource();
    return selected?.kind === 'disk'
      ? this.performance()?.disks[selected.index] ?? null
      : null;
  });

  readonly selectedNetwork = computed<DashboardNetworkPerformance | null>(() => {
    const selected = this.selectedResource();
    return selected?.kind === 'network'
      ? this.performance()?.networks[selected.index] ?? null
      : null;
  });

  readonly selectedGpu = computed(() => {
    const selected = this.selectedResource();
    return selected?.kind === 'gpu'
      ? this.performance()?.gpus[selected.index] ?? null
      : null;
  });

  readonly cpuCores = computed<readonly CpuCoreView[]>(() => {
    const liveCpu = this.performance()?.cpu;
    if (liveCpu?.logicalProcessors.length) {
      return liveCpu.logicalProcessors.map((core, index) => ({
        id: core.id,
        label: `逻辑处理器 ${index}`,
        utilizationPercent: core.utilizationPercent
      }));
    }
    const count = this.snapshot()?.about.value?.cpu.logicalProcessorCount ?? 0;
    return Array.from({ length: count }, (_, index) => ({
      id: `cpu${index}`,
      label: `逻辑处理器 ${index}`,
      utilizationPercent: null
    }));
  });

  readonly cpuSeries = computed(() => this.performanceHistory().map(
    ({ cpu }) => cpu.utilizationPercent
  ));
  readonly memorySeries = computed(() => this.performanceHistory().map(
    ({ memory }) => memory.utilizationPercent
  ));

  readonly formatBytes = formatBytes;

  ngOnInit(): void {
    merge(timer(0, 30_000), this.refreshRequested).pipe(
      exhaustMap(() => {
        this.loading.set(true);
        this.unexpectedError.set(null);
        return this.dashboard.load().pipe(finalize(() => this.loading.set(false)));
      }),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: (snapshot) => {
        this.snapshot.set(snapshot);
        if (!this.performance()) {
          this.lastUpdated.set(new Date());
        }
      },
      error: (error: unknown) => this.unexpectedError.set(this.errorMessage(error))
    });

    merge(timer(0, 1_000), this.refreshRequested).pipe(
      exhaustMap(() => this.dashboard.samplePerformance().pipe(
        catchError((error: unknown) => {
          this.performanceError.set(this.errorMessage(error));
          return of(null);
        })
      )),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe((sample) => {
      if (!sample) {
        return;
      }
      this.performanceError.set(null);
      this.performance.set(sample);
      this.performanceHistory.update((history) => [...history, sample].slice(-60));
      this.lastUpdated.set(new Date(sample.capturedAtUnixMilliseconds));
    });
  }

  refresh(): void {
    this.refreshRequested.next();
  }

  selectResource(key: string): void {
    this.selectedKey.set(key);
  }

  coreSeries(id: string): readonly (number | null)[] {
    return this.performanceHistory().map(({ cpu }) =>
      cpu.logicalProcessors.find((core) => core.id === id)?.utilizationPercent ?? null
    );
  }

  diskSeries(id: string, metric: 'active' | 'read' | 'write'): readonly (number | null)[] {
    return this.performanceHistory().map(({ disks }) => {
      const disk = disks.find((candidate) => candidate.id === id);
      if (metric === 'active') {
        return disk?.activePercent ?? null;
      }
      return metric === 'read'
        ? disk?.readBytesPerSecond ?? null
        : disk?.writtenBytesPerSecond ?? null;
    });
  }

  networkSeries(id: string, metric: 'received' | 'transmitted'): readonly (number | null)[] {
    return this.performanceHistory().map(({ networks }) => {
      const network = networks.find((candidate) => candidate.id === id);
      return metric === 'received'
        ? network?.receivedBytesPerSecond ?? null
        : network?.transmittedBytesPerSecond ?? null;
    });
  }

  gpuSeries(
    id: string,
    metric: 'core' | 'twoD' | 'threeD' | 'memory'
  ): readonly (number | null)[] {
    return this.performanceHistory().map(({ gpus }) => {
      const gpu = gpus.find((candidate) => candidate.id === id);
      if (!gpu) {
        return null;
      }
      if (metric === 'memory') {
        return gpu.memoryTotalBytes && gpu.memoryUsedBytes !== null
          ? gpu.memoryUsedBytes / gpu.memoryTotalBytes * 100
          : null;
      }
      const metricKey = {
        core: 'coreUtilizationPercent',
        twoD: 'twoDUtilizationPercent',
        threeD: 'threeDUtilizationPercent'
      } as const;
      return gpu[metricKey[metric]];
    });
  }

  formatPercent(value: number | null | undefined): string {
    return value === null || value === undefined ? '—' : `${Math.round(value)}%`;
  }

  formatFrequency(megahertz: number | null | undefined): string {
    if (megahertz === null || megahertz === undefined) {
      return '—';
    }
    return megahertz >= 1000
      ? `${(megahertz / 1000).toFixed(2)} GHz`
      : `${Math.round(megahertz)} MHz`;
  }

  formatRate(bytesPerSecond: number | null | undefined): string {
    return bytesPerSecond === null || bytesPerSecond === undefined
      ? '—'
      : `${formatBytes(bytesPerSecond)}/秒`;
  }

  formatOptionalBytes(bytes: number | null | undefined): string {
    return bytes === null || bytes === undefined ? '—' : formatBytes(bytes);
  }

  formatLinkSpeed(megabitsPerSecond: number | null | undefined): string {
    if (megabitsPerSecond === null || megabitsPerSecond === undefined) {
      return '—';
    }
    return megabitsPerSecond >= 1000
      ? `${(megabitsPerSecond / 1000).toFixed(1)} Gbps`
      : `${megabitsPerSecond} Mbps`;
  }

  private gpuResources(performance: DashboardPerformanceSample): readonly PerformanceResource[] {
    if (performance.gpus.length === 0) {
      return [{
        key: 'gpu:unavailable', kind: 'gpu', index: -1, title: 'GPU',
        subtitle: '未检测到可读取的显卡', metric: '—'
      }];
    }
    return performance.gpus.map((gpu, index) => ({
      key: `gpu:${gpu.id}`, kind: 'gpu' as const, index,
      title: index === 0 ? 'GPU' : `GPU ${index}`,
      subtitle: gpu.name,
      metric: this.formatPercent(gpu.coreUtilizationPercent)
    }));
  }

  private fallbackResources(): readonly PerformanceResource[] {
    const snapshot = this.snapshot();
    const about = snapshot?.about.value;
    const disks = snapshot?.storage.value?.disks ?? [];
    const networks = snapshot?.network.value ?? [];
    const fallbackDisks = disks.length > 0
      ? disks.map((disk, index) => ({
        key: `disk:${disk.id}`, kind: 'disk' as const, index,
        title: `磁盘 ${index}`, subtitle: disk.model ?? disk.name, metric: '—'
      }))
      : [{
        key: 'disk:system', kind: 'disk' as const, index: -1,
        title: '磁盘 0', subtitle: about?.systemStorage.model ?? '系统盘', metric: '—'
      }];
    return [
      {
        key: 'cpu', kind: 'cpu', index: 0, title: 'CPU',
        subtitle: about?.cpu.model ?? '等待处理器信息', metric: '—'
      },
      {
        key: 'memory', kind: 'memory', index: 0, title: '内存',
        subtitle: about ? formatBytes(about.memory.totalBytes) : '等待内存信息', metric: '—'
      },
      ...fallbackDisks,
      ...networks.map((network, index) => ({
        key: `network:${network.id}`, kind: 'network' as const, index,
        title: '网络', subtitle: network.model ?? network.name, metric: network.linkState
      })),
      {
        key: 'gpu:unavailable', kind: 'gpu', index: -1, title: 'GPU',
        subtitle: '等待显卡性能接口', metric: '—'
      }
    ];
  }

  private errorMessage(error: unknown): string {
    return error instanceof Error ? error.message : '系统性能数据加载失败';
  }
}
