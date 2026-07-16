import { Routes } from '@angular/router';
import { LoginPageComponent } from './core/auth/login/login-page.component';

export const routes: Routes = [
  { path: '', component: LoginPageComponent },
  { path: '**', redirectTo: '' }
];
