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
 */

import { readdirSync, readFileSync, statSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const LOCALES_DIR = resolve(ROOT, 'Assets/Resources/Data/locales');

const locales = readdirSync(LOCALES_DIR)
  .filter((entry) => statSync(join(LOCALES_DIR, entry)).isDirectory())
  .sort();

let nodes = 0;
let lines = 0;

for (const locale of locales) {
  const dialogue = JSON.parse(readFileSync(join(LOCALES_DIR, locale, 'dialogue.json'), 'utf8'));

  let localeNodes = 0;
  for (const [id, node] of Object.entries(dialogue)) {
    if (!node.needs_curation) continue;
    localeNodes += 1;
    nodes += 1;

    if (localeNodes === 1) console.log(`\n=== ${locale} ===`);
    console.log(`\n${id}  (speaker: ${node.npc})`);
    for (const line of node.lines ?? []) {
      if (line.verse) {
        console.log(`  quoted    ${line.verse}${line.frame ? `  frame: ${line.frame}` : ''}`);
      } else if (line.text) {
        // An em dash opens speech in these files; anything else is the narrator describing the
        // scene. Only the first kind puts words in a real person's mouth, and only the first kind
        // needs weighing against the passage - lumping them together buries the lines that matter.
        const isSpeech = line.text.trimStart().startsWith('\u2014');
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

console.log(
  nodes === 0
    ? '\nNothing awaiting curation.'
    : `\n${lines} spoken line(s) across ${nodes} node(s) in ${locales.length} locale(s) awaiting a\n` +
      'read against the text. Narration is listed for context only: describing what a figure did is\n' +
      'not the same as deciding what he said, and only the second needs a judgement.'
);
