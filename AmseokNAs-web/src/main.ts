//--------------------------//
//--------前端启动入口，仅装配 Angular 应用---------//
//--------Frontend bootstrap only composes the Angular application--------//
//-------------------------//
import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { AppComponent } from './app/app.component';

bootstrapApplication(AppComponent, appConfig)
  .catch((err) => console.error(err));
