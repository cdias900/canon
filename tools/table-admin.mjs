#!/usr/bin/env node
// The human side of the table: what a moderator may read, and the two things they may do.
//
//   node tools/table-admin.mjs --db table.db reports            the reported messages, newest first
//   node tools/table-admin.mjs --db table.db hide <code> <eventId> [note]
//   node tools/table-admin.mjs --db table.db mute <code> <seat> <hours> [note]
//
// Runs on the host, against the database file, and nowhere else: there is no admin route on the
// server and no admin credential, because a credential is a thing that leaks and a route is a
// surface that has to be defended. Whoever can read the file is the moderator.
//
// What it deliberately cannot do — rule 15. `reports` is the only read verb. There is no "show
// this seat's messages" and no "dump the table": a moderator sees what somebody flagged, and the
// rest of a table's talk is the table's. A tool that listed everything would be the leader
// dashboard the rule forbids, with a different job title.
//
// Every action is an append-only `moderated` event in the same log as everything else (see the
// moderation section of table-server.mjs), so the audit trail of what was hidden, by whom and
// why is the same trail as the game's.

import { openDatabase, hideMessage, muteSeat, listReports } from './table-server.mjs';

const args = process.argv.slice(2);
let dbPath = null;
const dbIndex = args.indexOf('--db');
if (dbIndex >= 0) {
  dbPath = args[dbIndex + 1];
  args.splice(dbIndex, 2);
}
if (!dbPath) {
  console.error('--db <path> is required: the file the server was started with');
  process.exit(2);
}

const [verb, ...rest] = args;
const db = openDatabase(dbPath);
const who = process.env.USER || 'moderator';

function stamp(ms) {
  return new Date(ms).toISOString().replace('T', ' ').slice(0, 16);
}

switch (verb) {
  case 'reports': {
    const reports = listReports(db);
    if (reports.length === 0) {
      console.log('no reports');
      break;
    }
    for (const r of reports) {
      const m = r.message;
      const what = !m ? '(message not found)'
        : m.hidden ? '(hidden)'
        : m.body != null ? JSON.stringify(m.body)
        : m.lineKey ?? '(no words)';
      console.log(`${stamp(r.at)}  table ${r.code}  report #${r.reportId}${r.note ? `  note: ${r.note}` : ''}`);
      console.log(`    ${m ? `message #${m.id} by ${m.seat} at ${stamp(m.at)}: ` : ''}${what}`);
    }
    break;
  }
  case 'hide': {
    const [code, eventId, note] = rest;
    const out = hideMessage(db, { code, eventId: Number(eventId), by: who, note: note ?? null });
    console.log(out.error ? `refused: ${out.error}` : `hidden message #${out.eventId} at ${code}`);
    process.exitCode = out.error ? 1 : 0;
    break;
  }
  case 'mute': {
    const [code, seat, hours, note] = rest;
    const until = Date.now() + Number(hours) * 3600_000;
    const out = muteSeat(db, { code, seatId: seat, untilEpochMs: until, by: who, note: note ?? null });
    console.log(out.error ? `refused: ${out.error}` : `${seat} at ${code} speaks in composed lines only until ${stamp(out.until)}`);
    process.exitCode = out.error ? 1 : 0;
    break;
  }
  default:
    console.error('usage: table-admin.mjs --db <path> reports | hide <code> <eventId> [note] | mute <code> <seat> <hours> [note]');
    process.exit(2);
}
