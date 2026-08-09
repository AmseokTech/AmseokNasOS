//--------------------------//
//--------加载并展示只读物理网卡信息---------//
//--------Loads and displays read-only physical network interface information--------//
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

import { TranslatePipe } from '../../../core/i18n';
import {
  NetworkConfigurationMode,
  NetworkInterfaceInformation
} from '../system-settings.models';
import { SystemSettingsService } from '../system-settings.service';

@Component({
  selector: 'app-network-settings',
  imports: [TranslatePipe],
  templateUrl: './network-settings.component.html',
  styleUrl: './network-settings.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class NetworkSettingsComponent implements OnInit {
  private readonly settings = inject(SystemSettingsService);
  private readonly destroyRef = inject(DestroyRef);

  readonly interfaces = signal<readonly NetworkInterfaceInformation[]>([]);
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
}
