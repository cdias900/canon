#!/usr/bin/env node
// The study endpoint: reads how somebody has been playing and answers with passages worth their
// time.
//
// ==================================================================================
// WHY THIS IS A SERVER AND NOT A FEW LINES INSIDE THE GAME
// ==================================================================================
// AGENTS.md rule 16: no AI key ever reaches the client. A Unity player is a file on somebody's
// machine and every string in it is readable, so the key lives here, in the environment of a
// process the player never sees. The game gets a URL and nothing else.
//
// ==================================================================================
// THE TWO THINGS THIS REFUSES TO LET THE MODEL DO
// ==================================================================================
// 1. WRITE SCRIPTURE (rule 1). The schema has no field for verse text, so a model that tried would
//    have nowhere to put it. Every suggestion carries a REFERENCE, and the game resolves the words
//    from its own corpus like every other citation.
// 2. POINT AT A PASSAGE THE BUILD DOES NOT SHIP. A reference outside verses.manifest.json would open
//    an empty reader, so it is rejected here rather than discovered by a player.
//
// And one it refuses to let itself do: paraphrase. Authored lines are checked for an 8-word overlap
// against the corpus — the same rule tools/validate-content.mjs enforces at build time, applied to
// text that never passes through a build.
//
// Usage:
//   ANTHROPIC_API_KEY=... node tools/study-server.mjs [--port 8787]
//   curl -s localhost:8787/health
//   curl -s localhost:8787/studies -X POST -d '{"conversations":4,"wallStages":6,"read":true}'

import { createServer } from 'node:http';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import Anthropic from '@anthropic-ai/sdk';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const MODEL = 'claude-opus-5';

/** How many suggestions one request may come back with. Four cards is a screenful. */
const MAX_STUDIES = 4;

/** The overlap that counts as quoting rather than writing. Same number the build validator uses. */
const OVERLAP_WORDS = 8;

const args = process.argv.slice(2);
const port = Number(args[args.indexOf('--port') + 1]) || 8787;

// ---------------------------------------------------------------- what the build actually ships

const manifest = JSON.parse(readFileSync(join(ROOT, 'tools/verses.manifest.json'), 'utf8'));

/** Every reference the player's build can open, as a set the model's answer is checked against. */
const shipped = new Set([...(manifest.verses ?? []), ...(manifest.chapters ?? [])]);

/**
 * Scripture as word runs, for the overlap check. Read from the generated verses.json rather than
 * from anything authored: what must not be paraphrased is the licensed text itself.
 */
function loadScriptureRuns() {
  const runs = new Set();

  for (const locale of ['pt-BR', 'en']) {
    let data;
    try {
      data = JSON.parse(
        readFileSync(join(ROOT, `Assets/Resources/Data/locales/${locale}/verses.json`), 'utf8'));
    } catch {
      continue;
    }

    // One text unit at a time, never concatenated. Joining two verses would invent runs that exist
    // in neither, and the check would then reject writing nobody quoted. The build validator
    // indexes the same way, for the same reason.
    for (const verse of Object.values(data.verses ?? {})) {
      addRuns(runs, verse?.text);
    }

    for (const chapter of Object.values(data.chapters ?? {})) {
      for (const verse of chapter?.verses ?? []) {
        addRuns(runs, verse?.text);
      }
    }
  }

  return runs;
}

function addRuns(runs, text) {
  if (!text) return;

  const words = normalise(text).split(' ').filter(Boolean);
  for (let i = 0; i + OVERLAP_WORDS <= words.length; i++) {
    runs.add(words.slice(i, i + OVERLAP_WORDS).join(' '));
  }
}

function normalise(text) {
  return text
    .toLowerCase()
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/[^a-z0-9\s]/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

const scriptureRuns = loadScriptureRuns();

/** True when a line shares an OVERLAP_WORDS run with the corpus — i.e. it is quoting, not writing. */
function paraphrasesScripture(line) {
  const words = normalise(line).split(' ').filter(Boolean);
  for (let i = 0; i + OVERLAP_WORDS <= words.length; i++) {
    if (scriptureRuns.has(words.slice(i, i + OVERLAP_WORDS).join(' '))) return true;
  }
  return false;
}

// ---------------------------------------------------------------- the model

const client = new Anthropic();

const SYSTEM = `You suggest passages of Nehemiah to somebody playing a game about rebuilding a wall.

You are given only what they have DONE in the game. Answer with studies that meet them there.

Rules you cannot break:
- Never write the words of Scripture. You choose a reference; the game resolves the text itself.
- Never name the book, chapter or verse in the title or the line. The game decides when a citation
  reveals itself, and saying it here would take that decision away.
- Choose only from the references offered to you. Anything else opens an empty page.
- Write like somebody talking about work, not about religion. No "blessing", "journey of faith",
  "devotional", "God has a plan", no exhortation and no moral instruction. You may not tell the
  player what to feel or what to learn.
- A title is at most five words. A line is one sentence, at most twenty words, and it refers to
  something the player actually did.`;

const STUDY_SCHEMA = {
  type: 'json_schema',
  schema: {
    type: 'object',
    properties: {
      studies: {
        type: 'array',
        maxItems: MAX_STUDIES,
        items: {
          type: 'object',
          properties: {
            title: { type: 'string' },
            line: { type: 'string' },
            reference: { type: 'string', enum: [...shipped] },
          },
          required: ['title', 'line', 'reference'],
          additionalProperties: false,
        },
      },
    },
    required: ['studies'],
    additionalProperties: false,
  },
};

/** The player's run, in words, because that is what the model reads. */
function describe(signals) {
  const lines = [
    `People spoken to: ${signals.conversations ?? 0}`,
    `Wall stages raised: ${signals.wallStages ?? 0}`,
    `Has opened a chapter on their own: ${signals.read ? 'yes' : 'no'}`,
    `Went down the valley when invited: ${signals.wentDownTheValley ? 'yes' : 'no'}`,
    `Stood the night trial: ${signals.stoodTheTrial ? 'yes' : 'no'}`,
    `Day: ${signals.day ?? 1}`,
  ];

  return `Here is how this player has been playing:\n${lines.join('\n')}\n\n` +
    `Suggest up to ${MAX_STUDIES} studies, most relevant first. ` +
    `References you may use: ${[...shipped].join(', ')}`;
}

async function suggest(signals) {
  const response = await client.messages.parse({
    model: MODEL,
    max_tokens: 4096,
    system: SYSTEM,
    // Adaptive thinking, low effort: the judgement here is small — read six numbers, pick two or
    // three passages — and a long deliberation would cost latency the player waits through.
    thinking: { type: 'adaptive' },
    output_config: { effort: 'low', format: STUDY_SCHEMA },
    messages: [{ role: 'user', content: describe(signals) }],
  });

  const studies = response.parsed_output?.studies ?? [];

  // The schema already constrains the reference to a shipped one; the overlap check is the part a
  // schema cannot do, and the reason it runs on the way out rather than in review.
  const clean = studies.filter((study) => {
    if (!shipped.has(study.reference)) return false;
    if (paraphrasesScripture(study.line) || paraphrasesScripture(study.title)) {
      console.warn(`[study] dropped "${study.title}" — it quotes the corpus`);
      return false;
    }
    return true;
  });

  return { studies: clean.slice(0, MAX_STUDIES), model: response.model };
}

// ---------------------------------------------------------------- the endpoint

const server = createServer(async (request, response) => {
  const send = (status, body) => {
    const payload = JSON.stringify(body);
    response.writeHead(status, {
      'content-type': 'application/json',
      'content-length': Buffer.byteLength(payload),
    });
    response.end(payload);
  };

  if (request.method === 'GET' && request.url === '/health') {
    return send(200, { ok: true, model: MODEL, references: shipped.size });
  }

  if (request.method !== 'POST' || !request.url.startsWith('/studies')) {
    return send(404, { error: 'POST /studies' });
  }

  const chunks = [];
  for await (const chunk of request) chunks.push(chunk);

  let signals;
  try {
    signals = JSON.parse(Buffer.concat(chunks).toString('utf8') || '{}');
  } catch {
    return send(400, { error: 'body must be JSON' });
  }

  try {
    const result = await suggest(signals);
    console.log(`[study] ${result.studies.length} suggestion(s) for`, signals);
    return send(200, result);
  } catch (error) {
    // The game falls back to its own authored table on any failure, so the useful thing to do here
    // is say what went wrong in the server's log and answer plainly.
    console.error('[study] request failed:', error?.message ?? error);
    return send(502, { error: 'suggestion failed', detail: error?.message ?? String(error) });
  }
});

server.listen(port, '127.0.0.1', () => {
  console.log(`[study] listening on http://127.0.0.1:${port}`);
  console.log(`[study] model ${MODEL}, ${shipped.size} shipped references, ` +
              `${scriptureRuns.size} scripture runs indexed`);
  if (!process.env.ANTHROPIC_API_KEY) {
    console.warn('[study] ANTHROPIC_API_KEY is not set — requests will fail until it is.');
  }
});
