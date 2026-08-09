//--------------------------//
//--------提供只读产品订阅与续费展示---------//
//--------Provides the read-only product subscription and renewal view--------//
//-------------------------//
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';

import { TranslatePipe } from '../../core/i18n';

@Component({
  selector: 'app-app-store-subscription-center',
  imports: [TranslatePipe],
  templateUrl: './app-store-subscription-center.component.html',
  styleUrl: './app-store-subscription-center.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AppStoreSubscriptionCenterComponent {
  readonly renewalNotice = signal('');

  showRenewalOptions(): void {
    this.renewalNotice.set('appStore.subscription.notice');
  }
}
