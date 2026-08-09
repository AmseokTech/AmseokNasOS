//--------------------------//
//--------按当前界面语言格式化系统运行时长---------//
//--------Formats system uptime using the active interface language--------//
//-------------------------//
import { ChangeDetectorRef, Pipe, PipeTransform, effect, inject } from '@angular/core';

import { LanguageService } from './language.service';

@Pipe({
  name: 'localizedUptime',
  pure: false
})
export class LocalizedUptimePipe implements PipeTransform {
  private readonly changeDetector = inject(ChangeDetectorRef);
  private readonly languageService = inject(LanguageService);

  constructor() {
    effect(() => {
      this.languageService.language();
      this.changeDetector.markForCheck();
    });
  }

  transform(seconds: number): string {
    if (!Number.isFinite(seconds) || seconds < 0) {
      return '—';
    }

    const totalMinutes = Math.floor(seconds / 60);
    const days = Math.floor(totalMinutes / 1440);
    const hours = Math.floor(totalMinutes % 1440 / 60);
    const minutes = totalMinutes % 60;
    const isEnglish = this.languageService.language() === 'en-US';
    const parts = [
      days ? this.unit(days, isEnglish ? 'day' : '天', isEnglish) : '',
      hours ? this.unit(hours, isEnglish ? 'hour' : '小时', isEnglish) : '',
      !days && minutes ? this.unit(minutes, isEnglish ? 'minute' : '分钟', isEnglish) : ''
    ].filter(Boolean);

    return parts.join(' ') || (isEnglish ? 'Less than 1 minute' : '不足 1 分钟');
  }

  private unit(value: number, label: string, pluralize: boolean): string {
    return `${value} ${label}${pluralize && value !== 1 ? 's' : ''}`;
  }
}
