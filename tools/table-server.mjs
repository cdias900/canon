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
//   curl -s localhost:8788/trumpet -X POST -d '{"code":"ABCDEF","playerId":"u1","atEpochMs":<now+30s>}'
//   curl -s 'localhost:8788/raid?code=ABCDEF&playerId=u1'

import { createServer } from 'node:http';
import { DatabaseSync } from 'node:sqlite';
import { randomUUID } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');

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
export function soundTrumpet(db, {
  code, playerId, atEpochMs, reason = null, seats = 4, watchPosted = true, acceptedInvite = false,
  now = Date.now()
}) {
  const table = db.prepare('SELECT * FROM tables WHERE code = ?').get(code);
  if (!table) return { error: 'no table with that code', status: 404 };

  const seat = db.prepare('SELECT * FROM seats WHERE table_id = ? AND player_id = ?')
    .get(table.id, playerId);
  if (!seat) return { error: 'you are not at this table', status: 403 };

  if (!Number.isFinite(atEpochMs) || atEpochMs <= now) {
    return { error: 'the trumpet names an hour that has not passed', status: 400 };
  }

  // The sounder is coming — that is what sounding it means — and brings their preparation with
  // the call, the same two facts answerTrumpet() takes from everyone else.
  record(db, table.id, seat.seat_id, 'trumpet', {
    at: atEpochMs, reason, seats, watchPosted: watchPosted !== false, acceptedInvite: acceptedInvite === true
  });
  const trumpet = db.prepare('SELECT last_insert_rowid() AS id').get();
  return { ok: true, trumpetId: trumpet.id, at: atEpochMs, seats };
}

/**
 * A move, held.
 *
 * Rule 11 — nobody sees anybody's choice before choosing — is the reason this writes the move and
 * returns nothing about it. The feed does not carry `committed` payloads (see feed()); they surface
 * only when the turn resolves, all at once, which is the moment the mechanic is for.
 */
export function commitMove(db, { code, playerId, turn, move, now = Date.now() }) {
  const table = db.prepare('SELECT * FROM tables WHERE code = ?').get(code);
  if (!table) return { error: 'no table with that code', status: 404 };

  const seat = db.prepare('SELECT * FROM seats WHERE table_id = ? AND player_id = ?')
    .get(table.id, playerId);
  if (!seat) return { error: 'you are not at this table', status: 403 };

  // Everything below is decided from the log, never from the request: whether there is a raid,
  // whether this seat is in it, which turn is open, which moves exist on it.
  const raid = settle(db, table.id, now);
  if (!raid || raid.finished) return { error: 'there is no raid to answer', status: 409 };
  if (!raid.present.includes(seat.seat_id)) return { error: 'you did not come to this one', status: 403 };
  if (turn !== raid.turn) return { error: 'that turn is not the open one', status: 409 };
  if (!isMoveOpen(contestConfig(), move, raid.turn)) {
    return { error: 'not a move this turn offers', status: 400 };
  }

  const existing = db.prepare(
    "SELECT 1 FROM events WHERE table_id = ? AND id > ? AND seat_id = ? AND kind = 'committed' " +
    "AND json_extract(payload, '$.turn') = ?"
  ).get(table.id, raid.openedId, seat.seat_id, turn);
  if (existing) return { error: 'that turn is already answered', status: 409 };

  record(db, table.id, seat.seat_id, 'committed', { turn, move });

  // The last pick closes the turn at once rather than at the clock: nobody waits two minutes for
  // a decision everyone has already made.
  settle(db, table.id, now);
  return { ok: true, turn };
}

/** Everything that happened after `since`, with held moves withheld. */
export function feed(db, { code, since = 0, playerId = null, now = Date.now() }) {
  const table = db.prepare('SELECT * FROM tables WHERE code = ?').get(code);
  if (!table) return { error: 'no table with that code', status: 404 };

  const raid = raidState(db, table.id, playerId, now);

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

  return { table: { code: table.code, band: table.band, freeText: table.free_text === 1 }, seats, events, raid };
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

// ---------------------------------------------------------------- the group mission
//
// docs/multiplayer.md §06: the raid at stage 6, with up to four seats instead of one player. What
// follows is the resolution the solo MoraleContest does on the device, done here instead — because
// rule 11 needs the hold to be on the server, and once the hold is here the arithmetic has to be
// here too, or two clients could disagree about what a turn did.
//
// THE STATE IS THE LOG. There is no raids table. A raid is a `raid_opened` event followed by
// `committed` and `resolved` events and, eventually, a `raid_finished`; currentRaid() replays them.
// An append-only list is the only structure that is the feed, the audit trail and the conflict
// resolution at once, and a second table would be a second version of the truth.
//
// NOTHING HERE IS SCHEDULED. There is no job runner. A trumpet whose hour has passed is opened, and
// a turn whose clock has run out is resolved, by the first request that arrives afterwards — see
// settle(). That is enough for a table of six and it is honest about what it is.
//
// WHAT THE CLIENT DECLARES, AND WHY THAT IS FINE HERE. The header of this file says the client
// decides nothing, and that holds for every rule: band, membership, whether a move exists, whose
// turn it is, what a turn did. The one thing the client DECLARES is its preparation — whether a
// watch stood the night before, whether the invitation was accepted — because that lives in an
// offline save this server has never seen. It is a balance input and not a safety property: a
// client that lies about its watch gets an easier fight, and nobody else pays for it (§04).

/**
 * The tuning, read from the same file the game reads. Structure and numbers have exactly one copy
 * in this repository, and the server is not allowed a second one: a table that fought with
 * different deltas from the solo game would be a different game.
 */
const CONTESTS = JSON.parse(
  readFileSync(join(ROOT, 'Assets/Resources/Data/contest.json'), 'utf8')
);

/** The contest a table fights. Stage 8's `letters` is the second one and is not in this slice. */
export const GROUP_CONTEST_ID = 'raid';

/**
 * How long a turn stays open once the first present seat can pick. Two minutes: the trumpet named
 * an hour and the people who came are looking at the same screen, so a turn is a decision, not a
 * day. A seat that has not picked when the clock runs out contributes nothing to that turn —
 * nothing, not a penalty (rule 7).
 */
export const TURN_CLOCK_MS = 2 * 60_000;

// The same numbers MoraleContest.Begin adds, so that a seat that prepared the same way faces the
// same wall. They are duplicated from that class doc on purpose and named the same.
const RESOLVE_FOR_MISSING_WATCH = 10;
const RESOLVE_FOR_ACCEPTED_INVITE = 10;
const PRESSURE_FOR_MISSING_WATCH = 6;
const PRESSURE_FOR_ACCEPTED_INVITE = 6;
/** A torch shown to a side that has already seen it, or a torch with nobody behind it. */
const WEAK_WATCH_RESOLVE_DELTA = -4;

function contestConfig(id = GROUP_CONTEST_ID) {
  const config = CONTESTS[id];
  if (!config || !Array.isArray(config.moves) || config.moves.length === 0) {
    throw new Error(`contest.json defines no moves for '${id}'`);
  }
  return config;
}

function findMove(config, moveId) {
  return config.moves.find((m) => m.id === moveId) ?? null;
}

/**
 * Whether a move is on the menu for this turn. The page unlocks a move for EVERY seat once the page
 * turn has passed — A Página arrives at turn 2 for the whole table at once (§06) and is not a
 * reward for the seat that read it (rule 19). Flag-gated moves belong to `letters` and are refused
 * here: the server has no run to read a flag from.
 */
export function isMoveOpen(config, moveId, turn) {
  const move = findMove(config, moveId);
  if (!move) return false;
  if (move.unlocked_by_flag) return false;
  if (move.unlocked_by_page) return config.page_turn > 0 && turn > config.page_turn;
  return true;
}

function eventsOf(db, tableId) {
  return db.prepare('SELECT * FROM events WHERE table_id = ? ORDER BY id').all(tableId)
    .map((row) => ({ ...row, payload: row.payload ? JSON.parse(row.payload) : {} }));
}

/** The latest answer of each seat to a trumpet, with the sounder counted as coming. */
function answersTo(events, trumpet) {
  const answers = new Map();
  answers.set(trumpet.seat_id, {
    coming: true, at: trumpet.at,
    watchPosted: trumpet.payload.watchPosted !== false,
    acceptedInvite: trumpet.payload.acceptedInvite === true
  });
  for (const e of events) {
    if (e.kind !== 'answered' || e.payload.trumpetId !== trumpet.id) continue;
    answers.set(e.seat_id, {
      coming: e.payload.coming === true, at: e.at,
      watchPosted: e.payload.watchPosted !== false,
      acceptedInvite: e.payload.acceptedInvite === true
    });
  }
  return answers;
}

/**
 * The raid a table is in, replayed from the log; null when there is none.
 *
 * Returned in full, moves included, because every caller of this is the server. What reaches a
 * client goes through raidState(), which is where rule 11 is applied.
 */
export function currentRaid(db, tableId) {
  const events = eventsOf(db, tableId);
  let opened = null;
  for (let i = events.length - 1; i >= 0; i--) {
    if (events[i].kind === 'raid_opened') { opened = events[i]; break; }
  }
  if (!opened) return null;

  const raid = {
    openedId: opened.id,
    trumpetId: opened.payload.trumpetId,
    present: opened.payload.present,
    prepared: opened.payload.prepared,
    resolveMax: opened.payload.resolveMax,
    resolve: opened.payload.resolveMax,
    moraleMax: opened.payload.moraleMax,
    morale: opened.payload.moraleMax,
    pressure: opened.payload.pressure,
    turnLimit: opened.payload.turnLimit,
    pageTurn: opened.payload.pageTurn,
    turn: 1,
    turnOpenedAt: opened.at,
    spentWatch: [],
    committed: {},          // seat -> move, this turn only
    resolvedTurns: [],
    finished: null
  };

  for (const e of events) {
    if (e.id <= opened.id) continue;
    if (e.kind === 'committed' && e.payload.turn === raid.turn && !raid.finished) {
      raid.committed[e.seat_id] = e.payload.move;
    } else if (e.kind === 'resolved') {
      raid.resolvedTurns.push({ turn: e.payload.turn, moves: e.payload.moves, resolve: e.payload.resolve, morale: e.payload.morale, at: e.at });
      raid.resolve = e.payload.resolve;
      raid.morale = e.payload.morale;
      raid.spentWatch = e.payload.spentWatch ?? raid.spentWatch;
      raid.turn = e.payload.turn + 1;
      raid.turnOpenedAt = e.at;
      raid.committed = {};
    } else if (e.kind === 'raid_finished') {
      raid.finished = e.payload.outcome;
    }
  }

  return raid;
}

/**
 * Says yes or no to a trumpet. The last answer before the hour is the one that counts, so a person
 * who said yes at lunch and cannot make it at six is not held to lunch. Answering after the hour is
 * refused: the window has closed and the raid has been opened with whoever committed.
 *
 * `watchPosted` and `acceptedInvite` are the seat's preparation, declared by the client — see the
 * section header for why that is acceptable here and nowhere else in this file.
 */
export function answerTrumpet(db, {
  code, playerId, trumpetId, coming, watchPosted = true, acceptedInvite = false, now = Date.now()
}) {
  const table = db.prepare('SELECT * FROM tables WHERE code = ?').get(code);
  if (!table) return { error: 'no table with that code', status: 404 };

  const seat = db.prepare('SELECT * FROM seats WHERE table_id = ? AND player_id = ?')
    .get(table.id, playerId);
  if (!seat) return { error: 'you are not at this table', status: 403 };

  const trumpet = db.prepare("SELECT * FROM events WHERE table_id = ? AND id = ? AND kind = 'trumpet'")
    .get(table.id, trumpetId);
  if (!trumpet) return { error: 'no such trumpet', status: 404 };

  const at = JSON.parse(trumpet.payload).at;
  if (at <= now) return { error: 'that hour has passed', status: 409 };

  record(db, table.id, seat.seat_id, 'answered', {
    trumpetId: trumpet.id, coming: coming === true, watchPosted: watchPosted !== false, acceptedInvite: acceptedInvite === true
  });
  return { ok: true, coming: coming === true };
}

/**
 * Brings the table up to date with the clock: opens a trumpet whose hour has come, resolves a turn
 * whose clock has run out or whose every present seat has picked. Called by every read and every
 * write that touches the raid, so the answer a client gets is never stale by more than one request.
 */
export function settle(db, tableId, now = Date.now()) {
  for (;;) {
    const raid = currentRaid(db, tableId);

    if (raid && !raid.finished) {
      if (!turnIsClosed(raid, now)) return raid;

      // A turn the clock closed is resolved AT the clock, not at the request that noticed: the
      // next turn's window opened when this one shut, and a table nobody looked at for an hour
      // has run through as many turns as the hour holds, not one.
      const everyone = raid.present.every((seat) => raid.committed[seat] != null);
      resolveTurn(db, tableId, raid, everyone ? now : raid.turnOpenedAt + TURN_CLOCK_MS);
      continue;
    }

    // No raid in progress: the next trumpet whose hour has come opens one — at its hour, for the
    // same reason as above. One at a time; a second trumpet due in the same window waits its turn.
    const events = eventsOf(db, tableId);
    const handled = new Set(events
      .filter((e) => e.kind === 'raid_opened' || e.kind === 'raid_skipped')
      .map((e) => e.payload.trumpetId));
    const due = events.find((e) => e.kind === 'trumpet' && !handled.has(e.id) && e.payload.at <= now);
    if (!due) return raid;

    openRaid(db, tableId, events, due, due.payload.at);
  }
}

function turnIsClosed(raid, now) {
  if (now - raid.turnOpenedAt >= TURN_CLOCK_MS) return true;
  return raid.present.every((seat) => raid.committed[seat] != null);
}

/**
 * Opens the raid on whoever said they would come, in the order they said it, up to the trumpet's
 * seat count. Nobody coming is not an error and not a defeat: it is recorded and the table moves
 * on, because not showing up is a legitimate move (§05) and a raid fought by no one is not a raid.
 *
 * The wall the table faces is the SUM of what each present seat brings — base resolve plus the
 * two preparation penalties, per seat — so a table of four faces four walls' worth, and a table
 * of one faces exactly the solo fight. Pressure is shared, because morale is: one number for the
 * whole table, pressed harder if anyone came unprepared. This is a first tuning and it says so in
 * docs/multiplayer.md §06; the solo contest also counts the courses still missing from the
 * contested segment, which this server cannot see and does not pretend to.
 */
function openRaid(db, tableId, events, trumpet, now) {
  const config = contestConfig();
  const wanted = Number.isInteger(trumpet.payload.seats) && trumpet.payload.seats > 0
    ? trumpet.payload.seats : 4;

  const present = [...answersTo(events, trumpet).entries()]
    .filter(([, a]) => a.coming)
    .sort((a, b) => a[1].at - b[1].at)
    .slice(0, wanted);

  if (present.length === 0) {
    record(db, tableId, null, 'raid_skipped', { trumpetId: trumpet.id });
    return;
  }

  const base = config.enemy_resolve_base > 0 ? config.enemy_resolve_base : 60;
  const basePressure = config.base_pressure > 0 ? config.base_pressure : 12;

  let resolveMax = 0;
  let anyMissedWatch = false;
  let anyAcceptedInvite = false;
  const prepared = {};
  for (const [seat, a] of present) {
    resolveMax += base
      + (a.watchPosted ? 0 : RESOLVE_FOR_MISSING_WATCH)
      + (a.acceptedInvite ? RESOLVE_FOR_ACCEPTED_INVITE : 0);
    anyMissedWatch = anyMissedWatch || !a.watchPosted;
    anyAcceptedInvite = anyAcceptedInvite || a.acceptedInvite;
    prepared[seat] = { watchPosted: a.watchPosted, acceptedInvite: a.acceptedInvite };
  }

  const pressure = basePressure
    + (anyMissedWatch ? PRESSURE_FOR_MISSING_WATCH : 0)
    + (anyAcceptedInvite ? PRESSURE_FOR_ACCEPTED_INVITE : 0);

  db.prepare(
    'INSERT INTO events (table_id, at, seat_id, kind, payload) VALUES (?, ?, ?, ?, ?)'
  ).run(tableId, now, null, 'raid_opened', JSON.stringify({
    trumpetId: trumpet.id,
    contest: config.id,
    present: present.map(([seat]) => seat),
    prepared,
    resolveMax,
    moraleMax: config.player_morale > 0 ? config.player_morale : 100,
    pressure,
    turnLimit: config.turn_limit > 0 ? config.turn_limit : 8,
    pageTurn: config.page_verse ? config.page_turn : 0,
    pageVerse: config.page_verse ?? null
  }));
}

/**
 * Closes a turn: every held move is applied at once and revealed at once. This is the moment rule
 * 11 exists for — the table finds out together that three of them held the line while the wall
 * needed somebody to call the others.
 *
 * Order mirrors MoraleContest.RunLoop: the seats' moves, then the other side gives up if its
 * resolve is gone; otherwise its pressure lands on the shared morale, and the table breaks if that
 * is gone; otherwise the next turn opens, or the limit ends it as a draw.
 */
function resolveTurn(db, tableId, raid, now) {
  const config = contestConfig();
  let resolve = raid.resolve;
  let morale = raid.morale;
  const spentWatch = [...raid.spentWatch];
  const moves = {};

  for (const seat of raid.present) {
    const moveId = raid.committed[seat];
    if (!moveId) continue;
    const move = findMove(config, moveId);
    if (!move) continue;
    moves[seat] = moveId;

    let resolveDelta = move.resolve_delta;
    if (moveId === 'show_watch') {
      // A torch is shown once. The second time, or a torch with nobody on the wall behind it,
      // is the weak version — the same rule the solo contest applies.
      const posted = raid.prepared?.[seat]?.watchPosted !== false;
      if (!posted || spentWatch.includes(seat)) {
        resolveDelta = WEAK_WATCH_RESOLVE_DELTA;
      }
      if (!spentWatch.includes(seat)) spentWatch.push(seat);
    }

    resolve = Math.max(0, resolve + resolveDelta);
    morale = Math.min(raid.moraleMax, Math.max(0, morale + move.morale_delta));
  }

  let outcome = null;
  if (resolve <= 0) {
    outcome = 'withdrew';
  } else {
    morale = Math.max(0, morale - raid.pressure);
    if (morale <= 0) outcome = 'broke';
    else if (raid.turn >= raid.turnLimit) outcome = 'limit';
  }

  db.prepare(
    'INSERT INTO events (table_id, at, seat_id, kind, payload) VALUES (?, ?, ?, ?, ?)'
  ).run(tableId, now, null, 'resolved', JSON.stringify({
    turn: raid.turn, moves, resolve, morale, spentWatch
  }));

  if (outcome) {
    db.prepare(
      'INSERT INTO events (table_id, at, seat_id, kind, payload) VALUES (?, ?, ?, ?, ?)'
    ).run(tableId, now, null, 'raid_finished', JSON.stringify({ outcome, resolve, morale }));
  }
}

/**
 * What a client may know about the raid: everything except the moves of the turn still open.
 * `youCommitted` is the only per-player fact, and it is about the asker.
 */
export function raidState(db, tableId, playerId = null, now = Date.now()) {
  const raid = settle(db, tableId, now);
  if (!raid) return null;

  const config = contestConfig();
  const seat = playerId
    ? db.prepare('SELECT seat_id FROM seats WHERE table_id = ? AND player_id = ?').get(tableId, playerId)
    : null;
  const seatId = seat ? seat.seat_id : null;

  return {
    open: !raid.finished,
    outcome: raid.finished,
    turn: raid.turn,
    turnLimit: raid.turnLimit,
    pageTurn: raid.pageTurn,
    pageVerse: config.page_verse ?? null,
    deadline: raid.turnOpenedAt + TURN_CLOCK_MS,
    resolve: raid.resolve,
    resolveMax: raid.resolveMax,
    morale: raid.morale,
    moraleMax: raid.moraleMax,
    present: raid.present,
    youArePresent: seatId != null && raid.present.includes(seatId),
    youCommitted: seatId != null && raid.committed[seatId] != null,
    committedCount: Object.keys(raid.committed).length,
    moves: config.moves
      .filter((m) => !m.unlocked_by_flag)
      .map((m) => ({ id: m.id, open: isMoveOpen(config, m.id, raid.turn) })),
    resolvedTurns: raid.resolvedTurns
  };
}

// ---------------------------------------------------------------- http

const ROUTES = {
  'POST /tables': (db, body) => createTable(db, body),
  'POST /join': (db, body) => joinTable(db, body),
  'POST /say': (db, body) => say(db, body),
  'POST /trumpet': (db, body) => soundTrumpet(db, body),
  'POST /commit': (db, body) => commitMove(db, body),
  'POST /answer': (db, body) => answerTrumpet(db, body),
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
      return send(200, { ok: true, freeTextAllowed: ALLOW_FREE_TEXT, seats: SEATS.length, turnClockMs: TURN_CLOCK_MS });
    }

    if (route === 'GET /feed') {
      const out = feed(db, {
        code: url.searchParams.get('code'),
        since: Number(url.searchParams.get('since') ?? 0),
        playerId: url.searchParams.get('playerId')
      });
      return send(out.error ? out.status : 200, out);
    }

    if (route === 'GET /raid') {
      const table = db.prepare('SELECT id FROM tables WHERE code = ?').get(url.searchParams.get('code'));
      if (!table) return send(404, { error: 'no table with that code' });
      return send(200, { raid: raidState(db, table.id, url.searchParams.get('playerId')) });
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
