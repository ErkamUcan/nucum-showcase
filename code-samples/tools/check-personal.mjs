// File summary: Proves that no personal content sits in git-tracked files
// (DECISIONS P7, Phase 6.5). The words to look for come FROM the personal
// pack (routine names, part names, book titles, categories, activity types,
// pack ids) — so this file itself names nothing personal. Every file in
// `git ls-files` is searched; a hit fails with file:line. Runs in
// `npm run lint`; skips with a clear message when the pack is absent.
// Lives in tools/.
//
// CONCEPT: Data-driven leak check.
// A hand-written blacklist would itself leak the words it guards. Reading the
// list from the git-ignored pack keeps the guard public and the data private.
// GOTCHA: Common words that also name a routine ("Spor", "Okuma", "Backend")
// are app vocabulary, not personal data; GENERIC below drops them. Matching
// is whole-word and case-insensitive (Turkish locale), so "taşınamaz" does
// not hit a shorter word inside it.

import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const packDir = path.join(root, 'packs', 'personal');
const routinesPath = path.join(packDir, 'routines.json');
const mappingPath = path.join(packDir, 'routine-mapping.json');

if (!existsSync(routinesPath) || !existsSync(mappingPath)) {
  console.log('SKIP check-personal: personal pack not found (packs/personal/routines.json, routine-mapping.json) — nothing to compare against.');
  process.exit(0);
}

// App vocabulary and everyday Turkish words; never personal on their own.
const GENERIC = new Set([
  'backend', 'yds', 'spor', 'okuma', 'almanca', 'ingilizce', 'mesleki', 'çalışma', 'kariyer', 'beden', 'dil',
  'sabah', 'akşam', 'gece', 'günlük', 'ilim', 'dinleme', 'fikrî', 'dava', 'kaliteli', 'yazılım', 'nasıl',
  'geliştirilir', 'sure', 'cüz', 'okuma', 'diğer',
  // Phase 17 (DECISIONS F128): everyday religious words the Vakitler / Kaza
  // screens must be able to say. A routine named only with these words stays
  // writable; any other word in its name is still a term.
  'namaz', 'namazı', 'namazlar', 'namazları', 'namazın', 'kaza', 'kazası', 'kazalar', 'kazaları',
  'vitir', 'vitr', 'kıble', 'kıblesi', 'kandil', 'kandili', 'cuma', 'kerahat', 'hicri', 'ezan', 'ezanı',
  'vakit', 'vakti', 'vakitler', 'kabe',
]);

const lower = (s) => s.toLocaleLowerCase('tr-TR');
const routines = JSON.parse(readFileSync(routinesPath, 'utf8'));
const mapping = JSON.parse(readFileSync(mappingPath, 'utf8'));

const phrases = new Set();
const ids = new Set();
for (const s of routines.seeds ?? []) {
  phrases.add(s.name);
  phrases.add(s.category);
  ids.add(s.legacyId);
  for (const p of s.parts ?? []) phrases.add(p.name);
  if (s.book?.title) phrases.add(s.book.title);
}
for (const t of mapping.activityTypes ?? []) phrases.add(t.name);
for (const t of mapping.templates ?? []) ids.add(t.legacyId);

const terms = new Set();
const words = (phrase) => lower(phrase).split(/[^\p{L}']+/u).filter(Boolean);
for (const phrase of phrases) {
  if (!phrase) continue;
  // A name made only of generic words ("YDS / İngilizce") is app vocabulary.
  if (words(phrase).some((w) => !GENERIC.has(w))) terms.add(lower(phrase));
  for (const word of words(phrase)) {
    if (word.length >= 4 && !GENERIC.has(word)) terms.add(word);
  }
}
// Pack ids: the personal ones only (generic ids like "yds" are app vocabulary;
// template ids look like "tuesday-rest" and say nothing personal).
const personalIds = [...ids].filter((id) => id && !GENERIC.has(lower(id)) && !/^[a-z]+-[a-z-]+$/.test(id));
for (const id of personalIds) if (id.length >= 4) terms.add(lower(id));

const escape = (s) => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
// Whole word: no letter, digit or underscore on either side.
const termPattern = new RegExp(`(?<![\\p{L}\\p{N}_])(${[...terms].map(escape).join('|')})(?![\\p{L}\\p{N}_])`, 'iu');
// Settings keys that embed a routine's pack id (e.g. "routines.<id>.minMinutes").
const keyPattern = new RegExp(`routines\\.(${personalIds.map(escape).join('|')})\\.`, 'i');

const files = execFileSync('git', ['ls-files'], { cwd: root, encoding: 'utf8' }).split('\n').filter(Boolean);
const hits = [];
let scanned = 0;
for (const file of files) {
  if (/\.(png|jpe?g|ico|icns|woff2?|ttf|db)$/i.test(file)) continue;
  const full = path.join(root, file);
  if (!existsSync(full)) continue;
  scanned++;
  const lines = readFileSync(full, 'utf8').split('\n');
  lines.forEach((line, i) => {
    const l = lower(line);
    const m = termPattern.exec(l) ?? keyPattern.exec(l);
    if (m) hits.push(`${file}:${i + 1}: "${m[0]}"`);
  });
}

console.log(`check-personal: ${terms.size} terms from the personal pack, ${scanned} tracked files scanned.`);
if (hits.length > 0) {
  console.error(`Personal content in tracked files (${hits.length}):\n  ${hits.join('\n  ')}`);
  process.exit(1);
}
console.log('check-personal: no personal content in tracked files.');
