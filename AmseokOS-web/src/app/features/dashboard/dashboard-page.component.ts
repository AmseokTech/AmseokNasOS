//--------------------------//
//--------呈现可自动恢复的只读系统概览---------//
//--------Presents a read-only system overview with automatic recovery--------//
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
import { exhaustMap, finalize, merge, Subject, timer } from 'rxjs';

import { formatBytes, formatUptime, storageUsagePercentage } from '../settings';
import type { DashboardSnapshot } from './dashboard.models';
import { DashboardService } from './dashboard.service';

@Component({
  selector: 'app-dashboard-page',
  imports: [MatButtonModule],
  templateUrl: './dashboard-page.component.html',
  styleUrl: './dashboard-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DashboardPageComponent implements OnInit {
  private readonly dashboard = inject(DashboardService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly refreshRequested = new Subject<void>();

  readonly snapshot = signal<DashboardSnapshot | null>(null);
  readonly loading = signal(false);
  readonly lastUpdated = signal<Date | null>(null);
  readonly unexpectedError = signal<string | null>(null);
  readonly errors = computed(() => {
    const snapshot = this.snapshot();
    if (!snapshot) {
      const unexpectedError = this.unexpectedError();
      return unexpectedError ? [unexpectedError] : [];
    }
    return [
      snapshot.health.error,
      snapshot.about.error,
      snapshot.network.error,
      snapshot.storage.error,
      snapshot.volumes.error,
      snapshot.smart.error,
      this.unexpectedError()
    ].filter((error): error is string => Boolean(error));
  });

  readonly formatBytes = formatBytes;
  readonly formatUptime = formatUptime;
  readonly storageUsagePercentage = storageUsagePercentage;

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
        this.lastUpdated.set(new Date());
      },
      error: (error: unknown) => {
        this.unexpectedError.set(
          error instanceof Error ? error.message : '概览数据加载失败'
        );
      }
    });
  }

  refresh(): void {
    this.refreshRequested.next();
  }

  isOnline(snapshot: DashboardSnapshot): boolean {
    const status = snapshot.health.value?.status.toLowerCase();
    return status === 'healthy' || status === 'ok';
  }

  connectedInterfaces(snapshot: DashboardSnapshot): number {
    return snapshot.network.value?.filter(({ linkState }) =>
      ['up', 'connected'].includes(linkState.toLowerCase())
    ).length ?? 0;
  }

  degradedArrays(snapshot: DashboardSnapshot): number {
    return snapshot.storage.value?.arrays.filter(({ degradedDeviceCount }) =>
      degradedDeviceCount > 0
    ).length ?? 0;
  }

  mountedVolumes(snapshot: DashboardSnapshot): number {
    return snapshot.volumes.value?.filter(({ mounted }) => mounted).length ?? 0;
  }

  verifiedVolumes(snapshot: DashboardSnapshot): number {
    return snapshot.volumes.value?.filter(({ readWriteVerified }) =>
      readWriteVerified
    ).length ?? 0;
  }
}
