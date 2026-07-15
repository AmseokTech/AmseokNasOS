//--------------------------//
//--------应用外壳只负责全局布局与连接状态---------//
//--------The app shell owns global layout and connection state--------//
//-------------------------//
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatChipsModule } from '@angular/material/chips';
import { MatToolbarModule } from '@angular/material/toolbar';
import { RouterOutlet } from '@angular/router';
import { ApiHealthService } from './core/services/api-health.service';

type ApiStatus = 'checking' | 'online' | 'offline';

@Component({
  selector: 'app-root',
  imports: [MatChipsModule, MatToolbarModule, RouterOutlet],
  templateUrl: './app.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './app.component.scss'
})
export class AppComponent {
  private readonly apiHealthService = inject(ApiHealthService);

  readonly apiStatus = signal<ApiStatus>('checking');
  readonly apiStatusLabel = computed(() => {
    switch (this.apiStatus()) {
      case 'online':
        return 'API 已连接';
      case 'offline':
        return 'API 未连接';
      default:
        return '正在检查 API';
    }
  });

  constructor() {
    this.apiHealthService
      .getHealth()
      .pipe(takeUntilDestroyed())
      .subscribe({
        next: () => this.apiStatus.set('online'),
        error: () => this.apiStatus.set('offline')
      });
  }
}
