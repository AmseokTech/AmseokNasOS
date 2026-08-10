//--------------------------//
//--------展示并编排 ext4 数据卷、目录权限、读写校验与 SMB/NFS---------//
//--------Displays and orchestrates ext4 volumes, permissions, verification, and SMB/NFS--------//
//-------------------------//
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  input,
  signal
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize, switchMap, takeWhile, timer } from 'rxjs';

import {
  ManagedVolume,
  NfsShareSettings,
  SmbShareSettings,
  StorageAction,
  StorageOperation,
  StorageOperationPreview,
  StorageOperationRequest
} from './storage-management.models';
import { StorageManagementService } from './storage-management.service';
import { RaidArray } from './storage-inventory.models';

@Component({
  selector: 'app-data-volume-management',
  templateUrl: './data-volume-management.component.html',
  styleUrl: './data-volume-management.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class DataVolumeManagementComponent implements OnInit {
  private readonly storage = inject(StorageManagementService);
  private readonly destroyRef = inject(DestroyRef);

  readonly arrays = input.required<readonly RaidArray[]>();
  readonly volumes = signal<readonly ManagedVolume[]>([]);
  readonly loading = signal(false);
  readonly loadError = signal('');
  readonly dialogOpen = signal(false);
  readonly action = signal<StorageAction>('provisionVolume');
  readonly selectedArray = signal<RaidArray | null>(null);
  readonly selectedVolume = signal<ManagedVolume | null>(null);
  readonly volumeName = signal('data');
  readonly ownerName = signal('root');
  readonly groupName = signal('amseoknas-data');
  readonly directoryMode = signal('0770');
  readonly smbEnabled = signal(false);
  readonly smbShareName = signal('data');
  readonly smbReadOnly = signal(false);
  readonly smbGuestAccess = signal(false);
  readonly smbAllowedNetwork = signal('192.168.188.0/24');
  readonly nfsEnabled = signal(false);
  readonly nfsClientNetwork = signal('192.168.188.0/24');
  readonly nfsReadOnly = signal(false);
  readonly password = signal('');
  readonly confirmationPassword = signal('');
  readonly confirmationInput = signal('');
  readonly preview = signal<StorageOperationPreview | null>(null);
  readonly operation = signal<StorageOperation | null>(null);
  readonly actionError = signal('');
  readonly actionBusy = signal(false);
  readonly availableArrays = computed(() => this.arrays().filter((array) =>
    array.degradedDeviceCount === 0
      && array.syncAction.toLowerCase() === 'idle'
      && !this.volumes().some((volume) => volume.arrayId === array.id)
  ));

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    if (this.loading()) {
      return;
    }
    this.loading.set(true);
    this.loadError.set('');
    this.storage.getVolumes().pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.loading.set(false))
    ).subscribe({
      next: (volumes) => this.volumes.set(volumes),
      error: (error: unknown) => {
        this.volumes.set([]);
        this.loadError.set(error instanceof Error ? error.message : '数据卷信息加载失败');
      }
    });
  }

  openProvision(): void {
    this.reset('provisionVolume', null);
    this.selectedArray.set(this.availableArrays()[0] ?? null);
    this.dialogOpen.set(true);
  }

  openAction(action: Exclude<StorageAction, 'provisionVolume'>, volume: ManagedVolume): void {
    this.reset(action, volume);
    this.dialogOpen.set(true);
  }

  closeDialog(): void {
    this.dialogOpen.set(false);
    this.password.set('');
    this.confirmationPassword.set('');
  }

  updateArray(arrayId: string): void {
    this.selectedArray.set(this.availableArrays().find((array) => array.id === arrayId) ?? null);
    this.invalidatePreview();
  }

  setText(target: 'volumeName' | 'ownerName' | 'groupName' | 'directoryMode' | 'smbShareName'
    | 'smbAllowedNetwork' | 'nfsClientNetwork', value: string): void {
    this[target].set(value);
    this.invalidatePreview();
  }

  setToggle(target: 'smbEnabled' | 'smbReadOnly' | 'smbGuestAccess' | 'nfsEnabled' | 'nfsReadOnly',
    value: boolean): void {
    this[target].set(value);
    this.invalidatePreview();
  }

  createPreview(): void {
    if (!this.password() || this.actionBusy()) {
      this.actionError.set('请输入当前管理员密码');
      return;
    }
    const request = this.buildRequest();
    if (!request) {
      this.actionError.set(this.action() === 'provisionVolume' ? '请选择可用的健康 RAID 阵列' : '数据卷已不存在');
      return;
    }
    this.actionBusy.set(true);
    this.actionError.set('');
    this.preview.set(null);
    this.operation.set(null);
    this.storage.preview(request, this.password()).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.actionBusy.set(false))
    ).subscribe({
      next: (preview) => {
        this.password.set('');
        this.preview.set(preview);
      },
      error: (error: unknown) => {
        this.password.set('');
        this.actionError.set(error instanceof Error ? error.message : '数据卷预检失败');
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
    this.storage.execute(
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
        this.actionError.set(error instanceof Error ? error.message : '数据卷操作启动失败');
      }
    });
  }

  actionLabel(action: StorageAction): string {
    return {
      provisionVolume: '初始化 ext4 数据卷',
      updatePermissions: '修改数据目录权限',
      configureShares: '配置 SMB/NFS 共享',
      verifyReadWrite: '校验文件写入与读取'
    }[action];
  }

  warningLabel(code: string): string {
    return {
      'storage.array_data_will_be_destroyed': '阵列上的现有数据将被永久清除并格式化为 ext4。',
      'storage.ext4_only': '当前版本固定使用 ext4 文件系统。',
      'storage.smb_guest_access_enabled': 'SMB 来宾访问无需账户，请严格限制局域网网段。',
      'storage.nfs_root_squash_enabled': 'NFS 固定启用 root_squash，客户端 root 不会获得本机 root 权限。'
    }[code] ?? code;
  }

  operationLabel(operation: StorageOperation): string {
    if (operation.status === 'succeeded') {
      return '操作已完成';
    }
    if (operation.status === 'running') {
      return '操作正在执行';
    }
    if (operation.status === 'interrupted') {
      return '连接中断，正在根据真实挂载状态复核';
    }
    return `操作失败${operation.errorCode ? `：${operation.errorCode}` : ''}`;
  }

  shareSummary(volume: ManagedVolume): string {
    const values: string[] = [];
    if (volume.smb.enabled) {
      values.push(`SMB \\${volume.smb.shareName ?? volume.name}`);
    }
    if (volume.nfs.enabled) {
      values.push(`NFS ${volume.nfs.clientNetwork ?? ''}`.trim());
    }
    return values.length === 0 ? '未启用' : values.join(' · ');
  }

  invalidatePreview(): void {
    this.preview.set(null);
    this.confirmationInput.set('');
    this.confirmationPassword.set('');
    this.operation.set(null);
    this.actionError.set('');
  }

  private buildRequest(): StorageOperationRequest | null {
    const action = this.action();
    const volume = this.selectedVolume();
    const array = this.selectedArray();
    if (action === 'provisionVolume' && !array) {
      return null;
    }
    if (action !== 'provisionVolume' && !volume) {
      return null;
    }
    const includesPermissions = action === 'provisionVolume' || action === 'updatePermissions';
    const includesShares = action === 'provisionVolume' || action === 'configureShares';
    return {
      action,
      arrayId: action === 'provisionVolume' ? array?.id ?? null : null,
      volumeId: action === 'provisionVolume' ? null : volume?.id ?? null,
      volumeName: action === 'provisionVolume' ? this.volumeName() : null,
      ownerName: includesPermissions ? this.ownerName() : null,
      groupName: includesPermissions ? this.groupName() : null,
      directoryMode: includesPermissions ? this.directoryMode() : null,
      smb: includesShares ? this.smbSettings() : null,
      nfs: includesShares ? this.nfsSettings() : null
    };
  }

  private smbSettings(): SmbShareSettings {
    return {
      enabled: this.smbEnabled(),
      shareName: this.smbEnabled() ? this.smbShareName() : null,
      readOnly: this.smbReadOnly(),
      guestAccess: this.smbGuestAccess(),
      allowedNetwork: this.smbEnabled() ? this.smbAllowedNetwork() : null
    };
  }

  private nfsSettings(): NfsShareSettings {
    return {
      enabled: this.nfsEnabled(),
      clientNetwork: this.nfsEnabled() ? this.nfsClientNetwork() : null,
      readOnly: this.nfsReadOnly()
    };
  }

  private pollOperation(operationId: string): void {
    timer(1200, 2000).pipe(
      switchMap(() => this.storage.getOperation(operationId)),
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
        this.actionError.set(error instanceof Error ? error.message : '无法查询数据卷操作状态');
      }
    });
  }

  private reset(action: StorageAction, volume: ManagedVolume | null): void {
    this.action.set(action);
    this.selectedVolume.set(volume);
    this.selectedArray.set(null);
    this.volumeName.set('data');
    this.ownerName.set(volume?.ownerName ?? 'root');
    this.groupName.set(volume?.groupName ?? 'amseoknas-data');
    this.directoryMode.set(volume?.directoryMode ?? '0770');
    this.smbEnabled.set(volume?.smb.enabled ?? false);
    this.smbShareName.set(volume?.smb.shareName ?? volume?.name ?? 'data');
    this.smbReadOnly.set(volume?.smb.readOnly ?? false);
    this.smbGuestAccess.set(volume?.smb.guestAccess ?? false);
    this.smbAllowedNetwork.set(volume?.smb.allowedNetwork ?? '192.168.188.0/24');
    this.nfsEnabled.set(volume?.nfs.enabled ?? false);
    this.nfsClientNetwork.set(volume?.nfs.clientNetwork ?? '192.168.188.0/24');
    this.nfsReadOnly.set(volume?.nfs.readOnly ?? false);
    this.password.set('');
    this.confirmationPassword.set('');
    this.confirmationInput.set('');
    this.preview.set(null);
    this.operation.set(null);
    this.actionError.set('');
  }

  private needsPolling(operation: StorageOperation): boolean {
    return operation.status === 'running' || operation.status === 'interrupted';
  }
}
