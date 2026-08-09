//--------------------------//
//--------提供浏览器本地界面语言选择---------//
//--------Provides browser-local interface language selection--------//
//-------------------------//
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';

import { LanguageService, SupportedLanguage, TranslatePipe } from '../../../core/i18n';

@Component({
  selector: 'app-language-settings',
  imports: [TranslatePipe],
  templateUrl: './language-settings.component.html',
  styleUrl: './language-settings.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class LanguageSettingsComponent {
  readonly languageService = inject(LanguageService);
  readonly selectedLanguageLabel = computed(
    () => this.languageService.options.find(({ code }) => code === this.languageService.language())?.nativeLabel ?? ''
  );

  selectLanguage(language: SupportedLanguage): void {
    this.languageService.setLanguage(language);
  }
}
