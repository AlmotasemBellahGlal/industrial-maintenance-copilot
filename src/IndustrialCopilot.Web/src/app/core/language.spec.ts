import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { beforeEach, afterEach, describe, expect, it } from 'vitest';
import { LanguageService, phrase } from './language';
import { WorkflowApi, hostHeaders } from './api';

describe('Presentation language boundaries', () => {
  beforeEach(() => {
    // Angular's isolated DOM runner does not expose persistent origin storage.
    // Browser tests separately prove actual localStorage survives a reload.
    const values = new Map<string, string>();
    Object.defineProperty(window, 'localStorage', {
      configurable: true,
      value: {
        getItem: (key: string) => values.get(key) ?? null,
        setItem: (key: string, value: string) => values.set(key, value),
        removeItem: (key: string) => values.delete(key),
      },
    });
    TestBed.resetTestingModule();
  });
  afterEach(() => {
    window.localStorage.removeItem('maintenance.language');
    document.documentElement.dir = 'ltr';
    document.documentElement.lang = 'en';
  });
  it('switches document language/direction, persists only preference, and restores it', () => {
    const language = TestBed.inject(LanguageService);
    language.select('ar');
    expect(document.documentElement.lang).toBe('ar');
    expect(document.documentElement.dir).toBe('rtl');
    expect(window.localStorage.getItem('maintenance.language')).toBe('ar');
    TestBed.resetTestingModule();
    expect(TestBed.inject(LanguageService).culture()).toBe('ar-EG');
    TestBed.inject(LanguageService).select('en');
    expect(document.documentElement.dir).toBe('ltr');
  });
  it('falls back to English and preserves technical interpolation values', () => {
    const language = TestBed.inject(LanguageService);
    language.select('ar');
    expect(language.text('Work order review')).toBe('مراجعة أمر العمل');
    expect(language.text('Missing translation key')).toBe('Missing translation key');
    expect(language.text(phrase('Approve revision {0}?', 'revision-123'))).toContain(
      'revision-123',
    );
  });
  it.each(['en', 'ar'] as const)(
    'propagates %s through the central HTTP interceptor without changing the URL',
    async (lang) => {
      TestBed.configureTestingModule({
        providers: [provideHttpClient(withInterceptors([hostHeaders])), provideHttpClientTesting()],
      });
      TestBed.inject(LanguageService).select(lang);
      const promise = TestBed.inject(WorkflowApi).run('11111111-1111-1111-1111-111111111111');
      const http = TestBed.inject(HttpTestingController);
      const request = http.expectOne('/api/runs/11111111-1111-1111-1111-111111111111');
      expect(request.request.headers.get('Accept-Language')).toBe(
        lang === 'ar' ? 'ar-EG' : 'en-US',
      );
      request.flush({});
      await promise;
      http.verify();
    },
  );
});
