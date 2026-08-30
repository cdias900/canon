# Porta das Ovelhas

POC for **Cinquenta e Dois Dias** — a turn-based building-and-defence game set in the book of
Nehemiah. Three days of play, one gate, Unity 6.

The POC exists to answer one question: **does the player open the chapter of their own accord?**
Everything else is a means. The event that answers it is `deep_read`.

Product context and the non-negotiable rules live in [`AGENTS.md`](AGENTS.md). The implementation
spec is [`POC-IMPLEMENTATION.md`](POC-IMPLEMENTATION.md). **Before writing code, read
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
tools/e2e.sh                     # build a player and play the opening in every language
```

`tools/e2e.sh` is the one that runs a real build. It launches the player per locale, drives it
through the EventSystem — refusing to click a control that something else is covering — screenshots
three beats into `Builds/e2e/`, and fails on an unresolved string or any error in the log. The other
three are necessary and not sufficient: nothing before it composes a scene.

Run it in one language only with `tools/e2e.sh --locale en`, or against the player already built
with `tools/e2e.sh --no-build`.

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

`Builds/ios` (device) and `Builds/ios-sim` (simulator) are separate exports on purpose: the two
SDKs produce libraries that cannot be linked against each other, so one directory cannot serve
both.

Reset a playtest — the save is meant to survive restarts, so clear it deliberately:

```bash
rm -rf ~/Library/Application\ Support/com.Create-Hack.Porta-das-Ovelhas   # player build
rm -rf ~/Library/Application\ Support/Create\ Hack                        # editor Play mode
```

**Both paths, because `Application.persistentDataPath` is not the same in the two.** A player
build resolves it from the bundle identifier; the editor resolves it from company and product
name. Clearing only one leaves the other's save behind, and the game resumes a run you thought
you had deleted. Boot logs the resolved paths for exactly this reason — read
`[Boot] Save ->` in the log rather than guessing.

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

**Art comes through one seam.** `ArtLibrary.Get(key)` is the only way to obtain a sprite. Most keys
are still generated procedurally in `SheepGate.Art` from a three-colour palette; the ground, rubble
and water tiles now come from a drawn CC0 sheet instead, which is what §11 of the implementation
spec asked for and what that seam existed to allow. A key with no drawn tile behind it falls
through to the generated one, so the swap happens a key at a time and the game runs with the sheet
missing.

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
- There is no game-over screen, no health bar, and no death counter anywhere, by design.
- Never send the reading out of the app. An external link destroys the measurement the whole
  product exists to take.
- **No player-facing string may be written in C#.** It goes in `locales/*/ui.json` and is read with
  `Loc.T`. The validator fails the build otherwise, and it derives what counts as player-facing from
  the declarations, so forwarding a literal through a helper does not get around it.
- **GameObject names are test handles.** `tools/e2e.sh` finds controls by name. They are English and
  they do not change when the language does; renaming one breaks the test that proves the screen
  works.
