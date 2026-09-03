# Multiplayer — the table, the trumpet and the seats

The wall was never built alone. Nehemiah 3 is a list of more than forty groups and the stretch each
one took, and this game has been playing all of them with nobody in the seats. This is the design
for putting people in them.

**Status: specified, server and client running on one machine.** What exists is this document, the
schema, `tools/table-server.mjs` with its tests, and the *Obra em grupo* screen in Unity — create or
join by code, the seats, the feed, the composed lines, the trumpet with its two answers, and the
group mission of §06 played through to its ending. What does not exist: accounts, moderation
tooling, or a deployment anywhere but a developer's laptop. Read §9 before estimating anything.

---

## 01 · What this is, in one paragraph

A **table** is a wall being built by up to six people. Every table is the same table the solo game
already plays — the segments, the stages, the contests — with the difference that some of the
**seats** belong to players instead of to the AI. You join a table with a **code**, you play your own
day whenever you can, and the table remembers what everyone did. When something needs all of you at
once, somebody **sounds the trumpet** and names an hour.

Nothing here is real time. The trumpet is the only appointment the game ever makes.

---

## 02 · Why seats, and why they cost no migration

`docs/nehemiah-game-design.md` already decided this and the schema was built for it: *"Players
replace NPCs, one seat at a time, in the same table. No schema change."*

That is the whole trick. A seat is not a new concept invented for multiplayer — it is a row that
already exists, because the forty-one groups of Nehemiah 3 are already in the game as residents with
names, stretches and work. Today `hananias` is played by the AI. Tomorrow a person occupies that
seat, and everything around it — the stretch he is responsible for, the dialogue that addresses him,
the wall he is raising — is unchanged. **The solo game is the multiplayer game with every seat
played by the house.**

Two consequences worth stating because they are load-bearing:

- **A table with one player is a solo run.** There is no separate mode to build, test or balance.
- **A player who stops coming does not break the table.** Their seat reverts to the AI after a
  configured absence, the way it was before they sat down. Rule 7 — *never punish* — applies to the
  people who stayed: they must not lose a wall because a friend lost interest.

---

## 03 · How a person gets in

There is no matchmaking. There is no browse. There is a code.

```
  Menu → Obra em grupo
    ├─ Criar uma obra      → server returns a 6-character code
    └─ Entrar com código   → type the code
```

**Codes rather than search, by rule 17**, and the reason survives the rule: this game's players are
a youth group, a class, a family. They already know each other, and the code is passed in the room
they are already in. An open lobby would add strangers to a product for thirteen-year-olds and buy
nothing, because nobody was asking to play with strangers.

A code is six characters from an alphabet with no `0/O` and no `1/I/L`, because it gets read aloud.
It expires when the table's season ends.

**Identity is pre-account.** There is no sign-up — §00 of `MVP-SCOPE.md` still holds. A player is a
UUID generated on the device and kept in the save, shown under the name they typed at creation. What
this cannot do is prove anybody is who they say: a lost save is a lost identity, and a shared device
is a shared identity. Everything in §06 is designed around that being true.

**The table is remembered on the device.** The code of the last table joined is kept beside the
player id, and opening *Obra em grupo* rejoins it before showing anything else — so a person who
comes back at the trumpet's hour finds their seat, not a code field. A code the server refuses (the
season ended, the table is gone) is forgotten and the two doors are shown again.

---

## 04 · The table, day to day

Asynchronous. You open the game when you open it.

**The feed** is what happened since you last looked, in the table's own vocabulary: who took which
stretch, what got built, who watched last night, who sounded the trumpet. It is a record of **acts,
not of people** — rule 15 forbids a dashboard that shows what somebody is becoming, and vocation
never appears here at all, not even to the person who owns it.

**The board** is the wall, shared. Sixteen courses across four segments, the same as solo, except
that a course laid by somebody else was laid by somebody else. The exposed segment is the one people
argue about, and that argument is the game.

**Nothing you do costs anybody else anything.** There is no shared pool to drain, no way to take
another player's material, no way to undo their work. The design has one competitive surface — the
choice of which stretch to take — and it is competitive the way a queue is, not the way a duel is.

---

## 05 · The trumpet — the only appointment

`NEH.4.20` is already in the game, already cited, and it already says what this mechanic does: *from
the place you hear the trumpet, gather to us there.* The design doc has been waiting for it — *"the
trumpet switches on here."*

Anyone at the table can sound it. It carries an hour and a reason:

```
  Salum tocou a trombeta.
  Investida esperada hoje, 20h.        4 assentos · 2 confirmados
  [ Eu vou ]   [ Não consigo hoje ]
```

At the named hour the table plays its group beat together — see §06 — with whoever showed up. **Not
showing up is a legitimate move**, and the game says so in those words. A trumpet that punished
absence would be a streak with a horn, and rule 7 forbids it.

The window closes at the hour. It resolves with whoever committed, and the result reaches everyone
else in the feed, consequences included. **This is the only place the game asks people to be
somewhere at a time, and it is one screen deep.**

---

## 06 · The group mission

**The `raid` at stage 6, with four seats instead of one player.** Deliberately not a new mechanic:
the contest already exists, is already balanced, already has A Página in it, and already carries the
beat this whole build exists to produce. Making it multi-seat is a change to who chooses, not to
what happens.

| Solo | At a table |
|---|---|
| One player picks one move a turn | Up to four seats each pick a move for the same turn |
| Enemy resolve from that player's preparation | Enemy resolve from **the table's** preparation, summed across seats |
| The turn resolves on the pick | The turn resolves when every present seat has picked, or the turn clock runs out |

**Nobody sees anybody's move before the turn closes.** Rule 11, and it is why the hold is on the
server: on the client it is one line of DevTools. Picks are written, not shown; the turn resolves and
then everyone sees what everyone chose, at once. The interesting part of a group contest is finding
out that three of you all played Hold the line while the wall needed Call the others — and that
discovery is destroyed by a lobby where you can watch each other decide.

**A Página still arrives at turn 2, for everyone, at the same time.** It is not a reward for the
seat that earned it and it is not divisible. Rule 19 governs: reading pays in understanding, and
understanding is the one thing that scales to a whole table for free.

**How it is played, in the order it happens.** The trumpet names an hour (§05). Until that hour,
everyone answers *Eu vou* or *Não consigo hoje*, and the last answer counts. At the hour, the first
request to arrive opens the raid on whoever said yes, in the order they said it, up to the seats the
trumpet named; the sounder counts as coming. Nobody saying yes is recorded and the table moves on —
it is not a defeat and nobody is told off. Each turn stays open **two minutes** from the moment it
opens, or until every present seat has picked, whichever is first. A seat that has not picked when
the clock runs out contributes nothing to that turn: nothing, not a penalty. There is no job runner
— a turn the clock closed is resolved by the next request, *at the clock*, so a table nobody looked
at for an hour has played out as many turns as the hour held.

**The wall the table faces, as a first tuning.** The other side's resolve is the **sum** of what each
present seat brings — the solo base, plus the solo penalties for a watch that did not stand the night
before and for an invitation from outside that was accepted, *per seat* — so four seats face four
walls' worth and one seat faces exactly the solo fight. Morale is one number for the whole table,
pressed by the base pressure plus the same two penalties if *anyone* came unprepared. Move deltas
are the ones in `contest.json`, one copy, read by the server from the same file the game reads; the
torch is shown once per seat and is the weak version after that. What the server does **not** do
that the solo contest does: count the courses still missing from the contested segment (it cannot
see a wall), and scale *Hold the line* and *Call the others* by what the player built and whom they
spoke to. Those are a second tuning pass, once real tables have been played.

**The preparation is declared by the client.** Whether a watch stood and whether the invitation was
accepted live in an offline save the server has never seen, so they travel with the answer to the
trumpet. This is the one thing the client tells the server rather than the other way round, and it
is a balance input, not a safety property: a client that lies about its watch gets an easier fight
and nobody else pays for it (§04).

**What a table's raid does to anybody's save: nothing.** Not the resolved flag that stops stage six
being fought twice, not the unfinished work that a broken line costs in the solo game, not morale.
The group mission is rendered by the table screen and resolved by the server, and the solo
`MoraleContest` is never involved — because a table's raid happens whenever the trumpet says, to
people at different stages of their own seasons, and a loss at a table that tore down a player's own
wall would be exactly what §04 forbids.

**Stage 8's `letters` is the second one**, and it is not in this slice. It is listed here because the
design falls out the same way, and because `keep_working` — the move only a player who refused the
invitation has — becomes something a seat brings *to the others*, which is the first mechanic in this
game where one person's choice on day 2 pays somebody else's day 8.

---

## 07 · Talking

Two vocabularies, and which one a table gets is a property of the table, not a preference.

### Composed lines — everyone, always available

About twenty lines in the residents' own register, situational rather than generic, keyed like every
other string (`table.line.*`, both locales, validated for parity). They are an **enum on the server**:
a client that sends anything not in the list is rejected, which is what makes this safe rather than
merely discouraged.

```
  "Pego o trecho ao lado do seu."      "Preciso de pedra."
  "Vou vigiar hoje."                    "Toquei a trombeta — vem."
  "Esse trecho é o exposto. Cuidado."   "Não consigo hoje."
  "Fica no meu lado uma hora."          "Já assentei o meu."
```

Plus **map pins** — point at a stretch — and a small set of emotes. In a game about a building site,
almost everything anyone needs to say is operational, and a fixed vocabulary says it faster than
typing. This is not the compromise; it is the default, and it is good.

### Free text — adults only, and behind a switch that is off

Free text and private tables are implemented and **disabled by default** (`ALLOW_FREE_TEXT=0`).
Turning them on is a decision by Pedro and cybersecurity, because of a specific argument in
`docs/persona-and-purpose.md`:

> *The chat protections apply on both sides of the boundary regardless of team composition —
> pre-composed speech, no DMs — **because age is self-declared** and the constraint is loose either
> way.*

That argument is not about trusting teenagers. It is that **a protection keyed to a self-declared
age protects nobody**: the thirteen-year-old who wants free text types `18`. Anything the switch
turns on has to be defensible for the youngest person who could plausibly be behind an adult
profile, and that is a judgement for the person on the team whose job it is.

What ships with the switch, because it cannot ship later: length cap, a `POST /report` that records
a message for a human, and per-player rate limiting. When the switch flips, moderation is a staffing
question, not a code question.

**Minor and adult tables never mix**, switch or no switch. It is a database `CHECK`, per rule 17,
and §08 shows it.

---

## 08 · The schema

One table of tables, one of seats, one append-only log. The log is the feed, the audit trail and the
conflict resolution, all three, because an append-only list of what happened is the only structure
that can be all three honestly.

```sql
CREATE TABLE tables (
  id            TEXT PRIMARY KEY,
  code          TEXT NOT NULL UNIQUE,
  band          TEXT NOT NULL CHECK (band IN ('minor','adult')),
  season_id     TEXT NOT NULL,
  created_at    INTEGER NOT NULL,
  free_text     INTEGER NOT NULL DEFAULT 0 CHECK (free_text IN (0,1)),
  -- Rule 17, as a constraint rather than a promise: a minor table cannot have free text,
  -- and no code path can make one that does.
  CHECK (band = 'adult' OR free_text = 0)
);

CREATE TABLE seats (
  table_id      TEXT NOT NULL REFERENCES tables(id),
  seat_id       TEXT NOT NULL,          -- 'hananias', 'salum' … the resident whose stretch this is
  player_id     TEXT,                   -- NULL means the house plays it
  player_name   TEXT,
  band          TEXT NOT NULL CHECK (band IN ('minor','adult')),
  joined_at     INTEGER,
  last_seen_at  INTEGER,
  PRIMARY KEY (table_id, seat_id)
);

CREATE TABLE events (
  id            INTEGER PRIMARY KEY AUTOINCREMENT,
  table_id      TEXT NOT NULL REFERENCES tables(id),
  at            INTEGER NOT NULL,
  seat_id       TEXT,
  kind          TEXT NOT NULL,          -- joined | built | watched | said | trumpet | committed | resolved
  line_key      TEXT,                   -- for 'said' with a composed line: the key, never the text
  body          TEXT,                   -- for 'said' with free text, and only on an adult table
  payload       TEXT                    -- JSON for the rest
);
```

Three things this shape buys:

**The band is on the seat as well as the table.** Denormalised on purpose: the join has to be
refusable in one statement, without reading the joiner's row from somewhere else and trusting it.

**`line_key` and `body` are different columns.** A composed line stores a key and resolves through
the same `Loc.T` every other string does, so it is translated for the reader rather than for the
writer — two people at one table can be playing in different languages and each sees their own. Free
text cannot do that, and the column shape says so.

**Moves are not here.** A committed contest move lives in `events` as `committed` with an encrypted-
at-rest payload the server does not return until the turn resolves. Rule 11 is a server property; a
column anyone can select is not one.

---

## 09 · What this costs, honestly

| | |
|---|---|
| Specified and running | This document · the schema · `tools/table-server.mjs` · its tests · the *Obra em grupo* screen, the trumpet's answers and the §06 raid, played on the iPhone simulator against a local server |
| Not started | Accounts; moderation tooling; deployment (the server has run only on a developer's machine); the `letters` mission; a second tuning pass on §06 once a real table has been played |
| Breaks | *"No sign-up"* survives (device UUID). ***"Offline at runtime" does not*** — a table needs a network, and that is a real change to `MVP-SCOPE.md` §01 |

**The single biggest risk is not technical.** It is that a table with nobody in it is worse than
solo: the feed is empty, the trumpet never sounds, and the game has promised company it cannot
deliver. The design answers this by making the AI keep playing every empty seat — a table of one is
a solo run that happens to have room — but the answer has never been tested with real people, and it
is the first thing to test.

---

## 10 · The one-line summary for the store page

You do not queue with strangers. You get a code from somebody you know, you take a stretch of wall
next to theirs, and when the horn sounds you both show up.
