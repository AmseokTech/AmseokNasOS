//--------------------------//
//--------登录页组合身份展示、密码输入与认证入口状态---------//
//--------The login page composes identity, password input, and authentication entry state--------//
//-------------------------//
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize } from 'rxjs';

import { AuthenticationService } from '../authentication.service';

const ADMINISTRATOR_USER_NAME = 'admin';

@Component({
  selector: 'app-login-page',
  imports: [ReactiveFormsModule],
  templateUrl: './login-page.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './login-page.component.scss'
})
export class LoginPageComponent {
  private readonly authentication = inject(AuthenticationService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly usernameControl = new FormControl(ADMINISTRATOR_USER_NAME, {
    nonNullable: true,
    validators: [Validators.required, Validators.maxLength(64)]
  });
  readonly passwordControl = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required]
  });
  readonly loginForm = new FormGroup({
    username: this.usernameControl,
    password: this.passwordControl
  });
  readonly submitting = signal(false);
  readonly errorMessage = signal('');
  readonly successMessage = signal(
    this.route.snapshot.queryParamMap.has('passwordChanged')
      ? '密码修改成功，请使用新密码登录'
      : ''
  );

  submitLogin(): void {
    this.errorMessage.set('');
    this.successMessage.set('');
    this.loginForm.markAllAsTouched();

    if (this.loginForm.invalid) {
      return;
    }

    // 当前认证边界只开放固定管理员，前端不能把未受支持的账户伪装成可登录账户
    if (this.usernameControl.value.trim().toLowerCase() !== ADMINISTRATOR_USER_NAME) {
      this.errorMessage.set('当前版本仅支持管理员账户 admin');
      return;
    }

    this.submitting.set(true);
    this.authentication
      .login(this.passwordControl.value)
      .pipe(finalize(() => this.submitting.set(false)))
      .subscribe({
        next: () => {
          this.passwordControl.reset();
          void this.router.navigate(['/desktop']);
        },
        error: (error: Error) => this.errorMessage.set(error.message)
      });
  }
}
