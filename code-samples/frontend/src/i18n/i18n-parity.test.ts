// File summary: Turkish/English parity of the UI copy (Phase 19, DECISIONS
// F165, ADR 0047). For every namespace in src/i18n/tr (source.json too) an
// English twin must exist with the same keys and the same {{placeholders}},
// and English values must not carry Turkish letters (a forgotten copy-paste
// would show Turkish in the English UI). Runs with `npm run test` (vitest).
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';

const dir = import.meta.dirname;
const namespaces = readdirSync(path.join(dir, 'tr')).filter((f) => f.endsWith('.json'));

type Json = string | number | boolean | null | Json[] | { [k: string]: Json };

// Flattens {"a":{"b":"x"},"c":["y"]} to [["a.b","x"],["c.0","y"]].
function flatten(value: Json, prefix = '', out = new Map<string, string>()): Map<string, string> {
  if (typeof value === 'string') out.set(prefix, value);
  else if (Array.isArray(value)) value.forEach((v, i) => flatten(v, prefix ? `${prefix}.${i}` : String(i), out));
  else if (value !== null && typeof value === 'object') {
    for (const [k, v] of Object.entries(value)) flatten(v, prefix ? `${prefix}.${k}` : k, out);
  } else out.set(prefix, String(value));
  return out;
}

// A missing twin loads as empty, so the key test lists what is missing
// instead of the whole file crashing at collection time.
function load(lang: string, file: string): Map<string, string> {
  const p = path.join(dir, lang, file);
  return existsSync(p) ? flatten(JSON.parse(readFileSync(p, 'utf8')) as Json) : new Map<string, string>();
}

// "{{n, plural(one: day; other: days)}}" → "n". English may use a variable
// twice (number + plural word), so names are compared as sets.
function placeholders(text: string): string[] {
  const names = [...text.matchAll(/\{\{\s*([^,}\s]+)[^}]*\}\}/g)].map((m) => m[1] ?? '');
  return [...new Set(names)].sort();
}

// Turkish-only grammar helpers English does not need (the locative suffix
// after a time: "07:30'da").
const DROPPABLE = new Set(['sfx']);

// Texts that are meant to stay as they are in English: language names
// written in their own language. The app name "Nücum" is a proper name.
const TURKISH_ALLOWED = new Set(['pages.json:settings.language.tr', 'pages.json:settings.cards.language']);
const TURKISH_LETTERS = /[çğıİöşüÇĞÖŞÜ]/;

describe.each(namespaces)('%s', (file) => {
  const tr = load('tr', file);
  const en = load('en', file);

  it('has the same keys in English', () => {
    expect([...en.keys()].filter((k) => !tr.has(k))).toEqual([]);
    expect([...tr.keys()].filter((k) => !en.has(k))).toEqual([]);
  });

  it('keeps every {{placeholder}}', () => {
    const wrong = [...tr].filter(([k, v]) => {
      const want = placeholders(v);
      const got = placeholders(en.get(k) ?? '');
      return en.has(k) && (got.some((p) => !want.includes(p)) || want.some((p) => !got.includes(p) && !DROPPABLE.has(p)));
    }).map(([k]) => k);
    expect(wrong).toEqual([]);
  });

  it('has no Turkish letters in English', () => {
    const turkish = [...en].filter(([k, v]) => TURKISH_LETTERS.test(v.replaceAll('Nücum', '')) && !TURKISH_ALLOWED.has(`${file}:${k}`)).map(([k, v]) => `${k}: ${v}`);
    expect(turkish).toEqual([]);
  });
});
