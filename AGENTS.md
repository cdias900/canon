# Create Hack 2026 — product context

Repository of a Bible-engagement game. This file is the context every agent reads before anything
else. The details live in `docs/`; **the rules below are not there — they are here because they
cannot be violated.**

## What we are building

**A Cidade Quebrada** — a turn-based building-and-defence game set in the book of Nehemiah. The
player is one of the people called up to rebuild the wall of Jerusalem. Scripture arrives as a
**strategy guide**. See `docs/nehemiah-game-design.md`.

**In development now:** the MVP — a nine-stage season, one gate, Unity, iOS and desktop. It is what
gets presented at the end of the hackathon. See `MVP-SCOPE.md`.

Names, in three layers, because four of them circulated and only these three mean anything:

| Name | What it names |
|---|---|
| **A Cidade Quebrada** | The game. The product name, and what appears on screen. |
| **Cinquenta e Dois Dias** | The season — the whole wall, 12 to 15 sessions. The MVP is its opening. |
| **Porta das Ovelhas** | This chapter. It is `NEH.3.1`, and it is fiction before it is a label: the gate the player raises. |

`SheepGate` is the Sheep Gate in English, and remains the namespace for all the code. The bundle id
(`com.createhack.portadasovelhas`) **does not change** — changing it orphans the save and the run
parked on the simulator, and buys nothing.

> **Cânon is discontinued.** It was the sibling concept — a text-based narrative RPG with scripture
> as equippable *loot*. The decision to go deep on Nehemiah is made. The shared layer (verse
> pipeline, vocation, telemetry) is still written with care because that is good engineering, no
> longer because another product depends on it. The Cânon plan leaves `docs/`; `git log` keeps it.

## The north-star metric

**Deepening rate: the player leaves the game, of their own accord, to read the whole chapter.**

The event is `deep_read`. Every product decision resolves by asking whether it raises or lowers that
number. A beautiful feature that does not move it is not a priority. A feature that moves it is the
product.

Practical corollary: **never send the reading outside the app.** A deep link to YouVersion, sending
by email, opening the browser — all of it destroys the measurement, which is the product's reason to
exist. Always an internal reader; an external channel is a secondary, optional button.

## Non-negotiable rules

### Text integrity

1. **The LLM never writes Scripture.** The model picks the *reference*; the literal text is fetched
   by that reference. This eliminates verse hallucination by construction, not by prompting.
2. **Neither code, nor spec, nor prompt contains a hand-written verse.** What circulates is
   `NEH.4.6`. Checkable with grep, and it is an acceptance criterion.
3. **God never speaks in generated text.** Never put words in the mouth of God, of Jesus, or of the
   Holy Spirit.
4. **A canonical figure may speak; what it may not do is go beyond the text.** A character named in
   the text but *with no recorded speech* (the 40+ builders of Nehemiah 3) speaks freely — the Bible
   names them and does not quote them. An invented character, likewise. And **a figure with recorded
   speech may also receive authored dialogue**, as long as it asserts nothing the passage does not
   support: no new event, no new motive, no new theological claim. The model is *The Chosen* —
   attested speech preserved, the gap filled with fiction that does not contradict it.
   - **Quotation is still reference only.** A line has `text` **or** `verse`, never both; the text of
     a quotation is resolved from `verses.json`. Authored dialogue fills the gap *around* recorded
     speech, never rewrites it in other words. The validator breaks the build on any run of 8+ words
     shared with Scripture, and that is what stops paraphrase creeping in.
   - **This is judgement, not a check.** No script decides whether an authored line overreaches. The
     nodes where this applies are marked `canonical_speaker` and `needs_curation` in `dialogue.json`,
     and `node tools/list-curation.mjs` prints the queue for a human read against the text. Nothing
     with authored speech by a canonical figure reaches the player without that pass.
   - Rule 3 remains intact and without exception: **God, Jesus and the Holy Spirit never speak in
     generated text.**
5. **No denominational bias.** Where readings diverge, show the readings.
6. **Two-layer validator.** Deterministic: does the reference exist? does the passage match character
   for character? is there a term from the smell checklist? By model: does the text assert something
   the passage does not support? Unvalidated content does not reach the player.

### Design

7. **Never punish.** Punishing failure in a game about guilt is an own goal. Operating rule: **you
   can lose tomorrow, never yesterday** — absence delays, never regresses; completed progress never
   rolls back; there is no game-over screen.
8. **No prayer button as a power.** Prayer returns *information*, never force, and costs time. The
   rule is in `NEH.4.9`: they prayed **and** posted a guard. Prayer alone loses the wall; guard alone
   loses the point.
9. **No death counter.** Defence is deterrence, not killing — which is what the text actually
   describes. A **morale** bar, not a health bar; you win by making the other side give up.
10. **Never show vocation progress.** If the player sees that three more actions make them a Zealot,
    discovery becomes a to-do list. Accumulate hidden, reveal the name.
11. **Nobody sees anyone's choice before choosing.** And the rule lives on the server — on the client
    it is one line of DevTools.

### Discretion

12. **Not announcing is not hiding.** Deferring a name is legitimate (a stonemason does not know who
    the man from the capital is); **swapping or removing a name is not.**
    - **The citation may be deferred; the access, never.** The footer with `ref_display` stays hidden
      until the reveal — someone who does not know that this is Scripture reads the quotation as what
      it is in the fiction, and chapter-and-verse would answer a question nobody asked. But the
      **Saber mais** button exists on every quotation from the first minute, and opens the chapter
      with its full name and numbering. The curious player is always one tap from the whole answer.
      `ScriptureVisibility` implements this, and the reveal is one-way: a game that showed references
      and then stopped would read as concealment.
    - **Deferral belongs to the fiction, not to the product.** *The Chosen* is openly biblical on the
      packaging and defers only the reveal internal to the narrative. Store, title and codex are
      honest about what this is. The version that betrays the player is the one that hides it in the
      product.
    - **Consciously accepted cost:** while the reference is absent, `deep_read` measures fewer people.
      The signal that matters — `unprompted_read` — is precisely the one from whoever opens it
      without the game asking.
13. **The smell checklist — what never gets in.**
    - *Words:* the forbidden terms are enforced per language by `tools/validate-content.mjs`, and are
      curated per language rather than translated. In pt-BR: `bênção`, `propósito`, `jornada de fé`,
      `devocional`, `versículo do dia`, `testemunho`, `Deus tem um plano`. In English: `blessing`,
      `faith journey`, `devotional`, `verse of the day`, `testimony`, `quiet time`, `God has a plan`.
    - *Art:* golden light, dove, cross, praying hands, robe, sandal.
    - *Mechanics:* prayer button, church invitation, a "which church do you attend" field, a religion
      question in onboarding. **Never.**
    - *Voice:* a narrator who knows the right answer and morally corrects the player. It may disagree;
      it may not pastor.
14. **The reveal is a gift, not a notice.** Design the moment the penny drops as a chapter. In this
    build it happens when the text becomes the strongest weapon available.

### Privacy and security

15. **The choice profile is a moral dossier.** There is no leader, mentor or pastor dashboard. You see
    another player's class, never their attributes.
16. **No AI key on the client.** Every call goes through a server function, with a per-player rate
    limit and a daily spend ceiling.
17. **Minors (13-17) are architecture, not configuration.** Age band in the profile, matchmaking
    blocked, team entry by code only, pre-composed lines with moderation. Minor and adult teams do not
    mix — a database constraint.
18. **No pay-to-advance.** In a game about a work raised by voluntary sacrifice, selling the shortcut
    refutes the theme. Monetisation: a new season, or cosmetics. Never a resource, never a timer,
    never a shortcut through the work.

### The reading

19. **Reading pays in understanding, never in numbers.** Reading never grants a bonus, a stat or a
    level — it grants knowing **which verb works when**. The central verbs (build, watch, split
    work/watch) are never locked behind reading: whoever did not read has a valid play, just a worse
    one. A bonus for reading is a slot machine with a Bible skin, and the motivation evaporates along
    with the bonus.
20. **Reading is always skippable, and the skippability is the instrument.** If reading paid a number,
    100% of players would "read" and `deep_read` would stop measuring anything. The product question
    is never *how do we stop them skipping*; it is **what fraction converts, and is it growing?** Plan
    the funnel: the second and third invitation, not only the first.

## Decisions taken

| Topic | Decision |
|---|---|
| Concept | **Nehemiah.** Cânon is discontinued; it is no longer an open fork. |
| Engine | **Unity 6 LTS · 2D URP.** More C#/Unity in model training than GDScript, and the implementation is done by agents. |
| Text source | **YouVersion** (access granted for the project). Licence unblocked. |
| Translation | **NVI (`129`) in pt-BR, World English Bible (`206`) in English.** The three licence questions are answered — see `docs/youversion-api.md`. NVI is all-rights-reserved, and **that is why this repository is private**; the copyright notice ships in `verses.json` and must appear in-game. English is public domain on purpose: it is the locale that could ship publicly first. |
| Corpus architecture | **Dual corpus:** embeddings over a public-domain translation (the index returns only a *reference*); display via YouVersion. With NVI back in play, the design regains its point. |
| MVP mode | Single player, no sign-up, offline at runtime. |
| Platforms | **iOS, Android and desktop.** iOS and macOS have been played since the start; **Android was first built and played on 03/09/2026** — an IL2CPP/ARM64 APK on an arm64 emulator (`tools/android-emu.sh`), through the opening to character creation and the village. The Android module for this Unity was installed that day; before it, no APK had ever existed. **No physical device of either platform has run it**, and that half of the definition of done stays open. |
| Classes | **Vocation / Trade / Post**, three layers. Vocation is a portable archetype across seasons, discovered through behaviour — never chosen from a menu. |
| Multiplayer | **Asynchronous co-op, in progress** — `docs/multiplayer.md`. The NPCs of chapter 3 are seats that players occupy, **in the same table, with no schema migration**; a table of one is the solo run. Never real time: the trumpet is the only appointment. Server (`tools/table-server.mjs`), the *Obra em grupo* screen, the §06 group raid and the moderator's tool (`tools/table-admin.mjs`) exist and have run against a local server; a container image is built and tested locally but **nothing is deployed** (where is a cost decision, §11), there are **no accounts**, and free text ships switched off. The hackathon MVP itself stays playable solo and offline: with no `-table-url` the feature does not exist. |
| Persona | **13-19 directly; 10-12 only through an adult channel** (youth leader, school, guardian). Believes the text matters, finds the text boring, has better options in their pocket. The competitor is the feed, not another Bible app. **The game does not spend a second convincing anyone** — the work is logistics and context. See `docs/persona-and-purpose.md`. |
| Calendar | 12 to 15 sessions per season. The 52 days are the feat the text announces, not the session count. |
| Monetisation | First season free and complete; later seasons paid. Cosmetics secondary. An institutional licence is the unexplored channel. |

## Open decisions

- **Reading level of the pt-BR translation** — the licence is settled, this is **not**. NVI is dynamic
  equivalence and passes; NTLH or NVT would pass better at 13, and the `deep_read` conversion rate
  depends on it. A product decision, not a licence one, and swapping is one line in
  `tools/verses.manifest.json`. Decides: Pedro.
- **What a `deep_read` means when the chapter fits on one screen — taken, revisable.** The dwell
  now scales with the text: `ChapterReaderUI.DeepReadSecondsFor` asks 1.5 s per verse with the old
  20 s as a floor (NEH.4 ≈ 35 s, NEH.12 ≈ 70 s), and the 60% scroll rule is unchanged. Taken on
  2026-09-03 as one constant; Pedro can move it. In the same pass the doors were sorted: A Página,
  the "Saber mais" under a quotation and the record at the gate now open the reader with
  `gameAsked = true`, so `unprompted_read` counts only the study card in the profile — until then
  every caller passed false and the event duplicated `chapter_opened`. A deep read from the ending's
  invitation is also reported as `ungamed_read`.
- **The engagement meter — settled in `fece0c5`.** The 62 baseline is gone and the six caps are
  24 + 24 + 16 + 12 + 16 + 8 = 100, so a player who does everything reaches the top. The days
  signal used to be full on the third morning (a literal 2 from the three-day build); it now
  reads the season's length from the stage table.
- **Sharing the wall.** The ending draws the whole wall with the names on it and is meant to be
  screenshotted; a share button needs a native share sheet, and the project has no plugin for one.
  Whether to add one (and which) is open. The reading never goes out that way in any case.
- **Jesus / the Holy Spirit as a guide** — deferred. The typological reading is legitimate; it comes
  back as an easter egg in a season that earns it, never as generated speech.
- **The curation read is done; two citations it identified are not.** The human read rule 4 requires
  has happened: all six nodes carrying authored canonical speech — `intro_gathering`, `gathering_d4`,
  `sanballat_d5`, `sanballat_d6`, `tobiah_d6`, `sanballat_d8` — were read against the passage **in
  both locales**, in two passes (`6d9d0ac`, then `079aac4`, which kept 32 of 36 lines and changed
  four). What that read surfaced and could not finish: `tobiah_d6` frames `NEH.4.12` and shows
  `NEH.4.11`, and `sanballat_d8` cites nothing at all in the episode the book records at most length.
  The verse the frame wants, `NEH.4.12`, is **not** in `tools/verses.manifest.json` — `NEH.4.11`,
  which the node actually renders, is there and always was; and `sanballat_d8` has no anchor verse
  chosen yet, so there is nothing to look up for it. `079aac4` held them back for want of the
  YouVersion key, and **that reason has expired**: `YOUVERSION_API_KEY` is set in `.env.local` on
  this machine, which is the file `tools/fetch-verses.mjs:42-50` reads. So what is left is a task —
  add the two references and refetch — not a blocker, and it stays open only because adding a
  citation is a content judgement about which verse the frame is actually describing.
  **And the key is not enough on its own:** on 03/09/2026 the key in `.env.local` returned
  `403 Access denied for 129` on NVI while serving BLT (`3254`) and WEB (`206`) — the state
  `docs/youversion-api.md` §2 describes, where the version has to be **enabled for the app in the
  YouVersion developer portal**. Until someone does that, or pt-BR is regenerated against BLT, no
  new pt-BR citation can be fetched, and `NEH.5` and `NEH.8` — the famine and the reading of the
  Law, the two beats of the design still missing — stay out of reach.
  `a943723` then taught the queue to say so: each read node carries `curated_in` with the commit
  that read it, and `node tools/list-curation.mjs` reports nothing waiting. `needs_curation` is
  **not** cleared on purpose — the queue is a record of what carries authored canonical speech,
  not a backlog — and a line edited after its `curated_in` commit has a stale read that nothing
  flags automatically. That is the one rule left standing here.
- **The English has never had a native pass.** It reads correctly and holds the register, but it was
  written by the same agent that wrote the code.
- **The landscape skirt — closed on 03/09/2026, kept here because no gate closes it.** The Mac
  player was run at 1920×1080 with the e2e's own arguments (`087f000`): the skirt paints past the
  map on all four sides and the patrol view has no clear-colour edge; what landscape actually broke
  was the UI — creation and the backpack — and that commit made the interface fit the window it is
  given (390 steps, 0 failures landscape; portrait unchanged). Still true: `tools/e2e.sh` forces
  portrait, so a regression here would be found the same way it was — by launching the player at
  1920×1080 by hand and looking. `ProjectSettings.asset` still ships `fullscreenMode: 1`.
- **The 13-19 band crosses the minor/adult boundary of rule 17**, which forbids mixed teams by a
  database constraint — and it becomes a schema decision the moment multiplayer arrives, which the
  decisions table puts outside this MVP. Refinement proposed in `docs/persona-and-purpose.md`: open
  matchmaking never crosses; a closed team joined by code may. **Pending ratification by Pedro +
  cybersecurity. Do not change rule 17 before that.**

## The team

Five people: 1 backend, 2 frontend, 1 cybersecurity specialist, 1 product designer. **Nobody is a
game developer, and there is no writer and no theologian on the team** — the implementation is done
by agents, and narrative content is adapted from material already written rather than invented from
scratch.

The team's brainstorm (Google Docs) had contributions from João, Cris, Pedro, Juliana and Matheus.
What survived it lives in `MVP-SCOPE.md` and in the decisions above.

## Document map

Twelve documents, plus a two-file archive under `docs/superpowers/`. **Everything a developer reads
is English** — this file included. Only what a *player* reads is pt-BR, because pt-BR is the
authoring locale for the content. `CLAUDE.md` is a symlink to this file, not a thirteenth document:
editing either edits both.

| File | What it is |
|---|---|
| **`AGENTS.md`** (this one) | The constitution. North-star metric, the 20 non-negotiable rules, decisions taken and open. Every agent reads it before anything else. |
| `MVP-SCOPE.md` | **What gets executed.** The season, the systems, the morale contests, what is done, what is left, and the acceptance criteria. |
| `README.md` | How to run, build, test and play it. The first file for anyone arriving. |
| `docs/persona-and-purpose.md` | **Who the game is for and what it has to cause.** Age band, the transformation thesis, the ladder of steps, the desire metrics, and rules 19-20. |
| `docs/nehemiah-game-design.md` | The design of the whole season, beyond the MVP: vocations, the day/night loop, the four threats, discretion, risks. |
| `docs/character-creation-scope.md` | **The record of a closed work order**, kept for its reasoning rather than its instructions — the file marks itself done, and creation and the backpack now draw every wardrobe row through the same `WardrobeRow`. Read it for *why* the two screens speak the catalogue's vocabulary, not for what to do next. |
| `docs/development-guidelines.md` | **How code is written here.** English in the code, player text only in `locales/`, how to add a string and a language, and what "tested" means — including the simulator dead ends. |
| `docs/architecture-contract.md` | The public seams nobody changes alone: scene names, signatures, schemas. |
| `docs/design-system.md` | Sistema Vale: tokens, typography, and the rules that are design rather than style. |
| `docs/handoff.md` | State of the build and what is not finished. |
| `docs/youversion-api.md` | The verified API surface, and the licence obligations. |
| `tools/README.md` | **What every script in `tools/` is for**, in one screen: the Node half that turns the manifest into `verses.json` and checks nothing copied scripture, and the shell half that drives Unity — compile, validate, acceptance, e2e, and the simulator. |
| `docs/superpowers/` | Archive of a plan and its spec (`daily-check-in`), carried out and then reversed: the streak and the talents it paid were removed on 2026-09-03 (rule 7 and §12), and `DailyCheckIn.cs` now only notices the first launch of a day so `WelcomeBackModal` can say nothing went backwards. Kept as history, not as a document to write new work against. The spec is in pt-BR and should not be: it was added *after* the pass that put every document in English (`e1fabdd`, hours after `17da494`), so it is a miss, not an exemption. |

## Conventions

**Detailed engineering rules in [`docs/development-guidelines.md`](docs/development-guidelines.md).**
The summary:

- **Everything a developer reads is in English. Only what a player reads is translated.** Identifiers,
  comments, log messages, JSON keys, file and directory names, GameObject names, telemetry events and
  flags, commit messages, branch names, PR titles — and **every document in this repository,
  including the product ones.** The one thing that stays pt-BR is **the content**: pt-BR is the
  authoring locale, and other languages are translations of it.

- **Quoting pt-BR inside an English text is correct, not an exception.** A commit that explains why a
  line of dialogue changed has to be able to name the line. What is asked is that the sentences
  *around* the quotation are English:

  ```
  Remove "Ele foi mais educado que você." from the refusal branch      yes
  Corrige a fala do vizinho no dia 2                                   no
  ```

- **None of this is enforced by a script.** There is no hook and no CI for the language of a commit:
  it is a reading rule, and it holds because whoever writes — person or agent — has read this. Catching
  it in code review is enough.
- **No string a player reads lives in a `.cs` file.** All of it lives in
  `Assets/Resources/Data/locales/<locale>/` and is read with `Loc.T("key")`. The validator breaks the
  build if a literal reaches the screen.
- **The game is bilingual: pt-BR and en.** `pt-BR` is the authoring language; the others are
  translations of it. Structure and numbers have **exactly one copy**, shared — only words duplicate
  per language, so balance cannot diverge between them.
- Biblical references in the form **`BOOK.CHAPTER.VERSE`** (`NEH.4.17`, `JHN.21.6`), always, in code
  and in spec.
- Game content lives in JSON under `Resources/Data/`, never in a ScriptableObject — it has to be
  editable outside Unity.
- `verses.json` is **generated**, one per locale. Never edit it by hand.
- **A change is not done until it has run in a build.** `tools/unity-check.sh` compiles,
  `node tools/validate-content.mjs` validates the content, `tools/acceptance.sh` asserts the rules,
  and `tools/e2e.sh` builds the player and plays the opening **and the whole season** in every language,
  with screenshots. The first three are not enough: this project has already shipped correct code that
  nothing called, and bugs that were invisible rather than broken.
- **Testing on an iPhone is always `tools/ios-sim.sh`, and touch always goes through it.** `setup`
  once per machine; then `tap`/`press`/`swipe`/`text`/`key` in **device points**, and `shot` to see the
  result. Underneath is **idb**, which injects like a real device: the cursor does not move, focus does
  not change, and the Simulator window can stay hidden for the whole session — the game can be played
  while the person keeps working on the machine. The dead ends that cost this project a session each
  are listed in `docs/development-guidelines.md` §3, and the short version is: **never
  `osascript ... click at`** (reports success, does nothing), **never anything that moves the pointer**,
  read the screen with `xcrun simctl io booted screenshot`, and **there is no finding a button by
  name** — tap a point, screenshot, look.
- **Testing on Android is `tools/android-emu.sh`**, the same verbs on an arm64 emulator, with taps in
  **pixels** (1080×2400) rather than points. No command line reaches the player there, so neither the
  e2e runner nor `-table-url` can be driven on Android: it is tap-and-look for the solo game.
