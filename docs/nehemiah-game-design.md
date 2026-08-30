# A Cidade Quebrada — season design

> The design of the whole season, beyond the MVP. Solo first, co-op after.
> A shared world built on the rebuilding of the wall of Jerusalem.

## The thesis

The book of Nehemiah does not need adapting to become a game. **It already is one.** It brings a build
order, a threat system, an economic crisis, an information war and a countdown — all of it written,
chapter by chapter.

| Chapter | System already in the text |
|---|---|
| 2 | Night reconnaissance, telling nobody. A scouting tutorial. |
| 3 | A build manifest: 40+ named groups, each with its own stretch. |
| 4 | Threat escalation in three stages + the allocation mechanic (half build, half watch). |
| 5 | Internal economic crisis: hunger, mortgaged fields, interest charged by the nobles themselves. |
| 6 | Information war: four letters to lure, then a paid prophet. |
| 6:15 | 52 days. The season's countdown. |
| 8 | The climax: a public reading of the Law. |

## The wall is the server

Nehemiah 3 is a multiplayer manifest — it lists more than forty groups and says which stretch each one
raised. The wall is not scenery: it is the state of the world, and it belongs to everyone.

Ten gates, with their builders recorded in the text:

| Gate | Builder | Ref |
|---|---|---|
| Sheep | Eliashib the high priest, and his brothers | `NEH.3.1` |
| Fish | The sons of Hassenaah | `NEH.3.3` |
| Old | Joiada and Meshullam | `NEH.3.6` |
| Valley | Hanun and the inhabitants of Zanoah | `NEH.3.13` |
| Dung | Malchijah, ruler of Beth Haccherem | `NEH.3.14` |
| Fountain | Shallun, ruler of Mizpah | `NEH.3.15` |
| Water | The temple servants | `NEH.3.26` |
| Horse | The priests, each in front of his own house | `NEH.3.28` |
| East | Shemaiah, keeper of the east gate | `NEH.3.29` |
| Guard | Malchijah, son of the goldsmiths | `NEH.3.31` |

**The hook:** at the end of the season, the name of whoever raised each stretch is engraved on that
stretch. It is what chapter 3 does with the real builders.

## The problem of the known ending

Everyone already knows the wall gets built. The freedom is not in *whether*, it is in *how*: which
gates first, at what cost, with who still beside you. The text helps — chapter 3 already records a
desertion — `NEH.3.5` records, in writing and for good, that the nobles of Tekoa would not stoop to
the work.

## Who you are — three layers

You are not Nehemiah. You are one of the people he convinced. If every player is the hero, nobody is.

The first version got the altitude wrong: goldsmith and perfumer are **building-site trades**, and they
do not survive a change of season — there is no perfumer in the story of Joseph in Egypt.

| Layer | Scope | What it is |
|---|---|---|
| **Vocation** | Permanent, across seasons | Not chosen: the game watches what you do and gives you a name. Identity and asymmetry. |
| **Trade** | Per season | Comes from the story. Stonemason in Nehemiah, granary scribe with Joseph. Brings the verbs and the look. Cosmetic customisation lives here. |
| **Post** | Per session | What you are doing today: quarry, wall, watch, ledger. Variety without commitment. |

### The six vocations

Archetypes, not professions. Tested against two different seasons.

| Vocation | In Nehemiah | In Joseph in Egypt |
|---|---|---|
| **The Zealot** | Works the exposed stretch, where damage lands first | Refuses Potiphar's wife. Confronts the brothers. |
| **The Scribe** | Reads the four letters and sees the trap of Ono | The granary ledger: seven years of accounting nobody did |
| **The Shepherd** | Covers the stretch of whoever did not come | Feeds Egypt, and then the family that sold him |
| **The Exile** | Knows the price of returning, and does not idealise the city | A foreigner from the first chapter to the last |
| **The Prophet** | Realises Shemaiah was paid | Reads the dream and sees the seven years |
| **The Steward** | A cupbearer who becomes governor — and refuses the office's ration | Potiphar's house, prison, all of Egypt |

**Ability design rule:** each vocation gives one work ability and one trial ability, and the two have
to be **the same concept in two contexts** — never a hammer plus a sword. The Scribe *reads ahead*: on
the work it reveals the optimal build order; in the trial it reveals the adversary's real intent.

**Discovery rule:** never show the vocation progress bar. If the player sees that three more brave
actions make them a Zealot, it becomes a to-do list. Accumulate hidden, reveal the name. This is rule
10.

**Reading pays in understanding, never in numbers.** Reading gives knowing which verb works when — never
a bonus or a level — and the central verbs are never locked behind it. See
[`persona-and-purpose.md`](persona-and-purpose.md) §*Rule 19*.

## The loop — half build, half watch

### Day
- **Gather rubble.** The stone comes from the burnt ruins of the city itself (`NEH.4.2`).
- **Lay it.** Direct control on your stretch.
- **Negotiate timber.** It is not gathered: it comes by letter to the keeper of the king's forest. A
  resource unlocked by diplomacy.
- **Sustain morale.** What `NEH.4.6` credits the wall's first half to is the people's will to work.
  Here that is a resource, and it drains.

### Night
- **Post the watch.** A stretch with no watch wakes up damaged.
- **The trumpet.** Whoever is attacked sounds it, and the others converge on the breach (`NEH.4.20`).
- **The patrol.** A night inspection of the wall (`NEH.2.13`).
- **Cover for someone.** Whoever could not come has their stretch covered by another, by name.

**The decision that repeats:** every worker on watch is a worker off the work. One control, and neither
end may go to zero (`NEH.4.16`).

**How time passes.** There is no end-of-day button. The day has a work budget, the village's light is
that budget seen another way, and every stone laid pulls the sun down — slowly in the morning, fast at
the end, the way an afternoon really does escape someone. Budget spent, night falls and the work/watch
split presents itself. Whoever wants to stop early has the mat, and that is all it does.

Three rules hold that clock:

- **It only moves when the player acts.** No wall clock. Standing still costs nothing, and neither does
  talking — dialogue is where the citations live, and charging time for it would be a toll on the
  north-star metric.
- **The night waits.** A panel open, a cutscene, a conversation: dusk waits. The chapter reader is a
  panel like any other, so a day never ends while someone is reading.
- **Stopping early is a choice, never a chore.** The mat exists for whoever is finished before their
  capacity is, and for whoever decided to build nothing today. The day would end by itself anyway.

## Four threat vectors

What makes Nehemiah good material is not that it has enemies — it is that the four attacks are of
**different kinds**, and each demands a response the previous one did not teach.

| Ch. | Threat | Response |
|---|---|---|
| 4 | **Mockery** — the work is ridiculed in public | There is no target to attack. A pure morale drain; the response is leadership and speech. |
| 4 | **Armed conspiracy** — a coalition agrees to attack by surprise | Vigilance and deterrence. **In the text the attack never happens** — the victory is not needing to fight. |
| 5 | **The famine, and your own nobles** — families sell children while nobles charge interest | Unsolvable by wall. Requires redistribution and the leader giving up privilege. The correct play = deliberately losing a resource. |
| 6 | **False prophecy** — letters to lure, then a paid prophet | Telling the true from the false by what is already written. Hermeneutics becoming a mechanic. |

## The trial system — a morale bar, not a health bar

If it is a turn-based RPG there has to be "combat", but in Nehemiah the attack never happens and a
death counter would be the project's worst mistake. **Swap the health bar for a morale bar.** The
maths is identical, the fiction is one the text supports, and victory becomes the adversary giving up
(`NEH.6.16`).

- **One engine, four grammars.** Mockery is a pure morale trial. The breach weighs position and
  preparation. The famine weighs your own resources. Prophecy weighs discernment.
- **No vocation covers all four.** The Shepherd defends against mockery and can do nothing against
  false prophecy. Your vocation decides which chapter you are afraid of.
- **Turns buy a great deal for free:** no netcode, no timing window, no impact animation. And skill in
  an action game is a particle effect; in turns it is a choice with the bill in plain sight.
- **Recruitment, not collection.** NPCs agree to work your stretch according to your reputation.
  "Collecting biblical characters" is the version that would go wrong.

## The rule of prayer

`NEH.4.9` solves the concept's most delicate problem, and the whole of it is in one conjunction:
they prayed **and** they posted a watch. Neither instead of the other.

- **Prayer returns information, never force.** Clarity about the enemy's intent, about which faction is
  wavering, about the state of the economy. Never damage.
- **It costs the night.** On a tight countdown, the vigil takes a cycle away from the work. Devotion
  that costs nothing is a mana recharge.
- **The rule closes in both directions:** prayer alone loses the wall, watch alone loses the point.
- **God never speaks in generated text.** A canonical figure says only what is attested; an invented
  character beside them talks freely.

## The season

- The work ends on day 52 — the text marks the date.
- **The win condition is not killing anyone.** In `NEH.6.16` the enemies lose their own confidence
  on seeing the finished wall. You win by the adversary's acknowledgement.
- **The prize for building the city is reading the book.** With the wall finished, the people ask Ezra
  to bring the Law, and he reads from morning until midday (`NEH.8`). The closing event is a public
  reading, with the whole server present. The north-star metric becomes the ending.

**Calendar note:** 52 sessions is too long for a game. Aim for **12 to 15 sessions per season**, each
covering a few days of the fiction. The 52 days remain the feat the text announces.

## Discretion

Playing a follower rather than the leader is what makes the discretion honest: a stonemason newly back
from exile **genuinely does not know** who the man from the capital with letters from the king is.
Point-of-view discipline, not a marketing trick.

- **Deferring a name is legitimate. Swapping a name is not.** In the first hours he is *the governor*.
  Renaming Jerusalem to throw people off is the trap: strip the references and let the player find out
  too late.
- **Nothing is denied, nothing is highlighted.** Every gate has a codex entry with its reference from
  the first hour. And the real names sound invented: *Dung Gate*, *Sheep Gate* read as worldbuilding.
- **The reveal is a weapon, not a notice.** A threat arrives that cannot be solved, and the game shows
  the page: half held spears, half built. The instant you discover it is the Bible is the same instant
  the Bible becomes useful.

## Production — solo first, co-op without migration

| Phase | What it is |
|---|---|
| **Solo** *(this MVP)* | Nehemiah 3 has 40+ groups. You play one; the AI plays the others. Threats, cycle, economy and countdown all work alone. |
| **Co-op** | Players replace NPCs, one seat at a time, **in the same table**. No schema change. The trumpet switches on here. |
| **A real MMO** | Do not. Hundreds of concurrent avatars buys nothing the item above does not deliver. |

**The problem with solo:** with nobody waiting for you, what brings you back? The forty-one groups
**have names**. You come back for Hananias, the perfumer, who is struggling on his stretch — and it is
the same table that later becomes a player's seat.

## Camera, control and reference

Pixel art in a 3/4 view is the shortest path to looking like a real game with a small team — and it is
the best possible camouflage.

1. **3/4, never pure top-down.** The central visual payoff is the wall going up, and height is the axis
   a top-down view flattens.
2. **Stardew promises safety; this game does not.** The grammar works, the palette does not. The closest
   *loop* reference is **Kingdom: Two Crowns** — recruit, order a wall built, and at night something
   comes out of the dark.
3. **Portrait, with two cameras.** Close, following the character; and the **Patrol**, a view of the
   whole wall that scrolls horizontally.
4. **Fewer, larger tiles, UI in the thumb zone.** More zoom than Stardew, automatic snapping to the
   stretch, no hover tooltips.

**Control:** tap to move works because there is no combat. Defence is deterrence and positioning.

**Hidden cost:** a tileset is cheap, animation is not. Four directions instead of eight, one skeleton
with palette and prop swaps.

> **Engine: Unity 6 LTS.** The implementation is done by agents, and there is far more C#/Unity in model
> training than GDScript. See [`../MVP-SCOPE.md`](../MVP-SCOPE.md) §01.

## What to take from Clash of Clans, and what to refuse

| | |
|---|---|
| **Take** | Work that runs while you are away. Thematically exact — chapter 3 is about work happening on forty stretches at once. |
| **Take** | The night resolves in your absence. Finding out is the hook. A short two-hour timer, a long one that crosses the night. |
| **Refuse** | Paying to skip a timer. In a game about a wall raised by voluntary sacrifice, selling the shortcut refutes the theme. |
| **Refuse** | Losing what was already standing. Night damage hits the **unfinished** stretch. **You can lose tomorrow, never yesterday.** |

## The pattern repeats by book

| Book | Mechanical genre |
|---|---|
| Nehemiah | Building and defence. The pilot. |
| Joseph in Egypt | Forecasting and stockpiling. Seven years of plenty, seven of famine. |
| Noah | Deadline and logistics. Unknown end date, an impossible cargo manifest. |
| David's mighty men | Squad. 2 Samuel 23 lists the men by name and by deed. |
| Esther | Intrigue and timing. Social deduction without a battle. |
| Acts | Expansion and map. Communities planted on a map that keeps opening. |

## Risks

| Risk | Antidote |
|---|---|
| **Farming Samaritans** — Sanballat, Tobiah and Geshem were real political figures | Defence is deterrence, not killing. No death counter. |
| **Scripture becoming a resource** — "collect 50 stones" | Chapter 8: the work exists to arrive at the reading, not the other way round. |
| **Art cost** — avatars, animation, tileset, staged construction | Orders of magnitude more asset than a text game. It is the biggest hidden cost. |
| **The fuse is short** | Someone only has to say "Jerusalem" and it is over. The turn has to be designed as a chapter from the start. |

> The refusal the whole day-2 invitation is built on: `NEH.6.3`.
