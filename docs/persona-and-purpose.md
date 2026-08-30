# Persona and purpose

> Who the game is for, what it has to cause, and the rule that connects reading to mechanics.
>
> **Two rules are born here and count as non-negotiable:** reading pays in understanding, never in
> numbers; and the skippability of reading is the measuring instrument, not a defect to be fixed.
> They are rules 19 and 20 in [`AGENTS.md`](../AGENTS.md).

## The persona

**Band: 13-19 directly. 10-12 only through an adult channel** — youth leader, school, guardian.

The persona is not a demographic, it is an **asymmetry: someone whose stated priority and whose
calendar disagree, and who knows it.**

They believe the text matters. They find the text boring. And they have infinitely better options in
their pocket.

From which comes the consequence that saves the most work: **the game does not spend a second
convincing anyone that the Bible matters.** No apologetics, no persuasion, no defence of the faith.
The work is logistics and context.

**The competitor is not another Bible app.** It is TikTok, Roblox, Free Fire, the arcade game on the
phone. The contest is for attention, not for a reading slot. It is won or lost there.

### The three layers

| Layer | Who | How they arrive | Architecture |
|---|---|---|---|
| **Core** | 13-17 | Friend invitation, team code | All of rule 17: age band in the profile, matchmaking blocked, pre-composed speech |
| **Adult edge** | 18-19 | Friend invitation, search | Adult, without the minor architecture |
| **Young edge** | 10-12 | **Only through an adult.** Never through store search | Consent through the institutional channel — see Pending |

### Why not 4-14

If the team's framing comes from the **4/14 Window** (a missions concept: spiritual formation happens
mostly between ages 4 and 14), the intent is right and the persona is not. The window says *when
formation takes hold*. It is mission strategy, not a player.

Two concrete problems:

- **The north-star metric is impossible in the bottom half of that band.** A 7-year-old does not leave
  the game of their own accord to read a whole chapter of Nehemiah. `deep_read` does not fire. Without
  the metric, the product has no reason to exist.
- **The Nehemiah material is adult.** Chapter 5 is a debt crisis in which families sell their own
  children. Chapter 6 is paid disinformation. Excellent material at 14. Unusable at 7.

You serve the 4/14 Window by doing **one excellent thing at the top of it** and reaching the bottom
through the adult — not by diluting a product between a 4-year-old and a 14-year-old.

**Serving 10 and under would be a different product:** read-aloud, co-play with the guardian, driven
by image, and the metric would become *"guardian and child read together"*, not `deep_read`. That is a
legitimate product. It is not this one.

### What the persona decides

| Question | The persona's answer |
|---|---|
| Does the game need to explain why the Bible matters? | No. They already believe it. Saves half the script. |
| Long session or short? | Short. The competitor is the feed. |
| Where is the game discovered? | Friend and adult. **Not** by store search. |
| Is reading an obstacle or a reward? | A reward — and always skippable (below). |
| Tone? | Never pastoral. Rule 13 is the border. |
| Which translation? | The one a teenager reads without effort. That makes it a product decision, not only a licence one. |

## The transformation thesis

**The game transforms nobody.** The game awakens the desire to read; what happens in the reading
belongs to the text and to God. That border is deliberately narrow — it is what the product can
promise and deliver.

What the game removes are nameable frictions:

| Friction | What the game does |
|---|---|
| *"I don't know where to start"* | The game chooses. |
| *"I don't understand the context"* | The game **is** the context. Nehemiah 3 is a list of forty names that says nothing to someone arriving cold. After three sessions with Hananias, the same list is a credits roll of people you know. |
| *"I don't see why it matters"* | `NEH.4.17` arrives as tactics, not as a lesson. |
| *"Guilt"* | No record of failure. Ever. A corollary of rule 7. |
| *"I don't have time"* | They do — and the time is already in the game. |

> **The game does not make the Bible interesting. It makes the reader invested. Interest is a
> property of the relationship, not of the text.**

Discretion serves this, and the formulation in `nehemiah-game-design.md` gets more precise this way:
**discretion is not camouflage against the unbeliever; it is insulation against guilt.** This persona
does not need the Bible hidden — they need the *accounting of failure* hidden. A devotional app opens
with "your plan is 47 days behind". This game opens with a city in ruins.

## The ladder

| Step | What it is | Status |
|---|---|---|
| 1 · **Curiosity** | Opens the chapter once, because the game pointed | **Target.** It is what this build measures |
| 2 · **Return** | Opens it again, without the game asking | **Target.** A completely different signal from the first |
| 3 · **Habit** | Reads on a day they did not play | **Target.** The real ruler |
| 4 · **Study** | Reads around it: cross-reference, context, divergent readings | A consequence, not a target |
| 5 · **Identity** | The reading changes a decision outside the game | A consequence, not a target |

Steps 4 and 5 are not product goals and must not become features. The product is a **desire engine**,
and that is why the metrics take the shape of desire — spontaneous return, reading ahead, reading the
game did not ask for — never the shape of comprehension.

**Step 3 is the victory, and it means the game became dispensable for that reading.** Any engagement
product would call that churn. This one calls it success — and the design already agreed: the season's
win condition is `NEH.8`, the public reading. **The win condition is already the north-star metric.**

## Desire is not incentive

**Reward produces behaviour. An unanswered question produces desire.** Reading driven by reward stops
the day the reward stops; reading driven by curiosity does not.

Nehemiah is well served here because **it is a memoir with a score to settle** — it is full of hooks
its own author left:

- The night patrol, alone, telling nobody (`NEH.2`). Why the secrecy?
- `NEH.3.5` — a group recorded in writing as having refused to work. A slight preserved for 2,400
  years is gossip, and gossip is the most reliable curiosity engine there is. A 13-year-old cares that
  someone was exposed forever.
- Four letters, and then a fifth, open (`NEH.6`). What was written in it?

The test, then, is not whether "Saber mais" was pressed. It is **which button was pressed**: the one
that wants a bonus, or the one that wants to know what happened. Same pixels, different products.

## Rule 19 — reading pays in understanding, never in numbers

> **Supersedes** the bridge described in `nehemiah-game-design.md` (*"the ability levels up when the
> player reads the chapter it comes from"*).

- ❌ Read chapter 4 → +10 morale. That is a slot machine with a Bible skin. It vanishes with the bonus
  and transfers nothing outside the game.
- ✅ Reading tells you **which verb works when**.

**The game never locks the central verbs.** Building, watching and splitting people between work and
watch are available from day one — the split is the core loop of session 01 and **cannot depend on
reading**, because the session-03 reveal only works if the player has been splitting blind. Locking
allocation behind the chapter dismantles the reveal.

What reading gives is knowing **that the letters of Ono are a trap before the fourth arrives; that the
famine comes while there is still grain; that Shemaiah was paid.** Knowledge as an advantage, never an
unlock as a gate.

Whoever did not read always has a valid play. They have a worse one. That respects rule 7: do not
punish, and still reward.

**The rule is self-enforcing.** Someone who opens the chapter without reading gets a tactic they do not
know how to use — there is no faking comprehension of a tactic that has to be executed. No policing is
needed, and no quiz.

## Rule 20 — skippability is the instrument

**If reading paid a number, 100% of players would "read" — and the number would stop meaning
anything.** That would be destroying the measuring instrument itself to make the curve go up.

`deep_read` only has meaning **because it can be skipped.** A metric that cannot be skipped measures
obedience, not desire — and desire is the entire product.

So the product question is never *"how do we stop them skipping"*. It is **"what fraction converts, and
is that fraction growing?"** — and that is not learned by bribing.

**Plan a funnel, not a conversion.** Some players will play whole seasons without opening the text.
That is top of funnel, not failure. The design work is the **second and third invitation**, not the
first: whoever skipped in session 03 may read in session 09 because the team needed them to.

## Multiplayer is what makes the rule work

Alone, understanding is private satisfaction — and private satisfaction is easy to skip. That was the
right objection to "understanding instead of numbers".

**In a team, understanding becomes status.** Whoever read chapter 5 ahead knows the debt crisis is
coming, and warns the group. That is not a bonus: it is being the person who saved everyone's harvest.
**Status among peers is the strongest motivator that exists in the 13-19 band** — stronger than any
number the game could hand out.

Multiplayer earned its place through acquisition (the friend invitation). What it buys as a bonus is
the motivation the rule above was missing.

> Multiplayer is **out of the hackathon MVP**, which is solo. This section is the design for the
> version after it.

### The Bible as the game's wiki

**Only after the reveal** — the arc *is* the discovery, so this is a season loop that begins where this
build ends.

Once the player knows the book is the strategy guide, **let them read ahead, and reward them by not
getting in the way.** Reading chapter 5 before the game gets there means knowing about the crisis and
preparing for it.

Teenagers already read wikis and guides compulsively, ahead of time, for pleasure. The behaviour
transfers directly — and it is the only mechanism designed so far that produces **reading on a day with
no play**, which is step 3.

Risks covered: it punishes nobody (not reading = normal difficulty), it cannot be farmed, and the
*"Scripture becoming a resource"* risk resolves because whoever reads ahead for tactics reads the whole
chapter anyway, name lists included.

## Multiplayer — the form, not just the decision

| | |
|---|---|
| **This MVP** | **Solo.** Twenty minutes answering one question. The schema is already born in multiplayer shape. |
| **Next** | **Asynchronous co-op over shared state.** Players replace NPCs, one seat at a time, in the same table, with no schema migration. |
| **Never** | Real time. Synchronous netcode finishes a team of five with no game developer. |

Chapter 3 is forty groups working at the same time: **asynchronous is the material's native format.**
It is a database, not netcode. And `NEH.4.20` — the trumpet — is a notification primitive with a
fictional reason to exist.

**The invitation is a game action, not a share button.** Every seat is already occupied by an NPC, so
there is never an open slot and **whoever has no friend to call is never penalised** (rule 7). The
invitation is: *replace Hananias with your actual friend.* A real person is an upgrade over the AI,
never a hole being plugged.

## Metrics

| Event | Step | What it measures |
|---|---|---|
| `chapter_opened` | 1 | Opened it |
| `deep_read` | 1 | Left the game of their own accord and read the chapter |
| `unprompted_read` | 2 | Opened it **without** the game asking |
| `read_ahead` | 3 | Read a chapter ahead of the season's point |
| `offday_read` | 3 | Read on a day with no session |
| `ungamed_read` | 3 | **Read a book the game never pointed at and gives no credit for reading** |

`ungamed_read` is the most honest metric available. Season 1 ends in Nehemiah; Ezra is the other half
of the same story — the return and the temple, not the wall — and has no game built on top of it. If
the season ends by naming Ezra **while offering nothing** — no reward, no unlock, no badge — and people
read it anyway, the thesis is proven. It is two lines of telemetry and worth more than all the rest
combined.

**Note:** count audio, with its own tag. YouVersion has audio, and for this band listening is a
legitimate on-ramp. Separate it from text in the measurement, never exclude it.

## Pending

| Pending | Who decides |
|---|---|
| **The 13-19 band crosses the minor/adult boundary of rule 17**, which forbids mixed teams by a database constraint. A 17-year-old and their 18-year-old friend from the same group are the most likely pair of players. With multiplayer in the MVP this becomes a schema decision. **Proposed refinement:** open matchmaking never crosses the boundary; a closed team joined by code may (it is a youth group, with an adult present). **The chat protections apply on both sides of the boundary regardless of team composition** — pre-composed speech, no DMs — because age is self-declared and the constraint is loose either way. | Pedro + cybersecurity. **Do not change rule 17 without ratification.** |
| **10-12 through the adult channel touches child data regimes.** The no-sign-up, no-storage posture is probably the mitigation; confirm that it suffices. | Cybersecurity |
| **`version_id` became a reading-level decision**, not only a licence one. A teenager gives up on a formal-equivalence translation. In pt-BR, NTLH or NVT would change the conversion rate. | Pedro |
| **Distribution inverts.** Teenagers do not discover games by store search — they discover them through friends and creators. With that, **an institutional licence stops being "the unexplored channel" and becomes the primary one**; the consumer channel is the speculative one. | Team |
