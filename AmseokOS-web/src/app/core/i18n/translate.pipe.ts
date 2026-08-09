//--------------------------//
//--------为 Angular 模板提供响应式翻译入口---------//
//--------Provides a reactive translation entry point for Angular templates--------//
//-------------------------//
import { ChangeDetectorRef, Pipe, PipeTransform, effect, inject } from '@angular/core';

import { LanguageService, TranslationParams } from './language.service';
import type { TranslationKey } from './translations';

@Pipe({
  name: 'translate',
  pure: false
})
export class TranslatePipe implements PipeTransform {
  private readonly changeDetector = inject(ChangeDetectorRef);
  private readonly languageService = inject(LanguageService);

  constructor() {
    effect(() => {
      this.languageService.language();
      this.changeDetector.markForCheck();
    });
  }

  transform(value: TranslationKey | string, params?: TranslationParams): string {
    return this.languageService.translate(value, params);
  }
}
