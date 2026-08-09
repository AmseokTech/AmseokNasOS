//--------------------------//
//--------定义前端支持的语言与语言选项---------//
//--------Defines frontend-supported languages and language options--------//
//-------------------------//
export const SUPPORTED_LANGUAGES = ['zh-CN', 'en-US'] as const;

export type SupportedLanguage = (typeof SUPPORTED_LANGUAGES)[number];

export interface LanguageOption {
  readonly code: SupportedLanguage;
  readonly nativeLabel: string;
  readonly secondaryLabel: string;
}

export const LANGUAGE_OPTIONS: readonly LanguageOption[] = [
  { code: 'zh-CN', nativeLabel: '中文（简体）', secondaryLabel: 'Simplified Chinese' },
  { code: 'en-US', nativeLabel: 'English', secondaryLabel: '英文' }
];

export function isSupportedLanguage(value: unknown): value is SupportedLanguage {
  return typeof value === 'string' && SUPPORTED_LANGUAGES.includes(value as SupportedLanguage);
}
