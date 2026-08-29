#!/usr/bin/env node
/**
 * Builds Assets/Resources/Data/locales/<locale>/verses.json from the YouVersion Platform API.
 *
 * This is the only place in the project where biblical text is allowed to exist.
 * Everything else — C#, authored JSON, prompts — carries references only (NEH.4.6).
 * The model never writes scripture; it picks a reference and this script resolves it.
 *
 * One set of references, one translation per locale: the manifest holds the references once and
 * a version id per language, so a new language is an entry in the manifest rather than a new list
 * of verses that could drift from the first one.
 *
 *   node tools/fetch-verses.mjs                    every locale in the manifest
 *   node tools/fetch-verses.mjs --locale en        just that one
 *   node tools/fetch-verses.mjs --placeholder      structure-only output, so the game runs pre-licence
 *   node tools/fetch-verses.mjs --version-id N     override the version id (needs --locale)
 */

import { readFileSync, writeFileSync, existsSync, mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(HERE, '..');
const MANIFEST_PATH = resolve(HERE, 'verses.manifest.json');
const outPathFor = (locale) => resolve(ROOT, `Assets/Resources/Data/locales/${locale}/verses.json`);

const API_BASE = 'https://api.youversion.com/v1';
const REQUEST_SPACING_MS = 120;

/**
 * Stand-in written into a placeholder build. The real marker a player sees comes from the locale
 * string table (scripture.unavailable); this one only has to be unmistakably not scripture, which
 * is why it stays in one form regardless of language.
 */
const missingMarker = (ref) => `⟨text unavailable — ${ref}⟩`;

// ---------------------------------------------------------------- environment

/** Minimal .env.local reader so the key never has to live in the shell profile. */
function loadEnvFile() {
  const envPath = resolve(ROOT, '.env.local');
  if (!existsSync(envPath)) return;
  for (const raw of readFileSync(envPath, 'utf8').split('\n')) {
    const line = raw.trim();
    if (!line || line.startsWith('#')) continue;
    const eq = line.indexOf('=');
    if (eq === -1) continue;
    const key = line.slice(0, eq).trim();
    if (!process.env[key]) process.env[key] = line.slice(eq + 1).trim();
  }
}

// ---------------------------------------------------------------- references

/** "NEH.4.6" -> {book:"NEH", chapter:4, verse:6}; "NEH.4" -> {book:"NEH", chapter:4} */
function parseRef(ref) {
  const parts = String(ref).split('.');
  if (parts.length < 2) throw new Error(`Malformed reference: ${ref}`);
  const [book, chapter, verse] = parts;
  const parsed = { book, chapter: Number(chapter) };
  if (parts.length >= 3) parsed.verse = Number(verse);
  if (!Number.isInteger(parsed.chapter)) throw new Error(`Malformed reference: ${ref}`);
  if (parts.length >= 3 && !Number.isInteger(parsed.verse)) throw new Error(`Malformed reference: ${ref}`);
  return parsed;
}

const chapterRefOf = (verseRef) => {
  const { book, chapter } = parseRef(verseRef);
  return `${book}.${chapter}`;
};

// ---------------------------------------------------------------- http

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function api(path, { apiKey }) {
  const url = `${API_BASE}${path}`;
  const res = await fetch(url, { headers: { 'X-YVP-App-Key': apiKey, Accept: 'application/json' } });
  if (res.status === 403) {
    const body = await res.text();
    throw new Error(
      `Access denied by YouVersion for ${path}.\n` +
      `  ${body}\n` +
      `  The app key does not entitle this Bible version. Request access for that version in the\n` +
      `  YouVersion developer portal, or pick one the key already allows (see docs/youversion-api.md).`
    );
  }
  if (!res.ok) throw new Error(`GET ${path} -> HTTP ${res.status}: ${(await res.text()).slice(0, 300)}`);
  await sleep(REQUEST_SPACING_MS);
  return res.json();
}

/** Collapses the API's stray whitespace without touching the words themselves. */
const tidy = (text) => String(text ?? '').replace(/\s+/g, ' ').trim();

async function fetchPassage(versionId, passageId, ctx) {
  const data = await api(`/bibles/${versionId}/passages/${encodeURIComponent(passageId)}`, ctx);
  return { text: tidy(data.content), refDisplay: tidy(data.reference) || passageId };
}

/** The chapters endpoint lists verse ids but carries no text; text needs one passage call each. */
async function fetchChapterVerseIds(versionId, book, chapter, ctx) {
  const data = await api(`/bibles/${versionId}/books/${book}/chapters/${chapter}`, ctx);
  const verses = data.verses ?? data.data ?? [];
  return verses
    .map((v) => Number(v.title ?? v.id))
    .filter((n) => Number.isInteger(n) && n > 0)
    .sort((a, b) => a - b);
}

// ---------------------------------------------------------------- build

/** Sorts object keys so the generated file diffs cleanly between runs. */
function sortedByRef(entries) {
  const weight = (ref) => {
    const { book, chapter, verse } = parseRef(ref);
    return [book, chapter, verse ?? 0];
  };
  return Object.fromEntries(
    Object.entries(entries).sort(([a], [b]) => {
      const [ba, ca, va] = weight(a);
      const [bb, cb, vb] = weight(b);
      return ba.localeCompare(bb) || ca - cb || va - vb;
    })
  );
}

async function build({ manifest, versionId, placeholder }) {
  const apiKey = process.env.YOUVERSION_API_KEY;
  const ctx = { apiKey };

  if (!placeholder && !apiKey) {
    throw new Error(
      'YOUVERSION_API_KEY is not set.\n' +
      '  Put it in .env.local (git-ignored) as YOUVERSION_API_KEY=..., or export it.\n' +
      '  To generate a runnable structure-only file instead, pass --placeholder.'
    );
  }

  const verseRefs = [...new Set(manifest.verses ?? [])];
  const chapterRefs = [...new Set(manifest.chapters ?? [])];

  let version = { id: String(versionId), abbrev: 'PLACEHOLDER', copyright: '' };
  const verses = {};
  const chapters = {};

  if (placeholder) {
    console.log('Generating a PLACEHOLDER verses.json — no text will be fetched.');
    for (const ref of verseRefs) {
      verses[ref] = { ref_display: ref, text: missingMarker(ref) };
    }
    for (const ref of chapterRefs) {
      chapters[ref] = {
        ref_display: ref,
        verses: Array.from({ length: 1 }, (_, i) => ({ n: i + 1, text: missingMarker(ref) })),
      };
    }
  } else {
    const meta = await api(`/bibles/${versionId}`, ctx);
    version = {
      id: String(meta.id ?? versionId),
      abbrev: meta.abbreviation ?? meta.localized_abbreviation ?? '',
      copyright: tidy(meta.copyright),
    };
    console.log(`Version ${version.id} ${version.abbrev} — ${meta.title ?? ''}`);

    // Chapters first: they subsume any verse in the same chapter, so we fetch each verse once.
    for (const chapterRef of chapterRefs) {
      const { book, chapter } = parseRef(chapterRef);
      const numbers = await fetchChapterVerseIds(versionId, book, chapter, ctx);
      if (numbers.length === 0) throw new Error(`Chapter ${chapterRef} came back with no verses.`);
      console.log(`  ${chapterRef}: ${numbers.length} verses`);

      const collected = [];
      for (const n of numbers) {
        const passageId = `${book}.${chapter}.${n}`;
        const { text, refDisplay } = await fetchPassage(versionId, passageId, ctx);
        if (!text) throw new Error(`Empty text for ${passageId}.`);
        collected.push({ n, text });
        // A chapter verse also satisfies a manifest verse request for the same reference.
        if (verseRefs.includes(passageId)) verses[passageId] = { ref_display: refDisplay, text };
      }
      const display = await fetchPassage(versionId, chapterRef, ctx).then((p) => p.refDisplay);
      chapters[chapterRef] = { ref_display: display, verses: collected };
    }

    for (const ref of verseRefs) {
      if (verses[ref]) continue;
      const { text, refDisplay } = await fetchPassage(versionId, ref, ctx);
      if (!text) throw new Error(`Empty text for ${ref}.`);
      verses[ref] = { ref_display: refDisplay, text };
      console.log(`  ${ref}`);
    }
  }

  // Layer 1 of the validator, applied at generation time: nothing ships half-resolved.
  for (const ref of verseRefs) {
    if (!verses[ref]) throw new Error(`Manifest reference ${ref} is missing from the output.`);
    if (!verses[ref].text) throw new Error(`Manifest reference ${ref} resolved to empty text.`);
  }
  for (const ref of chapterRefs) {
    if (!chapters[ref]?.verses?.length) throw new Error(`Manifest chapter ${ref} is missing from the output.`);
  }

  return {
    is_placeholder: placeholder,
    generated_by: 'tools/fetch-verses.mjs',
    version,
    verses: sortedByRef(verses),
    chapters,
  };
}

// ---------------------------------------------------------------- entry

async function main() {
  loadEnvFile();
  const argv = process.argv.slice(2);
  const placeholder = argv.includes('--placeholder');
  const versionFlag = argv.indexOf('--version-id');
  const localeFlag = argv.indexOf('--locale');
  const onlyLocale = localeFlag !== -1 ? argv[localeFlag + 1] : null;

  if (!existsSync(MANIFEST_PATH)) throw new Error(`Manifest not found at ${MANIFEST_PATH}`);
  const manifest = JSON.parse(readFileSync(MANIFEST_PATH, 'utf8'));

  const versions = manifest.versions ?? {};
  const locales = onlyLocale ? [onlyLocale] : Object.keys(versions);
  if (locales.length === 0) {
    throw new Error('The manifest lists no versions. Add one under "versions" keyed by locale.');
  }

  const overrideId = versionFlag !== -1 ? argv[versionFlag + 1] : null;
  if (overrideId && locales.length > 1) {
    throw new Error('--version-id applies to one locale. Pass --locale as well.');
  }

  for (const locale of locales) {
    const entry = versions[locale];
    if (!entry && !overrideId) {
      throw new Error(
        `No version is configured for locale "${locale}".\n` +
        `  Add it under "versions" in tools/verses.manifest.json, or pass --version-id.`
      );
    }

    // The manifest is per locale and is the authority. A single YOUVERSION_VERSION_ID in the
    // environment cannot be right for more than one language, so it is only a fallback for a
    // locale the manifest does not describe — never an override of one it does.
    const versionId = overrideId || entry?.id || process.env.YOUVERSION_VERSION_ID;
    if (!placeholder && (!versionId || String(versionId).startsWith('<'))) {
      throw new Error(
        `No version id resolved for "${locale}". Set it in tools/verses.manifest.json, or pass\n` +
        '  --version-id, or set YOUVERSION_VERSION_ID. See docs/youversion-api.md.'
      );
    }

    console.log(`\n== ${locale} ==`);
    const output = await build({ manifest, versionId, placeholder });

    const outPath = outPathFor(locale);
    mkdirSync(dirname(outPath), { recursive: true });
    writeFileSync(outPath, `${JSON.stringify(output, null, 2)}\n`, 'utf8');

    const verseCount = Object.keys(output.verses).length;
    const chapterCount = Object.keys(output.chapters).length;
    console.log(`Wrote ${outPath}`);
    console.log(`  ${verseCount} verses, ${chapterCount} chapters, is_placeholder=${output.is_placeholder}`);
    if (output.version.copyright) console.log(`  ${output.version.copyright.slice(0, 100)}...`);
  }
}

main().catch((err) => {
  console.error(`\nfetch-verses failed: ${err.message}`);
  process.exit(1);
});
