# A Cidade Quebrada

A turn-based building-and-defence game set in the book of Nehemiah. A nine-stage season, one gate,
Unity 6.

Names, in three layers: **A Cidade Quebrada** is the game, **Cinquenta e Dois Dias** is the season,
and **Porta das Ovelhas** is this chapter — `NEH.3.1`, the gate the player raises. `SheepGate` is
that gate in English, and it is the namespace for all the code.

The build exists to answer one question: **does the player open the chapter of their own accord?**
Everything else is a means. The event that answers it is `deep_read`.

Product context and the non-negotiable rules live in [`AGENTS.md`](AGENTS.md). The scope being
executed is [`MVP-SCOPE.md`](MVP-SCOPE.md). **Before writing code, read
[`docs/development-guidelines.md`](docs/development-guidelines.md)** — how this codebase is written,
where player-facing text is allowed to live, and what "tested" means here.

**Picking this up fresh? Read [`docs/handoff.md`](docs/handoff.md) first** — it covers what is
finished, what is not, and the things that cost real time to learn the hard way.

## Requirements

- **Unity 6000.3.23f1** (changeset `09d2ecc7fb28`)
- **Node 20+** for the scripture pipeline
- A YouVersion Platform app key with the chosen Bible version enabled

Install the editor headlessly:

```bash
brew install --cask unity-hub
"/Applications/Unity Hub.app/Contents/MacOS/Unity Hub" -- --headless install \
  --version 6000.3.23f1 --changeset 09d2ecc7fb28 --architecture arm64
```

## Running it

```bash
tools/unity-check.sh --open      # open the project in the editor, then press Play
tools/unity-check.sh             # headless compile, reports C# errors
node tools/validate-content.mjs  # scripture integrity, locale parity, hardcoded strings
tools/acceptance.sh              # assert the acceptance criteria, once per language
tools/e2e.sh                     # build a player and play the whole season, every language
tools/tile-preview.sh sheet      # every generated tile as a PNG in seconds, without a build
```

`tools/e2e.sh` is the one that runs a real build. It launches one player per locale and plays the
season the way `stages.json` declares it, from a cold save to the terminal stage — the opening,
character creation, the split, the night, the three contests, A Página, the reader, the backpack, the
progression map and the vocation reveal — driving it through the EventSystem, refusing to click a
control that something else is covering. It screenshots into `Builds/e2e/` and fails on an
unresolved string or any error in the log. The compile, the validator and the acceptance run are
necessary and not sufficient: nothing
before it composes a scene. **Read the screenshots** — a green exit code means nothing was missing,
not that the screen looks right.

Run it in one language only with `tools/e2e.sh --locale en`, or against the player already built
with `tools/e2e.sh --no-build`.

**The locales run one at a time, and that is the fix for a hang, not a preference.** Every player is
launched windowed at the same size and centred, so a second one covers the first completely; macOS
suspends rendering for a fully occluded window, and every step of the runner is a
`yield return null` that only resumes on the next frame. The run does not fail, it stops — no
exception, nothing in the log, until the watchdog kills it. `--parallel` restores the concurrent
run and the shorter wall clock; it is opt-in because it is the mode that hangs.

`tools/tile-preview.sh` is the art loop rather than a fifth gate. Every tile in this game is drawn
in C# at runtime, so seeing one used to cost a full export, Xcode compile, install and screenshot —
minutes per glance, for art you change ten times in a row. It compiles the shipping
`ArtPalette`, `PixelCanvas`, `ValueNoise` and `TileArt` — those files, not copies — against a stub
`UnityEngine`, using the Roslyn inside the Unity install, and writes a PNG to `Logs/tile-preview/`
in seconds. Because it compiles the shipping sources, drift breaks the build instead of producing a
preview that lies.

```bash
tools/tile-preview.sh sheet      # every tile side by side
tools/tile-preview.sh zoom       # the same at 5x, for judging pixels
tools/tile-preview.sh field 0.45 # a field of ruin tiles at that density, 0 to 1
tools/tile-preview.sh check      # every pixel in the world palette, and opaque
```

`field` is the mode that earns the tool: a rubble field a full device build reported as fine was
shown here to be a checkerboard of hard-edged squares. `check` is the only thing that palette-checks
a generated world tile at all — the e2e palette assertion walks UI sprites named `map_progress_*`,
`map_node_*` and `map_reward_*`, and reaches no world tile. Do not read it as covering them.

Build and run a player:

```bash
"/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -quit -nographics -projectPath . \
  -executeMethod SheepGate.EditorTools.BuildScript.BuildMac
open Builds/mac/SheepGate.app
```

Run it on an iPhone simulator:

```bash
tools/ios-sim.sh                 # export, compile with Xcode, install, launch
tools/ios-sim.sh shot opening    # write Logs/opening.png from the running simulator
tools/ios-sim.sh reset           # uninstall, which clears the save and the telemetry
```

The Xcode step compiles IL2CPP and takes several minutes. Requires Xcode and an iOS simulator
runtime; no signing team, because a simulator build needs none. `--device "iPhone 17 Pro Max"`
picks another simulator. The console, including `[Boot] Save ->`, lands in
`Logs/ios-sim-console.log`.

Play it without giving up your mouse:

```bash
tools/ios-sim.sh setup           # once per machine: idb-companion and the fb-idb client
tools/ios-sim.sh tap 200 800     # device points, origin top-left; iPhone 17 Pro is 402x874
tools/ios-sim.sh swipe 200 700 200 200
tools/ios-sim.sh text "Pedro"
```

Input goes through **idb**, which injects the way a real device does. The pointer does not move,
focus stays wherever you left it, and the Simulator window can stay hidden behind your editor for
the whole session — so a play-through can run while you keep working. `simctl` has no tap of its
own, and `osascript ... click at` is a trap: it reports success and does nothing, because the
Simulator's Metal view ignores synthetic accessibility clicks.

There is no tapping a button by name. Unity publishes no accessibility tree, so `idb ui
describe-all` sees one node for the whole app: tap a point, screenshot, look. Assertions about the
UI hierarchy belong in `tools/e2e.sh`, which drives the real EventSystem from inside the build.

`Builds/ios` (device) and `Builds/ios-sim` (simulator) are separate exports on purpose: the two
SDKs produce libraries that cannot be linked against each other, so one directory cannot serve
both.

Reset a playtest — the save is meant to survive restarts, so clear it deliberately.

**From inside the game:** Ajustes → *Começar de novo*, which asks before it deletes and then reboots
into a fresh run. That is the route a playtester has; the paths below are the ones you need when the
game will not start, or when you want the device to forget it ever ran.

```bash
rm -rf ~/Library/Application\ Support/com.Create-Hack.A-Cidade-Quebrada   # player build
rm -rf ~/Library/Application\ Support/Create\ Hack                        # editor Play mode
```

Deleting the save does **not** clear PlayerPrefs, and two preferences live there rather than in the
run: the sound switches, reduced motion, and whether the opening has been watched to the end on this
device. The last of those is what puts a skip on the opening from the second run onwards — so a
fresh save still skips, which is what a playtest wants, and a genuinely clean device does not.

**Both paths, because `Application.persistentDataPath` is not the same in the two.** A player
build resolves it from the bundle identifier; the editor resolves it from company and product
name. Clearing only one leaves the other's save behind, and the game resumes a run you thought
you had deleted.

**Do not trust the paths above — read `[Boot] Save ->` in the log.** Both are derived from
`productName`, so they move whenever the product is renamed, and the macOS player has no explicit
bundle identifier of its own (iOS and Android do: `com.createhack.portadasovelhas`). Boot logs the
resolved paths for exactly this reason.

Telemetry, including `deep_read` and `unprompted_read`, is appended to `telemetry.jsonl`
alongside the save, in whichever of those two roots the run actually used.

## Languages

The game ships in **pt-BR and English**. pt-BR is the authoring locale; everything else is a
translation of it. The language is taken from the system on first run, remembered after that, and
changeable from the toggle on the character-creation screen and in the HUD. `-locale en` on the
command line overrides both, which is how the e2e run pins a language.

Structure and numbers have exactly one copy, shared by every language; only words are duplicated.
`docs/development-guidelines.md` has the full layout and how to add a language.

## The scripture pipeline

The model never writes scripture. It chooses a **reference**; the literal text is resolved from
that reference at build time. This removes hallucinated verses by construction rather than by
prompting, and it is checkable with `grep`.

```bash
echo "YOUVERSION_API_KEY=..." > .env.local     # git-ignored
node tools/fetch-verses.mjs                    # every locale, into locales/<locale>/verses.json
node tools/fetch-verses.mjs --locale en        # just one
node tools/validate-content.mjs                # deterministic layer-1 validator
```

`Assets/Resources/Data/locales/<locale>/verses.json` is **generated — never edit it by hand.** The
set of references lives once in `tools/verses.manifest.json` and is language independent; each
locale names its own translation there. pt-BR resolves against NVI, English against the World
English Bible, which is public domain. See
[`docs/youversion-api.md`](docs/youversion-api.md) for the verified endpoint surface and the
licence obligations that come with the current translation.

The validator fails the build when a reference is missing or empty, when the corpus is still a
placeholder, when any authored string contains a run of 8+ words that also appears in the
scripture text (accidental paraphrase), when player-facing text uses a term from that language's
forbidden-vocabulary checklist, when a locale is missing a string or disagrees with the authoring
locale about anything that is not words, or when a C# file hardcodes a string a player can read.

## How this project is built

Two conventions are unusual enough to be worth stating up front.

**Scenes are near-empty on purpose.** Each `.unity` file holds a camera and a single bootstrap
behaviour; every GameObject, sprite and UI element is constructed at runtime from C#. Hand-written
scene YAML with GUID cross-references is the easiest thing to get silently wrong, and the compiler
cannot check it. Runtime construction moves that whole class of error into code.

**Art comes through one seam.** `ArtLibrary.Get(key)` is the only way to obtain a sprite. Almost
every key is generated procedurally in `SheepGate.Art`, out of the three base colour families and
the neutral ramp `ArtPalette` declares once and nothing else may add to. Exactly one key,
`tile_water`, comes from a drawn CC0 sheet today — which is what §11 of the implementation spec
asked for and what the seam existed to allow. A key with no drawn tile behind it falls through to
the generated one, so the swap happens a key at a time and the game runs with the sheet missing.

Ground and rubble were tried on the sheet and deliberately left generated: tiled five by five and
judged as a field, which is the only honest way to look at a fill, its seamless fills are pale
interior floors and its textured ones are autotile edges that show a seam every tile.
`TileArt.Ground` was tuned instead. `Tileset.cs` carries the map of which drawn tile stands behind
which key, and the reason next to every key deliberately not on it — read that before adding one.

The sheet is Kenney's Roguelike/RPG pack (CC0, licence in `Assets/Art/`). It ships as a `.bytes`
file and is decoded at runtime rather than imported as a Unity sprite: no import settings to get
wrong, no `.meta` to hand-write, and no slicing stored in an asset nobody can review in a diff.

[`docs/architecture-contract.md`](docs/architecture-contract.md) is the frozen interface every
module was built against — read it before changing a public signature.

## Layout

| Path | What it is |
|---|---|
| `Assets/Scripts/Core/` | State, save, telemetry, data loading, service locator |
| `Assets/Scripts/Core/Localization/` | `Locales` and `Loc` — which language is running, and every string in it |
| `Assets/Scripts/E2E/` | The autopilot that plays a built player and screenshots it |
| `Assets/Scripts/Art/` | Every sprite, generated from `ArtPalette`; `Tileset` holds the drawn keys |
| `Assets/Scripts/Scripture/` | Verse resolution and the chapter reader |
| `Assets/Scripts/World/` | Scene composition, wall, day cycle, camera, interactables |
| `Assets/Scripts/Contest/` | The morale trial, and the Page |
| `Assets/Scripts/Vocation/` | Silent scoring and the reveal |
| `Assets/Resources/Data/` | Structure and numbers — one copy, shared by every language |
| `Assets/Resources/Data/locales/` | Every string a player can read, one directory per language |
| `tools/` | Scripture pipeline, validator, compile and acceptance scripts |

## Things that will bite you

- `VocationTracker` deliberately has **no way to read a score.** That is a product rule, not an
  oversight: showing progress turns discovery into a checklist. Do not add a getter.
- Completed wall stages must never regress. Damage clears in-progress work only.
- **The outside of the map looks like terrain and is still not walkable.** Void cells draw the same
  ground the city stands on with fallen wall scattered over them, because the camera clamps to the
  drawn cells and flat near-black rectangles read as rendering holes. Appearance and walkability are
  separate questions about the same cell now. The old bug — the whole outer border walkable, the
  player strolling out of the village — was caught by eye when the outside looked wrong, and would
  not be today, so `E2ERunner.VerifyTheOutsideIsNotWalkable` gates it and asserts the map has an
  outside before asserting anything about it.
- There is no game-over screen, no health bar, and no death counter anywhere, by design.
- Never send the reading out of the app. An external link destroys the measurement the whole
  product exists to take.
- **No player-facing string may be written in C#.** It goes in `locales/*/ui.json` and is read with
  `Loc.T`. The validator fails the build otherwise, and it derives what counts as player-facing from
  the declarations, so forwarding a literal through a helper does not get around it.
- **GameObject names are test handles.** `tools/e2e.sh` finds controls by name. They are English and
  they do not change when the language does; renaming one breaks the test that proves the screen
  works.
