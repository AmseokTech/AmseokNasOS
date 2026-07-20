import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () =>
      import('./core/auth/login/login-page.component').then(
        ({ LoginPageComponent }) => LoginPageComponent
      )
  },
  {
    path: 'change-password',
    loadComponent: () =>
      import('./core/auth/change-password/change-password-page.component').then(
        ({ ChangePasswordPageComponent }) => ChangePasswordPageComponent
      )
  },
  {
    path: 'desktop',
    loadComponent: () =>
      import('./shell/desktop/desktop.component').then(({ DesktopComponent }) => DesktopComponent)
  },
  { path: '**', redirectTo: '' }
];
