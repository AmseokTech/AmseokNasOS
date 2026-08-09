//--------------------------//
//--------提供 macOS 风格的系统设置导航视窗---------//
//--------Provides the macOS-inspired system settings navigation view--------//
//-------------------------//
import { ChangeDetectionStrategy, Component, signal } from '@angular/core';

import { TranslatePipe } from '../../core/i18n';
import { DiskManagementComponent } from '../storage';
import { AboutSettingsComponent } from './about/about-settings.component';
import { LanguageSettingsComponent } from './language/language-settings.component';
import { NetworkSettingsComponent } from './network/network-settings.component';

type SettingsSection = 'about' | 'language' | 'network' | 'storage';

@Component({
  selector: 'app-settings-page',
  imports: [
    AboutSettingsComponent,
    DiskManagementComponent,
    LanguageSettingsComponent,
    NetworkSettingsComponent,
    TranslatePipe
  ],
  templateUrl: './settings-page.component.html',
  styleUrl: './settings-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class SettingsPageComponent {
  readonly activeSection = signal<SettingsSection>('about');

  selectSection(section: SettingsSection): void {
    this.activeSection.set(section);
  }
}
