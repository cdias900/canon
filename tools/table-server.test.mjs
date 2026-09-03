// The rules that cannot be wrong, tested where they live.
//
//   node --test tools/table-server.test.mjs
//
// Two of these are rule 17 and one is rule 11, and they are tested against the database rather than
// against the HTTP surface on purpose: a rule enforced by a route is a rule that a second route can
// forget. These call the operations directly, the way a future endpoint would.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  openDatabase, createTable, joinTable, say, commitMove, feed, soundTrumpet, makeCode, SEATS
} from './table-server.mjs';

const db = () => openDatabase(':memory:');

// ---------------------------------------------------------------- rule 17

test('a minor cannot join an adult table', () => {
  const d = db();
  const adult = createTable(d, { band: 'adult', playerId: 'grown', playerName: 'Ana' });

  const out = joinTable(d, { code: adult.code, playerId: 'kid', playerName: 'Kai', band: 'minor' });

  assert.equal(out.status, 403);
  assert.match(out.error, /age band/);
});

test('an adult cannot join a minor table', () => {
  const d = db();
  const minors = createTable(d, { band: 'minor', playerId: 'kid', playerName: 'Kai' });

  const out = joinTable(d, { code: minors.code, playerId: 'grown', playerName: 'Ana', band: 'adult' });

  assert.equal(out.status, 403);
});

test('a minor table cannot carry free text, whatever the environment says', () => {
  const d = db();
  const minors = createTable(d, { band: 'minor', playerId: 'kid' });

  assert.equal(minors.freeText, false);

  const out = say(d, { code: minors.code, playerId: 'kid', body: 'oi tudo bem' });

  assert.equal(out.status, 403);
  assert.match(out.error, /own words/);
});

test('the schema itself refuses a minor table with free text', () => {
  const d = db();

  // Not through createTable — straight at the table, the way a future code path that forgot the
  // rule would arrive. The constraint has to be the thing that stops it.
  assert.throws(
    () => d.prepare(
      'INSERT INTO tables (id, code, band, season_id, created_at, free_text) VALUES (?,?,?,?,?,?)'
    ).run('x', 'ABC234', 'minor', 's', Date.now(), 1),
    /CHECK constraint/
  );
});

// ---------------------------------------------------------------- composed speech

test('only lines this game says are sayable', () => {
  const d = db();
  const t = createTable(d, { band: 'minor', playerId: 'kid' });

  assert.equal(say(d, { code: t.code, playerId: 'kid', lineKey: 'table.line.need_stone' }).ok, true);

  const made_up = say(d, { code: t.code, playerId: 'kid', lineKey: 'table.line.whatever_i_want' });
  assert.equal(made_up.status, 400);
});

test('a composed line is stored as a key, never as words', () => {
  const d = db();
  const t = createTable(d, { band: 'minor', playerId: 'kid' });
  say(d, { code: t.code, playerId: 'kid', lineKey: 'table.line.will_watch' });

  const said = feed(d, { code: t.code }).events.find((e) => e.kind === 'said');

  assert.equal(said.lineKey, 'table.line.will_watch');
  assert.equal(said.body, null);
});

test('somebody who is not at the table cannot speak at it', () => {
  const d = db();
  const t = createTable(d, { band: 'minor', playerId: 'kid' });

  const out = say(d, { code: t.code, playerId: 'stranger', lineKey: 'table.line.thanks' });

  assert.equal(out.status, 403);
});

// ---------------------------------------------------------------- rule 11

test('a committed move is not readable before the turn resolves', () => {
  const d = db();
  const t = createTable(d, { band: 'minor', playerId: 'a', playerName: 'A' });
  joinTable(d, { code: t.code, playerId: 'b', playerName: 'B', band: 'minor' });

  commitMove(d, { code: t.code, playerId: 'a', turn: 2, move: 'half_and_half' });

  const committed = feed(d, { code: t.code }).events.find((e) => e.kind === 'committed');

  assert.equal(committed.turn, 2);
  assert.equal(committed.move, undefined, 'the move leaked into the feed');
  assert.equal(committed.payload, undefined, 'the payload leaked into the feed');
});

test('a seat answers a turn once', () => {
  const d = db();
  const t = createTable(d, { band: 'adult', playerId: 'a' });

  assert.equal(commitMove(d, { code: t.code, playerId: 'a', turn: 1, move: 'hold_line' }).ok, true);
  assert.equal(commitMove(d, { code: t.code, playerId: 'a', turn: 1, move: 'call_others' }).status, 409);
});

// ---------------------------------------------------------------- seats and codes

test('a table opens with every seat the game draws, and the maker takes one', () => {
  const d = db();
  const t = createTable(d, { band: 'minor', playerId: 'kid', playerName: 'Kai' });
  const state = feed(d, { code: t.code });

  assert.equal(state.seats.length, SEATS.length);
  assert.equal(state.seats.filter((s) => s.taken).length, 1);
});

test('a table fills and then refuses', () => {
  const d = db();
  const t = createTable(d, { band: 'minor', playerId: 'p0' });

  for (let i = 1; i < SEATS.length; i++) {
    assert.equal(joinTable(d, { code: t.code, playerId: `p${i}`, band: 'minor' }).error, undefined);
  }

  const late = joinTable(d, { code: t.code, playerId: 'late', band: 'minor' });
  assert.equal(late.status, 409);
  assert.match(late.error, /full/);
});

test('rejoining returns the seat you already had', () => {
  const d = db();
  const t = createTable(d, { band: 'minor', playerId: 'kid' });

  const again = joinTable(d, { code: t.code, playerId: 'kid', band: 'minor' });

  assert.equal(again.rejoined, true);
  assert.equal(again.seat, t.seat);
});

test('a code has no character that is read aloud wrong', () => {
  for (let i = 0; i < 200; i++) {
    assert.doesNotMatch(makeCode(), /[01OIL]/);
  }
});

// ---------------------------------------------------------------- the trumpet

test('the trumpet cannot name an hour that has passed', () => {
  const d = db();
  const t = createTable(d, { band: 'minor', playerId: 'kid' });

  const past = soundTrumpet(d, { code: t.code, playerId: 'kid', atEpochMs: Date.now() - 1000 });

  assert.equal(past.status, 400);
});

test('the trumpet reaches the table', () => {
  const d = db();
  const t = createTable(d, { band: 'minor', playerId: 'kid' });
  soundTrumpet(d, { code: t.code, playerId: 'kid', atEpochMs: Date.now() + 3600_000, seats: 4 });

  const call = feed(d, { code: t.code }).events.find((e) => e.kind === 'trumpet');

  assert.equal(call.payload.seats, 4);
});
