//--------------------------//
//--------处理首次登录后的强制密码修改流程---------//
//--------Handles the forced password-change flow after initial sign-in--------//
//-------------------------//
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';

import { PasswordFieldComponent } from '../../../shared/components/password-field/password-field.component';
import { UserAvatarComponent } from '../../../shared/components/user-avatar/user-avatar.component';
import { AuthenticationService } from '../authentication.service';

@Component({
  selector: 'app-change-password-page',
  imports: [MatButtonModule, PasswordFieldComponent, ReactiveFormsModule, UserAvatarComponent],
  templateUrl: './change-password-page.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './change-password-page.component.scss'
})
export class ChangePasswordPageComponent {
  private readonly authentication = inject(AuthenticationService);
  private readonly router = inject(Router);

  readonly currentPasswordControl = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required]
  });
  readonly newPasswordControl = new FormControl('', {
    nonNullable: true,
    validators: [
      Validators.required,
      Validators.minLength(8),
      Validators.pattern(/^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z\d]).+$/)
    ]
  });
  readonly confirmPasswordControl = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required]
  });
  readonly submitting = signal(false);
  readonly errorMessage = signal('');

  submitPasswordChange(): void {
    this.errorMessage.set('');
    this.currentPasswordControl.markAsTouched();
    this.newPasswordControl.markAsTouched();
    this.confirmPasswordControl.markAsTouched();

    if (
      this.currentPasswordControl.invalid ||
      this.newPasswordControl.invalid ||
      this.confirmPasswordControl.invalid
    ) {
      this.errorMessage.set('请填写所有字段，并确认新密码满足复杂度要求');
      return;
    }

    if (this.newPasswordControl.value !== this.confirmPasswordControl.value) {
      this.errorMessage.set('两次输入的新密码不一致');
      return;
    }

    if (this.currentPasswordControl.value === this.newPasswordControl.value) {
      this.errorMessage.set('新密码不能与当前密码相同');
      return;
    }

    this.submitting.set(true);
    this.authentication
      .changePassword(this.currentPasswordControl.value, this.newPasswordControl.value)
      .pipe(finalize(() => this.submitting.set(false)))
      .subscribe({
        next: () => {
          void this.router.navigate(['/'], { queryParams: { passwordChanged: true } });
        },
        error: (error: Error) => this.errorMessage.set(error.message)
      });
  }
}
