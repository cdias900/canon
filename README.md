# Porta das Ovelhas

POC for **Cinquenta e Dois Dias** — a turn-based building-and-defence game set in the book of
Nehemiah. Three days of play, one gate, Unity 6.

The POC exists to answer one question: **does the player open the chapter of their own accord?**
Everything else is a means. The event that answers it is `deep_read`.

Product context and the non-negotiable rules live in [`AGENTS.md`](AGENTS.md). The implementation
spec is [`POC-IMPLEMENTATION.md`](POC-IMPLEMENTATION.md).

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
tools/acceptance.sh              # assert the acceptance criteria headlessly
```

Build and run a player:

```bash
"/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -quit -nographics -projectPath . \
  -executeMethod SheepGate.EditorTools.BuildScript.BuildMac
open Builds/mac/SheepGate.app
```

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

## The scripture pipeline

The model never writes scripture. It chooses a **reference**; the literal text is resolved from
that reference at build time. This removes hallucinated verses by construction rather than by
prompting, and it is checkable with `grep`.

```bash
echo "YOUVERSION_API_KEY=..." > .env.local     # git-ignored
node tools/fetch-verses.mjs                    # writes Assets/Resources/Data/verses.json
node tools/validate-content.mjs                # deterministic layer-1 validator
```

`Assets/Resources/Data/verses.json` is **generated — never edit it by hand.** The set of
references lives in `tools/verses.manifest.json`, and the translation is one line there. See
[`docs/youversion-api.md`](docs/youversion-api.md) for the verified endpoint surface and the
licence obligations that come with the current translation.

The validator fails the build when a reference is missing or empty, when the corpus is still a
placeholder, when any authored string contains a run of 8+ words that also appears in the
scripture text (accidental paraphrase), or when player-facing text uses a term from the
forbidden-vocabulary checklist.

## How this project is built

Two conventions are unusual enough to be worth stating up front.

**Scenes are near-empty on purpose.** Each `.unity` file holds a camera and a single bootstrap
behaviour; every GameObject, sprite and UI element is constructed at runtime from C#. Hand-written
scene YAML with GUID cross-references is the easiest thing to get silently wrong, and the compiler
cannot check it. Runtime construction moves that whole class of error into code.

**There are no image assets.** All art is generated procedurally in `SheepGate.Art` from a
three-colour palette. `ArtLibrary.Get(key)` is the only way to obtain a sprite, so replacing the
placeholder art later means implementing that one seam.

[`docs/architecture-contract.md`](docs/architecture-contract.md) is the frozen interface every
module was built against — read it before changing a public signature.

## Layout

| Path | What it is |
|---|---|
| `Assets/Scripts/Core/` | State, save, telemetry, data loading, service locator |
| `Assets/Scripts/Scripture/` | Verse resolution and the chapter reader |
| `Assets/Scripts/World/` | Scene composition, wall, day cycle, camera, interactables |
| `Assets/Scripts/Contest/` | The morale trial, and the Page |
| `Assets/Scripts/Vocation/` | Silent scoring and the reveal |
| `Assets/Resources/Data/` | All game content as JSON, editable outside Unity |
| `tools/` | Scripture pipeline, validator, compile and acceptance scripts |

## Things that will bite you

- `VocationTracker` deliberately has **no way to read a score.** That is a product rule, not an
  oversight: showing progress turns discovery into a checklist. Do not add a getter.
- Completed wall stages must never regress. Damage clears in-progress work only.
- There is no game-over screen, no health bar, and no death counter anywhere, by design.
- Never send the reading out of the app. An external link destroys the measurement the whole
  product exists to take.
