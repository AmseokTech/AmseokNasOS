//--------------------------//
//--------登录页组合身份展示、密码输入与认证入口状态---------//
//--------The login page composes identity, password input, and authentication entry state--------//
//-------------------------//
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize } from 'rxjs';

import { PasswordFieldComponent } from '../../../shared/components/password-field/password-field.component';
import { UserAvatarComponent } from '../../../shared/components/user-avatar/user-avatar.component';
import { AuthenticationService } from '../authentication.service';

@Component({
  selector: 'app-login-page',
  imports: [MatButtonModule, PasswordFieldComponent, ReactiveFormsModule, UserAvatarComponent],
  templateUrl: './login-page.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './login-page.component.scss'
})
export class LoginPageComponent {
  private readonly authentication = inject(AuthenticationService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly passwordControl = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required]
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
    this.passwordControl.markAsTouched();

    if (this.passwordControl.invalid) {
      return;
    }

    this.submitting.set(true);
    this.authentication
      .login(this.passwordControl.value)
      .pipe(finalize(() => this.submitting.set(false)))
      .subscribe({
        next: (session) => {
          this.passwordControl.reset();
          if (session.mustChangePassword) {
            void this.router.navigate(['/change-password']);
            return;
          }

          this.successMessage.set('登录成功，管理界面将在后续阶段接入');
        },
        error: (error: Error) => this.errorMessage.set(error.message)
      });
  }
}
