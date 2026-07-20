import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter, Router } from '@angular/router';

import { routes } from '../../../app.routes';
import { ChangePasswordPageComponent } from './change-password-page.component';

describe('ChangePasswordPageComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ChangePasswordPageComponent],
      providers: [
        provideNoopAnimations(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter(routes)
      ]
    }).compileComponents();
  });

  it('should reject mismatched confirmation locally', () => {
    const fixture = TestBed.createComponent(ChangePasswordPageComponent);
    fixture.componentInstance.currentPasswordControl.setValue('AmseokNas');
    fixture.componentInstance.newPasswordControl.setValue('NewPassword1!');
    fixture.componentInstance.confirmPasswordControl.setValue('Different1!');

    fixture.componentInstance.submitPasswordChange();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')?.textContent)
      .toContain('两次输入的新密码不一致');
    TestBed.inject(HttpTestingController).verify();
  });

  it('should change the password and return to login', async () => {
    const fixture = TestBed.createComponent(ChangePasswordPageComponent);
    const http = TestBed.inject(HttpTestingController);
    const router = TestBed.inject(Router);
    fixture.componentInstance.currentPasswordControl.setValue('AmseokNas');
    fixture.componentInstance.newPasswordControl.setValue('NewPassword1!');
    fixture.componentInstance.confirmPasswordControl.setValue('NewPassword1!');

    fixture.componentInstance.submitPasswordChange();
    http.expectOne('/api/auth/csrf').flush(null);
    http.expectOne('/api/auth/change-password').flush(null);
    await fixture.whenStable();

    expect(router.url).toBe('/?passwordChanged=true');
    http.verify();
  });
});
