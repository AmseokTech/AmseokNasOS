//--------------------------//
//--------通过公开功能入口聚合只读概览数据---------//
//--------Aggregates read-only dashboard data through public feature entry points--------//
//-------------------------//

import { inject, Injectable } from '@angular/core';
import { catchError, forkJoin, map, Observable, of, switchMap } from 'rxjs';

import { ApiHealthService } from '../../core/services/api-health.service';
import { SystemSettingsService } from '../settings';
import type { SystemPerformanceSnapshot } from '../settings';
import {
  StorageInventoryService,
  StorageManagementService
} from '../storage';
import type { DiskSmartStatus, StorageInventory } from '../storage';
import {
  DashboardPerformanceSample,
  DashboardSection,
  DashboardSnapshot,
  SmartSummary
} from './dashboard.models';

@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly health = inject(ApiHealthService);
  private readonly settings = inject(SystemSettingsService);
  private readonly storage = inject(StorageInventoryService);
  private readonly storageManagement = inject(StorageManagementService);
  private previousPerformance: SystemPerformanceSnapshot | null = null;

  load(): Observable<DashboardSnapshot> {
    return forkJoin({
      health: this.capture(this.health.getHealth()),
      about: this.capture(this.settings.getAbout()),
      network: this.capture(this.settings.getNetworkInterfaces()),
      storage: this.capture(this.storage.getInventory()),
      volumes: this.capture(this.storageManagement.getVolumes())
    }).pipe(
      switchMap((base) => this.loadSmartSummary(base.storage).pipe(
        map((smart) => ({ ...base, smart }))
      ))
    );
  }

  samplePerformance(): Observable<DashboardPerformanceSample> {
    return this.settings.getPerformance().pipe(
      map((current) => {
        const sample = this.toPerformanceSample(current, this.previousPerformance);
        this.previousPerformance = current;
        return sample;
      })
    );
  }

  private toPerformanceSample(
    current: SystemPerformanceSnapshot,
    previous: SystemPerformanceSnapshot | null
  ): DashboardPerformanceSample {
    const elapsedMilliseconds = previous
      ? current.capturedAtUnixMilliseconds - previous.capturedAtUnixMilliseconds
      : 0;
    const elapsedSeconds = elapsedMilliseconds / 1000;
    const previousCores = new Map(
      previous?.cpu.logicalProcessors.map((counter) => [counter.id, counter]) ?? []
    );
    const previousDisks = new Map(previous?.disks.map((disk) => [disk.id, disk]) ?? []);
    const previousNetworks = new Map(
      previous?.networks.map((network) => [network.id, network]) ?? []
    );

    return {
      capturedAtUnixMilliseconds: current.capturedAtUnixMilliseconds,
      cpu: {
        model: current.cpu.model,
        physicalCoreCount: current.cpu.physicalCoreCount,
        logicalProcessorCount: current.cpu.logicalProcessorCount,
        currentFrequencyMhz: current.cpu.currentFrequencyMhz,
        maximumFrequencyMhz: current.cpu.maximumFrequencyMhz,
        l1CacheBytes: current.cpu.l1CacheBytes,
        l2CacheBytes: current.cpu.l2CacheBytes,
        l3CacheBytes: current.cpu.l3CacheBytes,
        utilizationPercent: this.cpuUtilization(
          current.cpu.aggregate,
          previous?.cpu.aggregate ?? null
        ),
        logicalProcessors: current.cpu.logicalProcessors.map((counter) => ({
          id: counter.id,
          utilizationPercent: this.cpuUtilization(
            counter,
            previousCores.get(counter.id) ?? null
          )
        }))
      },
      memory: {
        ...current.memory,
        utilizationPercent: this.percentage(
          current.memory.usedBytes,
          current.memory.totalBytes
        ) ?? 0
      },
      disks: current.disks.map((disk) => {
        const previousDisk = previousDisks.get(disk.id);
        return {
          ...disk,
          readBytesPerSecond: this.rate(
            disk.readBytes,
            previousDisk?.readBytes ?? null,
            elapsedSeconds
          ),
          writtenBytesPerSecond: this.rate(
            disk.writtenBytes,
            previousDisk?.writtenBytes ?? null,
            elapsedSeconds
          ),
          activePercent: previousDisk
            ? this.percentage(
              disk.busyMilliseconds - previousDisk.busyMilliseconds,
              elapsedMilliseconds
            )
            : null
        };
      }),
      networks: current.networks.map((network) => {
        const previousNetwork = previousNetworks.get(network.id);
        return {
          ...network,
          receivedBytesPerSecond: this.rate(
            network.receivedBytes,
            previousNetwork?.receivedBytes ?? null,
            elapsedSeconds
          ),
          transmittedBytesPerSecond: this.rate(
            network.transmittedBytes,
            previousNetwork?.transmittedBytes ?? null,
            elapsedSeconds
          )
        };
      }),
      gpus: current.gpus
    };
  }

  private cpuUtilization(
    current: SystemPerformanceSnapshot['cpu']['aggregate'],
    previous: SystemPerformanceSnapshot['cpu']['aggregate'] | null
  ): number | null {
    if (!previous) {
      return null;
    }
    const totalTicks = current.totalTicks - previous.totalTicks;
    const idleTicks = current.idleTicks - previous.idleTicks;
    return totalTicks > 0 && idleTicks >= 0
      ? this.percentage(totalTicks - idleTicks, totalTicks)
      : null;
  }

  private rate(current: number, previous: number | null, elapsedSeconds: number): number | null {
    const delta = previous === null ? -1 : current - previous;
    return delta >= 0 && elapsedSeconds > 0 ? delta / elapsedSeconds : null;
  }

  private percentage(value: number, total: number): number | null {
    return Number.isFinite(value) && Number.isFinite(total) && value >= 0 && total > 0
      ? Math.min(100, value / total * 100)
      : null;
  }

  private loadSmartSummary(
    storage: DashboardSection<StorageInventory>
  ): Observable<DashboardSection<SmartSummary>> {
    const eligibleDisks = storage.value?.disks.filter(
      ({ stable, identityConflict }) => stable && !identityConflict
    ) ?? [];
    if (eligibleDisks.length === 0) {
      return of({
        value: this.summarize([]),
        error: storage.error
      });
    }

    return forkJoin(eligibleDisks.map(({ id }) => this.storage.getSmart(id).pipe(
      map(({ status }) => status),
      catchError(() => of<DiskSmartStatus>('unknown'))
    ))).pipe(
      map((statuses) => ({ value: this.summarize(statuses), error: null }))
    );
  }

  private summarize(statuses: readonly DiskSmartStatus[]): SmartSummary {
    const count = (status: DiskSmartStatus): number =>
      statuses.filter((value) => value === status).length;
    return {
      queried: statuses.length,
      healthy: count('healthy'),
      warning: count('warning'),
      failing: count('failing'),
      unsupported: count('unsupported'),
      unknown: count('unknown')
    };
  }

  private capture<T>(source: Observable<T>): Observable<DashboardSection<T>> {
    return source.pipe(
      map((value) => ({ value, error: null })),
      catchError((error: unknown) => of({
        value: null,
        error: error instanceof Error ? error.message : '数据加载失败'
      }))
    );
  }
}
