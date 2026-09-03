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
  openDatabase, createTable, joinTable, say, commitMove, feed, soundTrumpet, answerTrumpet, settle,
  raidState, currentRaid, report, hideMessage, muteSeat, listReports, makeCode, SEATS, TURN_CLOCK_MS,
  SAY_WINDOW_MS, SAY_LIMIT
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

// ---------------------------------------------------------------- rule 11, and the group mission
//
// Every raid below is opened the way a real one is: a trumpet names an hour, people answer, the
// hour passes, and the first request after it opens the fight. Time is passed in explicitly, because
// a test cannot wait for a clock and a rule about a clock has to be tested against one.

const T0 = 1_700_000_000_000;
const HOUR = T0 + 3600_000;

/**
 * A table of `n` players with a trumpet sounded for HOUR by the first of them. Seats are handed out
 * in seat-id order, so p0 is baruque, p1 hananias, p2 malquias, p3 meremote, p4 salum, p5 zacur.
 */
function tableWithTrumpet(d, n, { band = 'minor', seats = 4, sounder = {} } = {}) {
  const t = createTable(d, { band, playerId: 'p0', playerName: 'P0' });
  for (let i = 1; i < n; i++) {
    joinTable(d, { code: t.code, playerId: `p${i}`, playerName: `P${i}`, band });
  }
  const call = soundTrumpet(d, { code: t.code, playerId: 'p0', atEpochMs: HOUR, seats, now: T0, ...sounder });
  return { code: t.code, tableId: t.id, trumpetId: call.trumpetId };
}

test('a committed move is not readable before the turn resolves', () => {
  const d = db();
  const { code, trumpetId, tableId } = tableWithTrumpet(d, 2);
  answerTrumpet(d, { code, playerId: 'p1', trumpetId, coming: true, now: T0 + 1 });
  settle(d, tableId, HOUR + 1);

  commitMove(d, { code, playerId: 'p0', turn: 1, move: 'hold_line', now: HOUR + 2 });

  const committed = feed(d, { code, now: HOUR + 3 }).events.find((e) => e.kind === 'committed');
  assert.equal(committed.turn, 1);
  assert.equal(committed.move, undefined, 'the move leaked into the feed');
  assert.equal(committed.payload, undefined, 'the payload leaked into the feed');

  const state = raidState(d, tableId, 'p1', HOUR + 3);
  assert.equal(state.turn, 1, 'the turn closed with one seat still to answer');
  assert.equal(state.committedCount, 1);
  assert.equal(state.youCommitted, false);
});

test('a seat answers a turn once', () => {
  const d = db();
  const { code, trumpetId, tableId } = tableWithTrumpet(d, 2);
  answerTrumpet(d, { code, playerId: 'p1', trumpetId, coming: true, now: T0 + 1 });
  settle(d, tableId, HOUR + 1);

  assert.equal(commitMove(d, { code, playerId: 'p0', turn: 1, move: 'hold_line', now: HOUR + 2 }).ok, true);
  assert.equal(commitMove(d, { code, playerId: 'p0', turn: 1, move: 'call_others', now: HOUR + 3 }).status, 409);
});

test('the raid opens at the hour with whoever said they would come, and not before', () => {
  const d = db();
  const { code, trumpetId, tableId } = tableWithTrumpet(d, 3);
  answerTrumpet(d, { code, playerId: 'p1', trumpetId, coming: true, now: T0 + 1 });
  answerTrumpet(d, { code, playerId: 'p2', trumpetId, coming: false, now: T0 + 2 });

  assert.equal(raidState(d, tableId, null, HOUR - 1), null, 'the raid opened before its hour');

  const state = raidState(d, tableId, 'p2', HOUR + 1);
  assert.equal(state.open, true);
  assert.deepEqual(state.present.sort(), ['baruque', 'hananias'].sort());
  assert.equal(state.youArePresent, false);
});

test('the last answer before the hour is the one that counts, and none after it', () => {
  const d = db();
  const { code, trumpetId, tableId } = tableWithTrumpet(d, 2);
  answerTrumpet(d, { code, playerId: 'p1', trumpetId, coming: true, now: T0 + 1 });
  answerTrumpet(d, { code, playerId: 'p1', trumpetId, coming: false, now: T0 + 2 });

  const late = answerTrumpet(d, { code, playerId: 'p1', trumpetId, coming: true, now: HOUR + 1 });
  assert.equal(late.status, 409);

  const state = raidState(d, tableId, null, HOUR + 2);
  assert.deepEqual(state.present, ['baruque']);
});

test('a trumpet nobody answers is recorded and the table moves on', () => {
  const d = db();
  const { code, trumpetId, tableId } = tableWithTrumpet(d, 1);
  // Even the sounder cannot make it after all.
  answerTrumpet(d, { code, playerId: 'p0', trumpetId, coming: false, now: T0 + 1 });

  assert.equal(raidState(d, tableId, null, HOUR + 1), null);
  const skipped = feed(d, { code, now: HOUR + 2 }).events.find((e) => e.kind === 'raid_skipped');
  assert.equal(skipped.payload.trumpetId, trumpetId);
});

test('the trumpet takes at most the seats it named, in the order they answered', () => {
  const d = db();
  const { code, trumpetId, tableId } = tableWithTrumpet(d, 6, { seats: 4 });
  for (let i = 1; i < 6; i++) {
    answerTrumpet(d, { code, playerId: `p${i}`, trumpetId, coming: true, now: T0 + i });
  }

  const state = raidState(d, tableId, 'p5', HOUR + 1);
  assert.equal(state.present.length, 4);
  assert.equal(state.youArePresent, false, 'the fifth to answer got a seat that was not there');
});

test('a seat that did not come cannot commit, and a stranger cannot either', () => {
  const d = db();
  const { code, trumpetId, tableId } = tableWithTrumpet(d, 2);
  answerTrumpet(d, { code, playerId: 'p1', trumpetId, coming: false, now: T0 + 1 });
  settle(d, tableId, HOUR + 1);

  assert.equal(commitMove(d, { code, playerId: 'p1', turn: 1, move: 'hold_line', now: HOUR + 2 }).status, 403);
  assert.equal(commitMove(d, { code, playerId: 'nobody', turn: 1, move: 'hold_line', now: HOUR + 2 }).status, 403);
});

test('the turn holds until every present seat has picked, then reveals every move at once', () => {
  const d = db();
  const { code, trumpetId, tableId } = tableWithTrumpet(d, 3);
  answerTrumpet(d, { code, playerId: 'p1', trumpetId, coming: true, now: T0 + 1 });
  answerTrumpet(d, { code, playerId: 'p2', trumpetId, coming: true, now: T0 + 2 });
  settle(d, tableId, HOUR + 1);

  commitMove(d, { code, playerId: 'p0', turn: 1, move: 'hold_line', now: HOUR + 2 });
  commitMove(d, { code, playerId: 'p1', turn: 1, move: 'call_others', now: HOUR + 3 });
  assert.equal(raidState(d, tableId, null, HOUR + 4).turn, 1, 'the turn closed with a seat still to answer');

  commitMove(d, { code, playerId: 'p2', turn: 1, move: 'show_watch', now: HOUR + 5 });

  const state = raidState(d, tableId, null, HOUR + 6);
  assert.equal(state.turn, 2);
  const resolved = feed(d, { code, now: HOUR + 6 }).events.find((e) => e.kind === 'resolved');
  assert.deepEqual(resolved.payload.moves, { baruque: 'hold_line', hananias: 'call_others', malquias: 'show_watch' });
  // Three seats' worth of wall: 60 each with the watch posted and no invitation accepted.
  assert.equal(state.resolveMax, 180);
  assert.equal(state.resolve, 180 - 8 - 0 - 20);
  // Shared morale: the call's +12 has nowhere to go on a full meter, and then the base pressure
  // of 12 lands — the same clamp the solo contest applies, and the same lesson about when to call.
  assert.equal(state.morale, 88);
});

test('the clock closes a turn with whoever picked, and an absent seat costs nothing', () => {
  const d = db();
  const { code, trumpetId, tableId } = tableWithTrumpet(d, 2);
  answerTrumpet(d, { code, playerId: 'p1', trumpetId, coming: true, now: T0 + 1 });
  settle(d, tableId, HOUR + 1);

  commitMove(d, { code, playerId: 'p0', turn: 1, move: 'hold_line', now: HOUR + 2 });
  assert.equal(raidState(d, tableId, null, HOUR + TURN_CLOCK_MS - 1).turn, 1);

  const state = raidState(d, tableId, null, HOUR + 1 + TURN_CLOCK_MS);
  assert.equal(state.turn, 2);
  const resolved = state.resolvedTurns[0];
  assert.deepEqual(resolved.moves, { baruque: 'hold_line' });
  assert.equal(state.resolve, 120 - 8);
  assert.equal(state.morale, 100 - 12);
});

test('the page unlocks a move for every seat once the page turn has passed, and not before', () => {
  const d = db();
  const { code, trumpetId, tableId } = tableWithTrumpet(d, 2);
  answerTrumpet(d, { code, playerId: 'p1', trumpetId, coming: true, now: T0 + 1 });
  settle(d, tableId, HOUR + 1);

  // Turn 1: no.
  assert.equal(commitMove(d, { code, playerId: 'p0', turn: 1, move: 'half_and_half', now: HOUR + 2 }).status, 400);
  commitMove(d, { code, playerId: 'p0', turn: 1, move: 'hold_line', now: HOUR + 2 });
  commitMove(d, { code, playerId: 'p1', turn: 1, move: 'hold_line', now: HOUR + 3 });

  // Turn 2 is the page turn: the page is on the table, the move is not yet.
  assert.equal(commitMove(d, { code, playerId: 'p0', turn: 2, move: 'half_and_half', now: HOUR + 4 }).status, 400);
  commitMove(d, { code, playerId: 'p0', turn: 2, move: 'hold_line', now: HOUR + 4 });
  commitMove(d, { code, playerId: 'p1', turn: 2, move: 'hold_line', now: HOUR + 5 });

  // Turn 3: for the seat that read it and for the seat that did not, alike.
  assert.equal(commitMove(d, { code, playerId: 'p1', turn: 3, move: 'half_and_half', now: HOUR + 6 }).ok, true);
});

test('a move the contest does not offer is refused, and so is the letters move', () => {
  const d = db();
  const { code, trumpetId, tableId } = tableWithTrumpet(d, 1);
  settle(d, tableId, HOUR + 1);

  assert.equal(commitMove(d, { code, playerId: 'p0', turn: 1, move: 'pray_harder', now: HOUR + 2 }).status, 400);
  assert.equal(commitMove(d, { code, playerId: 'p0', turn: 1, move: 'keep_working', now: HOUR + 2 }).status, 400);
});

test('the wall the table faces is the sum of what each seat brought', () => {
  const d = db();
  const { code, trumpetId, tableId } = tableWithTrumpet(d, 2, { sounder: { watchPosted: false } });
  answerTrumpet(d, { code, playerId: 'p1', trumpetId, coming: true, acceptedInvite: true, now: T0 + 1 });

  const state = raidState(d, tableId, null, HOUR + 1);
  // p0: 60 + 10 for the missing watch. p1: 60 + 10 for the accepted invitation.
  assert.equal(state.resolveMax, 140);

  // And two players who prepared the same way see the same fight.
  const e = db();
  const again = tableWithTrumpet(e, 2, { sounder: { watchPosted: false } });
  answerTrumpet(e, { code: again.code, playerId: 'p1', trumpetId: again.trumpetId, coming: true, acceptedInvite: true, now: T0 + 1 });
  assert.equal(raidState(e, again.tableId, null, HOUR + 1).resolveMax, 140);
});

test('a torch is shown once; the second time it is the weak version', () => {
  const d = db();
  const { code, tableId } = tableWithTrumpet(d, 1);
  settle(d, tableId, HOUR + 1);

  commitMove(d, { code, playerId: 'p0', turn: 1, move: 'show_watch', now: HOUR + 2 });
  assert.equal(raidState(d, tableId, null, HOUR + 3).resolve, 60 - 20);

  commitMove(d, { code, playerId: 'p0', turn: 2, move: 'show_watch', now: HOUR + 4 });
  assert.equal(raidState(d, tableId, null, HOUR + 5).resolve, 60 - 20 - 4);
});

test('the other side withdraws when its resolve is gone, and the raid is over', () => {
  const d = db();
  const { code, tableId } = tableWithTrumpet(d, 1);
  settle(d, tableId, HOUR + 1);

  // 60 of resolve; the torch takes 20, then 4 a time. Holding the line takes 8 a time.
  let now = HOUR + 2;
  for (let turn = 1; turn <= 8; turn++) {
    const out = commitMove(d, { code, playerId: 'p0', turn, move: turn === 1 ? 'show_watch' : 'hold_line', now: now++ });
    if (out.error) break;
  }

  const state = raidState(d, tableId, null, now);
  assert.equal(state.open, false);
  assert.equal(state.outcome, 'withdrew');
  assert.equal(state.resolve, 0);
  assert.equal(commitMove(d, { code, playerId: 'p0', turn: state.turn, move: 'hold_line', now }).status, 409);
});

test('a raid that runs out its clock with nobody picking finishes on its own', () => {
  const d = db();
  const { code, tableId } = tableWithTrumpet(d, 1);

  const state = raidState(d, tableId, null, HOUR + 1 + 8 * TURN_CLOCK_MS);
  assert.equal(state.open, false);
  // Eight turns of the base pressure take 96 of 100: the limit ends it, and nobody broke. That is
  // rule 7 in a number — a table that was simply not there did not lose anything.
  assert.equal(state.outcome, 'limit');
  assert.equal(state.morale, 4);
});

test('a second trumpet waits for the raid in progress', () => {
  const d = db();
  const { code, tableId } = tableWithTrumpet(d, 1);
  soundTrumpet(d, { code, playerId: 'p0', atEpochMs: HOUR + 1000, now: T0 });
  settle(d, tableId, HOUR + 2000);

  const raids = feed(d, { code, now: HOUR + 3000 }).events.filter((e) => e.kind === 'raid_opened');
  assert.equal(raids.length, 1);
  assert.equal(currentRaid(d, tableId).finished, null);
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

// ---------------------------------------------------------------- volume, and the human side

test('a seat that talks faster than the wall goes up is told so, and may speak again later', () => {
  const d = db();
  const t = createTable(d, { band: 'minor', playerId: 'kid' });

  for (let i = 0; i < SAY_LIMIT; i++) {
    assert.equal(say(d, { code: t.code, playerId: 'kid', lineKey: 'table.line.thanks', now: T0 + i }).ok, true);
  }
  assert.equal(say(d, { code: t.code, playerId: 'kid', lineKey: 'table.line.thanks', now: T0 + SAY_LIMIT }).status, 429);

  assert.equal(say(d, { code: t.code, playerId: 'kid', lineKey: 'table.line.thanks', now: T0 + SAY_WINDOW_MS + SAY_LIMIT }).ok, true);
});

test('a hidden message leaves the feed without its words, and the report still points at it', () => {
  const d = db();
  const t = createTable(d, { band: 'minor', playerId: 'kid' });
  say(d, { code: t.code, playerId: 'kid', lineKey: 'table.line.need_stone' });
  const said = feed(d, { code: t.code }).events.find((e) => e.kind === 'said');
  report(d, { code: t.code, playerId: 'other', eventId: said.id, note: 'spam' });

  assert.equal(hideMessage(d, { code: t.code, eventId: said.id, by: 'ana' }).ok, true);

  const after = feed(d, { code: t.code }).events.find((e) => e.id === said.id);
  assert.equal(after.hidden, true);
  assert.equal(after.lineKey, undefined, 'the words leaked');
  assert.equal(after.body, undefined, 'the words leaked');
  assert.equal(feed(d, { code: t.code }).events.some((e) => e.kind === 'moderated' || e.kind === 'reported'), false,
    'what a moderator did reached the feed');

  const [entry] = listReports(d);
  assert.equal(entry.message.id, said.id);
  assert.equal(entry.message.hidden, true);
  assert.equal(entry.note, 'spam');
});

/**
 * An adult table WITH free text, whatever this process's environment says. Written straight into
 * the schema, the way the constraint test does, because the mute is the one moderation action that
 * only matters when free text is on — and a test that ran against a table without it would be
 * asserting the wrong refusal and covering nothing.
 */
function adultTableWithFreeText(d, playerId) {
  d.prepare('INSERT INTO tables (id, code, band, season_id, created_at, free_text) VALUES (?,?,?,?,?,?)')
    .run('free', 'FREE22', 'adult', 's', T0, 1);
  for (const seat of SEATS) {
    d.prepare('INSERT INTO seats (table_id, seat_id, band) VALUES (?, ?, ?)').run('free', seat, 'adult');
  }
  return joinTable(d, { code: 'FREE22', playerId, playerName: 'Ana', band: 'adult' });
}

test('a muted seat loses free text until the hour named, and keeps the composed lines', () => {
  const d = db();
  const joined = adultTableWithFreeText(d, 'grown');
  assert.equal(say(d, { code: 'FREE22', playerId: 'grown', body: 'before', now: T0 }).ok, true, 'free text is on at this table');

  const until = T0 + 3600_000;
  assert.equal(muteSeat(d, { code: 'FREE22', seatId: joined.seat, untilEpochMs: until, now: T0 }).ok, true);

  const refused = say(d, { code: 'FREE22', playerId: 'grown', body: 'hi', now: T0 + 1 });
  assert.equal(refused.status, 403);
  assert.match(refused.error, /for now/, 'the mute refusal, not the table refusal');

  assert.equal(say(d, { code: 'FREE22', playerId: 'grown', lineKey: 'table.line.thanks', now: T0 + 1 }).ok, true);
  assert.equal(say(d, { code: 'FREE22', playerId: 'grown', body: 'hi again', now: until + 1 }).ok, true);
});

test('a mute cannot name an hour that has passed, and a hide needs a real message', () => {
  const d = db();
  const t = createTable(d, { band: 'minor', playerId: 'kid' });

  assert.equal(muteSeat(d, { code: t.code, seatId: t.seat, untilEpochMs: T0 - 1, now: T0 }).status, 400);
  assert.equal(hideMessage(d, { code: t.code, eventId: 999 }).status, 404);
});
