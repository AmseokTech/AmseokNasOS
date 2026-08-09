//--------------------------//
//--------管理全局界面语言、浏览器持久化与文案解析---------//
//--------Manages global UI language, browser persistence, and copy resolution--------//
//-------------------------//
import { DOCUMENT } from '@angular/common';
import { Injectable, effect, inject, signal } from '@angular/core';

import {
  LANGUAGE_OPTIONS,
  SupportedLanguage,
  isSupportedLanguage
} from './language.models';
import { TRANSLATIONS, TranslationKey, resolveTranslationKey } from './translations';

export type TranslationParams = Readonly<Record<string, string | number>>;

export const LANGUAGE_STORAGE_KEY = 'amseokos.language';
export const LANGUAGE_SELECTION_STORAGE_KEY = 'amseokos.language.user-selected';

@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly document = inject(DOCUMENT);
  private readonly hasUserSelection = signal(this.readUserSelection());
  private readonly selectedLanguage = signal<SupportedLanguage>(this.readPersistedLanguage());

  readonly language = this.selectedLanguage.asReadonly();
  readonly options = LANGUAGE_OPTIONS;

  constructor() {
    effect(() => {
      const language = this.selectedLanguage();
      this.document.documentElement.lang = language;
      if (this.hasUserSelection()) {
        this.persistLanguage(language);
      }
    });
  }

  setLanguage(language: SupportedLanguage): void {
    this.selectedLanguage.set(language);
    this.hasUserSelection.set(true);
  }

  translate(value: TranslationKey | string, params?: TranslationParams): string {
    const key = resolveTranslationKey(value);
    if (!key) {
      return value;
    }

    const template = TRANSLATIONS[this.selectedLanguage()][key] ?? TRANSLATIONS['zh-CN'][key];
    if (!params) {
      return template;
    }

    return template.replace(/\{\{\s*([\w.-]+)\s*\}\}/g, (placeholder, name: string) =>
      Object.hasOwn(params, name) ? String(params[name]) : placeholder
    );
  }

  private readPersistedLanguage(): SupportedLanguage {
    try {
      const storage = this.document.defaultView?.localStorage;
      if (storage?.getItem(LANGUAGE_SELECTION_STORAGE_KEY) !== 'true') {
        return 'zh-CN';
      }

      const language = storage.getItem(LANGUAGE_STORAGE_KEY);
      return isSupportedLanguage(language) ? language : 'zh-CN';
    } catch {
      return 'zh-CN';
    }
  }

  private readUserSelection(): boolean {
    try {
      return this.document.defaultView?.localStorage.getItem(LANGUAGE_SELECTION_STORAGE_KEY) === 'true';
    } catch {
      return false;
    }
  }

  private persistLanguage(language: SupportedLanguage): void {
    try {
      const storage = this.document.defaultView?.localStorage;
      storage?.setItem(LANGUAGE_STORAGE_KEY, language);
      storage?.setItem(LANGUAGE_SELECTION_STORAGE_KEY, 'true');
    } catch {
      // A blocked storage context must not prevent language switching in the current session.
    }
  }
}
