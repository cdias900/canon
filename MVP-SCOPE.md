# MVP — A Cidade Quebrada, Sheep Gate

| | |
|---|---|
| **Engine** | Unity 6 LTS · 2D URP |
| **Target** | iOS · macOS · Android (emulator since 03/09/2026; no physical device of any platform yet) |
| **Mode** | Single player |
| **Login** | None |
| **Length** | 9 stages · one season, `the_summons` → `the_dedication` |
| **Languages** | pt-BR (authoring) · en |

End-to-end scope. Written to be executed by agents: every section defines concrete artifacts,
schemas and acceptance criteria. Product rules live in [`AGENTS.md`](AGENTS.md) and are not repeated
here.

---

## 00 · Objective and success criterion

The build exists to answer one question: **does the player open the chapter of their own accord?**
Everything else is a means. If by the end of the season `deep_read` never fires without the game
asking, it has done its job — it answered no, and answered cheaply.

> **Definition of done**
> The game installs and opens with no sign-up, and a person who has never seen the project plays the
> season to the end with no outside help — from `the_summons` to `the_dedication`, the stage that
> declares itself terminal — with the wall visibly built, a morale contest won or lost, and the
> chapter reader opened at least once.

> **There are two `deep_read` doors, not one.** A Página offers `NEH.4` at the contest of stage 6,
> and the closed gate offers `NEH.12` at stage 9. Both go through **Saber mais**, which stays the
> only way into the reader. A build that opens one and not the other has answered the question by
> half.

Android keeps its build target and stays in this definition. **An APK was first built and played on
03/09/2026**, on an arm64 emulator (`tools/android-emu.sh`), through the opening, creation and the
village; iOS and macOS had been played since the start. **No physical device of any platform has run
the game** — that half of the definition needs hardware, not code.

---

## 01 · Stack

| Item | Choice | Why |
|---|---|---|
| Engine | Unity 6000.3.23f1 | More C#/Unity in model training than GDScript — decisive, because the implementation is done by agents. |
| Render | 2D URP | Needed for the day/night light. |
| Serialisation | `Newtonsoft.Json` | `JsonUtility` reads neither dictionaries nor polymorphism. Do not use it. |
| Content | JSON in `Resources/Data/` | A ScriptableObject needs the editor to author; JSON is editable by agent and by human outside Unity. |
| Backend | None at runtime, **for the solo game** | Verses are baked at build time (§09). Zero network during play. The multiplayer table (`docs/multiplayer.md`) is the one exception and it is opt-in by configuration: with no `-table-url` the feature does not exist and this row holds exactly as written. |
| Resolution | Portrait 1080×1920, PPU 32 | Orthographic camera, `CameraRig.CloseSize` 7.5. |

> **NON-NEGOTIABLE, INHERITED FROM THE PROJECT**
> Biblical text **never** appears written in this spec, in the code, or in a model prompt. What
> circulates is always the **reference** (`NEH.4.6`), and the literal text is resolved from
> `verses.json`. Any agent that hand-writes a verse has introduced an integrity bug, not a
> convenience.

---

## 02 · Project structure

```
Assets/
  Scenes/          Boot.unity  CharacterCreation.unity  Game.unity   // near-empty by design
  Scripts/
    Art/           ArtLibrary  CharacterArt  TileArt  Tileset  MapChartArt  PixelCanvas
    Audio/         AudioDirector  AudioLibrary
    Boot/          BootLoader  CharacterCreationBootstrap  GameBootstrap
    Contest/       MoraleContest  ContestUI  ThePagePanel
    Core/          GameState  SaveSystem  Telemetry  GameData  ServiceLocator
                   ScriptureVisibility  AppPaths  BootSequence
      Localization/  Loc  Locales
    Dialogue/      DialogueSystem  DialogueUI  DialogueData  ChapterReaderBridge
    E2E/           E2ERunner                                        // plays a built player
    Player/        PlayerController  GridPathfinder  CharacterAppearance
                   CharacterCatalog  CharacterPreset  Wardrobe  UnlockEvaluator
    Quiz/          DailyQuiz
    Scripture/     ScriptureService  ChapterReaderUI
    UI/            HUD  DesignTokens  UIKit  BackpackPanel  EndDayPanel
                   MorningReportUI  VocationRevealPanel  SettingsPanel  ModalRoot
    Vocation/      VocationTracker
    World/         GameScene  DayCycle  WallSystem  ResourceSystem  CameraRig
                   IntroCutscene  StageDirector  NpcActor  WorldMapView  RestPoint
                   CutsceneActor  NpcWander  MapViewportController  TapMarker
  Resources/Data/
    map.json  wall_segments.json  npcs.json  contest.json           // structure and numbers:
    stages.json                                                         // the season, in order
    vocations.json  quiz.json                                       // ONE copy, every language
    character_catalog.json  character_presets.json
    locales/<locale>/                                               // words only
      ui.json  dialogue.json  npcs.json  contest.json
      vocations.json  quiz.json  catalog.json  presets.json
      verses.json                                                   // GENERATED — never edit
tools/
  fetch-verses.mjs  verses.manifest.json  validate-content.mjs  list-curation.mjs
  unity-check.sh  acceptance.sh  e2e.sh  ios-sim.sh
```

**Structure and numbers have exactly one copy; only words duplicate per language.** A stage cost, a
resolve delta and a turn limit exist once, so two languages cannot disagree about balance.
`GameData.LoadAll` merges the locale's strings onto the same DTOs at load time.

---

## 03 · Data schemas

### `locales/<locale>/verses.json` — generated, read-only

```json
{
  "version": { "id": "129", "abbrev": "NVI", "copyright": "..." },
  "verses":   { "NEH.4.6": { "ref_display": "Neemias 4:6", "text": "<literal>" } },
  "chapters": { "NEH.4": { "ref_display": "Neemias 4", "verses": [ { "n": 1, "text": "..." } ] } }
}
```

The `copyright` string ships in the file and **must be displayed in-game** — a contractual
requirement of the NVI licence, not a courtesy.

### `npcs.json` — structure, shared

```json
[ { "id": "hananias", "source_ref": "NEH.3.8", "spawn": { "x": 12, "y": 9 }, "palette": "npc_a" } ]
```

Display names live in `locales/<locale>/npcs.json`, keyed by id.

### `locales/<locale>/dialogue.json` — one node per NPC per day

```json
{
  "hananias_d1": {
    "npc": "hananias",
    "day": 1,
    "lines": [
      { "text": "..." },
      { "verse": "NEH.4.6", "frame": "..." }
    ],
    "grants": { "vocation": { "pastor": 1 } },
    "reliable": true
  }
}
```

> **How a verse enters a line**
> A line has `text` **or** `verse`, never both. With `verse`, `DialogueSystem` resolves it through
> `ScriptureService` and renders it visually distinct, with `ref_display` in the bubble footer —
> **hidden until the reveal**, per rule 12, while **Saber mais** is present from the first citation
> onward. `frame` is the NPC's own line introducing the quotation, and that one is authored by us.

Dialogue is the one file copied whole per language. `checkLocaleParity` in the validator compares
every field that is *not* words — node ids, line counts, verse references, choices, grants, flags —
and fails on any disagreement. A translator cannot change what the game does.

### `wall_segments.json`

```json
[ { "id": "seg_01", "grid_x": 13, "stage_cost": [1, 1, 1, 1], "exposed": false } ]
```

Four segments, four **courses** each. `seg_02` is the exposed one.

> **The word `stage` means two different things, and the field names keep both.** A *course* is one
> of the four steps that build a wall segment (`stage_cost`, `WallSystem`). A *season stage* is one
> of the nine days in `stages.json`. The code cannot rename either without a migration, so this
> document says **course** for the wall and **stage** for the season, and never the reverse.

### `stages.json` — the season, structure, shared

```json
[ { "day": 6, "id": "enemies_rise", "type": "battle", "terminal": false, "night_threat": true,
    "contest": "raid", "cutscene_node": null, "finishes_wall": false, "closes_gate": false,
    "gate_segment": null, "reveals_page": true, "reveals_vocation": false,
    "reward_item": "acc_watch_horn", "vigil_verse": "NEH.4.16",
    "map_anchor": [0.67, 0.60], "map_focus": [0.68, 0.62] } ]
```

`vigil_verse` is the page the night's **vigil** returns (§05, `DayCycle`): a reference into
`verses.json`, always about the stage that follows, `null` on a night that offers none. The
validator checks it as a citation — the verse and its chapter both have to be there — and the
terminal stage, which has no night, carries `null`.

**Nine rows, and nothing in C# counts days.** `StageDirector` reads the row the run is standing in
and does what it declares; `DayCycle` reads `night_threat`; the map reads the anchors. Adding a
stage is a data change. The table is validated on load: a season without exactly one `terminal`
stage, or with the terminal stage anywhere but last, logs an error naming what would break — *"with
none, that beat never happens; with more than one, it happens twice."* It logs rather than throws,
which is enough because `e2e.sh` fails on any error in the log.

---

## 04 · Scenes and flow

**Scenes are near-empty on purpose.** Each `.unity` file holds a camera and one bootstrap
behaviour; every GameObject, sprite and UI element is constructed at runtime from C#. Hand-written
scene YAML with GUID cross-references is the easiest thing to get silently wrong, and the compiler
cannot check it.

**Boot.unity** — loads data, instantiates services, reads the save, then **always loads `Game`**.

**Game.unity** — village and wall in one scene, no loading between them.

**The opening is a cutscene, not a menu.** Character creation is a beat inside it, played in the
house the neighbour walks the player into, so the first thing a new player sees is a city rather than
a form:

> region shot with the other cities shut → push-in to the ruined circular city → a neighbour crosses
> the square and speaks → you follow him into his house → **character creation** → you follow him to
> the gathering → the unnamed man from the capital speaks and sends everyone for stone

Fourteen cards from the wordmark to the first stone, down from twenty-four on 2026-09-03: the
neighbour's two nodes before creation lost their asides, and the gathering keeps the man's every
attested word, both citations and both directives and drops only narration. The first run still
carries no skip.

`CharacterCreation.unity` stays registered and `CharacterCreationScreen.Compose()` still builds the
standalone screen, so that route keeps working; nothing routes to it.

> **The returning player gets a screen; the new one still does not.** `TitleScreen` opens over the
> composed world at every launch. With no character in the save it is a wordmark that fades on its
> own after 1.4s and hands the screen to the cutscene above — **the rule is about the first
> impression, and a new player's first impression is unchanged.** With a character it also carries
> the figure the player built, the name they chose, the stage the season stopped on, and one button.
>
> It is a modal rather than a scene, and that is what makes it cheap: everything the day must not do
> while it is up — the light falling, dusk resolving, the split opening by itself — is already held
> off by `ModalRoot.IsOpen` through `DayCycle.DuskWaits`, and the village is composed and waiting
> behind the card, so **Jogar closes a panel** rather than loading anything.
>
> **There is one save and Jogar resumes it.** Boot has always loaded the last state; this screen
> does not choose between states, because there has only ever been one. What it adds is the pause
> and the bearings before that state starts moving again.
>
> Shown **once per launch, not once per compose**: the language toggle reloads the scene, and
> without that latch changing language would put the player back on a screen they had dismissed.

> **Two cameras, one map**
> The default view is close, portrait, following the character. A HUD button switches to the
> **Patrol**: the camera pulls back and the player drags horizontally. It is the only view that shows
> total progress, and it is diegetic — Nehemiah inspects the wall at night before anything else
> (`NEH.2.13`).

---

## 05 · Systems

| System | Responsibility |
|---|---|
| `GameState` | Central serialisable state: day, resources, segments, morale, flags, vocation counters, equipped items, player cell. Single source of truth. |
| `PlayerController` · `GridPathfinder` | Tap the ground → path → move. Tap an interactable → approach and `Interact()`. A* on the tilemap grid. |
| `WallSystem` | Courses per segment, consumes work, swaps sprite, raises completion. `DamageSegment` clears in-progress work only. |
| `ResourceSystem` | Rubble split into stone, timber and crafted blocks, plus daily work capacity. **Capacity is the day's clock**, and it is **four courses** — what one morning's piles make (five of stone, four of timber, a block each, one stone pile to spare for a donation). A dry course on day 1 costs a block's worth of stone. The night crew is a separate number, `DayCycle.CrewSize` = 12; the two used to share one field and the day never ended by itself after the first. |
| `DayCycle` | **The day ends by itself.** Light tracks capacity spent; when it hits zero the village goes to dusk and the split panel opens on its own. `DuskWaits` holds the night for whatever has the screen. A stage with `night_threat: false` still plays its whole night — that flag switches off the damage and nothing else. **The vigil** is rule 8's information half, on the same split: whoever is not on the wall stays up over the page instead of building, the night's work is the price (zero units, the cleared path left unspent), and the morning report shows the stage's `vigil_verse` on a card with the way into its chapter. It is not a prayer button (rule 13) and it is independent of the watch — a vigil with no watch still loses the wall, a watch with no vigil still learns nothing. Offered only on a night whose stage declares a page. |
| `DialogueSystem` | Line queue, typewriter reveal, resolves `verse`, applies `grants`. |
| `ScriptureService` | In-memory index of the locale's `verses.json`. Never hits the network. |
| `ScriptureVisibility` | Decides whether `ref_display` is shown. One-way: hidden until the reveal, never re-hidden. |
| `ChapterReaderUI` | Scrollable panel with the whole chapter. Fires `deep_read` past a dwell of 1.5 s per verse (floor 20 s) **and** 60% scroll. |
| `MoraleContest` | Turn machine for any stage that names a `contest`. Three exist. See §07. |
| `StageDirector` | Everything a stage asks for beyond an ordinary working day: the contest, A Página, the gathering, the wall finishing, the gate closing, the vocation. **Driven entirely by `stages.json` — it counts nothing.** A beat holds the day open only while it runs and then gives the hold back, so the stage still ends through the one end-of-day path every other stage uses. The `terminal` stage is the single exception: once its gate segment is standing it takes `HoldFinalDay` and never releases it, because it has no tomorrow; before that it is a working day that repeats its date. |
| `VocationTracker` | Accumulates silently. **No public score getter.** Reveals at the terminal stage. |
| `DailyQuiz` | One question a stage, asked at **dusk** so it closes the session, with a one-line `hook` into tomorrow under the answer; only the terminal stage asks at its morning, because an earned gate leaves it no dusk. Counts whether right or wrong. |
| `WorldMapView` | The progression map behind **Mapa**: nine stops, complete / current / locked off `day`, each stage's piece off the catalogue, and a **diary** per stop read off what the day wrote — courses the player laid (`laid_d<n>`), the night's watch and work (`night_work_d<n>`), the choice made (the branch flags), how a contest ended (`contest_outcome_<id>`), and on the last stop whether the gate is still waiting. Nothing authored per stage; a stop not reached says only that. |
| `Wardrobe` · `CharacterCatalog` · `UnlockEvaluator` | Items, presets, and the world-state conditions that unlock them. Never a score condition. |
| `Telemetry` | Append-only JSONL behind `ITelemetrySink`. See §10. |

> **NEVER SHOW THE VOCATION BAR**
> If the player sees that three more actions make them a Zealot, discovery becomes a to-do list and
> the value evaporates. `VocationTracker` exposes no public score getter to any UI. Accumulate
> hidden, reveal the name.

> **The clock is the work, and nothing else**
> Work capacity is the only thing a day is spent on. Gathering rubble is free, and **talking is
> free**: charging time for dialogue would charge for the citations, which is exactly where the
> north-star metric lives. Whoever wants to stop early has **the mat at the door**; nobody *needs*
> it, because the day ends by itself. Nothing runs on a wall clock — standing still, talking, or
> reading a whole chapter, the day does not advance a step. And a night **never** resolves with a
> panel open: the chapter reader is a panel like any other, and a day that ended during a reading
> would charge the player for the exact thing the game exists to provoke (rule 20).

---

## 06 · Content — the season

Nine stages, from the summons to the dedication. The table is `stages.json`; this section is what
the rows mean.

| # | id | type | beat it declares | reward |
|---|---|---|---|---|
| 1 | `the_summons` | intro | opening cutscene, creation, the gathering | `acc_tool_bag` |
| 2 | `vision_and_plan` | work | the invitation from outside, the well | `hair_headscarf` |
| 3 | `preparation` | work | ordinary working day | `outfit_expedition_gear` |
| 4 | `the_people_united` | rest | `gathering_d4` · **`night_threat: false`** | `outfit_valley_mantle` |
| 5 | `the_work_begins` | battle | **contest `mockery`** — Sambalate first speaks, and the work answers | `outfit_work_apron` |
| 6 | `enemies_rise` | battle | **contest `raid` · `reveals_page`** — A Página, `NEH.4.17` | `acc_watch_horn` |
| 7 | `prayer_and_guard` | work | the watch | `acc_bead_bracelet` |
| 8 | `the_work_finished` | boss | **contest `letters` · `finishes_wall`** | `acc_old_seal` |
| 9 | `the_dedication` | gate | **`terminal` · `closes_gate` (`seg_02`) · `reveals_vocation`** | `acc_gate_key` |

> **The two beats the build exists for are now three stages apart.** A Página lands at stage 6 and the
> gate closes at stage 9. In the three-day build both happened on day 3; anything that still says so
> is describing a game that no longer exists.

**Every night but the last offers a vigil**, and what it returns is written in the table's
`vigil_verse`: the invitation's intent before the invitation (`NEH.6.2`), the nobles of Tekoa before
their stretch is asked for (`NEH.3.5`), the people's will before the roll call (`NEH.4.6`), the mockery
before Sambalate first speaks (`NEH.4.1`), the plot before the raid (`NEH.4.11`), the spear in the other
hand before the armed nights (`NEH.4.16`), the letters' purpose before the letters (`NEH.6.9`), and the
enemy's own verdict before the dedication (`NEH.6.16`). Each is about tomorrow, none is a number, and
all eight were already in the corpus — the vigil added no citation the key could not fetch.

Six residents, all named in Nehemiah 3 and **with no recorded speech in the text**. That is the
category where writing dialogue is legitimate: the Bible names them and does not quote them.

| id | Name | Source | Role |
|---|---|---|---|
| `hananias` | Hananias | `NEH.3.8` | Perfumer's son. Sets the tone of the people's pain. Reliable. |
| `salum` | Salum | `NEH.3.12` | Works with his daughters. Teaches the work/watch split. |
| `baruque` | Baruque | `NEH.3.20` | The one the text singles out for working with zeal. Pushes toward the exposed stretch. |
| `meremote` | Meremote | `NEH.3.4` | Day 2: saw horsemen on the road. **Correct information.** |
| `zacur` | Zacur | `NEH.3.2` | Day 2: says nobody is coming. **Wrong information.** The game does not say so. |
| `malquias` | Malquias | `NEH.3.14` | District ruler. Delivers the outside invitation on stage 2. |

**Two adversaries also speak** — `sanballat` (stages 5, 6 and 8) and `tobiah` (stage 6). They are a
different case from the six, and a harder one: **the text does quote them** (`NEH.2.19`, `NEH.4.1-3`,
`NEH.6.1-9`). Every line they say is authored against a passage that already puts words in their
mouths, so rule 4's human read is not optional there. **Both have been read**, in both locales,
against the passages that quote them (`079aac4`); `curated_in` on each node names the commit, and
`node tools/list-curation.mjs` reports nothing waiting. A line edited after that commit is a line
whose read is stale — the queue is a record, not a backlog.

### Stage 1 — `the_summons`

- Opening cutscene into the village. Minimal HUD: work capacity, materials. **No end-of-day button.**
- Talking to `hananias`, `salum` and `baruque` unlocks the stretch.
- Gather rubble → work a segment. Every stone laid pulls the light down.
- End of day: capacity runs out, night falls, and the panel asks for the split between **work** and
  **watch**.
- The question lands at the evening, on this and every other day but the last: it closes the session and its hook names tomorrow.

### Stage 2 — `vision_and_plan`, those who call from outside

- Morning report: what the night did, with and without a watch.
- `meremote` and `zacur` contradict each other. No indicator of which to believe.
- `malquias` brings the invitation. Accepting spends the whole day and damages a segment; refusing
  cites `NEH.6.3`.
- Fish in the well: 2 attempts fail, the 3rd brings the hint and cites `JHN.21.6`.
- **Timber arrives by letter, not by gathering.** The beams are in the village from this morning
  (`RubblePile.TimberFirstDay`), and Hananias says where they came from: a seal nobody can read and
  a letter that was written down — `NEH.2.8`, quoted on `hananias_d2`. The design's *negotiate
  timber* is a citation rather than a gate on purpose: gating the day's four courses behind a
  conversation is the economy failure the P0 wave just took out.
- Accepting zeroes capacity **without** ending the day: `malquias` takes a named hold
  (`DayCycle.HoldPendingBeat`) until the player comes back and hears the other half of it. The hold is
  re-derived in `Start`, so a scene rebuild mid-beat restores it. The mat can overrule it, and must —
  a player who never goes back would otherwise have no way to reach tomorrow.

### Stage 3 — `preparation`

A working day with one beat from the text: **the Tekoites** (`NEH.3.5`, on `salum_d3`). Their nobles
would not put their necks to the work, and Salum asks for an hour on their stretch. Taking it costs
one unit of today's work (`spend_work` on `salum_d3_help`) and scores the shepherd; the Tekoites
return the hour on the player's own stretch the next morning, after the night has resolved, and the
morning report says so with the number. Keeping the hour scores the zealot. The choice, not the
conversation, is what scores.

### Stage 5 — `the_work_begins`, the mockery

The morning opens on the **`mockery`** contest (§07), the design's first threat: the work is
ridiculed from the upper road, in public, and there is no target to attack. It is a pure morale
drain — the other side's lines are laughter, a dropped stone and the fox — and the answer is
leadership and the work carrying on: **Hold the line** is worth what the wall already says, **Call
the others** is worth who knows the player by name, and **Count out loud**, the contest's own move,
is the crew hearing the count instead of the joke. No trumpet: nobody is converging on a breach.
No Page: the reveal is the raid's, next stage. The residents then say what the day did to them
(`hananias_d5`, `salum_d5`, `zacur_d5`), and Baruque still takes the player up to hear
`sanballat_d5`, which is where `NEH.4.1-3` are cited.

Sambalate first speaks, and **the carriers** (on `meremote_d5`; the verse the beat comes from is
not in the manifest, so it is authored text). The path to the wall is choked with fallen stone;
clearing it with them costs one unit of work today and that night's crew builds double — the
morning report shows the doubled count and says why. The steward scores the clearing, the zealot
the refusal.

### Stage 7 — `prayer_and_guard`

From the night of this stage the crew works with the other hand on the weapon (`NEH.4.17`,
`DayCycle.HalfAndHalfStage`): a night unit for every two people instead of every three, and the
morning report says so. Zacur (`zacur_d7`) reports the upper road empty and asks why post a watch;
doubting him scores the prophet, believing him scores nothing and costs nothing beyond whatever
the player then decides at the split.

> **A migrated save never sees this stage.** Days 1 and 2 keep their numbers — those stages are the
> same stages, unedited. **Day 3 maps to stage 6**, because the anchor is the beat and not the
> position: the old day 3 *was* the day of the trial, and the trial is stage 6. A save that had
> already fought that trial lands on **7**, the first day it genuinely has not seen. Stages 3, 4 and
> 5 are content a migrated player never had, which is not the same as progress taken away. The
> mapping is a fixed historical step written in literals, not a lookup in `stages.json` — a later
> renumbering gets its own schema step rather than a quiet change of meaning under this one.

### Stage 6 — `enemies_rise`, the breach and the reading

- Short morning, then the assault fires the `raid` contest (§07).
- On turn 2 **A Página** arrives and unlocks the strong move.
- **Saber mais** opens `NEH.4` in the internal reader. First `deep_read` door.

### Stage 8 — `the_work_finished`

The `letters` contest, and the wall finishes. `finishes_wall` hands the segment enough work to
complete it — the story closing the wall, not the player spending anything.

### Stage 9 — `the_dedication`, the gate

- The gate closes with the player's name on it (`seg_02`), the record plate drawn from `NEH.3`.
- **The gate is earned.** `seg_02` is the one segment `finishes_wall` spares, and the dedication
  starts only once it is standing — `StageDirector.GateIsEarned`. Until then the last day is a
  working day: the mat works, a night on it keeps the date and puts every pile back
  (`DayCycle.RefillThePiles`), and the morning report says how many courses are left. Delayed,
  never cancelled (rule 7). It used to hand the segment sixty-four units of work nobody laid.
- **Saber mais** opens `NEH.12`. Second `deep_read` door.
- The vocation reveal. Ties break by the most recent award, and only a run with no awards at all
  falls back to the order of `vocations.json`.
- **The ending** (`SeasonEndPanel`), after the reveal and again from the HUD's drawer: the whole
  wall with who repaired each stretch — the player's name on `seg_02`, the six residents on theirs
  (`segment` in `npcs.json`), the reference `NEH.3` under it — the season's three numbers, the six
  vocations as names with this run's marked, a secondary way into `NEH.6` that pays nothing (its
  deep read is `ungamed_read`), and **Começar outra obra**. No share button: there is no native
  share plugin in the project, so the card is drawn to be screenshotted and the plugin stays open.
- This is the `terminal` stage: it has no tomorrow, and it holds the day open for good — from the
  moment the gate is earned.

> **Stages 4, 5 and 7 have been read, and their beats are ordinary** — a rest with a gathering, a
> working day, a working day. The residents who speak on them are the six of Nehemiah 3, whom the
> text names and never quotes, so rule 4 lets them speak freely; nothing they say contradicts the
> passage. `guard_d7` is the dense one: it cites `NEH.4.9`, `4.14`, `4.16` and `4.20` in nine lines,
> and its narration is what needed fixing rather than any authored speech.

---

## 07 · Morale contests

Alternating turns. **No health bar and no death counter:** you win by making the other side give up.
The outcome is decided by what the player did in the stages before it — that is what makes it feel
earned rather than rolled. **Nothing is random: two players who prepared the same way see the same
fight.**

**There are three contests, and they are data** (`contest.json`); a stage names which one it fights.
`MoraleContest` is instanced once and every encounter runs through it, so what used to be "the trial"
is now "this contest" — including whether it has already been fought, and whose lines the other side
speaks.

```
player.morale   = 100
enemy.resolve   = base + 10 when no watch was posted on the night before this stage
                       + 10 when the invitation from outside was accepted
enemy.pressure  = base +  6 for the missing watch + 6 for the accepted invitation
                       +  1 for every course of the contested segment still unbuilt
turn limit      = 8   // overflow = the enemy withdraws, a technical draw
```

| | `mockery` — stage 5 | `raid` — stage 6 | `letters` — stage 8 |
|---|---|---|---|
| resolve base | **48** | 60 | **78** |
| pressure base | **10** | 12 | **14** |
| A Página | none — not yet | **turn 2, `NEH.4.17`** | none — the reveal already happened |
| trumpet | **no** — there is no assault | yes (`NEH.4.20`) | yes |
| extra move | **Count out loud**, −10 resolve · +4 morale | — | **Keep working**, −24 resolve · +4 morale, only with `refused_invite` |
| not offered | Show the watch, Half and half | — | — |

| Move | Effect | Depends on |
|---|---|---|
| Hold the line | −8 resolve | +1 per course built |
| Call the others | +12 morale | ×(NPCs spoken to / 6) |
| Show the watch | −20 resolve (`raid`) · −14 (`letters`) | a watch was posted |
| **Half and half** *(unlocks on t2)* | −15 resolve **and** +8 morale, same turn | Only exists after A Página |
| **Keep working** *(`letters` only)* | −24 resolve **and** +4 morale | flag `refused_invite` |
| **Count out loud** *(`mockery` only)* | −10 resolve **and** +4 morale | nothing — the work is always an answer |

> **The second contest pays out the first refusal.** Whoever turned the invitation down at stage 2
> carries the strongest move in the game into stage 8 — six stages later, and never announced. It is
> the same choice that raises the other side's resolve when it is accepted. The flag is authored,
> not coded: `malquias_d2_refuse` grants `set_flag: refused_invite` (and 2 points of `zelote`), which
> is what `keep_working` gates on.

> **A Página — the moment this build exists to test**
> At the start of turn 2 the trial pauses and a panel slides in showing `NEH.4.17`, reference visible
> in the footer. On closing, **Half and half** appears in the menu. The reveal that this is the Bible
> happens in the same instant that the Bible becomes the strongest weapon available. Fires
> `reveal_shown`, and opens `ScriptureVisibility`.

> **LOSING IS NOT GAME OVER**
> If `morale <= 0` the enemy withdraws anyway at the end of the turn, a segment loses **one unfinished
> course**, and the stage continues normally to whatever it still owes — the reading, the gate, the
> vocation. **You can lose tomorrow, never yesterday:** a completed course never regresses. There is
> no defeat screen.

---

## 08 · Vocation scoring

Six vocations, accumulated in silence. At the **terminal stage** the highest is revealed; ties break
by the most recent award, and by the order in `vocations.json` only when nothing was ever awarded. **Where a conversation branches, the branch scores and the
conversation does not** — talking to everyone is the scribe's habit and is scored once as such;
a node with choices carries no vocation of its own.

| Vocation | Actions that score |
|---|---|
| `zelote` | Work the exposed segment · refuse the invitation outright · open a contest with Hold the line · keep your hour on your own stretch (stages 3, 5) · stay on your stretch rather than go and look (stages 5, 6, 7) |
| `escriba` | Open the chapter reader · speak to all 6 NPCs · re-read a dialogue · ask for the account (stage 7) · read the fifth letter and its signature (stage 8) |
| `pastor` | Use Call the others · speak to Hananias and Salum on both days · donate rubble · give an hour to the Tekoites (stage 3) |
| `exilado` | Use the Patrol 3+ times · catch the fish · walk to the map edge · go down to the plain (stage 2) · keep the letters for after sundown (stage 8) |
| `profeta` | Believe Meremote and not Zacur · close A Página without skipping · go up and listen (stages 5, 6) · doubt the empty road (stage 7) |
| `mordomo` | End **every night the run actually played** with capacity fully spent · the same for rubble left lying. Two separate awards, worth 3 each, checked once on the season's **last night** — which is the stage before the terminal one, because the terminal stage never resolves a night. It used to name days 1 and 2 by hand: correct only while the season had two nights, and in a longer one it handed the vocation out on the second night and asked nothing of the rest. |

---

## 09 · Verse pipeline

`tools/fetch-verses.mjs` runs outside Unity, reads `tools/verses.manifest.json`, fetches from the
YouVersion API and writes `Assets/Resources/Data/locales/<locale>/verses.json`. Run once per locale,
commit the output.

The references live **once** and are language independent; each locale names its own translation.
pt-BR resolves against NVI (`129`), English against the World English Bible (`206`), which is public
domain.

> **Every chapter a cited verse lives in must be in the manifest**, or **Saber mais** opens an empty
> chapter and rule 12 breaks: the citation may be deferred, the access never.

> **Deterministic validator — layer 1**
> `node tools/validate-content.mjs` fails the build when a reference is missing or empty, when the
> corpus is still a placeholder, when any authored string shares a run of 8+ words with the scripture
> text (accidental paraphrase), when player-facing text uses a term from that language's
> forbidden-vocabulary checklist, when a locale is missing a string or disagrees with the authoring
> locale about anything that is not words, or when a C# file hardcodes a string a player can read.

---

## 10 · Telemetry

Append-only at `AppPaths.DataRoot/telemetry.jsonl`, one JSON line per event, behind `ITelemetrySink`
so a real backend can arrive later without touching the call sites.

| Event | Why |
|---|---|
| `session_start` | Retention baseline. |
| `verse_shown` | Exposure to the text. |
| `chapter_opened` | `trigger` distinguishes "Saber mais" from a game prompt. |
| `deep_read` | **The north-star metric.** 1.5 s a verse (floor 20 s) and 60% scroll. |
| `unprompted_read` | Opened **without** the game asking — today, only the study card in the profile; A Página, a quotation's "Saber mais" and the gate's record are the game asking. The signal that matters most under rule 12. |
| `ungamed_read` | A deep read from the ending's invitation, where no move, page or record was attached to the chapter. |
| `reveal_shown` | The moment the penny drops. |
| `node_completed` | Work progress. |
| `vocation_revealed` | Real distribution of behaviour. |
| `locale_changed` | Which language people actually play in. |
| `vigil_kept` | The player paid a night's work to be shown what is written about tomorrow. `stage` and `ref`; its `verse_shown` carries `context: vigil`, and the card's reader opens with `trigger: vigil`. The one exposure in the build the player pays for up front. |

---

## 11 · Art

**Art comes through one seam.** `ArtLibrary.Get(key)` is the only way to obtain a sprite. Most keys
are generated procedurally in `SheepGate.Art` from a reduced palette. Exactly one key,
`tile_water`, comes from a drawn CC0 sheet (Kenney's Roguelike/RPG pack, licence in `Assets/Art/`) —
this paragraph used to say ground, rubble and water, and it was wrong in the direction that matters:
it sent a reader looking for a sheet to edit when the ground and the rubble are code
(`Assets/Art/Tileset.cs:42` is the whole dictionary). The sheet ships as a
`.bytes` file and is decoded at runtime rather than imported as a Unity sprite: no import settings to
get wrong, no `.meta` to hand-write, and no slicing stored in an asset nobody can review in a diff. A
key with no drawn tile falls through to the generated one, so the swap happens a key at a time.

Character art is procedural and layered (`CharacterArt`): build, skin tone, hair, top, legs,
accessory. Eight synthesised sounds (three of them the stone, so a day of courses is not one tap repeated), no audio assets.

**No golden light, no dove, no cross, no praying hands, no robes, no sandals** — rule 13.
`docs/design-system.md` covers tokens, typography and the rules that are design rather than style.

---

## 12 · Out of scope

- Multiplayer, expeditions, seats — **out of the MVP as shipped, and built beside it**: the server,
  the *Obra em grupo* screen and the group raid exist (`docs/multiplayer.md`,
  `tools/table-server.mjs`) and have run against a local server. They are **opt-in by
  configuration**: with no `-table-url` there is no button, no request and no network, so §01's
  *no network at runtime* still describes what ships, and criterion 12 still holds
- The village as a separate base with its own construction
- A shop, spendable talents, currency, any purchase
- A daily streak, push notifications
- Sign-up, accounts, cloud, sync
- LLM calls at runtime — all dialogue is authored in `dialogue.json`
- The other 9 gates, any other season, and **the famine of `NEH.5`** — the one threat of the design's four still missing (mockery, the raid and the letters are in); it waits on the corpus, not on code
- Jesus or the Holy Spirit as a guide — **deferred by decision, not discarded**

---

## 13 · Acceptance criteria

`tools/acceptance.sh` asserts 04-11 and 13, once per locale. **It cannot prove a scene works** — it
constructs systems directly and never calls `Compose()` — so 01, 02, 03, 12 and 14 are verified by
playing a build and reading the screenshots. 14 is confirmed: the chapter reader's colophon renders
the version abbreviation and its copyright notice (`ChapterReaderUI.BuildColophon`).

| # | Check |
|---|---|
| 01 | The build installs and opens with no login screen. |
| 02 | Character creation produces a distinct appearance, persisted between sessions. |
| 03 | Tapping the ground moves; tapping an NPC approaches and opens dialogue. |
| 04 | Every verse displayed came from `verses.json`; no literal in C#. Checkable with grep. |
| 05 | A segment changes sprite across its 4 courses and progress survives closing the app. |
| 06 | Ending stage 1 without a watch produces a different morning report than with one. |
| 07 | Refusing the invitation shows `NEH.6.3`; accepting spends the day and damages the segment. |
| 08 | A Página appears on turn 2 of the `raid` contest and unlocks **Half and half**. |
| 09 | Losing a contest shows **no** game over and does not regress a completed course. |
| 10 | `deep_read` appears in `telemetry.jsonl` after a real reading of `NEH.4`. |
| 11 | The vocation revealed matches the highest score; no UI exposed progress beforehand. |
| 12 | Runs offline start to finish, in aeroplane mode. |
| 13 | The day ends by itself when capacity hits zero; no HUD button ends it; no night resolves with a panel open. |
| 14 | The translation copyright is displayed in-game. |
| 15 | A run with no character is never offered a launch screen to press through (§04). |
| 16 | The vigil costs the night's work and returns a page that resolves; it changes nothing else — same damage as the ordinary night beside the same watch, and with no watch a vigil still loses the wall on a threat night. |

> **The harness carries more than the fourteen, and the prefix says which is which.** Numbered
> criteria come from this section; `L1`–`L3` are the localization checks that arrived with the
> content split, and **`S1`–`S5` are the season's own**: that the stage table holds together and is
> reachable, that **a save written by the three-day build comes forward without losing anything** —
> the one change that could quietly destroy a run somebody already played — that a move a flag
> unlocks is shut without it, and **`S4`, that the gate is earned**: a bare `seg_02` does not count,
> the mat stays alive on the last day, a night there refills the piles without moving the date, and
> the morning carries the courses left. The e2e run lays its capacity on `seg_02` day by day and
> asserts at the dedication that the whole cost came from its own work. **`S5`** is the night keeping
> the day's promises: the cleared path doubling one night's crew, the Tekoites' hour landing on the
> player's segment the morning after, the armed night's count, and none of it twice — the only place
> those beats execute, because the e2e never talks to a resident after the first day.
>
> **Every check that used to name a day now reads the season.** The old shape — day 3 for the
> contest, night 1 for the watch, `NEH.4` for the reader — passed happily on a season where six of
> the nine stages could not be reached, because it asked only about the part that had not moved.
> Criterion 06 now loops every night, 10 derives its chapters from what the content actually cites
> (both contests' `page_verse`, and the gate's own `NEH.12` read off `StageDirector`'s constants),
> and `night_threat: false` is asserted night by night.
>
> **`S3` closes the `letters` interlock.** `keep_working` exists only for a player who refused the
> invitation six stages earlier; criterion 08 proved the move **A Página** unlocks is shut before the
> Page, and nothing proved the same for a move a **flag** unlocks. `S3` reads the contest table the
> way 06 reads the nights — whatever moves declare a flag gate, in whatever contest — and asserts
> three things about each: shut without the flag, open with it, and **traceable back to a dialogue
> node that grants it**. That last half is the one worth the trouble: a gate that works perfectly on
> a flag no content raises is correct code nothing calls, and the move would be unreachable in a
> played game while every rule about it passed. It reports the whole chain, so the report names what
> it proved: `letters/keep_working <- refused_invite (malquias_d2_refuse)`.

---

## 14 · State — what is done, and what is not

### Done

**The nine-stage season plays end to end**, in both languages. Compiles at 0 errors on the editor
the project declares (`ProjectVersion.txt`, 6000.3.23f1). On a **6000.5** editor it still compiles
clean but raises ~118 `CS0618` obsolescence warnings — `FindFirstObjectByType`,
`FindObjectsSortMode`, `AndroidApiLevel25` — none of them from anything this project wrote wrong.
Whoever moves the project to 6000.5 inherits that list; nobody has decided to.
`tools/e2e.sh` builds a real player and plays whatever the season declares, in order, from a cold
save to the terminal stage — the opening, creation, the village, the split, the night, the morning
report, the quiz, both contests, A Página, the reader and the vocation reveal — screenshotting into
`Builds/e2e/`. It reads the stage count out of `stages.json` rather than knowing it.
`tools/acceptance.sh` passes every criterion in both languages, and the content validator is clean.

| | |
|---|---|
| Desktop | `Builds/mac/` — runnable |
| iOS simulator | `tools/ios-sim.sh` — builds, installs, boots and plays |
| iOS device | `Builds/ios/` — valid project, **never run on a device** |
| Android | `tools/android-emu.sh` — builds the APK (IL2CPP, ARM64), boots an arm64 emulator, installs, launches; played to the village on 03/09/2026. **Never on a device** |

The launch screen is covered from both sides: criterion 15 asserts the predicate, and `e2e.sh`'s
cold run asserts the behaviour — a Play button on the opening fails the step that requires a splash
with nothing to press.

**Closed since the three-day build:** the old **day-3 gap on a phone** — the trial, A Página, the
gate closing with the player's name, the **Saber mais** that opens `NEH.4`, the vocation reveal — was
played end to end on the iOS simulator; character creation now speaks the catalogue's vocabulary, so
the contradiction with the backpack is resolved (`docs/character-creation-scope.md` is the record of
a finished job, not a work order); and `WorldMapOverlay.Place.Caption`, which was never drawn, was
removed rather than wired.

> **What that does not say.** Those beats were proved on a phone in the **three-day** numbering,
> where they were day 3. In the nine-stage season the same beats live at stages 6 and 9, three and
> six stages further in, behind content that has never been played on a phone at all. The season
> end to end is proved by `e2e.sh` on a desktop player, not by a hand on a device.

### Not done

- **The curation queue is read, and the read is dated.** All twelve nodes that carry canonical
  speech — the governor's `intro_gathering` and `gathering_d4`, `sanballat_d5/d6/d8`, `tobiah_d6`,
  in both locales — were read against the passage in `6d9d0ac` and `079aac4`, and each node
  records the commit in `curated_in`. `node tools/list-curation.mjs` prints nothing waiting.
  What rule 4 still asks for is discipline, not work: **a line edited after its `curated_in`
  commit has a stale read**, and the queue will not say so on its own.
- **The English has had no native pass.**
- **No physical device has run the game** — iOS never, and Android only on an emulator (the first
  APK was built and played on 03/09/2026). Both halves are inside the §00 definition of done, and
  both need hardware, not code.
- **The buildable wall is a straight run** along the north of a circular city. Nehemiah 3 assigns each
  group a stretch, so it reads correctly, but a true arc would need `WallSystem`, the contest and the
  patrol camera to change together. **Deferred by decision.**
- **The four skin tones and the build silhouettes are unjudged.** Nobody has looked at whether tones 2
  and 3 are distinguishable at 32×48, or whether the narrower build reads.
