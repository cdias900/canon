#!/usr/bin/env node
/**
 * Lists the authored lines that a human still has to weigh against the passage.
 *
 * A canonical figure may be given words, but only a person holding the text can say whether those
 * words assert something it does not. This prints the queue for that read.
 */

import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const dialogue = JSON.parse(readFileSync(resolve(ROOT, 'Assets/Resources/Data/dialogue.json'), 'utf8'));

let nodes = 0;
let lines = 0;

for (const [id, node] of Object.entries(dialogue)) {
  if (!node.needs_curation) continue;
  nodes += 1;

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

console.log(
  nodes === 0
    ? '\nNothing awaiting curation.'
    : `\n${lines} spoken line(s) across ${nodes} node(s) awaiting a read against the text.\n` +
      'Narration is listed for context only: describing what a figure did is not the same as\n' +
      'deciding what he said, and only the second needs a judgement.'
);
