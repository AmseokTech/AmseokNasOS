//--------------------------//
//--------提供只读产品订阅与续费展示---------//
//--------Provides the read-only product subscription and renewal view--------//
//-------------------------//
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';

@Component({
  selector: 'app-app-store-subscription-center',
  templateUrl: './app-store-subscription-center.component.html',
  styleUrl: './app-store-subscription-center.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AppStoreSubscriptionCenterComponent {
  readonly renewalNotice = signal('');

  showRenewalOptions(): void {
    this.renewalNotice.set('续费与支付流程将在完成账户、订单和审计边界后接入。');
  }
}
