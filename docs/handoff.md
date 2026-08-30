# Handoff

State of the POC, and the things that are not obvious from reading the code.

Start with `README.md` for how to run it and `docs/architecture-contract.md` for the interfaces.
This file is the part that would otherwise have to be rediscovered.

## Where it is

Three days play end to end. Compiles at 0 errors and 0 warnings across ~60 C# files. Desktop and
iOS both build. `tools/acceptance.sh` passes every criterion **in both languages**, the content
validator is clean, and `tools/e2e.sh` plays the opening and a whole day of a real build in each
language and screenshots it.

**The game is bilingual: pt-BR and English.** pt-BR is the authoring locale. No player-facing string
exists in C# any more; they all live in `Assets/Resources/Data/locales/<locale>/` and are read
through `Loc.T`. Structure and numbers are shared, so balance cannot diverge between languages. See
[`docs/development-guidelines.md`](development-guidelines.md).

| | |
|---|---|
| Desktop | `Builds/mac/SheepGate.app` — runnable |
| iOS simulator | `tools/ios-sim.sh` — builds, installs and boots clean on iPhone 17 Pro / Pro Max |
| iOS device | `Builds/ios/Unity-iPhone.xcodeproj` — valid project, **never run on a device** |
| Android | Out of scope by decision. Toolchain is installed if it comes back. |

**The opening**: region shot with the other cities shut → push-in to the ruined circular city → a
neighbour crosses the square and speaks → you follow him into his house → character creation → you
follow him to the gathering → the unnamed man from the capital speaks and sends everyone for stone.

**Day 3** starts the trial, A Página lands at turn 2 and unlocks *Metade e metade*, the gate closes
with the player's name on it, then an optional "Saber mais" and the vocation reveal.

**The day ends on its own.** There is no end-of-day button any more. Work capacity is the only thing
a day is spent on, so the light is that capacity seen a second way: every stone laid pulls it down,
and when there is nothing left to spend the village goes to dusk and the split opens itself. Stopping
early is the mat at the door of the house. Three things this must keep doing, all of them easy to
break by accident:

- **Nothing runs on a wall clock.** Standing still, talking and reading cost nothing. The moment
  anything charges real time, reading a chapter starts costing the player a day, which is rule 20
  pointed at its own foot.
- **A pending night waits for whatever has the screen** — `DayCycle.DuskWaits`, asserted by the
  acceptance harness. The chapter reader is a panel like any other.
- **Accepting the day-2 invitation zeroes capacity without ending the day.** `NpcActor` takes a
  named hold (`DayCycle.HoldPendingBeat`) and gives it back when the resident says the other half
  of it; the hold is re-derived in `Start`, so a scene rebuild mid-beat restores it. The mat can
  overrule that hold, and must be able to — a player who never goes back would otherwise have no
  way to reach tomorrow.

## What is not finished

- **iOS is played through day 1 and no further.** On an iPhone 17 Pro simulator: the opening, the
  creation screen, the village, rubble, the wall, the mat, the split, the night, the morning report
  and the day-2 quiz all behave. Days 2 and 3, the trial, the Page and the reveal are still unseen
  on a phone.
- **Driving the Simulator needs a real `CGEvent`, and an unlocked Mac.** `simctl` has no tap.
  `osascript ... click at` reports success and does nothing — the Simulator's Metal view ignores
  synthetic AX clicks — which reads exactly like a game that is not responding. `/tmp/simtap.swift`
  in the session scratch posts `leftMouseDown`/`Up` through `.cghidEventTap`, which works. Two
  things to know before blaming the build: `simctl io booted screenshot` grabs the framebuffer and
  is immune to window stacking, whereas `screencapture -R` silently photographs whatever window is
  on top; and if `System Events` answers a click with `window Login of application process
  loginwindow`, the Mac is locked and no synthetic input will land at all.
- **The framing of the opening has ~10 px of margin.** `WorldMapOverlay` places the closed cities
  at x ±14 to ±17 world units and the camera half-width at 19.5:9 is 20.2, so the leftmost city
  clears the screen edge by about a third of a world unit. At the 1080×1920 the project nominally
  targets, the half-width is 24.8 and the margin is comfortable. Nothing is clipped on the phones
  tested; there is simply no room left, and a squarer screen would eat into it.
- **`WorldMapOverlay.Place.Caption` is never drawn.** Every entry carries `"fechada"` and
  `BuildPlace` ignores it. Either the caption was meant to render or the field should go.
- **Day 1 has no daily check-in.** The quiz used to fire on scene composition, which put a question
  on top of the opening; it now arrives only from `MorningStarted`, which never fires on day 1. If
  it is wanted back, the end of the day is the right home — not the start.
- **The buildable wall is a straight run** along the north of a circular city. `WallSystem` places
  a segment by `grid_x` on one row. Nehemiah 3 assigns each group a stretch, so it reads correctly,
  but a true arc would need `WallSystem`, the contest and the patrol camera to change together.
- **The four skin tones and the build silhouettes are unjudged.** Nobody has looked at whether tones
  2 and 3 are distinguishable at 32×48, or whether the narrower build actually reads.

## What the localisation pass found, and what it did not fix

The e2e run was written as part of that pass and immediately found things nothing else could:

- **Four preview captions were still Portuguese in the English build.** `DirectionCaptions` was an
  array of string literals, so the validator's check on call arguments could not see it. The
  validator now also checks fields whose *name* says they hold player words, which is the shape
  that escaped.
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

Still open:

- **`tools/e2e.sh` stops at the second morning.** It plays the opening through character creation
  into day 1, spends that day, turns the night down once, lets the day end on its own and confirms
  the split — then stops. Days 2 and 3, the trial, the Page and the reveal are asserted nowhere.
  Extending it is mostly a matter of naming more beats; the machinery for waiting, tapping and
  screenshotting is there. Note what it deliberately does *not* drive: it spends the day through
  `ResourceSystem` rather than walking the player to the wall, so the interaction layer between a
  tap on the ground and a stone on the wall is still only covered by playing.
- **Nobody has read the English translation against the passage.** `node tools/list-curation.mjs`
  now queues both languages and `intro_gathering` is in the queue for `en` as well as `pt-BR`. A
  translation of a canonical figure's speech is newly authored speech in that language, so rule 4's
  human read applies to it and has not happened.
- **The English text has had no native pass.** It reads correctly and keeps the register, but it
  was written by the same agent that wrote the code.

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
the person at that machine gets on their next launch. `E2ERunner` sets
`Locales.SuppressPersistence` before anything can switch.

**A macOS player stops rendering when its window loses focus.** The e2e run is launched from a
terminal that keeps focus, so the first `WaitForEndOfFrame` never returned and the run hung before
its first screenshot with nothing in the log. `E2ERunner` sets `Application.runInBackground` and
carries a watchdog, because a hang reads exactly like a slow step.

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

**A control built this frame raycasts to nothing.** Until a canvas has been through one batch,
every graphic on it reports `depth == -1` and `GraphicRaycaster` skips it outright — so a live,
correctly placed, fully interactable button is hit by no ray at all. `E2ERunner.TapObject` judged on
the first frame and reported the settings panel's language chips as unreachable; it now retries
until the ray lands or the step times out. The distinction it must preserve: a control genuinely
under an opaque panel still fails, having spent the timeout proving it.

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
3. `tools/acceptance.sh` — asserts the rules from POC-IMPLEMENTATION.md §13, once per language.
4. `tools/e2e.sh` — builds a player and plays the opening and a day in every language, screenshots in
   `Builds/e2e/`. **Read the screenshots.** A green exit code means nothing was covered and no
   string was missing; it does not mean the screen looks right.
5. `node tools/list-curation.mjs` — authored canonical speech awaiting a human read, every language.
5. `tools/ios-sim.sh` — build and boot the player on an iPhone simulator, then look at it.

The API key lives in `.env.local`, which is git-ignored and has never been committed.
