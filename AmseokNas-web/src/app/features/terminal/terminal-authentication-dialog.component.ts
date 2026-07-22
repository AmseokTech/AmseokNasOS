//--------------------------//
//--------在打开终端前重新验证当前 Web 管理员---------//
//--------Reauthenticates the current web administrator before opening a terminal--------//
//-------------------------//
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

import { PasswordFieldComponent } from '../../shared/components/password-field/password-field.component';
import { TerminalSession, TerminalSessionService } from './terminal-session.service';

const INITIAL_COLUMNS = 100;
const INITIAL_ROWS = 30;

@Component({
  selector: 'app-terminal-authentication-dialog',
  imports: [
    MatButtonModule,
    MatDialogModule,
    MatProgressSpinnerModule,
    PasswordFieldComponent,
    ReactiveFormsModule
  ],
  templateUrl: './terminal-authentication-dialog.component.html',
  styleUrl: './terminal-authentication-dialog.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TerminalAuthenticationDialogComponent {
  private readonly dialogRef = inject(
    MatDialogRef<TerminalAuthenticationDialogComponent, TerminalSession | undefined>
  );
  private readonly sessions = inject(TerminalSessionService);

  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);
  readonly form = new FormGroup({
    password: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(256)]
    })
  });

  authenticate(): void {
    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();
      return;
    }

    this.errorMessage.set(null);
    this.submitting.set(true);
    this.sessions
      .create(this.form.controls.password.value, INITIAL_COLUMNS, INITIAL_ROWS)
      .subscribe({
        next: (session) => {
          this.form.reset();
          this.dialogRef.close(session);
        },
        error: (error: Error) => {
          this.form.controls.password.setValue('');
          this.errorMessage.set(error.message);
          this.submitting.set(false);
        }
      });
  }
}
