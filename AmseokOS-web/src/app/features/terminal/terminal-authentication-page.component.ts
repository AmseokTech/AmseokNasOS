//--------------------------//
//--------在受管窗口中重新验证当前 Web 管理员---------//
//--------Reauthenticates the current web administrator in a managed window--------//
//-------------------------//
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

import { PasswordFieldComponent } from '../../shared/components/password-field/password-field.component';
import { WindowManagerService } from '../../shell/window-manager/window-manager.service';
import { WINDOW_ID } from '../../shell/window-manager/window-state.model';
import { TerminalSessionService } from './terminal-session.service';

const INITIAL_COLUMNS = 100;
const INITIAL_ROWS = 30;

@Component({
  selector: 'app-terminal-authentication-page',
  imports: [
    MatButtonModule,
    MatProgressSpinnerModule,
    PasswordFieldComponent,
    ReactiveFormsModule
  ],
  templateUrl: './terminal-authentication-page.component.html',
  styleUrl: './terminal-authentication-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class TerminalAuthenticationPageComponent {
  private readonly destroyRef = inject(DestroyRef);
  private readonly sessions = inject(TerminalSessionService);
  private readonly windowId = inject(WINDOW_ID);
  private readonly windowManager = inject(WindowManagerService);

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
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (session) => {
          this.form.reset();
          this.windowManager.close(this.windowId);
          this.windowManager.open('terminal', { data: session });
        },
        error: (error: Error) => {
          this.form.controls.password.setValue('');
          this.errorMessage.set(error.message);
          this.submitting.set(false);
        }
      });
  }

  cancel(): void {
    if (!this.submitting()) {
      this.windowManager.close(this.windowId);
    }
  }
}
