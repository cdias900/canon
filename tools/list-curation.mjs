#!/usr/bin/env node
/**
 * Lists the authored lines that a human still has to weigh against the passage.
 *
 * A canonical figure may be given words, but only a person holding the text can say whether those
 * words assert something it does not. This prints the queue for that read.
 *
 * Every locale is queued, not just the authoring one. A translation of a canonical figure's speech
 * is newly authored speech in that language: it can drift from the passage on its own, in ways the
 * original never did, and no automatic check can tell. Rule 4's safeguard is a human read, so a
 * language that has not had one has not had the safeguard.
 *
 * THIS TOOL FAILS LOUDLY OR NOT AT ALL. It is the only place rule 4's safeguard is administered,
 * and its dangerous output is not a crash - it is "Nothing awaiting curation." printed over a
 * locale whose dialogue could not be read. So: a locale directory that yields no dialogue at all,
 * or a file that will not parse, names itself and exits non-zero. Empty is a claim about the
 * content, and this tool only makes that claim when it has actually read the content.
 *
 * For the same reason it no longer opens one hard-coded filename. It reads every JSON file in the
 * locale, recognises dialogue by its shape rather than by its name, and says which file each node
 * came from. A season that outgrows a single dialogue.json and is split in two must not be able to
 * take half the queue with it silently.
 */

import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { dirname, extname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const LOCALES_DIR = resolve(ROOT, 'Assets/Resources/Data/locales');

const failures = [];

if (!existsSync(LOCALES_DIR)) {
  fail(`No locales directory at ${relative(ROOT, LOCALES_DIR)}.`);
  report();
}

const locales = readdirSync(LOCALES_DIR)
  .filter((entry) => statSync(join(LOCALES_DIR, entry)).isDirectory())
  .sort();

if (locales.length === 0) {
  fail(`No locales under ${relative(ROOT, LOCALES_DIR)}.`);
  report();
}

let nodes = 0;
let lines = 0;
let unflagged = 0;

for (const locale of locales) {
  const documents = dialogueDocumentsFor(locale);
  if (documents.length === 0) {
    // Not "nothing to curate". Nothing to READ - which is the failure this tool exists to not hide.
    fail(`${locale}: no dialogue found in ${relative(ROOT, join(LOCALES_DIR, locale))}.`);
    continue;
  }

  // Ordered by day so ~40 nodes can be signed off stage by stage against the passage each stage
  // draws on, instead of as one flat sitting that mixes chapter 3 with chapter 12. Within a day,
  // ordinal id order, so two runs of this tool list the same queue in the same order.
  const queue = [];
  for (const { file, document } of documents) {
    for (const [id, node] of Object.entries(document)) {
      if (!node || typeof node !== 'object') continue;
      if (node.canonical_speaker === true && node.needs_curation !== true) unflagged += 1;
      if (!node.needs_curation) continue;
      queue.push({ id, node, file });
    }
  }

  queue.sort((a, b) => dayOf(a.node) - dayOf(b.node) || (a.id < b.id ? -1 : a.id > b.id ? 1 : 0));

  if (queue.length === 0) continue;

  console.log(`\n=== ${locale} ===`);

  let currentDay = null;
  for (const { id, node, file } of queue) {
    const day = dayOf(node);
    if (day !== currentDay) {
      currentDay = day;
      console.log(`\n--- day ${day === Number.MAX_SAFE_INTEGER ? '(unassigned)' : day} ---`);
    }

    nodes += 1;
    console.log(`\n${id}  (speaker: ${node.npc || '(none)'}${file === 'dialogue.json' ? '' : `, in ${file}`})`);

    for (const line of node.lines ?? []) {
      if (line.verse) {
        console.log(`  quoted    ${line.verse}${line.frame ? `  frame: ${line.frame}` : ''}`);
      } else if (line.text) {
        // An em dash opens speech in these files; anything else is the narrator describing the
        // scene. Only the first kind puts words in a real person's mouth, and only the first kind
        // needs weighing against the passage - lumping them together buries the lines that matter.
        const isSpeech = line.text.trimStart().startsWith('—');
        if (isSpeech) {
          lines += 1;
          console.log(`  SPEECH    ${line.text}`);
        } else {
          console.log(`  narration ${line.text}`);
        }
      }
    }
  }
}

// "Nothing awaiting curation." is a claim, and it must never be printed over content this run
// failed to read - that sentence over an unparseable file is the whole failure mode this tool was
// hardened against. A partial run says so, in place of the reassuring line.
if (failures.length > 0) {
  console.log(
    `\nINCOMPLETE: ${lines} spoken line(s) across ${nodes} node(s) listed, but some content could\n` +
      'not be read. See the errors below; this is not the whole queue.'
  );
} else {
  console.log(
    nodes === 0
      ? '\nNothing awaiting curation.'
      : `\n${lines} spoken line(s) across ${nodes} node(s) in ${locales.length} locale(s) awaiting a\n` +
        'read against the text. Narration is listed for context only: describing what a figure did\n' +
        'is not the same as deciding what he said, and only the second needs a judgement.'
  );
}

if (unflagged > 0) {
  // The validator fails the build on this, and it is repeated here because this is where a person
  // is looking at the queue and could otherwise take a short one for a finished one.
  console.log(
    `\nWARNING: ${unflagged} node(s) are marked canonical_speaker but not needs_curation, so they\n` +
    'are NOT in the queue above. Run node tools/validate-content.mjs, which names them.'
  );
}

report();

/**
 * Every JSON document in a locale that reads as dialogue, with the file it came from.
 *
 * Shape, not filename: a top-level map whose entries are nodes, at least one of which carries a
 * lines array. verses.json is excluded outright - it is generated licensed text and is never
 * authored, so it can never be a thing awaiting a judgement.
 */
function dialogueDocumentsFor(locale) {
  const directory = join(LOCALES_DIR, locale);
  const found = [];

  for (const filePath of jsonFilesUnder(directory)) {
    const name = relative(directory, filePath);
    if (name === 'verses.json') continue;

    let document;
    try {
      document = JSON.parse(readFileSync(filePath, 'utf8'));
    } catch (error) {
      // Never skipped quietly. A file that will not parse is exactly the file whose flagged nodes
      // would vanish from the queue while the run still printed a total and exited zero.
      fail(`${locale}: ${relative(ROOT, filePath)} could not be read - ${error.message}`);
      continue;
    }

    if (looksLikeDialogue(document)) {
      found.push({ file: name, document });
    }
  }

  return found.sort((a, b) => (a.file < b.file ? -1 : a.file > b.file ? 1 : 0));
}

function looksLikeDialogue(document) {
  if (!document || typeof document !== 'object' || Array.isArray(document)) return false;
  for (const node of Object.values(document)) {
    if (node && typeof node === 'object' && Array.isArray(node.lines)) return true;
  }
  return false;
}

function* jsonFilesUnder(directory) {
  let entries;
  try {
    entries = readdirSync(directory);
  } catch (error) {
    fail(`Could not list ${relative(ROOT, directory)} - ${error.message}`);
    return;
  }

  for (const entry of entries.sort()) {
    const fullPath = join(directory, entry);
    let stats;
    try {
      stats = statSync(fullPath);
    } catch (error) {
      continue;
    }
    if (stats.isDirectory()) {
      yield* jsonFilesUnder(fullPath);
    } else if (stats.isFile() && extname(entry).toLowerCase() === '.json') {
      yield fullPath;
    }
  }
}

/** Nodes are grouped by the stage they belong to; an unauthored day sorts last rather than first. */
function dayOf(node) {
  return Number.isFinite(node.day) ? node.day : Number.MAX_SAFE_INTEGER;
}

function fail(message) {
  failures.push(message);
}

function report() {
  if (failures.length === 0) return;
  console.error('');
  for (const message of failures) {
    console.error(`ERROR: ${message}`);
  }
  console.error(
    '\nThe curation queue is rule 4\'s only human safeguard. A queue that cannot read its own\n' +
    'content is not an empty queue, so this run is a failure rather than a clean sheet.'
  );
  process.exit(1);
}
