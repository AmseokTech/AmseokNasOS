//--------------------------//
//--------加载并展示磁盘与 RAID 只读清单---------//
//--------Loads and displays read-only disk and RAID inventory--------//
//-------------------------//
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  inject,
  signal
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';

import { RaidArray, StorageInventory } from './storage-inventory.models';
import { StorageInventoryService } from './storage-inventory.service';

@Component({
  selector: 'app-disk-management',
  templateUrl: './disk-management.component.html',
  styleUrl: './disk-management.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DiskManagementComponent implements OnInit {
  private readonly inventoryService = inject(StorageInventoryService);
  private readonly destroyRef = inject(DestroyRef);

  readonly inventory = signal<StorageInventory | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    if (this.loading()) {
      return;
    }

    this.loading.set(true);
    this.error.set('');
    this.inventoryService.getInventory().pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.loading.set(false))
    ).subscribe({
      next: (inventory) => this.inventory.set(inventory),
      error: (error: unknown) => {
        this.inventory.set(null);
        this.error.set(error instanceof Error ? error.message : '磁盘信息加载失败');
      }
    });
  }

  formatBytes(bytes: number): string {
    if (!Number.isFinite(bytes) || bytes <= 0) {
      return '0 B';
    }

    const units = ['B', 'KB', 'MB', 'GB', 'TB', 'PB'];
    const unitIndex = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1);
    const value = bytes / 1024 ** unitIndex;
    return `${value.toLocaleString('zh-CN', { maximumFractionDigits: unitIndex === 0 ? 0 : 1 })} ${units[unitIndex]}`;
  }

  arrayStateLabel(array: RaidArray): string {
    if (array.degradedDeviceCount > 0) {
      return `已降级（缺少 ${array.degradedDeviceCount} 块）`;
    }

    const state = array.state.toLowerCase();
    if (state.includes('clean') || state.includes('active')) {
      return '正常';
    }
    return array.state || '未知';
  }

  degradedArrayCount(arrays: readonly RaidArray[]): number {
    return arrays.filter((array) => array.degradedDeviceCount > 0).length;
  }

  syncPercentage(array: RaidArray): number | null {
    if (
      array.syncCompletedSectors === null ||
      array.syncTotalSectors === null ||
      array.syncTotalSectors <= 0
    ) {
      return null;
    }

    return Math.min(100, Math.max(0, array.syncCompletedSectors / array.syncTotalSectors * 100));
  }
}
