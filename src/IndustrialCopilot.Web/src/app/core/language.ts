import { DOCUMENT } from '@angular/common';
import { inject, Injectable, Pipe, PipeTransform, signal } from '@angular/core';
import { arabic } from './translations.ar';

export type Language = 'en' | 'ar';
export type UiText =
  string | { readonly key: string; readonly values: readonly (string | number)[] };
export function phrase(key: string, ...values: (string | number)[]): UiText {
  return { key, values };
}

/** Presentation-only preference. Credentials and workflow records never enter local storage. */
@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly document = inject(DOCUMENT);
  readonly language = signal<Language>('en');
  constructor() {
    let saved: string | null = null;
    try {
      saved = this.document.defaultView?.localStorage.getItem('maintenance.language') ?? null;
    } catch {
      /* Storage may be disabled. */
    }
    this.select(saved === 'ar' ? 'ar' : 'en');
  }
  select(language: Language): void {
    this.language.set(language);
    this.document.documentElement.lang = language;
    this.document.documentElement.dir = language === 'ar' ? 'rtl' : 'ltr';
    try {
      this.document.defaultView?.localStorage.setItem('maintenance.language', language);
    } catch {
      /* In-memory switching still works. */
    }
  }
  culture(): string {
    return this.language() === 'ar' ? 'ar-EG' : 'en-US';
  }
  requestHeaders(): { 'Accept-Language': string } {
    return { 'Accept-Language': this.culture() };
  }
  text(source: UiText): string {
    const key = typeof source === 'string' ? source : source.key;
    const translated = this.language() === 'ar' ? (arabic[key] ?? key) : key;
    return typeof source === 'string'
      ? translated
      : translated.replace(/\{(\d+)\}/g, (_, index: string) =>
          String(source.values[Number(index)] ?? ''),
        );
  }
}

/** Impure so existing status/confirmation text responds immediately to language changes. */
@Pipe({ name: 't', pure: false })
export class TranslatePipe implements PipeTransform {
  private readonly language = inject(LanguageService);
  transform(value: UiText | null | undefined): string {
    return this.language.text(value ?? '');
  }
}
