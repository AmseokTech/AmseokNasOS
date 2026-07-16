//--------------------------//
//--------登录页组合身份展示、密码输入与认证入口状态---------//
//--------The login page composes identity, password input, and authentication entry state--------//
//-------------------------//
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';

import { PasswordFieldComponent } from '../../../shared/components/password-field/password-field.component';
import { UserAvatarComponent } from '../../../shared/components/user-avatar/user-avatar.component';

@Component({
  selector: 'app-login-page',
  imports: [MatButtonModule, PasswordFieldComponent, ReactiveFormsModule, UserAvatarComponent],
  templateUrl: './login-page.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './login-page.component.scss'
})
export class LoginPageComponent {
  readonly passwordControl = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required]
  });
  readonly loginUnavailable = signal(false);

  submitLogin(): void {
    this.loginUnavailable.set(false);
    this.passwordControl.markAsTouched();

    if (this.passwordControl.invalid) {
      return;
    }

    // 后端认证接口尚未建立，当前页面不能在浏览器中伪造登录成功
    // Authentication is not simulated in the browser while the backend endpoint is unavailable
    this.loginUnavailable.set(true);
  }
}
