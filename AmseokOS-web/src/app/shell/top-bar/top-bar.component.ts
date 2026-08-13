//--------------------------//
//--------展示桌面顶部的产品、当前应用、系统摘要与本地通知---------//
//--------Presents product, active app, system summary, and local notifications--------//
//-------------------------//
import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  HostListener,
  OnDestroy,
  computed,
  inject,
  input,
  signal,
  viewChild
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import {
  DateAdapter,
  MAT_DATE_LOCALE,
  NativeDateAdapter,
  provideNativeDateAdapter
} from '@angular/material/core';
import { MatCalendar } from '@angular/material/datepicker';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';

class NumericDateAdapter extends NativeDateAdapter {
  override getDateNames(): string[] {
    return Array.from({ length: 31 }, (_, index) => String(index + 1));
  }
}

interface DesktopNotification {
  readonly id: string;
  readonly title: string;
  readonly description: string;
  readonly time: string;
  readonly read: boolean;
}

interface WifiNetwork {
  readonly id: string;
  readonly name: string;
  readonly strength: 1 | 2 | 3;
  readonly secured: boolean;
}

const WIFI_NETWORKS: readonly WifiNetwork[] = [
  { id: 'home', name: 'HomeNet-5G', strength: 3, secured: true },
  { id: 'office', name: 'Amseok-Office', strength: 3, secured: true },
  { id: 'guest', name: 'Guest Network', strength: 2, secured: false },
  { id: 'backup', name: 'NAS-Backup', strength: 1, secured: true }
];

const INITIAL_NOTIFICATIONS: readonly DesktopNotification[] = [
  {
    id: 'app-store-demo',
    title: '应用商店演示已准备就绪',
    description: '可在应用商店查看内置应用详情与下载演示。',
    time: '刚刚',
    read: false
  },
  {
    id: 'security-reminder',
    title: '安全设置提醒',
    description: '首次使用时，请完成管理员密码设置。',
    time: '今天',
    read: false
  },
  {
    id: 'system-ready',
    title: '系统服务已就绪',
    description: '本地桌面与基础服务连接正常。',
    time: '今天',
    read: true
  }
];

@Component({
  selector: 'app-top-bar',
  imports: [DatePipe, MatButtonModule, MatCalendar, MatSlideToggleModule],
  providers: [
    { provide: MAT_DATE_LOCALE, useValue: 'zh-CN' },
    provideNativeDateAdapter(),
    { provide: DateAdapter, useClass: NumericDateAdapter }
  ],
  templateUrl: './top-bar.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './top-bar.component.scss'
})
export class TopBarComponent implements OnDestroy {
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly calendar = viewChild(MatCalendar<Date>);

  readonly activeApp = input.required<string>();
  readonly currentTime = signal(new Date());
  readonly selectedDate = signal(new Date());
  readonly selectedDateLabel = computed(() =>
    new Intl.DateTimeFormat('zh-CN', {
      year: 'numeric',
      month: 'long',
      day: 'numeric',
      weekday: 'long'
    }).format(this.selectedDate())
  );
  readonly notifications = signal<readonly DesktopNotification[]>(INITIAL_NOTIFICATIONS);
  readonly dateTimePanelOpen = signal(false);
  readonly notificationCenterOpen = signal(false);
  readonly wifiPanelOpen = signal(false);
  readonly wifiEnabled = signal(true);
  readonly wifiNetworks = WIFI_NETWORKS;
  readonly selectedWifiId = signal('home');
  readonly unreadNotificationCount = computed(
    () => this.notifications().filter((notification) => !notification.read).length
  );

  private readonly clockInterval = window.setInterval(() => this.currentTime.set(new Date()), 30_000);

  ngOnDestroy(): void {
    window.clearInterval(this.clockInterval);
  }

  toggleNotificationCenter(): void {
    this.dateTimePanelOpen.set(false);
    this.wifiPanelOpen.set(false);
    this.notificationCenterOpen.update((isOpen) => !isOpen);
  }

  toggleDateTimePanel(): void {
    this.notificationCenterOpen.set(false);
    this.wifiPanelOpen.set(false);
    this.dateTimePanelOpen.update((isOpen) => !isOpen);
  }

  toggleWifiPanel(): void {
    this.dateTimePanelOpen.set(false);
    this.notificationCenterOpen.set(false);
    this.wifiPanelOpen.update((isOpen) => !isOpen);
  }

  setWifiEnabled(enabled: boolean): void {
    this.wifiEnabled.set(enabled);
    if (!enabled) {
      this.selectedWifiId.set('');
    }
  }

  selectWifiNetwork(id: string): void {
    if (this.wifiEnabled()) {
      this.selectedWifiId.set(id);
    }
  }

  selectDate(date: Date | null): void {
    if (date) {
      this.selectedDate.set(date);
    }
  }

  selectToday(): void {
    const today = new Date();
    this.selectedDate.set(today);

    const calendar = this.calendar();
    if (calendar) {
      calendar.activeDate = today;
    }
  }

  markAllNotificationsRead(): void {
    this.notifications.update((notifications) =>
      notifications.map((notification) => (notification.read ? notification : { ...notification, read: true }))
    );
  }

  dismissNotification(id: string): void {
    this.notifications.update((notifications) =>
      notifications.filter((notification) => notification.id !== id)
    );
  }

  @HostListener('document:click', ['$event'])
  closeOpenPanelsOnOutsideClick(event: MouseEvent): void {
    const target = event.target;
    if (
      (!this.notificationCenterOpen() && !this.dateTimePanelOpen() && !this.wifiPanelOpen()) ||
      (target instanceof Node && this.host.nativeElement.contains(target))
    ) {
      return;
    }

    this.notificationCenterOpen.set(false);
    this.dateTimePanelOpen.set(false);
    this.wifiPanelOpen.set(false);
  }

  @HostListener('document:keydown.escape')
  closeOpenPanelsOnEscape(): void {
    this.notificationCenterOpen.set(false);
    this.dateTimePanelOpen.set(false);
    this.wifiPanelOpen.set(false);
  }
}
