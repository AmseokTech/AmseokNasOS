import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { routes } from '../../app.routes';
import { DesktopComponent } from './desktop.component';

describe('DesktopComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DesktopComponent],
      providers: [
        provideNoopAnimations(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter(routes)
      ]
    }).compileComponents();
  });

  it('should restore the session and show the forced password-change reminder', () => {
    const fixture = TestBed.createComponent(DesktopComponent);
    const http = TestBed.inject(HttpTestingController);

    fixture.detectChanges();
    http.expectOne('/api/auth/session').flush({ userName: 'admin', mustChangePassword: true });
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.desktop-reminders')?.textContent).toContain('请修改初始密码');
    expect(compiled.querySelector<HTMLAnchorElement>('[reminder-actions]')?.getAttribute('href'))
      .toBe('/change-password');
    http.verify();
  });
});
