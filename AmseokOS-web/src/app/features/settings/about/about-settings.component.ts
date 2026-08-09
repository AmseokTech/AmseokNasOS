//--------------------------//
//--------加载并展示只读本机硬件信息---------//
//--------Loads and displays read-only host hardware information--------//
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

import { LocalizedUptimePipe, TranslatePipe } from '../../../core/i18n';
import {
  formatBytes,
  storageUsagePercentage
} from '../settings-format';
import { SystemAbout } from '../system-settings.models';
import { SystemSettingsService } from '../system-settings.service';

@Component({
  selector: 'app-about-settings',
  imports: [LocalizedUptimePipe, TranslatePipe],
  templateUrl: './about-settings.component.html',
  styleUrl: './about-settings.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AboutSettingsComponent implements OnInit {
  private readonly settings = inject(SystemSettingsService);
  private readonly destroyRef = inject(DestroyRef);

  readonly about = signal<SystemAbout | null>(null);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly formatBytes = formatBytes;
  readonly storageUsagePercentage = storageUsagePercentage;

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    if (this.loading()) {
      return;
    }
    this.loading.set(true);
    this.error.set('');
    this.settings.getAbout().pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.loading.set(false))
    ).subscribe({
      next: (about) => this.about.set(about),
      error: (error: unknown) => {
        this.about.set(null);
        this.error.set(error instanceof Error ? error.message : '系统信息加载失败');
      }
    });
  }
}
