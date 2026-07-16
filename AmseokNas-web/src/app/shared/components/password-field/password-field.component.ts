//--------------------------//
//--------密码输入框统一基础校验展示与密码显隐交互---------//
//--------The password field unifies basic validation display and visibility interaction--------//
//-------------------------//
import { ChangeDetectionStrategy, Component, Input, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

@Component({
  selector: 'app-password-field',
  imports: [MatButtonModule, MatFormFieldModule, MatInputModule, ReactiveFormsModule],
  templateUrl: './password-field.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './password-field.component.scss'
})
export class PasswordFieldComponent {
  @Input({ required: true }) control!: FormControl<string>;
  @Input() label = '密码';
  @Input() autocomplete = 'current-password';
  @Input() requiredMessage = '请输入密码';

  readonly passwordVisible = signal(false);

  toggleVisibility(): void {
    this.passwordVisible.update((visible) => !visible);
  }
}
