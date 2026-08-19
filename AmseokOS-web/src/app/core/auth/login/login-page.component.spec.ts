import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { routes } from '../../../app.routes';
import { LoginPageComponent } from './login-page.component';

describe('LoginPageComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LoginPageComponent],
      providers: [
        provideNoopAnimations(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter(routes)
      ]
    }).compileComponents();
  });

  it('should show the AmseokOS identity and compact account credentials', () => {
    const fixture = TestBed.createComponent(LoginPageComponent);

    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const username = compiled.querySelector<HTMLInputElement>('input[autocomplete="username"]');
    const password = compiled.querySelector<HTMLInputElement>('input[autocomplete="current-password"]');
    expect(compiled.querySelector('h1')?.textContent).toContain('AmseokOS');
    expect(username?.value).toBe('admin');
    expect(password?.type).toBe('password');
    expect(compiled.querySelectorAll('.glass-field')).toHaveLength(2);
  });

  it('should require a password before login', () => {
    const fixture = TestBed.createComponent(LoginPageComponent);

    fixture.detectChanges();
    fixture.componentInstance.submitLogin();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('[role="alert"]')?.textContent).toContain('请输入密码');
  });

  it('should reject an unsupported account before sending credentials', () => {
    const fixture = TestBed.createComponent(LoginPageComponent);
    const http = TestBed.inject(HttpTestingController);
    fixture.componentInstance.usernameControl.setValue('someone');
    fixture.componentInstance.passwordControl.setValue('Password1!');

    fixture.componentInstance.submitLogin();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('[role="alert"]')?.textContent).toContain('仅支持管理员账户 admin');
    http.expectNone('/api/auth/csrf');
    http.expectNone('/api/auth/login');
  });

  it('should open the desktop and preserve the forced password-change session', async () => {
    const fixture = TestBed.createComponent(LoginPageComponent);
    const http = TestBed.inject(HttpTestingController);
    const router = TestBed.inject(Router);
    fixture.componentInstance.passwordControl.setValue('AmseokNas');

    fixture.detectChanges();
    const form = fixture.nativeElement.querySelector('form') as HTMLFormElement;
    const submitEvent = new Event('submit', { bubbles: true, cancelable: true });
    form.dispatchEvent(submitEvent);

    expect(submitEvent.defaultPrevented).toBe(true);
    http.expectOne('/api/auth/csrf').flush(null);
    http.expectOne('/api/auth/login').flush({ userName: 'admin', mustChangePassword: true });
    await fixture.whenStable();

    expect(router.url).toBe('/desktop');
    http.verify();
  });

  it('should open the desktop after a regular login', async () => {
    const fixture = TestBed.createComponent(LoginPageComponent);
    const http = TestBed.inject(HttpTestingController);
    const router = TestBed.inject(Router);
    fixture.componentInstance.passwordControl.setValue('NewPassword1!');

    fixture.componentInstance.submitLogin();
    http.expectOne('/api/auth/csrf').flush(null);
    http.expectOne('/api/auth/login').flush({ userName: 'admin', mustChangePassword: false });
    await fixture.whenStable();

    expect(router.url).toBe('/desktop');
    http.verify();
  });
});
