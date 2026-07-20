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

  it('should show the default administrator and password input', () => {
    const fixture = TestBed.createComponent(LoginPageComponent);

    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.username')?.textContent).toContain('admin');
    expect(compiled.querySelector('input')?.getAttribute('type')).toBe('password');
  });

  it('should require a password before login', () => {
    const fixture = TestBed.createComponent(LoginPageComponent);

    fixture.detectChanges();
    fixture.componentInstance.submitLogin();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('mat-error')?.textContent).toContain('请输入密码');
  });

  it('should open the desktop and preserve the forced password-change session', async () => {
    const fixture = TestBed.createComponent(LoginPageComponent);
    const http = TestBed.inject(HttpTestingController);
    const router = TestBed.inject(Router);
    fixture.componentInstance.passwordControl.setValue('AmseokNas');

    fixture.detectChanges();
    fixture.componentInstance.submitLogin();
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
