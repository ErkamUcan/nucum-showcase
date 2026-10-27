// File summary: Date, number and percent formatting in the UI language
// (Phase 19, DECISIONS F167). uiLocale() maps the current i18next language
// to a BCP 47 locale: Turkish → tr-TR, English → en-GB (24-hour clock,
// day before month, week starts on Monday, like the Turkish UI). Modules
// keep their formatter constants, but build them with uiDateFormat /
// uiNumberFormat instead of `new Intl.*('tr-TR')`. Tested in locale.test.ts.
// Lives in src/i18n.
import { currentLanguage } from './i18n';
import type { UiLanguage } from './language-rules';

export type UiLocale = 'tr-TR' | 'en-GB';

// WHY en-GB and not en-US: the app is built around a 24-hour day (bedtime
// 23:30, logical day ends at 04:00) and Monday-first weeks. en-US would
// show "11:30 PM" and "10/27/2026", which reads oddly next to the rest.
export function localeFor(lang: UiLanguage): UiLocale {
  return lang === 'en' ? 'en-GB' : 'tr-TR';
}

export function uiLocale(): UiLocale {
  return localeFor(currentLanguage());
}

// CONCEPT: Lazy formatter (resolve the locale at call time).
// `const DATE = new Intl.DateTimeFormat('tr-TR', …)` at module level is
// built once, when the file first loads; switching the language later would
// leave it Turkish forever. These wrappers keep the same shape (a constant
// with .format()), but pick the Intl object for the CURRENT locale on every
// call. Intl objects are slow to build, so each locale+options pair is
// built once and cached.
// GOTCHA: the cache key is the options as JSON. Options objects written in
// a different key order get two cache entries; harmless, just one more.
// INTERVIEW: "Why not create Intl.DateTimeFormat inside render?" → it is
// expensive (locale data lookup); cache it, but key the cache by locale.
// → Roadmap: Ay 2 · Faz 7 — Localization & Globalization
const dateCache = new Map<string, Intl.DateTimeFormat>();
const numberCache = new Map<string, Intl.NumberFormat>();

function cachedDate(locale: UiLocale, options: Intl.DateTimeFormatOptions): Intl.DateTimeFormat {
  const key = `${locale}|${JSON.stringify(options)}`;
  let f = dateCache.get(key);
  if (!f) {
    f = new Intl.DateTimeFormat(locale, options);
    dateCache.set(key, f);
  }
  return f;
}

function cachedNumber(locale: UiLocale, options: Intl.NumberFormatOptions): Intl.NumberFormat {
  const key = `${locale}|${JSON.stringify(options)}`;
  let f = numberCache.get(key);
  if (!f) {
    f = new Intl.NumberFormat(locale, options);
    numberCache.set(key, f);
  }
  return f;
}

export interface UiDateFormat {
  format(date?: Date | number): string;
  formatToParts(date?: Date | number): Intl.DateTimeFormatPart[];
}

export interface UiNumberFormat {
  format(value: number | bigint): string;
}

// `locale` is for tests and for code that must format in a fixed language;
// the UI leaves it out.
export function uiDateFormat(options: Intl.DateTimeFormatOptions, locale?: () => UiLocale): UiDateFormat {
  const pick = () => cachedDate(locale ? locale() : uiLocale(), options);
  return {
    format: (date) => pick().format(date),
    formatToParts: (date) => pick().formatToParts(date),
  };
}

export function uiNumberFormat(options: Intl.NumberFormatOptions = {}, locale?: () => UiLocale): UiNumberFormat {
  return { format: (value) => cachedNumber(locale ? locale() : uiLocale(), options).format(value) };
}

// Percent from a 0–100 number: Turkish puts the sign first ("%67"),
// English after ("67%"). Intl knows which; a template like `%${n}` does not.
export function formatPercent(value: number, maximumFractionDigits = 0, locale: UiLocale = uiLocale()): string {
  return cachedNumber(locale, { style: 'percent', maximumFractionDigits }).format(value / 100);
}
