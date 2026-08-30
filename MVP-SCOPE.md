# MVP — A Cidade Quebrada, Sheep Gate

| | |
|---|---|
| **Engine** | Unity 6 LTS · 2D URP |
| **Target** | iOS · macOS · (Android target kept, never installed) |
| **Mode** | Single player |
| **Login** | None |
| **Length** | ~20 min · 3 days of play |
| **Languages** | pt-BR (authoring) · en |

End-to-end scope. Written to be executed by agents: every section defines concrete artifacts,
schemas and acceptance criteria. Product rules live in [`AGENTS.md`](AGENTS.md) and are not repeated
here.

---

## 00 · Objective and success criterion

The build exists to answer one question: **does the player open the chapter of their own accord?**
Everything else is a means. If by the end of day 3 `deep_read` never fires without the game asking,
it has done its job — it answered no, and answered cheaply.

> **Definition of done**
> The game installs and opens with no sign-up, and a person who has never seen the project plays all
> three days to the end with no outside help, with the wall visibly built, a morale trial won or
> lost, and the chapter reader opened at least once.

Android keeps its build target and stays in this definition, but **no APK has ever been installed**.
iOS and macOS are what has actually been played. Known gap, not a scope change.

---

## 01 · Stack

| Item | Choice | Why |
|---|---|---|
| Engine | Unity 6000.3.23f1 | More C#/Unity in model training than GDScript — decisive, because the implementation is done by agents. |
| Render | 2D URP | Needed for the day/night light. |
| Serialisation | `Newtonsoft.Json` | `JsonUtility` reads neither dictionaries nor polymorphism. Do not use it. |
| Content | JSON in `Resources/Data/` | A ScriptableObject needs the editor to author; JSON is editable by agent and by human outside Unity. |
| Backend | None at runtime | Verses are baked at build time (§09). Zero network during play. |
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
                   IntroCutscene  Day3Director  NpcActor  WorldMapView  RestPoint
  Resources/Data/
    map.json  wall_segments.json  npcs.json  contest.json           // structure and numbers:
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

Four segments, four stages each. `seg_02` is the exposed one.

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

`CharacterCreation.unity` stays registered and `CharacterCreationScreen.Compose()` still builds the
standalone screen, so that route keeps working; nothing routes to it.

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
| `WallSystem` | Stages per segment, consumes work, swaps sprite, raises completion. `DamageSegment` clears in-progress work only. |
| `ResourceSystem` | Rubble split into stone, timber and crafted blocks, plus daily work capacity. **Capacity is the day's clock.** |
| `DayCycle` | **The day ends by itself.** Light tracks capacity spent; when it hits zero the village goes to dusk and the split panel opens on its own. `DuskWaits` holds the night for whatever has the screen. |
| `DialogueSystem` | Line queue, typewriter reveal, resolves `verse`, applies `grants`. |
| `ScriptureService` | In-memory index of the locale's `verses.json`. Never hits the network. |
| `ScriptureVisibility` | Decides whether `ref_display` is shown. One-way: hidden until the reveal, never re-hidden. |
| `ChapterReaderUI` | Scrollable panel with the whole chapter. Fires `deep_read` past 20s **and** 60% scroll. |
| `MoraleContest` | Turn machine for day 3. See §07. |
| `Day3Director` | Holds day 3's evening open for the whole day — a split appearing there is a bug. |
| `VocationTracker` | Accumulates silently. **No public score getter.** Reveals at the end of day 3. |
| `DailyQuiz` | One question a day. Check-in counts whether right or wrong. Listens to `MorningStarted` **and** `DuskBegan`, because day 1 opens in a cutscene and has no morning. |
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

## 06 · Content — the three days

Six residents, all named in Nehemiah 3 and **with no recorded speech in the text**. That is the
category where writing dialogue is legitimate: the Bible names them and does not quote them.

| id | Name | Source | Role |
|---|---|---|---|
| `hananias` | Hananias | `NEH.3.8` | Perfumer's son. Sets the tone of the people's pain. Reliable. |
| `salum` | Salum | `NEH.3.12` | Works with his daughters. Teaches the work/watch split. |
| `baruque` | Baruque | `NEH.3.20` | The one the text singles out for working with zeal. Pushes toward the exposed stretch. |
| `meremote` | Meremote | `NEH.3.4` | Day 2: saw horsemen on the road. **Correct information.** |
| `zacur` | Zacur | `NEH.3.2` | Day 2: says nobody is coming. **Wrong information.** The game does not say so. |
| `malquias` | Malquias | `NEH.3.14` | District ruler. Delivers the outside invitation on day 2. |

### Day 1 — The summons

- Opening cutscene into the village. Minimal HUD: work capacity, materials. **No end-of-day button.**
- Talking to `hananias`, `salum` and `baruque` unlocks the stretch.
- Gather rubble → work a segment. Every stone laid pulls the light down.
- End of day: capacity runs out, night falls, and the panel asks for the split between **work** and
  **watch**.
- The check-in lands at the evening, because day 1 opens in a cutscene and never has a morning.

### Day 2 — Those who call from outside

- Morning report: what the night did, with and without a watch.
- `meremote` and `zacur` contradict each other. No indicator of which to believe.
- `malquias` brings the invitation. Accepting spends the whole day and damages a segment; refusing
  cites `NEH.6.3`.
- Fish in the well: 2 attempts fail, the 3rd brings the hint and cites `JHN.21.6`.
- Accepting zeroes capacity **without** ending the day: `malquias` takes a named hold
  (`DayCycle.HoldPendingBeat`) until the player comes back and hears the other half of it. The hold is
  re-derived in `Start`, so a scene rebuild mid-beat restores it. The mat can overrule it, and must —
  a player who never goes back would otherwise have no way to reach tomorrow.

### Day 3 — The breach and the reading

- Short morning, then the assault fires the trial (§07).
- On turn 2 **A Página** arrives and unlocks the strong move.
- Win or lose, the gate closes with the player's name on it.
- **Saber mais** opens `NEH.4` in the internal reader.
- The vocation reveal, and the end.

---

## 07 · Morale trial

Alternating turns. **No health bar and no death counter:** you win by making the other side give up.
The outcome is decided by what the player did on days 1 and 2 — that is what makes it feel earned
rather than rolled.

```
player.morale   = 100
enemy.resolve   = 60 + (10 if !watchPostedD2) + (10 if acceptedInvite)
turn limit      = 8   // overflow = the enemy withdraws, a technical draw
```

| Move | Effect | Depends on |
|---|---|---|
| Hold the line | −8 resolve | +1 per stage built |
| Call the others | +12 morale | ×(NPCs spoken to / 6) |
| Show the watch | −20 resolve if a watch was posted | flag `watch_posted_d2` |
| **Half and half** *(unlocks on t2)* | −15 resolve **and** +8 morale, same turn | Only exists after A Página |

> **A Página — the moment this build exists to test**
> At the start of turn 2 the trial pauses and a panel slides in showing `NEH.4.17`, reference visible
> in the footer. On closing, **Half and half** appears in the menu. The reveal that this is the Bible
> happens in the same instant that the Bible becomes the strongest weapon available. Fires
> `reveal_shown`, and opens `ScriptureVisibility`.

> **LOSING IS NOT GAME OVER**
> If `morale <= 0` the enemy withdraws anyway at the end of the turn, a segment loses **one unfinished
> stage**, and day 3 continues normally to the reading and the vocation. **You can lose tomorrow,
> never yesterday:** a completed stage never regresses. There is no defeat screen.

---

## 08 · Vocation scoring

Six vocations, accumulated in silence. At the end of day 3 the highest is revealed; ties break by the
order in `vocations.json`.

| Vocation | Actions that score |
|---|---|
| `zelote` | Work the exposed segment · refuse the invitation outright · open the trial with Hold the line |
| `escriba` | Open the chapter reader · speak to all 6 NPCs · re-read a dialogue |
| `pastor` | Use Call the others · speak to Hananias and Salum on both days · donate rubble |
| `exilado` | Use the Patrol 3+ times · catch the fish · walk to the map edge |
| `profeta` | Believe Meremote and not Zacur · close A Página without skipping |
| `mordomo` | End days 1 and 2 with capacity fully spent · zero rubble wasted |

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
| `deep_read` | **The north-star metric.** 20s and 60% scroll. |
| `unprompted_read` | Opened **without** the game asking. The signal that matters most under rule 12. |
| `reveal_shown` | The moment the penny drops. |
| `node_completed` | Work progress. |
| `vocation_revealed` | Real distribution of behaviour. |
| `locale_changed` | Which language people actually play in. |

---

## 11 · Art

**Art comes through one seam.** `ArtLibrary.Get(key)` is the only way to obtain a sprite. Most keys
are generated procedurally in `SheepGate.Art` from a reduced palette; ground, rubble and water come
from a drawn CC0 sheet (Kenney's Roguelike/RPG pack, licence in `Assets/Art/`). The sheet ships as a
`.bytes` file and is decoded at runtime rather than imported as a Unity sprite: no import settings to
get wrong, no `.meta` to hand-write, and no slicing stored in an asset nobody can review in a diff. A
key with no drawn tile falls through to the generated one, so the swap happens a key at a time.

Character art is procedural and layered (`CharacterArt`): build, skin tone, hair, top, legs,
accessory. Six synthesised sounds, no audio assets.

**No golden light, no dove, no cross, no praying hands, no robes, no sandals** — rule 13.
`docs/design-system.md` covers tokens, typography and the rules that are design rather than style.

---

## 12 · Out of scope

- Multiplayer, expeditions, seats — **the schema anticipates it, this build does not implement it**
- The village as a separate base with its own construction
- A shop, spendable talents, currency, any purchase
- A daily streak, push notifications
- Sign-up, accounts, cloud, sync
- LLM calls at runtime — all dialogue is authored in `dialogue.json`
- The other 9 gates, the other 3 threats, any other season
- Jesus or the Holy Spirit as a guide — **deferred by decision, not discarded**

---

## 13 · Acceptance criteria

Asserted by `tools/acceptance.sh`, once per locale, unless noted.

| # | Check |
|---|---|
| 01 | The build installs and opens with no login screen. |
| 02 | Character creation produces a distinct appearance, persisted between sessions. |
| 03 | Tapping the ground moves; tapping an NPC approaches and opens dialogue. |
| 04 | Every verse displayed came from `verses.json`; no literal in C#. Checkable with grep. |
| 05 | A segment changes sprite across its 4 stages and progress survives closing the app. |
| 06 | Ending day 1 without a watch produces a different morning report than with one. |
| 07 | Refusing the invitation shows `NEH.6.3`; accepting spends the day and damages the segment. |
| 08 | A Página appears on turn 2 and unlocks **Half and half**. |
| 09 | Losing the trial shows **no** game over and does not regress a completed stage. |
| 10 | `deep_read` appears in `telemetry.jsonl` after a real reading of `NEH.4`. |
| 11 | The vocation revealed matches the highest score; no UI exposed progress beforehand. |
| 12 | Runs offline start to finish, in aeroplane mode. |
| 13 | The day ends by itself when capacity hits zero; no HUD button ends it; no night resolves with a panel open. |
| 14 | The translation copyright is displayed in-game. |

---

## 14 · State — what is done, and what is not

### Done

Three days play end to end. Compiles at 0 errors and 0 warnings across ~85 C# files. `tools/e2e.sh`
builds a real player and plays **all three days in both languages** — the opening, creation, the
village, the split, the night, the morning report, the quiz, the trial, A Página, the reader and the
vocation reveal — screenshotting into `Builds/e2e/`. `tools/acceptance.sh` passes every criterion in
both languages, and the content validator is clean.

| | |
|---|---|
| Desktop | `Builds/mac/` — runnable |
| iOS simulator | `tools/ios-sim.sh` — builds, installs, boots and plays |
| iOS device | `Builds/ios/` — valid project, **never run on a device** |
| Android | Build target exists; **never installed** |

### Not done

- **iOS is played through day 2 and no further. Day 3 is the gap on a phone.** What it still has to
  prove there: the trial, A Página at turn 2, the gate closing with the player's name, the **Saber
  mais** that opens `NEH.4` — this is the `deep_read` path and the reason this build exists — and the
  vocation reveal. A run is parked on the simulator ready for exactly that; `tools/ios-sim.sh run`
  resumes it.
- **Character creation contradicts the catalogue.** Creation offers hair variants the backpack then
  says are locked. This is the live work order — see
  [`docs/character-creation-scope.md`](docs/character-creation-scope.md), which carries the decisions,
  the art cost (+5 procedural shapes) and the execution order.
- **The curation queue has never been read.** `intro_gathering` (the governor) awaits a human read
  against the passage, in **both** languages. Rule 4 requires it.
- **The English has had no native pass.**
- **`WorldMapOverlay.Place.Caption` is never drawn.** Either it was meant to render, or the field
  should go.
- **The buildable wall is a straight run** along the north of a circular city. Nehemiah 3 assigns each
  group a stretch, so it reads correctly, but a true arc would need `WallSystem`, the contest and the
  patrol camera to change together.
- **The four skin tones and the build silhouettes are unjudged.** Nobody has looked at whether tones 2
  and 3 are distinguishable at 32×48, or whether the narrower build reads.
