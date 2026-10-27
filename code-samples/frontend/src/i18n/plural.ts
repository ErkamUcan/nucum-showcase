// File summary: The "plural" interpolation formatter for i18next (Phase 19,
// DECISIONS F168). English copy writes `{{n}} {{n, plural(one: day; other:
// days)}}`; this picks the word with Intl.PluralRules. Turkish copy never
// uses it (Turkish nouns do not change after a number). Registered in
// i18n.ts, tested in plural.test.ts. Lives in src/i18n.

// CONCEPT: Plural rules (CLDR).
// Languages split numbers into groups: English has "one" (1) and "other"
// (0, 2, 1.5 …); Arabic has six groups. Intl.PluralRules knows them, so the
// copy only names the forms it needs and the browser picks the group.
// WHY: i18next's own plurals want a `count` option and a key per form
// (day_one, day_other). That would change hundreds of t() calls that pass
// `{ n }` today. One formatter keeps every call as it is.
// GOTCHA: a missing form falls back to "other", then to an empty string,
// never to the raw format text; a non-number value is shown as "other".
// INTERVIEW: "How do you handle plurals in i18n?" → never `n === 1 ? …`;
// use CLDR rules (Intl.PluralRules), because many languages have more than
// two forms.
// → Roadmap: Ay 2 · Faz 7 — Localization & Globalization
export function pluralWord(value: unknown, lng: string | undefined, forms: Readonly<Record<string, unknown>>): string {
  const n = typeof value === 'number' ? value : Number(value);
  const group = Number.isFinite(n) ? new Intl.PluralRules(lng ?? 'en').select(n) : 'other';
  const word = forms[group] ?? forms['other'];
  return typeof word === 'string' ? word : '';
}
