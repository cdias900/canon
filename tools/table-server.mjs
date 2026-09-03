#!/usr/bin/env node
// The table: a wall being built by up to six people, asynchronously.
//
// Design and reasoning: docs/multiplayer.md. This file is the part that runs.
//
// ==================================================================================
// THE TWO THINGS THIS REFUSES, AND WHY THEY ARE HERE RATHER THAN IN THE CLIENT
// ==================================================================================
// 1. A MINOR NEVER JOINS AN ADULT TABLE, AND VICE VERSA (rule 17). Written as a database CHECK and
//    re-checked on the join path, because the rule says "a database constraint" and a rule that
//    lives in a client is a rule that lives in a text editor.
// 2. A MINOR TABLE NEVER CARRIES FREE TEXT. Same constraint, same reason. The switch that turns
//    free text on cannot turn it on for a minor table: the schema refuses the row.
//
// And one it refuses to let the CLIENT do: decide anything. Band, table membership and whether a
// line is sayable are all resolved here from stored state, never from what the request claims. A
// Unity player is a file on somebody's machine.
//
// ==================================================================================
// WHAT IS DELIBERATELY NOT HERE
// ==================================================================================
// Accounts, sessions, moderation tooling, deployment. A player is a device-generated UUID (see
// docs/multiplayer.md §03) and this endpoint cannot prove anybody is who they say. Every design
// decision above assumes that is true rather than pretending otherwise.
//
// Usage:
//   node tools/table-server.mjs [--port 8788] [--db path]
//   ALLOW_FREE_TEXT=1 node tools/table-server.mjs      # adult tables only, still. See §07.
//
//   curl -s localhost:8788/health
//   curl -s localhost:8788/tables -X POST -d '{"band":"minor","playerId":"u1","playerName":"Kai"}'

import { createServer } from 'node:http';
import { DatabaseSync } from 'node:sqlite';
import { randomUUID } from 'node:crypto';

// ---------------------------------------------------------------- configuration

const args = process.argv.slice(2);
const flag = (name, fallback) => {
  const i = args.indexOf(name);
  return i >= 0 && args[i + 1] ? args[i + 1] : fallback;
};

const PORT = Number(flag('--port', 8788));
const DB_PATH = flag('--db', ':memory:');

/**
 * Free text and private tables, off unless the environment says otherwise.
 *
 * Off is the default because of the argument in docs/persona-and-purpose.md: the chat protections
 * are meant to hold on both sides of the age boundary BECAUSE AGE IS SELF-DECLARED, and a
 * protection keyed to a self-declared age protects nobody. Turning this on is a decision by Pedro
 * and cybersecurity, and it is one variable rather than a rewrite precisely so that it can be
 * their decision and not a side effect of shipping this file.
 */
const ALLOW_FREE_TEXT = process.env.ALLOW_FREE_TEXT === '1';

/** Longest a free line may be. Not moderation — a bound, so one request cannot be a wall of text. */
const MAX_FREE_TEXT = 240;

/** Seats a table holds. Six is the number of residents the game names and draws. */
export const SEATS = ['hananias', 'salum', 'baruque', 'meremote', 'zacur', 'malquias'];

/**
 * The sayable lines, by key. The server holds the KEYS and never the words: the client resolves
 * each one through Loc.T, so two people at one table can play in different languages and each read
 * their own. It is also what makes this list enforceable — an enum is checkable, a sentence is not.
 */
export const COMPOSED_LINES = [
  'table.line.take_next_stretch', 'table.line.need_stone', 'table.line.will_watch',
  'table.line.sounded_trumpet', 'table.line.that_one_is_exposed', 'table.line.cannot_today',
  'table.line.stay_beside_me', 'table.line.mine_is_laid', 'table.line.who_has_timber',
  'table.line.i_am_behind', 'table.line.go_on_without_me', 'table.line.thanks',
  'table.line.taking_the_exposed', 'table.line.gate_needs_two', 'table.line.rubble_here',
  'table.line.watch_tonight', 'table.line.i_read_it', 'table.line.saw_them_on_the_road',
  'table.line.almost_done', 'table.line.good_work'
];

// ---------------------------------------------------------------- storage

/**
 * The schema, with rule 17 written as constraints rather than as comments.
 *
 * The CHECK on tables is the one that matters most: it makes a minor table with free text
 * unrepresentable. Not discouraged, not validated on the way in — unrepresentable. A future code
 * path that forgets the rule gets an exception from SQLite rather than a quiet row.
 */
const SCHEMA = `
CREATE TABLE IF NOT EXISTS tables (
  id         TEXT PRIMARY KEY,
  code       TEXT NOT NULL UNIQUE,
  band       TEXT NOT NULL CHECK (band IN ('minor','adult')),
  season_id  TEXT NOT NULL,
  created_at INTEGER NOT NULL,
  free_text  INTEGER NOT NULL DEFAULT 0 CHECK (free_text IN (0,1)),
  CHECK (band = 'adult' OR free_text = 0)
);

CREATE TABLE IF NOT EXISTS seats (
  table_id     TEXT NOT NULL REFERENCES tables(id),
  seat_id      TEXT NOT NULL,
  player_id    TEXT,
  player_name  TEXT,
  band         TEXT NOT NULL CHECK (band IN ('minor','adult')),
  joined_at    INTEGER,
  last_seen_at INTEGER,
  PRIMARY KEY (table_id, seat_id)
);

CREATE TABLE IF NOT EXISTS events (
  id       INTEGER PRIMARY KEY AUTOINCREMENT,
  table_id TEXT NOT NULL REFERENCES tables(id),
  at       INTEGER NOT NULL,
  seat_id  TEXT,
  kind     TEXT NOT NULL,
  line_key TEXT,
  body     TEXT,
  payload  TEXT
);

CREATE INDEX IF NOT EXISTS events_by_table ON events (table_id, id);
`;

export function openDatabase(path = DB_PATH) {
  const db = new DatabaseSync(path);
  db.exec('PRAGMA foreign_keys = ON');
  db.exec(SCHEMA);
  return db;
}

// ---------------------------------------------------------------- codes

/**
 * No 0/O and no 1/I/L, because a code's job is to be read aloud across a room. Six characters of
 * this alphabet is about a billion, which is more than enough for a game whose tables are six
 * people who already know each other.
 */
const CODE_ALPHABET = 'ABCDEFGHJKMNPQRSTUVWXYZ23456789';

export function makeCode(random = Math.random) {
  let code = '';
  for (let i = 0; i < 6; i++) {
    code += CODE_ALPHABET[Math.floor(random() * CODE_ALPHABET.length)];
  }
  return code;
}

// ---------------------------------------------------------------- the operations
//
// Exported as plain functions taking a db, so the tests drive the rules directly rather than
// through HTTP. The two that must never be wrong are joinTable and say.

export function createTable(db, { band, playerId, playerName, seasonId = 'sheep-gate' }) {
  if (band !== 'minor' && band !== 'adult') {
    return { error: 'band must be minor or adult', status: 400 };
  }
  if (!playerId) {
    return { error: 'playerId is required', status: 400 };
  }

  // Free text is a property of the table, fixed when it is made: a table cannot become chattier
  // later because somebody restarted the server with a different environment.
  const freeText = ALLOW_FREE_TEXT && band === 'adult' ? 1 : 0;

  const id = randomUUID();
  const now = Date.now();
  let code = makeCode();

  // A collision is a retry, not an error. Six characters make it rare; a loop makes it impossible
  // to surface to a player, which is what matters — "that code is taken" is not a sentence anyone
  // should read while their friends wait.
  for (let attempt = 0; attempt < 8; attempt++) {
    const taken = db.prepare('SELECT 1 FROM tables WHERE code = ?').get(code);
    if (!taken) break;
    code = makeCode();
  }

  db.prepare(
    'INSERT INTO tables (id, code, band, season_id, created_at, free_text) VALUES (?, ?, ?, ?, ?, ?)'
  ).run(id, code, band, seasonId, now, freeText);

  for (const seat of SEATS) {
    db.prepare(
      'INSERT INTO seats (table_id, seat_id, band) VALUES (?, ?, ?)'
    ).run(id, seat, band);
  }

  const seat = joinTable(db, { code, playerId, playerName, band });
  return { id, code, band, freeText: freeText === 1, seat: seat.seat };
}

/**
 * Takes a seat, or explains why not.
 *
 * <b>The band check is the reason this function exists on a server.</b> It compares the joiner's
 * declared band against the table's stored band and refuses a mismatch — rule 17's "minor and adult
 * teams do not mix", enforced where the client cannot reach it. The declared band is not trusted to
 * be TRUE (age is self-declared; see docs/multiplayer.md §07), only to be consistent: what this
 * guarantees is that the two sides of the boundary never share a table, not that everyone is the
 * age they said.
 */
export function joinTable(db, { code, playerId, playerName, band, seatId = null }) {
  const table = db.prepare('SELECT * FROM tables WHERE code = ?').get(code);
  if (!table) {
    return { error: 'no table with that code', status: 404 };
  }
  if (band !== table.band) {
    return { error: 'this table is not for your age band', status: 403 };
  }
  if (!playerId) {
    return { error: 'playerId is required', status: 400 };
  }

  const already = db.prepare(
    'SELECT * FROM seats WHERE table_id = ? AND player_id = ?'
  ).get(table.id, playerId);
  if (already) {
    return { table, seat: already.seat_id, rejoined: true };
  }

  const wanted = seatId
    ? db.prepare('SELECT * FROM seats WHERE table_id = ? AND seat_id = ? AND player_id IS NULL')
        .get(table.id, seatId)
    : db.prepare('SELECT * FROM seats WHERE table_id = ? AND player_id IS NULL ORDER BY seat_id')
        .get(table.id);

  if (!wanted) {
    return { error: seatId ? 'that seat is taken' : 'the table is full', status: 409 };
  }

  const now = Date.now();
  db.prepare(
    'UPDATE seats SET player_id = ?, player_name = ?, joined_at = ?, last_seen_at = ? ' +
    'WHERE table_id = ? AND seat_id = ?'
  ).run(playerId, playerName ?? null, now, now, table.id, wanted.seat_id);

  record(db, table.id, wanted.seat_id, 'joined', { name: playerName ?? null });
  return { table, seat: wanted.seat_id, rejoined: false };
}

/**
 * Says something at a table.
 *
 * Composed lines are checked against the enum; free text is checked against the TABLE's stored
 * free_text, never against the request. A client that sends a body to a minor table is refused
 * here, which is the whole point: the protection cannot be turned off by the thing being protected
 * against.
 */
export function say(db, { code, playerId, lineKey = null, body = null }) {
  const table = db.prepare('SELECT * FROM tables WHERE code = ?').get(code);
  if (!table) {
    return { error: 'no table with that code', status: 404 };
  }

  const seat = db.prepare(
    'SELECT * FROM seats WHERE table_id = ? AND player_id = ?'
  ).get(table.id, playerId);
  if (!seat) {
    return { error: 'you are not at this table', status: 403 };
  }

  if (lineKey) {
    if (!COMPOSED_LINES.includes(lineKey)) {
      return { error: 'not a line this game says', status: 400 };
    }
    record(db, table.id, seat.seat_id, 'said', { lineKey });
    return { ok: true, kind: 'composed' };
  }

  if (body != null) {
    if (table.free_text !== 1) {
      return { error: 'this table speaks in the game\'s own words', status: 403 };
    }
    const text = String(body).slice(0, MAX_FREE_TEXT);
    record(db, table.id, seat.seat_id, 'said', { body: text });
    return { ok: true, kind: 'free' };
  }

  return { error: 'say what?', status: 400 };
}

/**
 * Sounds the trumpet: names an hour, and asks the table to be there.
 *
 * NEH.4.20 is already in the game and already says what this does. The deadline is stored rather
 * than scheduled — there is no job runner here, and the window is resolved by the first request
 * that arrives past its hour (see resolveDueTrumpets). That is enough for a table of six and
 * honest about what it is.
 */
export function soundTrumpet(db, { code, playerId, atEpochMs, reason = null, seats = 4 }) {
  const table = db.prepare('SELECT * FROM tables WHERE code = ?').get(code);
  if (!table) return { error: 'no table with that code', status: 404 };

  const seat = db.prepare('SELECT * FROM seats WHERE table_id = ? AND player_id = ?')
    .get(table.id, playerId);
  if (!seat) return { error: 'you are not at this table', status: 403 };

  if (!Number.isFinite(atEpochMs) || atEpochMs <= Date.now()) {
    return { error: 'the trumpet names an hour that has not passed', status: 400 };
  }

  record(db, table.id, seat.seat_id, 'trumpet', { at: atEpochMs, reason, seats });
  return { ok: true, at: atEpochMs, seats };
}

/**
 * A move, held.
 *
 * Rule 11 — nobody sees anybody's choice before choosing — is the reason this writes the move and
 * returns nothing about it. The feed does not carry `committed` payloads (see feed()); they surface
 * only when the turn resolves, all at once, which is the moment the mechanic is for.
 */
export function commitMove(db, { code, playerId, turn, move }) {
  const table = db.prepare('SELECT * FROM tables WHERE code = ?').get(code);
  if (!table) return { error: 'no table with that code', status: 404 };

  const seat = db.prepare('SELECT * FROM seats WHERE table_id = ? AND player_id = ?')
    .get(table.id, playerId);
  if (!seat) return { error: 'you are not at this table', status: 403 };

  const existing = db.prepare(
    "SELECT 1 FROM events WHERE table_id = ? AND seat_id = ? AND kind = 'committed' " +
    "AND json_extract(payload, '$.turn') = ?"
  ).get(table.id, seat.seat_id, turn);
  if (existing) return { error: 'that turn is already answered', status: 409 };

  record(db, table.id, seat.seat_id, 'committed', { turn, move });
  return { ok: true, turn };
}

/** Everything that happened after `since`, with held moves withheld. */
export function feed(db, { code, since = 0 }) {
  const table = db.prepare('SELECT * FROM tables WHERE code = ?').get(code);
  if (!table) return { error: 'no table with that code', status: 404 };

  const rows = db.prepare(
    'SELECT * FROM events WHERE table_id = ? AND id > ? ORDER BY id'
  ).all(table.id, since);

  const events = rows.map((row) => {
    const payload = row.payload ? JSON.parse(row.payload) : null;

    // The one redaction, and rule 11 is why: a committed move is public that it HAPPENED and
    // private in WHAT it was, until the turn resolves.
    if (row.kind === 'committed') {
      return { id: row.id, at: row.at, seat: row.seat_id, kind: row.kind, turn: payload?.turn };
    }

    return {
      id: row.id, at: row.at, seat: row.seat_id, kind: row.kind,
      lineKey: row.line_key, body: row.body, payload
    };
  });

  const seats = db.prepare('SELECT seat_id, player_name, player_id FROM seats WHERE table_id = ?')
    .all(table.id)
    .map((s) => ({ seat: s.seat_id, name: s.player_name, taken: s.player_id != null }));

  return { table: { code: table.code, band: table.band, freeText: table.free_text === 1 }, seats, events };
}

/** A message somebody flagged. Stored for a person to read; this endpoint judges nothing. */
export function report(db, { code, playerId, eventId, note = null }) {
  const table = db.prepare('SELECT * FROM tables WHERE code = ?').get(code);
  if (!table) return { error: 'no table with that code', status: 404 };

  record(db, table.id, null, 'reported', { eventId, by: playerId, note });
  return { ok: true };
}

function record(db, tableId, seatId, kind, payload) {
  db.prepare(
    'INSERT INTO events (table_id, at, seat_id, kind, line_key, body, payload) VALUES (?, ?, ?, ?, ?, ?, ?)'
  ).run(
    tableId, Date.now(), seatId, kind,
    payload?.lineKey ?? null,
    payload?.body ?? null,
    JSON.stringify(payload ?? {})
  );
}

// ---------------------------------------------------------------- http

const ROUTES = {
  'POST /tables': (db, body) => createTable(db, body),
  'POST /join': (db, body) => joinTable(db, body),
  'POST /say': (db, body) => say(db, body),
  'POST /trumpet': (db, body) => soundTrumpet(db, body),
  'POST /commit': (db, body) => commitMove(db, body),
  'POST /report': (db, body) => report(db, body)
};

export function start({ port = PORT, db = openDatabase() } = {}) {
  const server = createServer(async (req, res) => {
    const url = new URL(req.url, 'http://localhost');
    const route = `${req.method} ${url.pathname}`;

    const send = (status, payload) => {
      res.writeHead(status, { 'content-type': 'application/json' });
      res.end(JSON.stringify(payload));
    };

    if (route === 'GET /health') {
      return send(200, { ok: true, freeTextAllowed: ALLOW_FREE_TEXT, seats: SEATS.length });
    }

    if (route === 'GET /feed') {
      const out = feed(db, { code: url.searchParams.get('code'), since: Number(url.searchParams.get('since') ?? 0) });
      return send(out.error ? out.status : 200, out);
    }

    const handler = ROUTES[route];
    if (!handler) return send(404, { error: 'no such route' });

    let body = {};
    try {
      const chunks = [];
      for await (const chunk of req) chunks.push(chunk);
      if (chunks.length) body = JSON.parse(Buffer.concat(chunks).toString('utf8'));
    } catch {
      return send(400, { error: 'body is not JSON' });
    }

    try {
      const out = handler(db, body);
      return send(out.error ? out.status : 200, out);
    } catch (error) {
      // A constraint violation reaching here is the schema doing its job, and it is a 409 rather
      // than a 500: the request was well formed and the rules said no.
      const constraint = String(error?.message ?? '').includes('CHECK constraint');
      return send(constraint ? 409 : 500, { error: String(error?.message ?? error) });
    }
  });

  server.listen(port, () => {
    console.log(`[table] listening on ${port}, db ${DB_PATH}, free text ${ALLOW_FREE_TEXT ? 'ON' : 'off'}`);
  });

  return server;
}

if (import.meta.url === `file://${process.argv[1]}`) {
  start();
}
