import { Routes } from '@angular/router';
import { ChangePasswordPageComponent } from './core/auth/change-password/change-password-page.component';
import { LoginPageComponent } from './core/auth/login/login-page.component';

export const routes: Routes = [
  { path: '', component: LoginPageComponent },
  { path: 'change-password', component: ChangePasswordPageComponent },
  {
    path: 'desktop',
    loadComponent: () =>
      import('./shell/desktop/desktop.component').then(({ DesktopComponent }) => DesktopComponent)
  },
  { path: '**', redirectTo: '' }
];
