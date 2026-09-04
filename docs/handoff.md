# Handoff

State of the build at `fa4b715`, and the things that are not obvious from reading the code.

Start with `README.md` for how to run it and `docs/architecture-contract.md` for the interfaces.
This file is the part that would otherwise have to be rediscovered — and the part most likely to
have gone stale, so every claim below carries the file, the line or the commit it was checked
against. **If a statement here has no cite, treat it as a memory rather than a fact.**

## Where it is

**A nine-stage season plays end to end**, `the_summons` → `the_dedication`. The season is
`Assets/Resources/Data/stages.json` — nine rows, ids `the_summons`, `vision_and_plan`,
`preparation`, `the_people_united`, `the_work_begins`, `enemies_rise`, `prayer_and_guard`,
`the_work_finished`, `the_dedication` — and nothing in C# counts days. `StageDirector` does what the
row it is standing in declares.

**The game is bilingual: pt-BR and English.** pt-BR is the authoring locale. No player-facing string
exists in C#; they all live in `Assets/Resources/Data/locales/<locale>/` and are read through
`Loc.T`. Structure and numbers are shared, so balance cannot diverge between languages. See
[`docs/development-guidelines.md`](development-guidelines.md).

| | |
|---|---|
| Desktop | `Builds/mac/SheepGate.app` — runnable |
| iOS simulator | `tools/ios-sim.sh` — builds, installs, boots and plays, on iPhone 17 Pro / Pro Max |
| iOS device | `Builds/ios/Unity-iPhone.xcodeproj` — valid project, **never run on a device** |
| Android | `tools/android-emu.sh` — IL2CPP/ARM64 APK built and played on an arm64 emulator on 03/09/2026; **no physical device yet**. |
| iPhone (device) | `tools/ios-device.sh` — installed and launched on an iPhone 16 Pro over USB on 04/09/2026, signed with a personal team via `IOS_TEAM=`; the committed `appleDeveloperTeamID` is not on this Mac's Xcode account. |

**The opening**: region shot with the other cities shut → push-in to the ruined circular city → a
neighbour crosses the square and speaks → you follow him into his house → character creation → you
follow him to the gathering → the unnamed man from the capital speaks and sends everyone for stone.

**Stage 6** (`enemies_rise`) starts the `raid` contest and carries `reveals_page`; `contest.json`
gives `raid` a `page_turn` of 2 and a `page_verse` of `NEH.4.17`, and `half_and_half` — *Metade e
metade* — is the one move marked `unlocked_by_page`. An optional "Saber mais" then opens `NEH.4`.
**Stage 8** (`the_work_finished`) fights `letters` and carries `finishes_wall`. **Stage 9**
(`the_dedication`) is the only row with `terminal`, and carries `closes_gate` on `seg_02` and
`reveals_vocation`: it closes the gate with the player's name on it, offers `NEH.12` behind a second
"Saber mais", and names the vocation. All of that is read off `stages.json` and `contest.json`, not
off C#.

**The day ends on its own.** There is no end-of-day button. Work capacity is the only thing a day is
spent on, so the light is that capacity seen a second way: every stone laid pulls it down, and when
there is nothing left to spend the village goes to dusk and the split opens itself. Stopping early
is the mat at the door of the house. Three things this must keep doing, all of them easy to break by
accident:

- **Nothing runs on a wall clock.** Standing still, talking and reading cost nothing. The moment
  anything charges real time, reading a chapter starts costing the player a day, which is rule 20
  pointed at its own foot.
- **A pending night waits for whatever has the screen** — `DayCycle.DuskWaits`
  (`Assets/Scripts/World/DayCycle.cs:550`), asserted by the acceptance harness. The chapter reader
  is a panel like any other.
- **Accepting the day-2 invitation zeroes capacity without ending the day.** `NpcActor` takes a
  named hold (`DayCycle.HoldPendingBeat`, `DayCycle.cs:86`, taken at `NpcActor.cs:219` and `:593`)
  and gives it back when the resident says the other half of it; the hold is re-derived in `Start`,
  so a scene rebuild mid-beat restores it. The mat can overrule that hold, and must be able to — a
  player who never goes back would otherwise have no way to reach tomorrow.

### The gates, and what each one is actually worth

| Gate | Last recorded state | What it structurally cannot see |
|---|---|---|
| `tools/unity-check.sh` | clean at the commits below | anything about layout — the scenes are near-empty and everything is built at runtime |
| `node tools/validate-content.mjs` | **run against `991a0ba` while writing this: PASSED, 0 warnings.** 35 verses, 7 chapters per locale, 20 hardcoded-string sinks, 255 locale keys, 12 nodes queued for a human read | whether a sentence is *good*, and whether authored canonical speech overreaches — that is rule 4's human read |
| `tools/acceptance.sh` | passes every criterion in both languages | it never calls `Compose()`, so it is a gate on rules and not evidence that anything is wired |
| `tools/e2e.sh` | **749 steps, 9 of 9 stages, both locales, 0 failures** (`4d5681a`; the per-locale split, 387 pt-BR + 362 en, is recorded in `079aac4`) | the interaction layer — it spends a day through `ResourceSystem` rather than walking the player to the wall; and it runs the macOS player at exactly 1080×1920, where the canvas unit and the pixel are the same number |
| `tools/tile-preview.sh` | new at `991a0ba`; `characters` and the Unity 6000.5 Roslyn path at `e7f1b8c` | anything that is not a procedural tile or a character body; and it renders, it never judges — the tones and silhouettes still want a human eye |

**`tools/e2e.sh` runs the locales serially now, and `--parallel` is opt-in** (`tools/e2e.sh:22`,
`:138`). The harness hung four times — 30/08, 01/09 and twice on 02/09 — always a few steps past the
opening screenshot, no exception, no log line, until the outer watchdog killed the player 990
seconds later. Both players launch windowed at the same 1080×1920, so on one screen the second is
born on top of the first and covers it completely, and **macOS suspends rendering for a fully
occluded window**. Every step of `E2ERunner` is a `yield return null`, so a player that stops
getting frames does not fail a step — it stops *between* them, and nothing is left running that
could report it. `Application.runInBackground` is set and does not lift occlusion suspension; that
cost a whole investigation and the comment now says so. Three fixes that do not work are written
into the script rather than left to be re-derived: an in-runner watchdog is a coroutine and
coroutines do not run when frames do not; `-nographics` removes the renderer and every beat here is
a screenshot; different window sizes per locale still nests the smaller window inside the larger.

## What landed since the last handoff

### Saving on the way out, a fight that survives a kill, and the first day's nudge

Three answers to "does it save, and does the player know what to do". Every save was taken
after an action and none on the way out, so a walk was a cell behind when the phone locked and
a contest killed in turn five reopened in turn one (seen on the simulator: 88/100 × 38/48 came
back as 100 × 48). `PauseSave` on the systems object saves on pause and quit with the cell under
the player's feet; `MoraleContest` writes its turn, morale, resolve, enemy line and spent moves
into the state after every survived turn and resumes there on the next `Begin`, saying so in the
log — criterion 19. The help's first undone step is now said once over the village at the
intro's hand-over (`HelpPanel.NudgeOnce`, a six-second toast, `help_nudged` flag), because
everything the help knows was behind the "?" and a first-day player found the loop by trial.
And `help.step.trial` stopped saying "the third day": the tests are days 5 to 8 now.

### The map states each stop's objective

The progression map's detail card carries an **OBJETIVO** line per stop, authored per stage in
`world.progress_map.objective.<day>` (the two keys that had sat unused in `ui.json` since the map
was drawn are now nine, one per stage, written out as literals in `WorldMapView.DayObjectiveKeys`
for the reason the title keys are). It shows for locked stops too — what a day asks is the one
thing the map can say about a stop nobody has reached — while the diary stays reached-only. The
lines are the stage's ask in the fiction and nothing more: no reading as a goal (rules 19, 20), no
prayer (rule 13), no reference (rule 12). The card grew from 256 to 344 design points to hold
them beside a five-line diary on a phone — measured on the iPhone simulator, where 312 left
half a line under a four-line diary and stage 7 writes five. The e2e asserts the objective on stop 1 and that it
changes on stop 2.

### The codex of the gates — the design's "every gate with its reference from the first hour"

`gates.json` (structure) and `locales/<locale>/gates.json` (name and a builders line in our words)
carry the ten gates of `NEH.3`; the nine verses the table lacked were copied out of the chapter
already in `verses.json`, same API, same version, no call spent. `CodexPanel` opens from a new row
of the HUD's drawer on every day, lists the ten as scroll cards with Saber mais into the chapter,
and marks the player's gate by the terminal stage's `gate_segment`. **Its references show from the
first hour** — the design keeps rule 12's deferral inside the fiction and names the codex as one of
the surfaces that is honest about what the product is; MVP-SCOPE §04 has the reasoning. Criterion
18 checks the table in both languages; the e2e opens the panel on the full-battery stages and reads
a reference on stage 1, before any reveal. A deep read from a codex card counts as
`unprompted_read` too — nobody put that quotation in front of the player. The phone found one more
thing: **the Saber mais on a parchment card was invisible** — `Secondary` is parchment ink over a
dark surface, and on `CardStyle.Scroll` that is parchment on parchment. The vigil page had shipped
that way. `ButtonVariant.SecondaryOnScroll` is the same shape in the scroll's own inks, and both
cards use it. Two data fixes rode along after a look at the iPhone
simulator: the mockery's and the famine's chapter moves — *seguir contando*, *abrir mão*, *recusar
a ração* — were third and fourth in their lists, under the fold on a phone, and are now first; the
e2e sorts moves by delta so it never noticed.

### The famine and the reading — the two beats the corpus was holding back

NVI was enabled for the app's key on 04/09/2026 and `NEH.5`, `NEH.8` and `NEH.4.12` were fetched
in both locales. With them: the **`famine`** contest on stage 7 (the design's third threat, from
inside the wall — six narrated lines of hunger, the tax and the lenders; two moves that **cost the
day**, `costs_work` on the move row, once per fight, spent through `ResourceSystem`; the shepherd
and the steward score them; criterion 17 proves the price), and the **reading of the Law** as the
season's closing event: `reading_d9` is stage 9's new `closing_node`, played by the director
between the gate panel and the reveal, quoting `NEH.8` around a narrator with no authored line
for the governor or Ezra — the third `deep_read` door, and the ending the design asked for. The
vigil of stage 6 now returns `NEH.5.1`. The e2e fights the famine and detours through the
reading's Saber mais, asserting the chapter is the node's own.

### Timber by letter — one citation, no gate

The design says timber *is not gathered: it comes by letter to the keeper of the king's forest*.
The beams already appeared on day 2; now Hananias says why, and `NEH.2.8` is quoted on
`hananias_d2` in both locales. Content only — gating the day's courses behind a conversation would
reopen the economy defect the engagement wave closed.

### The mockery — the design's first threat, as its own contest

Stage 5 opened on Sambalate's first words as dialogue; the design lists mockery as one of four
threats with its own grammar, *a pure morale drain with no target to attack*. It is now the
**`mockery`** contest on the same engine (`contest.json`, `type: battle` on stage 5): resolve 48,
pressure 10, six narrated lines of laughter from the upper road, no trumpet (`trumpet: false` — the
row is the first to carry the field; the other two default to true), no Page, and three moves —
Hold the line, Call the others, and its own **Count out loud** (−10 resolve · +4 morale, the crew
hearing the count instead of the joke). Show the watch and Half and half are not offered: there is
nothing to show a torch to, and the Page has not happened. The residents' day-5 lines already
described exactly this, so nothing in the dialogue moved. Criterion 07 now checks three fights, and
the e2e fights the mockery at stage 5 before the raid at stage 6 without the Page intruding.

### The vigil — rule 8's information half, on the split

The design's *rule of prayer* had no code behind it: prayer returns information, never force, and
costs the night. The split panel now carries a **vigil** toggle on every night whose stage declares a
`vigil_verse` (eight of the nine; the terminal stage has no night). Switched on, the crew off the wall
stays up over the page instead of building — `DayCycle` lands zero night work, leaves the cleared path
unspent, writes `vigil_kept_d<n>` and the `night_vigil` counter, and raises `vigil_kept`. The morning
report then shows the stage's verse on a parchment card, italic, with the reference gated by
`ScriptureVisibility` like every quotation and **Saber mais** beside it opening the chapter with the
game asking (`trigger: vigil`). The watch is untouched by design: it is the other half of `NEH.4.9`,
so a vigil with no watch still loses the wall, and the split says so under the toggle before the
player commits. Deliberately not a prayer button (rule 13) and deliberately not a power: the whole
return is text, and text pays in understanding (rule 19). Criterion 16 in the harness proves the
three assertions per night; the e2e keeps the vigil on the eve of the Page and reads the card the
next morning, in both languages.

**The engagement wave of 2026-09-03** — nine commits from `87bd338` to `fa4b715`, each behind the
validator, the compile, the acceptance harness and, in three batches, the end-to-end run in both
locales. What changed, in the order it matters:

- **The day is four courses** (`GameState.DefaultWorkCapacityMax`, schema 4). Capacity was twelve,
  the material a morning makes is four blocks, and from day 2 the day never ended by itself again;
  the mat, written as optional, was the only way to tomorrow. The night crew is its own number now,
  `DayCycle.CrewSize = 12`. A dry course on day 1 costs a block's worth of stone. The player's
  stretch, `seg_02`, costs 20 units. The steward's second habit counts piles left in the village.
- **The gate is earned** (`StageDirector.GateIsEarned`). The dedication used to hand `seg_02`
  sixty-four units nobody laid. Now the last stage takes its hold only once the segment stands;
  until then it is a working day that repeats its date with the piles refilled
  (`DayCycle.RefillThePiles`), and the morning says how many courses are left. Harness `S4`; the
  e2e lays its capacity on the gate segment day by day and asserts the whole cost was its own.
- **The wall is on the screen** — a four-bar silhouette under the work plate, the player's bar
  taller — and `WallBeats` answers every course with a toast, a finished segment with a look, and
  design-system rule 11 with the ruin outside thinning as the courses go up.
- **Stages 3, 5 and 7 have a beat with a number in the morning** (the Tekoites, the carriers,
  the armed night), a node may cost an hour (`spend_work`), and where a conversation branches the
  branch scores, not the conversation.
- **There is an ending**: `SeasonEndPanel`, after the reveal and from the HUD's drawer — the wall
  with who repaired each stretch (`segment` in `npcs.json`), the season's numbers, the six
  vocations with this run's marked, `NEH.6` behind a button that pays nothing (`ungamed_read`),
  and a new work. Ties in the vocation go to the most recent award.
- **The streak is gone**, with the talents it paid and every price in the wardrobe;
  `WelcomeBackModal` says nothing went backwards after a day away. The opening is fourteen cards,
  not twenty-four. The day's question is asked at dusk with a hook into tomorrow. The raid speaks
  eight lines; the stone is three stones. Each stage's piece unlocks on the stage that names it.
  The page's skip no longer sits on the turn counter.

What that wave did **not** do, and why, is in `MVP-SCOPE.md` §06 and the session notes it came
from: no share button (no native plugin), no contest "tells" beyond the extra lines (the contest's
pressure model is unchanged), no haptics, and no playtest — nobody in the persona has played this
build, and every finding above came from code and a harness that never chose a branch.


**The backpack is two levels.** Three sections along the top — Perfil, Itens, Aparência — and, under
Aparência only, three wardrobe slots along the bottom: Cabelo, Roupa, Extras. Five content panels
behind two bars (`BackpackPanel.cs:230-250`). The levels sit at opposite ends for a reason recorded
in the file: the design system anchors a segmented row at the bottom because that is where the thumb
is, which belongs to the slots tapped over and over while dressing; choosing a section happens once
per visit, so it reads as the sheet's own navigation and goes up top. **Three cells per bar is also
what made "Aparência" sayable** — at five cells a label has 57.3 points and "Personalização" needs
112.8; at three it has 95.5 and "Aparência" needs 74.6. Materiais and Detalhes did not survive that
squeeze either and were re-authored as **Itens** and **Extras** (`backpack.tab.materials`,
`slot.accessory` in `locales/<locale>/ui.json`). Pedro ruled this design authoritative;
`docs/design-system.md` was updated by that merge — read it rather than rewriting it.

**One row renderer, two screens.** `BackpackPanel` builds every wardrobe row through
`Assets/Scripts/UI/WardrobeRow.cs`, the same renderer `CharacterCreationScreen` uses. It went from
**0 references in `BackpackPanel` to 31** (checked against `7d82dfe^`), and that commit removed 701
lines from the file and added 172 — a net −529. Both screens draw the three slot names from the
shared `slot.hair` / `slot.outfit` / `slot.accessory` keys (`BackpackPanel.cs:196-198`,
`CharacterCreationScreen.cs:209`), so renaming a slot lands on both at once. The reason it mattered
on a device: the backpack kept PR #7's design and drew its own rows, and on a 402×874 phone exactly
**one** item row fitted the content band, at ~220 points a row, with a name like "Curto denso"
wrapping inside a ~100-point column.

**Character creation allocates a budget instead of hoping.** `Composer.ApplyVerticalBudget`
(`CharacterCreationScreen.cs:1885`) hands the list `MinimumVisibleRows` × the tallest row in the
running locale **first**, then `FixedChrome()` — the eyebrow row, the tab bar, the action row, the
safe-area lane — and the preview strip takes what is left. It used to lay out its preferences and
then cut three things when they did not fit, and still landed at 0.93 of the 2.0 rows its own guard
demands. **`MinimumVisibleRows` is still 2.0 and still fatal** (`:481`; the shortfall is a
`Debug.LogError` at `:1934`, and `E2ERunner.OnLog` at `:2596` counts any logged error as a run
failure). It went quiet because the list genuinely has its rows, not because a guard was loosened.
Step 1's card floor is now derived from the card's own measured caption — `CardMinimumHeight` at
`:1197` sums padding, accent stripe, `CardFigureMinHeight` and the measured caption band — replacing
a 240dp clamp that sat **above** its own 120dp error threshold, so a negative band silently became
240 and the error could never fire (`CharacterCreationScreen.cs:353-362`). The personality line is measured with
`WardrobeRow.PinText` rather than reserving two lines. **"Continuar" was never dead**: its
explanation was the last block inside an overflowing scroll, so the sentence arrived below the fold
and a primary call to action answered a tap with no ring, no message and no movement, twice in a
row, on the first screen of the game. It is pinned above the action row, outside the scroll, and the
band is measured from the sentence rather than counted in lines because it is one line in one
language and two in the next (`BuildChoiceMessage`, `:1534-1557`).

**`RecomputeMetrics` was reading raw screen pixels.** It runs on the frame the canvas is created,
before `CanvasScaler` sets `scaleFactor`, so every rect under the canvas is still measured in screen
pixels. Taking `Mathf.Min(CanvasWidth(), root.rect.width)` therefore took the pixel count. Per
`2b95c27`: at 402×874 the content width came out **285.69 instead of 860.30**, and
`WardrobeRow.MetricsFor` was handed a text column of **−13.38**. Both axes now come from
`Screen.safeArea`'s *share* of the screen — a ratio, which is the one thing about the inset that is
true before a layout pass — applied to the scaler's own log-space lerp (`RecomputeMetrics` at
`CharacterCreationScreen.cs:789`, `SafeAreaHeight()` at `:836`). At 1080×1920 the numbers are
bit-identical because `safe == screen` makes the ratio 1, which is why the earlier green run carries
over — **and is also exactly why `tools/e2e.sh` could not see this.**

**Accessories can turn.** `CharacterFigure` passed the facing to the body and to the hair and not to
the accessory; `ArtKeys.Accessory` built a bare `acc_<n>` with no direction token, and the parser
fell back to Down. Alone among the worn layers the accessory is never mirrored — every variant is
anchored to one side or one face (`shoulder_r`, `wrist_r`, `back_center`, `waist_front`) — so four
correct per-facing drawings per variant existed and never reached a screen: the map tube across the
back rendered as the front view of the map tube. The key carries a facing now, the way `ArtKeys.Hair`
always did (`ArtKeys.cs:164-179`).

**The outside of the map is terrain, and the wall lies on it.** The camera clamps to
`TilemapBuilder.ContentBounds` (`TilemapBuilder.cs:99`, consumed at `CameraRig.cs:283`), the
bounding box of drawn cells, and the city is an oval in a 40×28 rectangle — so on a phone it framed
big flat near-black rectangles that read as holes in the rendering. Off-map cells now draw the same
ground the city stands on, with a scattered collapse of fallen wall over the outer band.
`Assets/Scripts/World/VoidScatter.cs` decides which off-map cells carry stone: density is a
smoothstep on `dCity`, the distance from the nearest drawn cell, **not** on the cell's fraction
across the band — the fraction had `map.json`'s rectangle built into it and put a one-cell vertical
line of near-solid stone down each side of the map. `TileArt.FallenWall` (`TileArt.cs:440`) is drawn
from the standing wall's own masonry — the same `StoneRamp`, `DrawCourse`'s block proportions, a lit
top face and a dark underside — tilted, chipped and half sunk, not `RubbleTile` with a different
seed. Twelve variants, keyed `ArtKeys.TileFallenWall` / `FallenWallVariantCount` /
`FallenWallVariant(n)` (`ArtKeys.cs:33`, `:129`, `:132`).

**THE INVARIANT, and it is the important part: void cells remain non-walkable.** Appearance and
walkability are now separate questions about the same cell. `Walkable`, `KindAt` and `map.json` were
not touched. Void exists because a space in `map.json` was once read as a second ground character,
which left the whole outer border walkable and let the player stroll out of the village.

**That invariant finally has a gate.** `E2ERunner.VerifyTheOutsideIsNotWalkable` (`:3698`) asserts
no void cell is walkable **and asserts the map has an outside first**, because an assertion that
passes for want of anything to check is the failure it exists to prevent. `afec50e` records 640 void
cells of 1120, none walkable; `map.json` independently carries 640 spaces in its 40×28 grid, so the
denominator is right. The old bug was caught by eye, back when the outside looked wrong. **It would
not be caught by eye now, because the outside looks right.**

**New tool: `tools/tile-preview.sh`** (`991a0ba`). Renders the game's procedural tile art to a PNG
in seconds **without Unity**, by compiling `ArtPalette.cs` / `PixelCanvas.cs` / `ValueNoise.cs` /
`TileArt.cs` — the real files, not copies — against a small `UnityEngine` stub, using the Roslyn
that ships inside Unity. Modes: `sheet`, `zoom`, `field <density>`, `check`. Output to
`Logs/tile-preview/`, gitignored. Because it compiles the shipping sources, drift breaks the build
rather than producing a lying preview. It earned its place: a rubble field a full device build
reported as fine was shown here to be a checkerboard of hard-edged squares.

### Two defects that each hid inside their own fix

Worth recording as a pattern, not as trivia. **Both passed compile, validator and e2e. Both were
found by counting, not by reading.**

1. The first ruin tile shaded the whole 32px tile before drawing stones, so every cell was a
   uniformly darker square: a checkerboard. The cure that works is letting blocks **cross the tile
   edge** — a stone drawn wholly inside its own cell leaves the lattice readable however it is
   shaded. `FallenBlock` coordinates are in tile space and wrap (`TileArt.cs:491-495`).
2. The variant picker was `Mathf.Abs(x * 40503461 ^ y * 12582917) % 12`. Both multipliers are 1 mod
   4 and 12 divides by 4, so `variant % 4` was exactly `((x ^ y) & 3)` for every cell on the map:
   twelve variants collapsed into four classes on a 4×4 lattice, **by the code choosing the
   anti-lattice tile.** Fixed with a bit-mixing finalizer before the modulo
   (`TilemapBuilder.cs:605`); `afec50e` records the twelve now spreading 77 to 122 across the
   map.

## What is not finished

This is the section that matters. It is ordered by what would embarrass the product first.

### Two items this section used to open with are settled

The engagement meter's 62 baseline and the 50-of-100 caps were fixed in `fece0c5` (caps 24 / 24 /
16 / 12 / 16 / 8, no baseline), and its days signal now reads the season's length instead of a
literal 2. The `deep_read` on a chapter that fits one screen was settled on 2026-09-03 as a dwell
that grows with the text — `ChapterReaderUI.DeepReadSecondsFor`, 1.5 s a verse with the old 20 s as
a floor — and in the same pass every door the game draws (A Página, a quotation's "Saber mais", the
gate's record, the ending's invitation) opens the reader as *the game asking*, so `unprompted_read`
counts only the profile's study card; before that every caller passed false and the event was a
copy of `chapter_opened`. Both are written up as taken-and-revisable in `AGENTS.md`.

### The `e2e` palette assertion does not reach a single world tile

`CountOffPaletteMapSprites` (`E2ERunner.cs:1685`) walks UI `Image` components and filters them
through `IsMapSpriteName` (`:1704`), which matches only `map_progress_*`, `map_node_*` and
`map_reward_*` — the **progression map**. World tiles are `SpriteRenderer`s and never appear in that
enumeration. **Any belief that the end-to-end run palette-checks generated world sprites is wrong.**
What covers them is `tools/tile-preview.sh check`, which asserts every pixel is in the world palette
and opaque, and which nothing runs automatically. If world-tile palette drift matters, that check
needs a caller.

### The landscape skirt has been on a screen — since 03/09/2026

`TilemapBuilder` paints a skirt of terrain past the map on all four sides, sized by
`_skirtColumns` / `_skirtRows` against `MaxCoveredAspect = 2f`. For a while the only thing that
said it worked was arithmetic — and arithmetic is what the previous, broken version also had. On
03/09/2026 the macOS player was launched with the e2e's own arguments at 1920×1080 and looked at:
the skirt covers the patrol view with no clear-colour edge. What landscape did break was the UI —
character creation and the backpack — and `087f000` made the interface fit the window it is given
(390 e2e steps and 0 failures landscape, portrait unchanged). `tools/e2e.sh` still runs portrait
only, so this is checked by hand, the same way, whenever the skirt or the camera changes.

### The backpack's list density is asserted by two comments that disagree

The stage above the wardrobe list is `StageHeight = DesignTokens.Px(124f)`
(`BackpackPanel.cs:382`), plus `Space.S16` before the content band (`:438`). It went horizontal to
buy rows, and **two comments in the same file give different results for that trade**:

- `BackpackPanel.cs:903` — "A vertical stage … wanted 190 design points and gave the sheet 1.7 rows;
  a 100-point figure with a 201-point text column beside it wants 124 and gives it **2.7**."
- `CharacterCreationScreen.cs:472` — "The backpack fought its way from 1.7 to **2.10** by turning
  its stage sideways."

Same start, two finishes. One of them is stale and neither is checked by anything: **the backpack
has no equivalent of `MinimumVisibleRows`** — no floor, no error, no assertion that the sheet shows
more than one row at the tallest row in the running locale. Creation's guard fires on a device; the
backpack's does not exist.

Where the room would come from, if a measurement says a row is missing: the stage's text column
reserves `RefusalHeight = 3 × BodyLineHeight` (`BackpackPanel.cs:586`, `:578-585` for the reserve rule) whether or not there is a refusal to
show, and it is empty in every session where nothing was refused, above a one-line player name. That
is the only band on the sheet that is structurally idle. **A claim circulated that the card spends
~135 of a ~382-point content band and is ~60% empty; that measurement is not in the code and could
not be reproduced from it, so it is recorded here as unverified rather than repeated as fact.**
Measure it on a 402×874 phone with `tools/ios-sim.sh` before spending anything on it.

### The season has never been played on a phone past its third stage

On an iPhone 17 Pro simulator the first three stages behave end to end: the opening, creation, the
village, rubble, the wall, the mat, the split, the night, the morning report, the quiz, and on stage
2 Baruque, Zacur, the well (`verse_shown` confirms `JHN.21.6`) and the whole invitation branch. The
old day-3 payoff was played there too — but in the **three-day numbering**, where it was the third
stage. The same beats now sit at stages 6 and 9, behind five stages of content no hand has touched
on a device.

**The invitation chain is the one worth knowing held**, because it is the path nothing else covers:
accepting spent the day to `0/12` (twelve then; the day is four courses now), the village went to dusk and the split correctly did **not** open;
twelve idle seconds did not shake it loose; relaunching mid-beat put the hold back (`NpcActor.Start`
re-deriving it); Malquias's return line released it and the split opened by itself, offering no way
back because capacity was zero.

**What the season still has to prove on a phone:** stages 4 through 9 — the rest and its gathering,
Sambalate and Tobias speaking, the `raid` contest with A Página at turn 2 unlocking *Metade e
metade*, the `letters` contest and its `refused_invite` move, the wall finishing, the gate closing
with the player's name, both "Saber mais" doors (`NEH.4` and `NEH.12` — this is the `deep_read` path
and the reason this build exists) and the vocation reveal. Only the **terminal** stage holds its
evening open: every other stage, contest or not, ends through the ordinary split-panel path, so a
split that fails to appear on stage 6 or 8 is now the bug.

The backpack and character creation *have* been driven on a 402×874 phone — that is where the
one-row content band and the −13.38 text column were found — so the screens are ahead of the season
here, not behind it.

`tools/e2e.sh --from-stage <n>` seeds a save at a stage and starts there — **authoring only**, and
the fastest way to put a late stage in front of a human without playing the eight before it.
`tools/ios-sim.sh run` resumes whatever is parked on the simulator; `tools/ios-sim.sh reset` throws
it away and starts clean.

### `tools/e2e.sh` reaches the end of the season, but not through the world

It plays every stage the table declares, both contests, the Page, the reader and the reveal —
asserting hard on three stages it picks out of the table (the first, the one that turns chapter and
verse on, and the one that ends the season) and traversing the rest cheaply. What it deliberately
does *not* drive: it spends a day through `ResourceSystem` rather than walking the player to the
wall, so **the interaction layer between a tap on the ground and a stone on the wall is still only
covered by playing it.**

### Text work that no script can close

- **The curation read is done, in both locales.** Twelve nodes carry canonical speech; all twelve
  were read against the passage in two passes (`6d9d0ac`, then `079aac4`, each touching `en` and
  `pt-BR` together), and `curated_in` on each node names the commit (`a943723` taught the queue to
  say so). `node tools/list-curation.mjs` reports nothing waiting. A translation of a canonical
  figure's speech is newly authored speech in that language, so the `en` side was read on its own
  terms, not carried by the `pt-BR`. **What remains is a rule, not a task:** a line edited after its
  `curated_in` commit has a stale read, and nothing flags it automatically.
- **The English text has had no native pass.** It reads correctly and keeps the register, but it was
  written by the same agent that wrote the code.

### Smaller, and honest about it

- **The framing of the opening has almost no horizontal margin.** `WorldMapOverlay` places the four
  closed cities at x −14, +16, −17, +15 (`WorldMapOverlay.cs:38-44`) and frames them at
  `FramingSize = 44f` (`:47`). At 19.5:9 portrait the camera half-width is 44 × 9/19.5 ≈ **20.3**;
  at the 1080×1920 the project nominally targets it is 44 × 1080/1920 = **24.75**. Nothing is
  clipped on the phones tested; there is simply no room left, and a squarer screen would eat into
  it.
- **The buildable wall is a straight run** along the north of a circular city. `WallSystem` places a
  segment by `grid_x` on one row. `NEH.3` assigns each group a stretch, so it reads correctly,
  but a true arc would need `WallSystem`, the contest and the patrol camera to change together.
- **The four skin tones and the build silhouettes are unjudged.** Nobody has looked at whether tones
  2 and 3 are distinguishable at 32×48, or whether the narrower build actually reads.
- **`WorldMapOverlay.Place.Caption` no longer exists** — earlier handoffs listed a never-drawn
  `"fechada"` caption field as open work. `Place` now carries only `Offset` and `Size`
  (`WorldMapOverlay.cs:30-33`), and the overlay draws a plate on every place. Recorded so the bullet
  is not resurrected.

## Driving the village by tapping is slow, and the reason is worth knowing before you start

There is no accessibility tree to query, and the camera clamps at the map edges, so "the player is
at the centre of the screen" is false near an edge. Three things make it tractable:

1. `save.json` carries `playerCellX/Y`, but **only updates on a save event** — rubble, the well, the
   wall, or an `NpcActor` conversation. The gathering crowd (`StandingNpc`) answers with `multidao`
   lines and does **not** save, so a crowd line is not a position fix.
2. **Relaunching pins the player to a known cell.** `BuildPlayer` restores from `playerCellX/Y` and
   `tools/ios-sim.sh run` reinstalls without clearing the save. It is the cheap way out of "I do not
   know where I am", and it exercises the scene-rebuild restore on the way past.
3. **The crowd looks like residents and is not.** The six real ones stand at the `spawn` cells in
   `npcs.json`; the crowd stands around the plaza.

In the close view one cell is about **58 device points** (`CameraRig.CloseSize` 7.5, at
`CameraRig.cs:31`, gives 15 cells over 874 points). Fix the cell from a save, then compute — do not
eyeball it.

**Tapping the Simulator goes through idb**, which takes neither the pointer nor the focus, so the
window can stay hidden for a whole session. The two dead ends that cost this project a session each
— `osascript ... click at`, which reports success and does nothing, and anything that moves the
physical cursor — plus how to read the screen and why there is no finding a control by name, are
documented once in [`development-guidelines.md`](development-guidelines.md) §3. Read that before
driving a phone. Coordinates are device points, origin top-left; an iPhone 17 Pro is 402×874.

## What the localisation pass found, and what it did not fix

The e2e run was written as part of that pass and immediately found things nothing else could:

- **Four preview captions were still Portuguese in the English build.** `DirectionCaptions` was an
  array of string literals, so the validator's check on call arguments could not see it. The
  validator now also checks fields whose *name* says they hold player words, which is the shape that
  escaped.
- **The language chips rendered their labels stacked vertically**, because the chip was narrower
  than its own two-letter label once `CreateButton`'s 18px insets were taken off.
- **Every slot label on the character creation screen was clipped** by the chip row running up
  underneath it — 40 + 96 in a row 118 tall. Pre-existing in both languages and worse in English,
  where the words are longer. Fixed by shortening the chips to 74.

And one that nothing else could have found, because it is the feature the whole pass was named
after: **the language toggle changed the label and not the language.** `SwitchLocale` set
`Locales.Active`, persisted it and reloaded the scene — and reloading the scene is not what changes
the language. Only the Boot scene runs `BootSequence.Run`; `GameBootstrap` just calls
`GameScene.Compose()`. `Loc`, `GameData` and `ScriptureService` are statics that outlive a scene
load, so the village rebuilt in the old words while the toggle lit up the new language, and the
persisted preference meant the *next* launch was the one that actually switched. Correct code
(`ApplyLocale`) that nothing on that path called — the same shape as `MoraleContest.Begin()`.
`SwitchLocale` now calls it, and the e2e run taps a chip and reads a label back to prove it.

## Things that cost real time to learn

**`Application.persistentDataPath` differs between the editor and a player build.** The editor uses
company/product, a player build uses the bundle identifier. Clearing one leaves the other's save
behind and the game resumes a run that looked deleted. Boot logs both paths for this reason — read
`[Boot] Save ->` rather than guessing.

**A simulator build defaults to x86_64, and the failure names baselib rather than architecture.**
`PlayerSettings.iOS.sdkVersion = SimulatorSDK` is only half of it: `simulatorSdkArchitecture`
defaults to `X86_64`, which exports the x86_64 baselib. Linking that against an arm64 simulator
slice on an Apple Silicon machine fails with a page of undefined `il2cpp_baselib::` symbols and no
mention of architecture anywhere. `BuildScript.BuildIOSSimulator` sets both and restores both.
`lipo -info Builds/ios-sim/Libraries/baselib.a` is the one-line check that the export is right —
worth running before spending ten minutes on an Xcode compile that cannot link.

**The device export and the simulator export cannot share a directory.** Their `Libraries/` are
built for different SDKs, so pointing `xcodebuild -sdk iphonesimulator` at `Builds/ios` fails no
matter what is overridden on the command line. Hence `Builds/ios-sim`.

**A 403 from YouVersion means the version is not enabled, not that it will clear.** NVI returned
`Access denied` with the licence showing as accepted on the account; it started serving only after
NVI was explicitly enabled for the app in the developer portal. Do not sit and retry.

**The version listing under-reports.** `GET /bibles?language_ranges[]=pt` still returns only BLT
even while NVI serves fine. Probe entitlement by direct passage access; never gate on the listing.

**`/passages/{id}` is the only endpoint that returns text.** `/chapters/{n}/verses` gives structure
with no words. A whole chapter comes back as one unnumbered blob, so the reader fetches verse by
verse to keep numbering.

**The acceptance harness writes through the real `SaveSystem`.** It captures and restores any
existing save now, but it did destroy a live playtest before that was added. Do not remove the
`finally` block in `SaveRoundTrip`.

**So does the e2e runner, which is why `-data-path` exists.** `AppPaths.DataRoot` redirects the save
and the telemetry somewhere disposable, and `tools/e2e.sh` always passes it. Never launch a player
with `-e2e` and no `-data-path`.

**`PlayerPrefs` is not covered by `-data-path`.** The language choice is stored there, deliberately —
it has to be resolvable before a `GameState` exists, and deleting a run is not a request to be
spoken to in another language. But it means an automated run that taps the toggle would change what
the person at that machine gets on their next launch. `E2ERunner` sets `Locales.SuppressPersistence`
before anything can switch.

**A macOS player stops rendering when its window loses focus, *and again* when it is fully
occluded, and these are two different problems.** `Application.runInBackground` fixes the first and
does nothing about the second — see the serial-e2e note above. Both read as a hang.

**`Canvas.enabled` is not `GameObject.activeInHierarchy`.** `HUD.SetVisible` disables the canvas and
leaves the object active, so an e2e step that waited for the HUD *object* passed instantly, during
the cutscene, and screenshotted a black fade. Wait for what is on screen, not for what is in the
hierarchy.

**The Unity Hub deadlocks on its own database.** An install that "Completed with errors" with
LevelDB lock errors in the log usually just needs the stale Hub process killed and a retry. The
first Android attempt failed this way and the second filled the disk.

**`android-open-jdk` does not resolve in Unity 6.** The id is `android-open-jdk-17.0.18+8`.

## Traps specific to how this project is built

**Scenes are near-empty and everything is constructed at runtime.** That moves whole classes of
error into the compiler, which is why it was chosen — but it also means the compiler cannot catch
anything about *layout*. Bugs so far that were invisible-not-broken: character creation drawn at
sorting order 0 underneath an opaque fade at 400; four `SpriteRenderer`s stacked on one GameObject
where `[DisallowMultipleComponent]` silently dropped three of them; and, most recently, a text
column of −13.38 that produced words breaking mid-letter rather than an exception. None of them
logged anything.

**A number can be valid and mean the wrong thing, and only counting finds it.** The tile checkerboard
and the twelve-variants-that-were-four picker both compiled, validated and passed e2e. So did a
progress meter whose six caps sum to half its ceiling. **Count the distribution, do not read the
code.** `tools/tile-preview.sh field` exists for exactly this on the art side; the arithmetic side
has no equivalent and is where `EngagementMeter` was found.

**Parallel agents produce good modules and miss the seams.** Two verification rounds found 33
defects, 5 of them blockers, and nearly all shared one shape: correct code that nothing ever called.
`MoraleContest.Begin()` and `VocationTracker.Resolve()` had no runtime caller at all, so day 3 was
unreachable in a built game while every unit-level rule about it passed. The same shape produced a
wardrobe tab off-by-one that hid inside `RecordOnlyBackpackTab` scanning four of five panels, so a
leaked `TabProfile` drawn over another panel passed "exactly one panel is on screen" in silence.
**Verify reachability, not just correctness.**

**A control built this frame raycasts to nothing.** Until a canvas has been through one batch, every
graphic on it reports `depth == -1` and `GraphicRaycaster` skips it outright — so a live, correctly
placed, fully interactable button is hit by no ray at all. `E2ERunner.TapObject` judged on the first
frame and reported the settings panel's language chips as unreachable; it now retries until the ray
lands or the step times out. The distinction it must preserve: a control genuinely under an opaque
panel still fails, having spent the timeout proving it.

**The acceptance harness cannot prove a scene works.** It constructs systems directly and never runs
`Compose()`. It is a good gate on rules; it is not evidence that anything is wired.

**EditMode cannot pump coroutines.** The contest turn loop and `DayCycle.EndDay` both defer to
coroutines, so the harness asserts their configuration and drives `DayCycle` through its synchronous
path by disabling the component. The live Page beat is still only verified by playing.

## Decisions taken here, with their reasons

- **Rule 4 was loosened** (see `AGENTS.md`). Canonical figures may speak authored dialogue that
  asserts nothing the passage does not. The argument that settled it: rule 4 as written forced
  verbatim quotation as the *only* way for such a figure to speak, which pushed the most
  Bible-looking artifact into the earliest minutes — backwards for an audience that has not opened a
  Bible. The safeguard moved from mechanical to human, so `tools/list-curation.mjs` exists.
- **References are withheld until A Página**, never removed. Printing the citation and offering the
  chapter were one condition in `DialogueUI`; they are now two, because hiding the reference would
  otherwise have hidden the only entry to the reader — and that tap is `deep_read`.
- **`NEH.3` is packaged deliberately, not by accident.** Six residents take their names from it
  through `npcs.json` `source_ref`, the gate panel's record plate is `NEH.3`, and
  `ScriptureService.ChapterDisplay("NEH.3")` can only print "Neemias 3" instead of a raw database
  key because the chapter is packaged. Stage 4 now cites five of its verses outright, so the
  coupling is stated rather than inherited (`tools/verses.manifest.json`, `_chapters_note`).
- **`verses.json` ships NVI**, which is all-rights-reserved. The repo is **private for this reason**.
  Public would redistribute copyrighted scripture outside the app. Regenerate against BLT
  (`--version-id 3254`, CC BY-SA) before making it public.
- **The crowd does not block movement.** A dozen solid bodies in the centre of a circular village
  turns the route to the wall into a maze.
- **The name in character creation is optional.** Gating the game on a form field is the opposite of
  "sem cadastro".
- **Nothing is pre-selected on step 1 of creation.** `AskBeforeChoosing` asserts the screen does not
  advance, and a default selection would advance and fail it. `QuickStart` is the honest form of a
  default: a button that dresses you and moves on, rather than a decision made quietly on your
  behalf.
- **Conversation is free, and must stay free.** The obvious way to make time pass is to charge for
  actions generally. Dialogue is where every citation lives and what `HasSpokenWithEveryNpc` scores,
  so a talk tax would put a toll on the exact behaviour the product measures.
- **Resting is optional, never a chore.** The mat exists for the player who is finished before their
  capacity is, the player who decided to build nothing today, and the player past a held beat. A day
  always ends without it, which is what stops it becoming the button that was just removed.

## If picking this up cold

0. [`docs/development-guidelines.md`](development-guidelines.md) — how code is written here.
1. `tools/unity-check.sh` — compiles, reports C# errors.
2. `node tools/validate-content.mjs` — scripture integrity, the forbidden-word checklist per
   language, locale parity, and hardcoded player strings.
3. `tools/acceptance.sh` — asserts the rules from MVP-SCOPE.md §13, once per language.
4. `tools/e2e.sh` — builds a player and plays the whole season in every language, screenshots in
   `Builds/e2e/`. Serial by default; `--parallel` is the mode that hangs. **Read the screenshots.**
   A green exit code means nothing was uncovered and no string was missing; it does not mean the
   screen looks right.
5. `node tools/list-curation.mjs` — authored canonical speech awaiting a human read, every language.
6. `tools/tile-preview.sh sheet` — see every procedural tile in seconds, without Unity.
   `field 0.45` is how you tell scattered stone from wallpaper; `check` asserts the palette.
7. `tools/ios-sim.sh` — build, boot and *play* the player on an iPhone simulator. `setup` once per
   machine, then `tap X Y` in device points and `shot` to look. It drives through idb, so it never
   takes the pointer or the focus and the window can stay hidden — which is the reason it is the
   standard here and hand-rolled clicking is not. See `docs/development-guidelines.md` §3.

The API key lives in `.env.local`, which is git-ignored and has never been committed.
