//--------------------------//
//--------用户头像组件只负责呈现通用身份图形---------//
//--------The user avatar only presents a reusable identity graphic--------//
//-------------------------//
import { ChangeDetectionStrategy, Component, Input } from '@angular/core';

@Component({
  selector: 'app-user-avatar',
  templateUrl: './user-avatar.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './user-avatar.component.scss',
  host: {
    '[attr.aria-hidden]': 'accessibleLabel ? null : "true"',
    '[attr.aria-label]': 'accessibleLabel',
    '[attr.role]': 'accessibleLabel ? "img" : null'
  }
})
export class UserAvatarComponent {
  @Input() accessibleLabel: string | null = null;
}
