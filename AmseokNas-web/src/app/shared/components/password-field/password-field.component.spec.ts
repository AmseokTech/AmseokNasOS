import { FormControl } from '@angular/forms';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TestBed } from '@angular/core/testing';

import { PasswordFieldComponent } from './password-field.component';

describe('PasswordFieldComponent', () => {
  it('should toggle password visibility without changing the control value', async () => {
    await TestBed.configureTestingModule({
      imports: [PasswordFieldComponent],
      providers: [provideNoopAnimations()]
    }).compileComponents();
    const fixture = TestBed.createComponent(PasswordFieldComponent);
    const control = new FormControl('secret', { nonNullable: true });
    fixture.componentRef.setInput('control', control);

    fixture.detectChanges();
    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('button')?.click();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('input')?.type).toBe('text');
    expect(control.value).toBe('secret');
  });
});
