import { TestBed } from '@angular/core/testing';

import {
  LANGUAGE_SELECTION_STORAGE_KEY,
  LANGUAGE_STORAGE_KEY,
  LanguageService
} from './language.service';

describe('LanguageService', () => {
  beforeEach(() => {
    window.localStorage.removeItem(LANGUAGE_STORAGE_KEY);
    window.localStorage.removeItem(LANGUAGE_SELECTION_STORAGE_KEY);
    document.documentElement.lang = '';
    TestBed.configureTestingModule({});
  });

  afterEach(() => {
    window.localStorage.removeItem(LANGUAGE_STORAGE_KEY);
    window.localStorage.removeItem(LANGUAGE_SELECTION_STORAGE_KEY);
  });

  it('uses Chinese by default and persists a language change locally', () => {
    const service = TestBed.inject(LanguageService);
    TestBed.flushEffects();

    expect(service.language()).toBe('zh-CN');
    expect(document.documentElement.lang).toBe('zh-CN');

    service.setLanguage('en-US');
    TestBed.flushEffects();

    expect(service.language()).toBe('en-US');
    expect(document.documentElement.lang).toBe('en-US');
    expect(window.localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('en-US');
    expect(window.localStorage.getItem(LANGUAGE_SELECTION_STORAGE_KEY)).toBe('true');
  });

  it('uses Chinese for first login when only a legacy language value exists', () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, 'en-US');
    const service = TestBed.inject(LanguageService);

    expect(service.language()).toBe('zh-CN');
  });

  it('restores an explicitly selected language and interpolates translated copy', () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, 'en-US');
    window.localStorage.setItem(LANGUAGE_SELECTION_STORAGE_KEY, 'true');
    const service = TestBed.inject(LanguageService);

    expect(service.language()).toBe('en-US');
    expect(service.translate('language.preview.noNotifications')).toBe('No notifications');
  });

  it('ignores an unsupported persisted language', () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, 'fr-FR');
    window.localStorage.setItem(LANGUAGE_SELECTION_STORAGE_KEY, 'true');
    const service = TestBed.inject(LanguageService);

    expect(service.language()).toBe('zh-CN');
  });
});
