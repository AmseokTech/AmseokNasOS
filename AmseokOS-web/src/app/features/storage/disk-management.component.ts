//--------------------------//
//--------展示磁盘清单并编排 RAID 两阶段管理交互---------//
//--------Displays disk inventory and orchestrates two-phase RAID management interactions--------//
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
import { finalize, switchMap, takeWhile, timer } from 'rxjs';

import { DataVolumeManagementComponent } from './data-volume-management.component';
import { RaidAction, RaidOperation, RaidOperationPreview } from './raid-management.models';
import { RaidManagementService } from './raid-management.service';
import { BlockDevice, RaidArray, StorageInventory } from './storage-inventory.models';
import { StorageInventoryService } from './storage-inventory.service';

@Component({
  selector: 'app-disk-management',
  imports: [DataVolumeManagementComponent],
  templateUrl: './disk-management.component.html',
  styleUrl: './disk-management.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DiskManagementComponent implements OnInit {
  private readonly inventoryService = inject(StorageInventoryService);
  private readonly raidService = inject(RaidManagementService);
  private readonly destroyRef = inject(DestroyRef);

  readonly inventory = signal<StorageInventory | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly dialogOpen = signal(false);
  readonly selectedArray = signal<RaidArray | null>(null);
  readonly action = signal<RaidAction>('create');
  readonly arrayName = signal('data');
  readonly level = signal('raid1');
  readonly selectedDeviceIds = signal<readonly string[]>([]);
  readonly sourceDeviceId = signal('');
  readonly targetDeviceCount = signal<number | null>(null);
  readonly password = signal('');
  readonly confirmationPassword = signal('');
  readonly confirmationInput = signal('');
  readonly preview = signal<RaidOperationPreview | null>(null);
  readonly operation = signal<RaidOperation | null>(null);
  readonly actionError = signal('');
  readonly actionBusy = signal(false);
  readonly eligibleDisks = computed(() => (this.inventory()?.disks ?? []).filter(
    (disk) => this.isEligibleNewDisk(disk)
  ));

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

  openCreate(): void {
    this.resetActionState('create', null);
    this.dialogOpen.set(true);
  }

  openModify(array: RaidArray): void {
    this.resetActionState(this.actionsFor(array)[0] ?? 'delete', array);
    this.dialogOpen.set(true);
  }

  closeDialog(): void {
    this.dialogOpen.set(false);
    this.password.set('');
    this.confirmationPassword.set('');
  }

  selectAction(value: string): void {
    if (!this.isRaidAction(value)) {
      return;
    }
    this.action.set(value);
    this.invalidatePreview();
    const array = this.selectedArray();
    this.targetDeviceCount.set(array?.configuredDeviceCount ?? null);
  }

  updateArrayName(value: string): void {
    this.arrayName.set(value);
    this.invalidatePreview();
  }

  updateLevel(value: string): void {
    this.level.set(value);
    this.invalidatePreview();
  }

  updateSourceDevice(value: string): void {
    this.sourceDeviceId.set(value);
    this.invalidatePreview();
  }

  updateTargetDeviceCount(value: string): void {
    const parsed = Number.parseInt(value, 10);
    this.targetDeviceCount.set(Number.isFinite(parsed) ? parsed : null);
    this.invalidatePreview();
  }

  toggleDevice(deviceId: string, checked: boolean): void {
    const selected = new Set(this.selectedDeviceIds());
    if (checked) {
      selected.add(deviceId);
    } else {
      selected.delete(deviceId);
    }
    this.selectedDeviceIds.set([...selected]);
    this.invalidatePreview();
  }

  createPreview(): void {
    if (!this.password() || this.actionBusy()) {
      this.actionError.set('请输入当前管理员密码');
      return;
    }
    const array = this.selectedArray();
    this.actionBusy.set(true);
    this.actionError.set('');
    this.preview.set(null);
    this.operation.set(null);
    this.raidService.preview({
      action: this.action(),
      arrayId: array?.id ?? null,
      arrayName: this.action() === 'create' ? this.arrayName() : null,
      level: this.action() === 'create' ? this.level() : null,
      deviceIds: this.usesNewDevices() ? this.selectedDeviceIds() : [],
      sourceDeviceId: this.usesSourceDevice() ? this.sourceDeviceId() || null : null,
      targetDeviceCount: this.usesTargetCount() ? this.targetDeviceCount() : null
    }, this.password()).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.actionBusy.set(false))
    ).subscribe({
      next: (preview) => {
        this.password.set('');
        this.preview.set(preview);
      },
      error: (error: unknown) => {
        this.password.set('');
        this.actionError.set(error instanceof Error ? error.message : 'RAID 预检失败');
      }
    });
  }

  execute(): void {
    const preview = this.preview();
    if (!preview?.canExecute || !preview.previewToken || this.actionBusy()) {
      return;
    }
    if (!this.confirmationPassword()) {
      this.actionError.set('请再次输入当前管理员密码');
      return;
    }
    this.actionBusy.set(true);
    this.actionError.set('');
    this.raidService.execute(
      preview.previewToken,
      this.confirmationInput(),
      crypto.randomUUID(),
      this.confirmationPassword()
    ).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.actionBusy.set(false))
    ).subscribe({
      next: (operation) => {
        this.confirmationPassword.set('');
        this.operation.set(operation);
        if (this.needsPolling(operation)) {
          this.pollOperation(operation.operationId);
        } else {
          this.load();
        }
      },
      error: (error: unknown) => {
        this.confirmationPassword.set('');
        this.actionError.set(error instanceof Error ? error.message : 'RAID 操作启动失败');
      }
    });
  }

  actionsFor(array: RaidArray): readonly RaidAction[] {
    const level = array.level.toLowerCase().replace('-', '');
    const actions: RaidAction[] = ['delete'];
    if (level !== 'raid0' && level !== '0') {
      actions.unshift('addDevice', 'removeDevice', 'replaceDevice');
    }
    if (['raid0', '0', 'raid1', '1', 'raid5', '5', 'raid6', '6'].includes(level)) {
      actions.unshift('grow', 'shrink');
    }
    return actions;
  }

  actionLabel(action: RaidAction): string {
    return {
      create: '创建阵列',
      delete: '删除阵列',
      addDevice: '添加磁盘',
      removeDevice: '移除成员',
      replaceDevice: '替换成员',
      grow: '扩容阵列',
      shrink: '缩容阵列'
    }[action];
  }

  memberDevices(array: RaidArray | null): readonly { id: string; label: string }[] {
    if (!array) {
      return [];
    }
    const disks = this.inventory()?.disks ?? [];
    return array.members.flatMap((member) => {
      const disk = disks.find((candidate) =>
        candidate.path === member.path || candidate.partitions.some((partition) => partition.path === member.path)
      );
      return disk ? [{ id: disk.id, label: `${disk.model || disk.name} · ${member.path}` }] : [];
    }).filter((value, index, values) => values.findIndex((candidate) => candidate.id === value.id) === index);
  }

  usesNewDevices(): boolean {
    return ['create', 'addDevice', 'replaceDevice', 'grow'].includes(this.action());
  }

  usesSourceDevice(): boolean {
    return ['removeDevice', 'replaceDevice'].includes(this.action());
  }

  usesTargetCount(): boolean {
    return ['grow', 'shrink'].includes(this.action());
  }

  warningLabel(code: string): string {
    return {
      'raid.selected_disks_will_be_erased': '所选新磁盘上的数据将被永久清除。',
      'raid.all_array_data_will_be_destroyed': '阵列及其全部数据将被永久删除。',
      'raid.array_may_become_degraded': '移除活动成员会让阵列进入降级状态。',
      'raid.reshape_may_take_a_long_time': '重塑可能持续很长时间，期间不要关机或拔盘。',
      'raid.shrink_requires_raw_unmounted_array': '缩容只允许没有文件系统签名且未挂载的原始阵列。',
      'raid.reshape_backup_required': 'mdadm 会使用重塑 backup-file；它不是用户数据备份。'
    }[code] ?? code;
  }

  operationLabel(operation: RaidOperation): string {
    if (operation.status === 'succeeded') {
      return '操作已完成';
    }
    if (operation.status === 'running') {
      return operation.progressPercentage === null
        ? '操作正在后台执行'
        : `操作正在后台执行（${operation.progressPercentage}%）`;
    }
    if (operation.status === 'interrupted') {
      return '连接中断，正在根据真实阵列状态复核结果';
    }
    return `操作失败${operation.errorCode ? `：${operation.errorCode}` : ''}`;
  }

  private pollOperation(operationId: string): void {
    timer(1500, 2500).pipe(
      switchMap(() => this.raidService.getOperation(operationId)),
      takeWhile((operation) => this.needsPolling(operation), true),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: (operation) => {
        this.operation.set(operation);
        if (!this.needsPolling(operation)) {
          this.load();
        }
      },
      error: (error: unknown) => {
        this.actionError.set(error instanceof Error ? error.message : '无法查询 RAID 操作进度');
      }
    });
  }

  private resetActionState(action: RaidAction, array: RaidArray | null): void {
    this.action.set(action);
    this.selectedArray.set(array);
    this.arrayName.set('data');
    this.level.set('raid1');
    this.selectedDeviceIds.set([]);
    this.sourceDeviceId.set('');
    this.targetDeviceCount.set(array?.configuredDeviceCount ?? null);
    this.password.set('');
    this.confirmationPassword.set('');
    this.confirmationInput.set('');
    this.preview.set(null);
    this.operation.set(null);
    this.actionError.set('');
  }

  invalidatePreview(): void {
    this.preview.set(null);
    this.confirmationInput.set('');
    this.confirmationPassword.set('');
    this.operation.set(null);
    this.actionError.set('');
  }

  private isEligibleNewDisk(disk: BlockDevice): boolean {
    return disk.stable
      && !disk.identityConflict
      && disk.topologyComplete
      && !disk.systemDevice
      && !disk.swap
      && !disk.raidMember
      && !disk.inUse
      && !disk.readOnly
      && !disk.removable
      && disk.partitions.length === 0
      && disk.mountPoints.length === 0
      && disk.dependentDevices.length === 0;
  }

  private needsPolling(operation: RaidOperation): boolean {
    return operation.status === 'running' || operation.status === 'interrupted';
  }

  private isRaidAction(value: string): value is RaidAction {
    return ['create', 'delete', 'addDevice', 'removeDevice', 'replaceDevice', 'grow', 'shrink'].includes(value);
  }
}
