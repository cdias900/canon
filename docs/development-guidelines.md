# Development guidelines

How to write code in this repository. Product rules live in [`AGENTS.md`](../AGENTS.md) and are not
repeated here; this file is about the code itself.

Two rules carry the rest:

1. **Everything a developer reads is in English. Only what a player reads is translated.**
2. **A change is not done until a build of it has been played.**

---

## 1. English is the language of the code

Every artifact a developer reads is written in English, with no exceptions:

| | |
|---|---|
| Identifiers | types, methods, fields, locals, generics |
| Comments and XML docs | including the long explanatory ones this codebase favours |
| Log messages | `Debug.Log`, warnings, errors — these are diagnostics, never player text |
| JSON keys | `stage_cost`, `reveal_line`, `player_spawn` |
| File and directory names | `WallSystem.cs`, `wall_segments.json` |
| GameObject names | `"PatrolButton"`, `"HUDCanvas"` |
| Telemetry event and flag names | `deep_read`, `watch_posted_d1` |
| Branch names, commit messages, PR titles | `add-english-locale`, never `adiciona-idioma` |
| Engineering documentation | `README.md`, `docs/architecture-contract.md`, `docs/handoff.md`, `docs/youversion-api.md`, `tools/README.md` |

The reason is not preference. The team is Brazilian and most of this code is written by agents, so
the codebase is read far more often than it is written, by readers and tools that do not share a
first language. English identifiers keep one vocabulary across the code, the model, and every
library it calls.

**GameObject names are English on purpose, and that has become load-bearing.** `tools/e2e.sh` finds
controls by name — `QuickStart`, `HUDCanvas`, `Locale_en`. Those names are the only handles into a
UI that is entirely constructed at runtime, and they are stable precisely because they are not
translated. Renaming one breaks a test; translating one breaks it in exactly one language.

**Every document in this repository is English, product documents included** — `AGENTS.md`,
`MVP-SCOPE.md`, `docs/persona-and-purpose.md`, `docs/nehemiah-game-design.md`,
`docs/character-creation-scope.md`. This was not always the rule: product docs used to be written in
pt-BR on the grounds that they were the team thinking in the team's own language. That split cost
more than it bought. Half the repository is read by agents that also read the code, the line between
"what the game is" and "how the code works" was never as clean as it looked — `MVP-SCOPE.md` is both
— and a rule with an exception is a rule people have to remember.

One thing is deliberately **not** English, and it is about audience rather than preference: **the
content is authored in pt-BR.** It is the authoring locale — the game is written in Portuguese first
and translated outward. That is the only pt-BR that belongs in this repository.

### Commit messages and branch names

`git log` is read by everyone who ever touches this repository, and a branch name shows up in every
listing, so both follow the same rule as the code.

**Quoting pt-BR inside an English message is correct, not an exception.** A commit that explains why
a line of dialogue changed has to be able to name the line. What the rule asks is that the sentences
*around* the quotation are English:

```
Remove "Ele foi mais educado que você." from the refusal branch      yes
Corrige a fala do vizinho no dia 2                                   no
```

**None of this is enforced by a script**, deliberately. There is no commit hook and no CI check on
language: it is a reading rule, and it holds because whoever writes — person or agent — has read
this file. Catching it in review is enough, and a checker that judges prose would spend more of
everyone's attention on false positives than the rule costs to follow.

## 2. Player-facing text lives in one place

**No string a player can read may appear in a `.cs` file.** Not a label, not a button, not a
placeholder, not an error shown on screen. Every one of them lives under
`Assets/Resources/Data/locales/<locale>/` and is reached through `Loc`:

```csharp
UIKit.CreateButton(root, "EndDay", Loc.T("hud.end_day"), ...);   // yes
UIKit.CreateButton(root, "EndDay", "Fim do dia", ...);           // no — fails the validator
```

This is what makes adding a language a content change rather than a code change. It is enforced,
not trusted: `tools/validate-content.mjs` fails the build on a literal reaching a player-facing
sink, and it derives those sinks from the method declarations rather than from a list, so a string
forwarded through a helper is caught too.

### The shape of the content

```
Assets/Resources/Data/
  map.json            structure and numbers — ONE copy, shared by every language
  wall_segments.json
  npcs.json           ids, spawns, palettes
  contest.json        deltas, turn limit
  vocations.json      ids
  quiz.json           day, answer index
  locales/
    pt-BR/            every string a player can read, in the authoring language
      ui.json         key -> string, for everything written from C#
      dialogue.json   the whole conversation file: it is ~95% prose
      npcs.json       id -> display name
      contest.json    id -> { display, description }
      vocations.json  id -> { display, reveal_line }
      quiz.json       day -> { prompt, options, note }
      verses.json     GENERATED — never edit by hand
    en/               the same files, translated
```

**Numbers never live in a locale file.** A stage cost, a resolve delta and a turn limit exist once,
so two languages cannot disagree about balance. `GameData.LoadAll` merges the locale's strings onto
the same DTOs at load time, so nothing downstream knows a locale was involved.

Dialogue is the exception that proves the rule: it is copied whole per language, because splitting
prose from structure would make the file unreadable to the people who write it. The safety net is
`checkLocaleParity` in the validator, which compares every field that is *not* words — node ids,
line counts, verse references, choices, grants, flags — and fails on any disagreement. A translator
cannot change what the game does.

### Adding a string

1. Add the key to **every** `locales/*/ui.json`. Namespace it by screen: `hud.`, `end_day.`,
   `contest.`, `vocation.`.
2. Read it with `Loc.T("key")`, or `Loc.T("key", arg)` for a `{0}` placeholder.
3. For anything that counts, add `key.one` and `key.other` and call `Loc.Plural("key", count)`.
   The count is `{0}`.
4. Run `node tools/validate-content.mjs`.

There is **no runtime fallback between languages**. A missing key renders `⟨key⟩` on screen and logs
an error. That is deliberate: a silent fallback to Portuguese would hide a missing translation until
a player found it, and the whole point of the parity checks is that a build either has the strings
or fails.

### Adding a language

1. `Locales.Supported` in `Assets/Scripts/Core/Localization/Locales.cs`.
2. Copy `locales/pt-BR/` to `locales/<new>/` and translate everything but `verses.json`.
3. Add a version id to `tools/verses.manifest.json` under `versions`, then
   `node tools/fetch-verses.mjs --locale <new>`. **Probe entitlement by fetching a passage** — the
   YouVersion version listing under-reports and cannot be trusted (see `docs/youversion-api.md`).
   Prefer a public-domain translation: it is what lets a locale ship publicly.
4. Add a **curated** forbidden-term list for the language in `tools/validate-content.mjs`. Do not
   translate the existing one. The checklist targets a register, and a literal translation of the
   pt-BR list puts bare "purpose" on the English one, which fires on "picked on purpose" and teaches
   everyone to ignore the validator.
5. Run `node tools/list-curation.mjs`. A translation of a canonical figure's speech is newly
   authored speech in that language and needs its own read against the passage (rule 4).
6. Run the full check list below. The validator, the acceptance harness and the e2e run all
   discover locales from the filesystem, so all three cover the new language with no further edits.

## 3. Testing: build it and play it

**Every change is verified against a build, not against a compile.** In order, cheapest first:

```bash
tools/tile-preview.sh             # ART ONLY: draws the tiles to a PNG in seconds, without Unity
tools/unity-check.sh              # compiles; 0 errors AND 0 warnings is the bar
node tools/validate-content.mjs   # scripture integrity, locale parity, hardcoded strings
tools/acceptance.sh               # the product rules, asserted per locale
tools/e2e.sh                      # builds a player and plays the declared season, every language
tools/ios-sim.sh                  # the same build on a phone, driven by hand
```

`tools/e2e.sh` deliberately does not have a day count here. It reads
`Assets/Resources/Data/stages.json` and plays whatever the season declares; this line said "the
opening and a day" for a season that has been nine stages for a while, and a stale description is
worse than none, because it is what people read instead of the code.

### Seeing a tile without building the game

`tools/tile-preview.sh` sits first because it is by far the cheapest thing in this list — `check`
measured under a second on this machine, against a Unity batch-mode launch for anything below it —
and last in usefulness for everything that is not art. **It answers exactly one class of question:
what does the generated tile art actually look like.** It composes no scene, runs no game loop and
proves no rule, so it never substitutes for a gate below it. It is the inner loop that comes
*before* them.

**Why it exists.** Every tile in this game is drawn in C# at runtime, so the only way to see one
used to be a full build — Unity export, Xcode compile, install, launch, screenshot. Minutes per
glance, for art that gets changed ten times in a row, which in practice means the art stopped being
looked at.

**Why its output can be trusted.** It compiles `ArtPalette.cs`, `PixelCanvas.cs`, `ValueNoise.cs`,
`TileArt.cs` and `CharacterArt.cs` **from `Assets/`, not copies** (the `ART` list in
`tools/tile-preview.sh`), against a small `UnityEngine` stub, with the Roslyn that ships inside
Unity — found at either of the two places Unity 6000.3 and 6000.5 keep it. A preview harness with its own copy of
the drawing code drifts and then lies; this one breaks the build instead. It also never opens the
project — no `-projectPath`, only the bundled `dotnet` and `csc.dll` — so unlike `unity-check.sh` it
runs happily while the Editor holds the `Library` lock.

Modes: `sheet` (every tile side by side), `zoom` (5x, for judging pixels), `field <density>` (a
field of ruin tiles, which is how you tell scattered stone from wallpaper), `characters` (the eight
bodies — two builds, four skin tones — at 1x and at 4x, bare and dressed, on the game's own ground:
the sheet for the one judgement no gate makes) and `check` (asserts every pixel is in the world
palette and opaque). Output lands in `Logs/tile-preview/`, gitignored.

**What it caught, which is the whole argument.** A rubble field that a full device build reported
as fine was shown here to be a checkerboard of hard-edged squares — the tile shaded its whole 32px
square before drawing stones, so the lattice was readable however the stones fell. The cure is
letting blocks **cross the tile edge**, and that is a thing you can only see in a field, not in one
tile. The same harness then showed twelve variants collapsing into four classes on a 4x4 lattice,
because the variant picker multiplied by constants that are 1 mod 4 and took `% 12`; both bugs had
passed the compile, the validator and the e2e run. **Both were found by counting, not by reading.**

**`check` is also the only palette gate that reaches world tiles.** The e2e palette assertion walks
UI `Image` components and filters them by `IsMapSpriteName` — `map_progress_`, `map_node_`,
`map_reward_` (`Assets/Scripts/E2E/E2ERunner.cs:1704-1710`). No generated ground, wall or rubble
sprite matches those prefixes, so nothing in the world is palette-checked by e2e.

The compile, the validator and the acceptance run are necessary and not sufficient. This project has
learned three times that they are not enough:

- **Correct code that nothing calls.** `MoraleContest.Begin()` and `VocationTracker.Resolve()` once
  had no runtime caller at all, so day 3 was unreachable in a built game while every unit-level rule
  about it passed. *Verify reachability, not just correctness.*
- **Bugs that are invisible rather than broken.** Character creation drawn underneath an opaque
  fade; four `SpriteRenderer`s on one GameObject where `[DisallowMultipleComponent]` silently
  dropped three. Neither logged anything. Everything in this project is constructed at runtime, so
  the compiler cannot check a single thing about layout.
- **Merges that lose nothing and break everything.** Two branches both edited `CharacterArt.cs`: one
  replaced the accessory facing convention with an explicit four-way switch, the other added three
  new accessories written against the old `behind`/mirror convention. Different regions of the file,
  so git would have merged them without a single conflict marker, and every gate above would have
  gone green with six accessories drawn correctly and three silently back on the old logic. This one
  was caught before it landed, and only because someone compared the two *conventions* rather than
  the two diffs. *A merge that loses no lines can still lose the agreement the lines were written
  under.*

### When two branches touched the same thing

The check that catches the third bullet is not a diff. **A merge check that compares two sides
misses everything both sides touched** — verifying "no method still has the base version" proves
only that nobody was ignored, and says nothing about which of two edits to the same idea survived.
Base-vs-current is structurally blind here, so a second reviewer running the same comparison finds
the same nothing.

**Check a property of the output instead of the inputs.** An output property does not care which
side of a merge introduced it, which is precisely what makes it survive a merge that a diff cannot
read. The worked example, for character art:

> Render each sprite facing **Left** and facing **Right** and compare the two images. If they are
> pixel-identical, that sprite is still on the mirror convention, whoever wrote it and whichever
> branch it arrived on.

That is one line of intent and it is worth more than any number of careful diff reads, because it
is a fact about what the player sees.

**And write the agreement down where something reads it.** The same merge also carried an unlock
rescale that two sessions had agreed in writing and neither applied — `outfit_valley_mantle` was
unlocking a stage *before* the stage that hands it out, with every gate green. Nothing was
overruled; the change was simply agreed and never made. **A promise in a message is not a gate.** If
two people settle a number, a convention or an ordering, it belongs in a file that a script, a test
or this document can be pointed at — not only in the conversation that settled it.

`tools/e2e.sh` is the answer to both. It builds the real player, launches it per locale, and drives
it **through the EventSystem** — it raycasts at a control's own screen position and refuses to
dispatch a click unless that control is what the ray actually hits. Calling `Button.onClick`
directly would pass happily on a button buried under a full-screen fade, which is the bug it exists
to catch. It screenshots three beats, sweeps every `Text` on screen for unresolved `⟨key⟩` markers,
and fails on any error in the log.

Read the screenshots in `Builds/e2e/`. A passing exit code means nothing was covered and no string
was missing; it does not mean the screen looks right.

**An e2e run drives the real `SaveSystem`**, so it always passes `-data-path` at a disposable
directory. Never run the player with `-e2e` and no `-data-path`: it will write over a real playtest.
That has happened here before.

### On a phone

**iOS testing is `tools/ios-sim.sh` and nothing else.** `setup` once per machine, then `tap`,
`press`, `swipe`, `text` and `key` in **device points** (iPhone 17 Pro is 402x874, origin
top-left), with `shot` to see what happened.

Underneath it is **idb**, which injects through IndigoHID — the path a real device uses. That is
the whole reason it is the standard rather than one option among several: the pointer does not
move, focus stays where the user left it, and the Simulator window can be hidden behind the editor
for the entire session. A play-through costs the person at the keyboard nothing.

Two approaches are **not** to be used here, both of which have already cost this project a session:

- **`osascript -e 'tell application "System Events" to click at {x, y}'` reports success and does
  nothing.** The Simulator's Metal view ignores synthetic accessibility clicks. A run driven this
  way looks exactly like a game that has stopped responding, and the temptation is to go debugging
  the build.
- **Anything that moves the physical cursor** — a `CGEvent` posted to the HID tap, `cliclick` — does
  work, and takes the machine hostage while it runs. It also fails outright on a locked Mac: if a
  click answers with `window Login of application process loginwindow`, that is what has happened,
  and no synthetic input of any kind will land until someone unlocks it.

Read the screen with `xcrun simctl io booted screenshot`, which grabs the framebuffer and cannot be
fooled by window stacking. `screencapture -R` photographs whatever window is on top and has already
returned a picture of the terminal where the game should have been.

**There is no tapping a control by its name.** Unity draws into a single Metal view and publishes no
accessibility tree, so `idb ui describe-all` reports one node for the whole application. Tap a
point, screenshot, look. Anything that needs to assert on the UI hierarchy belongs in `tools/e2e.sh`
instead, which drives the real EventSystem from inside the build and can see it.

### The iteration loop

**Unity's Play Mode works here and is far faster than the export-compile-install loop.** Every gate
above goes through a build, which is right for a gate and wrong for the twentieth time you nudge a
number. Nothing about section 4's near-empty scenes stops the Editor from running the game: all
three are in `ProjectSettings/EditorBuildSettings.asset` and each carries its own entry point —
`Boot.unity` -> `BootLoader`, `CharacterCreation.unity` -> `CharacterCreationBootstrap`,
`Game.unity` -> `GameBootstrap`, all three in `Assets/Scripts/Boot/`, each one an `Awake` that
calls a composer. Opening `Game.unity` and pressing Play composes the game scene directly, with no
export and no device.

**Making Play near-instant is one setting, and it is the one place to be careful.**
`ProjectSettings/EditorSettings.asset:27-28` currently reads `m_EnterPlayModeOptionsEnabled: 1` with
`m_EnterPlayModeOptions: 0` — the feature is switched on, and **neither** domain reload nor scene
reload is disabled, so entering Play still costs a full domain reload. `3` disables both.

Do not reach for `3` first. **This codebase leans on static state, and disabling domain reload
carries all of it from one Play session into the next**, which is how a stale cache presents itself
as a new bug:

| Static | Where | What survives |
|---|---|---|
| `TilemapBuilder.Instance` | `Assets/Scripts/World/TilemapBuilder.cs:78` | **nothing** — its `OnDestroy` nulls it (`:215-221`). This is the one row that already resets itself, and it is the shape the others are missing |
| `ArtLibrary` sprite caches | `Assets/Scripts/Art/ArtLibrary.cs:116-119` | every sprite generated last run, including the one you just edited |
| `Loc` string table | `Assets/Scripts/Core/Localization/Loc.cs:24-26` | the table **and** the loaded-locale marker |
| `CharacterCatalog.LoadedLocale` and its indexes | `Assets/Scripts/Player/CharacterCatalog.cs:469-480` | the catalogue read under the previous locale |
| `Wardrobe` warn sets and `_presetsRequested` | `Assets/Scripts/Player/Wardrobe.cs:159-167` | one-shot flags that will not fire again |

**That table is what was observed, not a census.** The authoritative list of what a locale touches
is the body of `BootSequence.ApplyLocale` (`Assets/Scripts/Core/BootSequence.cs:102-116`) — it
reloads `GameData` and `CharacterPresets` as well, each with statics of its own — and it is the
right thing to mirror, because it is compiler-checked and a list in this file is not.

The sharpest of these is worth spelling out, because which scene you press Play on decides it.
`Loc.Load` returns early when `!force && _loadedLocale == canonical`
(`Assets/Scripts/Core/Localization/Loc.cs:54-59`), and with the domain kept `_loadedLocale` outlives
the session. `Boot.unity` is safe: `BootSequence.ApplyLocale` calls `Loc.Reload()`
(`Assets/Scripts/Core/BootSequence.cs:105`), which is `Load(Locales.Active, true)` and forces the
reread. **`Game.unity` and `CharacterCreation.unity` are not** — their bootstraps call a composer,
never `BootSequence`, so `Loc` is left on its lazy `EnsureLoaded` path, which calls `Load` without
`force`. Edit a string, press Play on `Game.unity`, and the screen shows the previous session's
words with nothing in the log. That is the fast loop, so that is exactly where it bites; the art
cache does the same for a tile you just redrew, in every scene, because nothing calls
`ArtLibrary.ClearCache()` at all.

So, in order:

1. **Use Play Mode as it is configured today.** The reloads still run, nothing is carried over, and
   it is already far cheaper than a build. This needs no change to any file.
2. **Only then consider `m_EnterPlayModeOptions: 3`, and only alongside explicit static resets** —
   a `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` that clears
   every static above that does not already clear itself, mirroring `ApplyLocale`.
   `ArtLibrary.ClearCache()` already exists for this
   (`Assets/Scripts/Art/ArtLibrary.cs:154`) and currently has no caller anywhere in the repository;
   `Loc.Reload()` (`Loc.cs:102`) is the matching handle for the string table. Turning the setting on
   without that work buys speed by trading it for a class of bug this project has already paid for
   twice — the invisible kind, that logs nothing.

**Play Mode does not replace a build.** It runs the Editor's player loop, not the shipped one, and
it says nothing about the phone. It is the loop for the twenty iterations before the gate, not the
gate.

## 4. Conventions worth stating

- **Scenes stay near-empty; everything is built in C#.** Hand-written scene YAML with GUID
  cross-references is the easiest thing to get silently wrong and the compiler cannot check it.
- **Content is JSON under `Resources/Data`, never a ScriptableObject.** It has to be editable
  outside Unity, by people and by agents.
- **Biblical references are `BOOK.CHAPTER.VERSE`** (`NEH.4.17`) everywhere — code, specs, prompts.
  Scripture text exists in exactly one place, `locales/*/verses.json`, and that file is generated.
- **Never widen a public signature without reading `docs/architecture-contract.md` first.**
