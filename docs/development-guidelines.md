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

Two things are deliberately **not** English, and both are about audience rather than preference.

**The content is authored in pt-BR.** It is the authoring locale: the game is written in Portuguese
first and translated outward.

**Product and design documents stay in pt-BR** — `AGENTS.md`, `docs/persona-e-proposito.md`,
`docs/nehemiah-game-design.md`, `docs/poc-scope.md`, `docs/canon-24h-plan.md`. These are the team
thinking about the product in the team's own language, and they are read by the five people who
decide what this is, not by the compiler. The line falls where the audience changes: a document
about *how the code works* is English, a document about *what the game is* is pt-BR.

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
tools/unity-check.sh              # compiles; 0 errors AND 0 warnings is the bar
node tools/validate-content.mjs   # scripture integrity, locale parity, hardcoded strings
tools/acceptance.sh               # the product rules, asserted per locale
tools/e2e.sh                      # builds a player and plays the opening in every language
```

The first three are necessary and not sufficient. This project has learned twice that they are not
enough:

- **Correct code that nothing calls.** `MoraleContest.Begin()` and `VocationTracker.Resolve()` once
  had no runtime caller at all, so day 3 was unreachable in a built game while every unit-level rule
  about it passed. *Verify reachability, not just correctness.*
- **Bugs that are invisible rather than broken.** Character creation drawn underneath an opaque
  fade; four `SpriteRenderer`s on one GameObject where `[DisallowMultipleComponent]` silently
  dropped three. Neither logged anything. Everything in this project is constructed at runtime, so
  the compiler cannot check a single thing about layout.

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

## 4. Conventions worth stating

- **Scenes stay near-empty; everything is built in C#.** Hand-written scene YAML with GUID
  cross-references is the easiest thing to get silently wrong and the compiler cannot check it.
- **Content is JSON under `Resources/Data`, never a ScriptableObject.** It has to be editable
  outside Unity, by people and by agents.
- **Biblical references are `BOOK.CHAPTER.VERSE`** (`NEH.4.17`) everywhere — code, specs, prompts.
  Scripture text exists in exactly one place, `locales/*/verses.json`, and that file is generated.
- **Never widen a public signature without reading `docs/architecture-contract.md` first.**
