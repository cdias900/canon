# Handoff

State of the POC, and the things that are not obvious from reading the code.

Start with `README.md` for how to run it and `docs/architecture-contract.md` for the interfaces.
This file is the part that would otherwise have to be rediscovered.

## Where it is

Three days play end to end. Compiles at 0 errors and 0 warnings across ~60 C# files. Desktop and
iOS both build. All 18 checks in `tools/acceptance.sh` pass and the content validator is clean.

| | |
|---|---|
| Desktop | `Builds/mac/SheepGate.app` — runnable |
| iOS | `Builds/ios/Unity-iPhone.xcodeproj` — valid project, **never run on a device** |
| Android | Out of scope by decision. Toolchain is installed if it comes back. |

**The opening**: region shot with the other cities shut → push-in to the ruined circular city → a
neighbour crosses the square and speaks → you follow him into his house → character creation → you
follow him to the gathering → the unnamed man from the capital speaks and sends everyone for stone.

**Day 3** starts the trial, A Página lands at turn 2 and unlocks *Metade e metade*, the gate closes
with the player's name on it, then an optional "Saber mais" and the vocation reveal.

## What is not finished

- **The iOS build has never been run.** It compiles; that is all anyone knows. Portrait UI at
  1080×1920 on a real phone is where the six-row creation screen and the thumb-zone HUD are most
  likely to break.
- **Day 1 has no daily check-in.** The quiz used to fire on scene composition, which put a question
  on top of the opening; it now arrives only from `MorningStarted`, which never fires on day 1. If
  it is wanted back, the end of the day is the right home — not the start.
- **The buildable wall is a straight run** along the north of a circular city. `WallSystem` places
  a segment by `grid_x` on one row. Nehemiah 3 assigns each group a stretch, so it reads correctly,
  but a true arc would need `WallSystem`, the contest and the patrol camera to change together.
- **The four skin tones and the build silhouettes are unjudged.** Nobody has looked at whether tones
  2 and 3 are distinguishable at 32×48, or whether the narrower build actually reads.

## Things that cost real time to learn

**`Application.persistentDataPath` differs between the editor and a player build.** The editor uses
company/product, a player build uses the bundle identifier. Clearing one leaves the other's save
behind and the game resumes a run that looked deleted. Boot logs both paths for this reason — read
`[Boot] Save ->` rather than guessing.

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

**The Unity Hub deadlocks on its own database.** An install that "Completed with errors" with
LevelDB lock errors in the log usually just needs the stale Hub process killed and a retry. The
first Android attempt failed this way and the second filled the disk.

**`android-open-jdk` does not resolve in Unity 6.** The id is `android-open-jdk-17.0.18+8`.

## Traps specific to how this project is built

**Scenes are near-empty and everything is constructed at runtime.** That moves whole classes of
error into the compiler, which is why it was chosen — but it also means the compiler cannot catch
anything about *layout*. Two bugs so far were invisible-not-broken: character creation drawn at
sorting order 0 underneath an opaque fade at 400, and four `SpriteRenderer`s stacked on one
GameObject where `[DisallowMultipleComponent]` silently dropped three of them. Neither logged
anything. If layered UI keeps growing, a screenshot check would find these far faster than reasoning
about z-order.

**Parallel agents produce good modules and miss the seams.** Two verification rounds found 33
defects, 5 of them blockers, and nearly all shared one shape: correct code that nothing ever called.
`MoraleContest.Begin()` and `VocationTracker.Resolve()` had no runtime caller at all, so day 3 was
unreachable in a built game while every unit-level rule about it passed. **Verify reachability, not
just correctness.**

**The acceptance harness cannot prove a scene works.** It constructs systems directly and never runs
`Compose()`. It is a good gate on rules; it is not evidence that anything is wired.

**EditMode cannot pump coroutines.** The contest turn loop and `DayCycle.EndDay` both defer to
coroutines, so the harness asserts their configuration and drives `DayCycle` through its synchronous
path by disabling the component. The live Page beat is still only verified by playing.

## Decisions taken here, with their reasons

- **Rule 4 was loosened** (see `AGENTS.md`). Canonical figures may speak authored dialogue that
  asserts nothing the passage does not. The argument that settled it: rule 4 as written forced
  verbatim quotation as the *only* way for such a figure to speak, which pushed the most
  Bible-looking artifact into the earliest minutes — backwards for an audience that has not opened
  a Bible. The safeguard moved from mechanical to human, so `tools/list-curation.mjs` exists.
- **References are withheld until A Página**, never removed. Printing the citation and offering the
  chapter were one condition in `DialogueUI`; they are now two, because hiding the reference would
  otherwise have hidden the only entry to the reader — and that tap is `deep_read`.
- **`verses.json` ships NVI**, which is all-rights-reserved. The repo is **private for this reason**.
  Public would redistribute copyrighted scripture outside the app. Regenerate against BLT
  (`--version-id 3254`, CC BY-SA) before making it public.
- **The crowd does not block movement.** A dozen solid bodies in the centre of a circular village
  turns the route to the wall into a maze.
- **The name in character creation is optional.** Gating the game on a form field is the opposite of
  "sem cadastro".

## If picking this up cold

1. `tools/unity-check.sh` — compiles, reports C# errors.
2. `tools/acceptance.sh` — asserts the rules from POC-IMPLEMENTATION.md §13.
3. `node tools/validate-content.mjs` — scripture integrity and the forbidden-word checklist.
4. `node tools/list-curation.mjs` — authored canonical speech awaiting a human read.

The API key lives in `.env.local`, which is git-ignored and has never been committed.
