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
  signal
} from '@angular/core';

interface DesktopNotification {
  readonly id: string;
  readonly title: string;
  readonly description: string;
  readonly time: string;
  readonly read: boolean;
}

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
  imports: [DatePipe],
  templateUrl: './top-bar.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './top-bar.component.scss'
})
export class TopBarComponent implements OnDestroy {
  private readonly host = inject(ElementRef<HTMLElement>);

  readonly activeApp = input.required<string>();
  readonly currentTime = signal(new Date());
  readonly notifications = signal<readonly DesktopNotification[]>(INITIAL_NOTIFICATIONS);
  readonly notificationCenterOpen = signal(false);
  readonly unreadNotificationCount = computed(
    () => this.notifications().filter((notification) => !notification.read).length
  );

  private readonly clockInterval = window.setInterval(() => this.currentTime.set(new Date()), 30_000);

  ngOnDestroy(): void {
    window.clearInterval(this.clockInterval);
  }

  toggleNotificationCenter(): void {
    this.notificationCenterOpen.update((isOpen) => !isOpen);
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
  closeNotificationCenterOnOutsideClick(event: MouseEvent): void {
    const target = event.target;
    if (
      !this.notificationCenterOpen() ||
      (target instanceof Node && this.host.nativeElement.contains(target))
    ) {
      return;
    }

    this.notificationCenterOpen.set(false);
  }

  @HostListener('document:keydown.escape')
  closeNotificationCenterOnEscape(): void {
    this.notificationCenterOpen.set(false);
  }
}
