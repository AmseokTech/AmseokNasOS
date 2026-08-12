//--------------------------//
//--------加载并展示只读物理网卡信息---------//
//--------Loads and displays read-only physical network interface information--------//
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
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';

import {
  NetworkConfigurationMode,
  NetworkInterfaceInformation
} from '../system-settings.models';
import { SystemSettingsService } from '../system-settings.service';
import {
  NetworkConfigurationOperation,
  NetworkConfigurationPreview,
  NetworkConfigurationRequest
} from './network-configuration.models';
import { NetworkConfigurationService } from './network-configuration.service';

@Component({
  selector: 'app-network-settings',
  imports: [ReactiveFormsModule],
  templateUrl: './network-settings.component.html',
  styleUrl: './network-settings.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class NetworkSettingsComponent implements OnInit {
  private readonly settings = inject(SystemSettingsService);
  private readonly configurations = inject(NetworkConfigurationService);
  private readonly forms = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  readonly interfaces = signal<readonly NetworkInterfaceInformation[]>([]);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly editorInterfaceId = signal<string | null>(null);
  readonly preview = signal<NetworkConfigurationPreview | null>(null);
  readonly operation = signal<NetworkConfigurationOperation | null>(null);
  readonly operationError = signal('');
  readonly operationMessage = signal('');
  readonly working = signal(false);
  readonly mode = signal<'dhcp' | 'static'>('dhcp');
  readonly isStatic = computed(() => this.mode() === 'static');
  readonly form = this.forms.nonNullable.group({
    mode: this.forms.nonNullable.control<'dhcp' | 'static'>('dhcp'),
    ipAddress: ['', Validators.maxLength(64)],
    subnetMask: ['', Validators.maxLength(64)],
    gateway: ['', Validators.maxLength(64)],
    password: ['', [Validators.required, Validators.maxLength(256)]]
  });

  constructor() {
    this.form.controls.mode.valueChanges.pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe((mode) => {
      this.mode.set(mode);
      this.preview.set(null);
      this.operationError.set('');
      if (mode === 'dhcp') {
        this.form.patchValue({ ipAddress: '', subnetMask: '', gateway: '' });
      }
    });
  }

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    if (this.loading()) {
      return;
    }
    this.loading.set(true);
    this.error.set('');
    this.settings.getNetworkInterfaces().pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.loading.set(false))
    ).subscribe({
      next: (interfaces) => this.interfaces.set(interfaces),
      error: (error: unknown) => {
        this.interfaces.set([]);
        this.error.set(error instanceof Error ? error.message : '网络信息加载失败');
      }
    });
  }

  modeLabel(mode: NetworkConfigurationMode): string {
    switch (mode) {
      case 'dhcp':
        return 'DHCP';
      case 'static':
        return '固定地址';
      case 'unknown':
        return '配置来源未知';
      default:
        return '未配置';
    }
  }

  linkLabel(linkState: string): string {
    return linkState.toLowerCase() === 'up' ? '已连接' : '未连接';
  }

  openEditor(network: NetworkInterfaceInformation): void {
    const address = network.addresses.find((value) => value.includes('.')) ?? '';
    const [ipAddress, prefix] = address.split('/');
    const mode = network.configurationMode === 'static' ? 'static' : 'dhcp';
    this.editorInterfaceId.set(network.id);
    this.preview.set(null);
    this.operation.set(null);
    this.operationError.set('');
    this.operationMessage.set('');
    this.mode.set(mode);
    this.form.reset({
      mode,
      ipAddress: mode === 'static' ? ipAddress : '',
      subnetMask: mode === 'static' ? this.maskFromPrefix(prefix) : '',
      gateway: mode === 'static' ? (network.gateway ?? '') : '',
      password: ''
    });
  }

  closeEditor(): void {
    if (this.working() || this.operation()?.state === 'awaitingConfirmation') {
      return;
    }
    this.editorInterfaceId.set(null);
    this.preview.set(null);
    this.form.controls.password.setValue('');
  }

  createPreview(interfaceId: string): void {
    if (this.form.invalid || this.working()) {
      this.form.markAllAsTouched();
      return;
    }
    this.run(
      this.configurations.preview(this.request(interfaceId)),
      (preview) => {
        this.preview.set(preview);
        this.operationMessage.set('预览已生成，请核对后应用。');
      }
    );
  }

  apply(interfaceId: string): void {
    if (!this.preview()?.canApply || this.form.invalid || this.working()) {
      return;
    }
    this.run(
      this.configurations.apply(this.request(interfaceId)),
      (operation) => {
        this.operation.set(operation);
        this.preview.set(null);
        this.form.controls.password.setValue('');
        this.operationMessage.set('新配置已应用，请在截止时间前确认连通性。');
      }
    );
  }

  confirm(): void {
    const operation = this.operation();
    if (!operation || this.working()) {
      return;
    }
    this.run(this.configurations.confirm(operation.operationId), (confirmed) => {
      this.operation.set(confirmed);
      this.operationMessage.set('网络配置已确认并永久保留。');
      this.load();
    });
  }

  rollback(): void {
    const operation = this.operation();
    if (!operation || this.working()) {
      return;
    }
    this.run(this.configurations.rollback(operation.operationId), (rolledBack) => {
      this.operation.set(rolledBack);
      this.operationMessage.set('已恢复修改前的网络配置。');
      this.load();
    });
  }

  deadlineLabel(value: string | null): string {
    return value ? new Date(value).toLocaleString() : '—';
  }

  newAddressUrl(): string | null {
    const address = this.operation()?.state === 'awaitingConfirmation'
      ? this.form.controls.ipAddress.value.trim()
      : '';
    if (this.mode() !== 'static' || !address) {
      return null;
    }
    return `${location.protocol}//${address}${location.port ? `:${location.port}` : ''}/`;
  }

  private request(interfaceId: string): NetworkConfigurationRequest {
    const value = this.form.getRawValue();
    const staticMode = value.mode === 'static';
    return {
      interfaceId,
      mode: value.mode,
      ipAddress: staticMode ? value.ipAddress.trim() || null : null,
      subnetMask: staticMode ? value.subnetMask.trim() || null : null,
      gateway: staticMode ? value.gateway.trim() || null : null,
      password: value.password
    };
  }

  private run<T>(source: import('rxjs').Observable<T>, success: (value: T) => void): void {
    this.working.set(true);
    this.operationError.set('');
    source.pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.working.set(false))
    ).subscribe({
      next: success,
      error: (error: unknown) => this.operationError.set(
        error instanceof Error ? error.message : '网络配置操作失败'
      )
    });
  }

  private maskFromPrefix(prefix: string | undefined): string {
    const bits = Number(prefix);
    if (!Number.isInteger(bits) || bits < 1 || bits > 30) {
      return '';
    }
    const mask = (0xffffffff << (32 - bits)) >>> 0;
    return [24, 16, 8, 0].map((shift) => (mask >>> shift) & 255).join('.');
  }
}
