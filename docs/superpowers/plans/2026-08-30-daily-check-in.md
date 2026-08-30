# Daily Check-In Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reward the player with talents (a cosmetic-only currency) once per real calendar day at boot, with the reward escalating on a streak that resets — without clawing back talents already earned — when a day is missed.

**Architecture:** A pure, state-only function (`DailyCheckIn.Apply`) does the date math and mutates `GameState`; `BootSequence` calls it once at boot and stashes the result in a static field; a new `CheckInToast` component (composed into the Game scene the same way `DailyQuiz` is) shows a one-shot modal once the screen is quiet; `BackpackPanel`'s existing materials strip grows a fourth cell for the talent balance.

**Tech Stack:** Unity 6000.3.23f1, C#, the project's existing `ModalRoot`/`UIKit`/`Loc` UI toolkit, `SaveSystem`/`Telemetry` from `SheepGate.Core`.

**Spec:** `docs/superpowers/specs/2026-08-30-daily-check-in-design.md`

## Global Constraints

- Every identifier, comment, log message, JSON key, filename, and commit message is English. Only player-facing strings (via `Loc.T`) and this plan/spec's prose are pt-BR/en pairs.
- No player-facing string may live in a `.cs` file — every one goes in `Assets/Resources/Data/locales/<locale>/ui.json`, read through `Loc.T("key")`, and both locales (`pt-BR` source, `en` translation) get the same keys.
- Reward schedule: streak 1-3 → 1 talent; streak 4+ → 3 talents. A gap of more than one calendar day resets `checkInStreak` to 1 (not to 0 — the day being checked in counts as day 1 of the new streak). Talents already awarded are never removed.
- `lastCheckInDate` uses `yyyy-MM-dd`, the device's local date (`DateTime.Now`, not `UtcNow` — a login reward that flips at UTC midnight lands mid-afternoon for most players and would feel arbitrary).
- New `GameState` fields are purely additive with safe zero-value defaults (`""`, `0`, `0`) — no `schemaVersion` bump, matching the precedent set by `equippedItems`/`seenItems` in `Assets/Scripts/Core/GameState.cs:122-147`.
- Nothing on the check-in toast may read as a slot-machine payout (rule 13's checklist: no gold glow, no fanfare) — it's a receipt: "+1 talento" / "+3 talentos" and a close button.
- Every task ends in a state where `tools/unity-check.sh` compiles clean.

---

### Task 1: `GameState` fields and telemetry event

**Files:**
- Modify: `Assets/Scripts/Core/GameState.cs`
- Modify: `Assets/Scripts/Core/Telemetry.cs`

**Interfaces:**
- Produces: `GameState.lastCheckInDate` (`string`), `GameState.checkInStreak` (`int`), `GameState.talents` (`int`) — read/written directly as public fields, same as every other field on `GameState`.
- Produces: `TelemetryEvents.CheckIn` (`const string = "check_in"`).

- [ ] **Step 1: Add the three fields to `GameState`**

In `Assets/Scripts/Core/GameState.cs`, add after the `watchAssigned`/`workAssigned` fields (around line 161), before `HasFlag`:

```csharp
// ---------------------------------------------------------------------- daily check-in
//
// A calendar-day reward, independent of `day` (the in-fiction day, 1..3). lastCheckInDate is
// the device's local date in "yyyy-MM-dd" — see DailyCheckIn for the read/write logic. All
// three fields are purely additive: a save written before this feature existed carries none
// of them and loads them at their zero value, so no schemaVersion bump is needed (the same
// reasoning as equippedItems/seenItems above).

/// <summary>Local date of the last awarded check-in, "yyyy-MM-dd", or empty before the first one.</summary>
public string lastCheckInDate = "";

/// <summary>Consecutive calendar days checked in. Resets to 1 (not 0) on any gap greater than one day.</summary>
public int checkInStreak;

/// <summary>Cosmetic-only currency awarded by the daily check-in. Never spent by anything in this build.</summary>
public int talents;
```

- [ ] **Step 2: Add the telemetry event constant**

In `Assets/Scripts/Core/Telemetry.cs`, inside `TelemetryEvents`, add next to the other event constants (near `LocaleChanged`):

```csharp
public const string CheckIn = "check_in";
```

- [ ] **Step 3: Compile**

Run: `tools/unity-check.sh`
Expected: `Compiled clean.`

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Core/GameState.cs Assets/Scripts/Core/Telemetry.cs
git commit -m "Add GameState fields and telemetry event for the daily check-in"
```

---

### Task 2: `DailyCheckIn` pure logic

**Files:**
- Create: `Assets/Scripts/Economy/DailyCheckIn.cs`
- Test: `Assets/Editor/AcceptanceHarness.cs` (new criterion, added in this task since the project has no separate unit-test runner — `tools/acceptance.sh` is the test layer for state logic; see `docs/development-guidelines.md`)

**Interfaces:**
- Consumes: `GameState.lastCheckInDate`, `GameState.checkInStreak`, `GameState.talents` (Task 1).
- Produces: `DailyCheckIn.Apply(GameState state, DateTime today) -> DailyCheckIn.Result`, where `Result` is a struct with `bool Awarded`, `int Streak`, `int TalentsAwarded`. Produces `DailyCheckIn.PendingResult` (`Result?`, static, in-memory only, not serialized) — set by `BootSequence` in Task 3, read and cleared by `CheckInToast` in Task 4.
- Produces: `DailyCheckIn.DateFormat` (`const string = "yyyy-MM-dd"`), reused by the acceptance criterion.

- [ ] **Step 1: Write the failing acceptance criterion**

In `Assets/Editor/AcceptanceHarness.cs`, add a new method near `SaveRoundTrip` (this one needs no disk I/O — it only exercises `DailyCheckIn.Apply` against in-memory `GameState` instances):

```csharp
// New — the daily check-in's date math: streak advances, escalates at day 4, and resets
// (never below 1) on any gap, without ever removing talents already awarded.
static void CheckInSchedule()
{
    var today = new DateTime(2026, 1, 10);

    GameState state = GameState.NewGame();
    DailyCheckIn.Result first = DailyCheckIn.Apply(state, today);
    Check("check-in first day awards streak 1 / 1 talent",
        first.Awarded && first.Streak == 1 && first.TalentsAwarded == 1 && state.talents == 1,
        "streak=" + first.Streak + " awarded=" + first.TalentsAwarded + " talents=" + state.talents);

    DailyCheckIn.Result sameDay = DailyCheckIn.Apply(state, today);
    Check("check-in does not re-award the same day",
        !sameDay.Awarded && state.talents == 1,
        "awarded=" + sameDay.Awarded + " talents=" + state.talents);

    for (int i = 1; i <= 3; i++)
    {
        DailyCheckIn.Apply(state, today.AddDays(i));
    }
    Check("check-in reaches streak 4 on the fourth consecutive day",
        state.checkInStreak == 4, "streak=" + state.checkInStreak);

    DailyCheckIn.Result fourth = DailyCheckIn.Apply(state, today.AddDays(4));
    Check("check-in pays 3 talents at streak 5",
        fourth.Awarded && fourth.Streak == 5 && fourth.TalentsAwarded == 3,
        "streak=" + fourth.Streak + " awarded=" + fourth.TalentsAwarded);

    int talentsBeforeGap = state.talents;
    DailyCheckIn.Result afterGap = DailyCheckIn.Apply(state, today.AddDays(7));
    Check("check-in resets the streak to 1 after a missed day, without removing earned talents",
        afterGap.Awarded && afterGap.Streak == 1 && afterGap.TalentsAwarded == 1
            && state.talents == talentsBeforeGap + 1,
        "streak=" + afterGap.Streak + " talents=" + state.talents + " (had " + talentsBeforeGap + ")");
}
```

Wire it into `RunAll`, alongside the other calls (after `DaylightClock();`):

```csharp
CheckInSchedule();
```

- [ ] **Step 2: Run it to confirm it fails to compile (the type doesn't exist yet)**

Run: `tools/unity-check.sh`
Expected: `CS0246: The type or namespace name 'DailyCheckIn' could not be found`

- [ ] **Step 3: Write `DailyCheckIn`**

Create `Assets/Scripts/Economy/DailyCheckIn.cs`:

```csharp
using System;
using System.Globalization;
using SheepGate.Core;

namespace SheepGate.Economy
{
    /// <summary>
    /// The daily check-in: once per real calendar day, at boot, the player is paid talents. The
    /// streak that decides the payout tier resets on any gap greater than one day; the talents
    /// already paid out never do — see the design doc's note on rule 7 for why that split is the
    /// deliberate boundary rather than an oversight.
    /// </summary>
    public static class DailyCheckIn
    {
        public const string DateFormat = "yyyy-MM-dd";

        /// <summary>Streak at or above which a check-in pays the higher tier.</summary>
        const int EscalationStreak = 4;

        const int BaseTalents = 1;
        const int EscalatedTalents = 3;

        /// <summary>One check-in's outcome. `Awarded` is false when today was already paid.</summary>
        public struct Result
        {
            public bool Awarded;
            public int Streak;
            public int TalentsAwarded;
        }

        /// <summary>
        /// Set by <see cref="SheepGate.Core.BootSequence"/> right after a boot that paid a reward,
        /// and cleared by whatever shows the toast for it. In-memory only — never serialized, so a
        /// reward can never replay itself from a save written mid-toast.
        /// </summary>
        public static Result? PendingResult;

        /// <summary>
        /// Applies today's check-in to <paramref name="state"/>, mutating it when a reward is due.
        /// Safe to call more than once for the same day: every call after the first for that date
        /// is a no-op that returns <c>Awarded = false</c>.
        /// </summary>
        public static Result Apply(GameState state, DateTime today)
        {
            string todayKey = today.ToString(DateFormat, CultureInfo.InvariantCulture);
            if (state.lastCheckInDate == todayKey)
            {
                return new Result { Awarded = false, Streak = state.checkInStreak, TalentsAwarded = 0 };
            }

            bool consecutive = IsNextCalendarDay(state.lastCheckInDate, today);
            state.checkInStreak = consecutive ? state.checkInStreak + 1 : 1;

            int talents = state.checkInStreak >= EscalationStreak ? EscalatedTalents : BaseTalents;
            state.talents += talents;
            state.lastCheckInDate = todayKey;

            return new Result { Awarded = true, Streak = state.checkInStreak, TalentsAwarded = talents };
        }

        /// <summary>True when today is exactly one calendar day after the stored date.</summary>
        static bool IsNextCalendarDay(string lastCheckInDate, DateTime today)
        {
            if (string.IsNullOrEmpty(lastCheckInDate))
            {
                return false;
            }

            DateTime last;
            if (!DateTime.TryParseExact(lastCheckInDate, DateFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out last))
            {
                return false;
            }

            return today.Date == last.Date.AddDays(1);
        }
    }
}
```

- [ ] **Step 4: Run the criterion and confirm it passes**

Run: `tools/acceptance.sh`
Expected: every `check-in ...` line reports `PASS` in the report printed for each locale, and the script exits 0.

- [ ] **Step 5: Compile check**

Run: `tools/unity-check.sh`
Expected: `Compiled clean.`

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Economy/DailyCheckIn.cs Assets/Editor/AcceptanceHarness.cs
git commit -m "Add DailyCheckIn date-math and its acceptance criterion"
```

---

### Task 3: Wire the check-in into boot

**Files:**
- Modify: `Assets/Scripts/Core/BootSequence.cs`

**Interfaces:**
- Consumes: `DailyCheckIn.Apply(GameState, DateTime)`, `DailyCheckIn.PendingResult` (Task 2); `TelemetryEvents.CheckIn` (Task 1); `SaveSystem.Save(GameState)` (existing).
- Produces: nothing new — `DailyCheckIn.PendingResult` is set as a side effect for Task 4 to consume.

- [ ] **Step 1: Call `DailyCheckIn.Apply` after the state is loaded, before the scene loads**

In `Assets/Scripts/Core/BootSequence.cs`, inside `Run()`, right after the `Telemetry.Track(TelemetryEvents.SessionStart, ...)` / `Telemetry.Flush()` block (currently ending at line 59) and before the `AudioDirector.Ensure()` call, insert:

```csharp
ApplyDailyCheckIn(state);
```

Add the new method near the bottom of the class, alongside `ReconcileSegments`:

```csharp
/// <summary>
/// Pays today's check-in, if one is due, and stashes the outcome for CheckInToast to show once
/// the Game scene has settled. Saved immediately so a force-quit mid-toast cannot replay the
/// reward — the same reasoning DailyQuiz already applies to its own seen-counter.
/// </summary>
static void ApplyDailyCheckIn(GameState state)
{
    DailyCheckIn.Result result = DailyCheckIn.Apply(state, DateTime.Now);
    if (!result.Awarded)
    {
        return;
    }

    SaveSystem.Save(state);

    Telemetry.Track(TelemetryEvents.CheckIn, new Dictionary<string, object>
    {
        { "streak", result.Streak },
        { "talents_awarded", result.TalentsAwarded }
    });
    Telemetry.Flush();

    DailyCheckIn.PendingResult = result;

    Debug.Log("[Boot] Check-in -> streak " + result.Streak + ", +" + result.TalentsAwarded + " talents.");
}
```

`DailyCheckIn` is in `SheepGate.Economy`; add `using SheepGate.Economy;` to the file's using block.

- [ ] **Step 2: Compile**

Run: `tools/unity-check.sh`
Expected: `Compiled clean.`

- [ ] **Step 3: Manual smoke test**

Run: `tools/unity-check.sh --open`, press Play in the editor.
Expected: the console shows a `[Boot] Check-in -> streak 1, +1 talents.` line (first run ever has no `lastCheckInDate`, so it always awards). No visible UI yet — that's Task 4.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Core/BootSequence.cs
git commit -m "Run the daily check-in at boot and persist its result"
```

---

### Task 4: The check-in toast

**Files:**
- Create: `Assets/Scripts/UI/CheckInToast.cs`
- Modify: `Assets/Scripts/World/GameScene.cs`
- Modify: `Assets/Resources/Data/locales/pt-BR/ui.json`
- Modify: `Assets/Resources/Data/locales/en/ui.json`

**Interfaces:**
- Consumes: `DailyCheckIn.PendingResult` (Task 2/3); `ModalRoot`, `InputLock`, `UIKit`, `Loc`, `DesignTokens` (existing, same APIs `DailyQuiz` already uses).
- Produces: nothing further tasks depend on — this is the leaf that displays the reward.

- [ ] **Step 1: Add the locale strings**

In `Assets/Resources/Data/locales/pt-BR/ui.json`, add next to the `quiz.*` keys:

```json
  "checkin.reward_one": "+1 talento",
  "checkin.reward_many": "+3 talentos",
  "checkin.continue": "Continuar",
```

In `Assets/Resources/Data/locales/en/ui.json`, add the matching keys (same positions, translated):

```json
  "checkin.reward_one": "+1 talent",
  "checkin.reward_many": "+3 talents",
  "checkin.continue": "Continue",
```

- [ ] **Step 2: Write `CheckInToast`**

Create `Assets/Scripts/UI/CheckInToast.cs`, modeled on `DailyQuiz`'s scheduling (`Assets/Scripts/Quiz/DailyQuiz.cs`) but simplified: no options, no per-day loop — it shows the one pending result once and then has nothing left to do.

```csharp
using SheepGate.Dialogue;
using SheepGate.Economy;
using SheepGate.World;
using UnityEngine;
using UnityEngine.UI;

namespace SheepGate.UI
{
    /// <summary>
    /// Shows the one pending daily check-in reward, if <see cref="DailyCheckIn.PendingResult"/> is
    /// set. Waits for a quiet screen the same way <see cref="SheepGate.Quiz.DailyQuiz"/> does, so it
    /// never lands on top of the day-1 opening or a conversation. Has nothing left to do once shown,
    /// so unlike DailyQuiz it does not resubscribe to anything — one boot, at most one toast.
    /// </summary>
    public class CheckInToast : MonoBehaviour
    {
        public const string ModalId = "check_in_toast";

        const float SettleSeconds = 0.6f;

        RectTransform _container;
        DialogueSystem _dialogue;
        DayCycle _dayCycle;
        bool _lockHeld;
        float _earliestShowTime;
        bool _shown;

        void Start()
        {
            if (!DailyCheckIn.PendingResult.HasValue)
            {
                enabled = false;
                return;
            }

            _earliestShowTime = Time.unscaledTime + SettleSeconds;
            _dialogue = FindFirstObjectByType<DialogueSystem>();
            _dayCycle = FindFirstObjectByType<DayCycle>();
        }

        void Update()
        {
            if (_shown || !DailyCheckIn.PendingResult.HasValue)
            {
                return;
            }

            if (Time.unscaledTime < _earliestShowTime || !IsScreenQuiet())
            {
                return;
            }

            Show(DailyCheckIn.PendingResult.Value);
        }

        /// <summary>True when nothing else owns the screen — the same rule DailyQuiz applies.</summary>
        bool IsScreenQuiet()
        {
            if (ModalRoot.IsOpen || InputLock.IsLocked)
            {
                return false;
            }

            if (_dayCycle != null && _dayCycle.IsResolving)
            {
                return false;
            }

            if (_dialogue != null && _dialogue.IsPlaying)
            {
                return false;
            }

            return true;
        }

        void Show(DailyCheckIn.Result result)
        {
            ModalRoot root = ModalRoot.Instance;
            if (root == null)
            {
                Debug.LogError("[CheckInToast] No modal root is available; the reward is not shown, but it was already paid.");
                DailyCheckIn.PendingResult = null;
                enabled = false;
                return;
            }

            _shown = true;
            _container = root.Push(ModalId);
            if (_container == null)
            {
                DailyCheckIn.PendingResult = null;
                enabled = false;
                return;
            }

            _lockHeld = true;
            InputLock.Push();

            Build(result);
        }

        void Build(DailyCheckIn.Result result)
        {
            Image card = UIKit.CreateCard(_container, "CheckInCard", UIKit.CardStyle.Card);
            var cardRect = (RectTransform)card.transform;
            cardRect.anchorMin = new Vector2(0f, 0.5f);
            cardRect.anchorMax = new Vector2(1f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.offsetMin = new Vector2(DesignTokens.Space.Gutter, cardRect.offsetMin.y);
            cardRect.offsetMax = new Vector2(-DesignTokens.Space.Gutter, cardRect.offsetMax.y);

            UIKit.VerticalGroup(card.gameObject, DesignTokens.Space.S16, new RectOffset(
                Mathf.RoundToInt(DesignTokens.Space.S20), Mathf.RoundToInt(DesignTokens.Space.S20),
                Mathf.RoundToInt(DesignTokens.Space.S24), Mathf.RoundToInt(DesignTokens.Space.S24)));

            var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            string rewardKey = result.TalentsAwarded >= 3 ? "checkin.reward_many" : "checkin.reward_one";
            UIKit.CreateText(card.transform, "Reward", Loc.T(rewardKey), DesignTokens.Type.Title,
                DesignTokens.Ink.Primary, TextAnchor.UpperLeft, DesignTokens.TypeRole.Title);

            UIKit.CreateButton(card.transform, "Continue", Loc.T("checkin.continue"),
                UIKit.ButtonVariant.Primary, Close);

            UIKit.RebuildNow(cardRect);
        }

        void Close()
        {
            _container = null;
            ModalRoot.CloseId(ModalId);

            if (_lockHeld)
            {
                _lockHeld = false;
                InputLock.Pop();
            }

            DailyCheckIn.PendingResult = null;
            enabled = false;
        }

        void OnDestroy()
        {
            if (_lockHeld)
            {
                _lockHeld = false;
                InputLock.Pop();
            }
        }
    }
}
```

- [ ] **Step 3: Compose it into the Game scene**

In `Assets/Scripts/World/GameScene.cs`, add a sibling to `EnsureQuizSystem` (near line 591):

```csharp
private static void EnsureCheckInToast(GameObject host)
{
    Type toastType = TypeBridge.Find("SheepGate.UI.CheckInToast");
    if (toastType == null || !typeof(Component).IsAssignableFrom(toastType))
    {
        return;
    }

    UnityEngine.Object existing = UnityEngine.Object.FindFirstObjectByType(toastType);
    if (existing != null)
    {
        return;
    }

    TypeBridge.AddComponent(host, toastType);
}
```

And call it next to `EnsureQuizSystem(systemsObject);` (line 94):

```csharp
EnsureQuizSystem(systemsObject);
EnsureCheckInToast(systemsObject);
```

- [ ] **Step 4: Compile**

Run: `tools/unity-check.sh`
Expected: `Compiled clean.`

- [ ] **Step 5: Validate content (locale parity, no hardcoded strings)**

Run: `node tools/validate-content.mjs`
Expected: exits 0 — both locales carry the three new `checkin.*` keys, and `CheckInToast.cs` never passes a literal to `Loc.T`-adjacent parameters.

- [ ] **Step 6: Manual test — first-ever run**

Run: `tools/unity-check.sh --open`, press Play with no existing save (or delete the save first: `rm -rf ~/Library/Application\ Support/Create\ Hack`).
Expected: shortly after the opening settles, a card reading "+1 talento" (pt-BR) or "+1 talent" (en) appears with a Continue button; tapping it closes the card and does not reappear on a second Play in the same session.

- [ ] **Step 7: Manual test — streak escalation and reset, by editing the save**

Quit Play mode. Open the save file under `~/Library/Application Support/Create Hack` (the design doc's test section has the exact path) and set `lastCheckInDate` to **yesterday** and `checkInStreak` to `3`. Press Play again.
Expected: today is consecutive with yesterday, so the streak advances to 4 and the toast reads "+3 talentos" / "+3 talents".

Quit again, set `lastCheckInDate` to **5+ days ago** (leave `checkInStreak` at `4`), and Play once more.
Expected: the gap is bigger than one day, so the streak resets and the toast reads "+1 talento" / "+1 talent".

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/UI/CheckInToast.cs Assets/Scripts/World/GameScene.cs \
  Assets/Resources/Data/locales/pt-BR/ui.json Assets/Resources/Data/locales/en/ui.json
git commit -m "Show the daily check-in reward as a one-shot toast on boot"
```

---

### Task 5: Talent balance in the backpack

**Files:**
- Modify: `Assets/Scripts/UI/BackpackPanel.cs`
- Modify: `Assets/Resources/Data/locales/pt-BR/ui.json`
- Modify: `Assets/Resources/Data/locales/en/ui.json`

**Interfaces:**
- Consumes: `GameState.talents` (Task 1).
- Produces: nothing further tasks depend on.

- [ ] **Step 1: Add the locale string**

In `Assets/Resources/Data/locales/pt-BR/ui.json`, next to the other `backpack.material.*` keys:

```json
  "backpack.material.talents": "Talentos",
```

In `Assets/Resources/Data/locales/en/ui.json`:

```json
  "backpack.material.talents": "Talents",
```

- [ ] **Step 2: Extend the three parallel arrays**

In `Assets/Scripts/UI/BackpackPanel.cs`, extend `MaterialLabelKeys` (around line 108) and `MaterialObjectNames` (around line 132) with a fourth entry each:

```csharp
static readonly string[] MaterialLabelKeys =
{
    "backpack.material.stone",
    "backpack.material.timber",
    "backpack.material.blocks",
    "backpack.material.talents"
};
```

```csharp
static readonly string[] MaterialObjectNames = { "Material_stone", "Material_timber", "Material_blocks", "Material_talents" };
```

`BuildMaterials` already loops over `MaterialLabelKeys.Length`, so the fourth cell is built automatically — no change needed there.

- [ ] **Step 3: Read the fourth value in `RefreshMaterials`**

In the same file, extend `RefreshMaterials` (around line 964):

```csharp
void RefreshMaterials()
{
    if (_materialCounts == null)
    {
        return;
    }

    int stone = _state != null ? Mathf.Max(0, _state.stone) : 0;
    int timber = _state != null ? Mathf.Max(0, _state.timber) : 0;
    int blocks = _state != null ? Mathf.Max(0, _state.blocks) : 0;
    int talents = _state != null ? Mathf.Max(0, _state.talents) : 0;

    SetCount(0, stone);
    SetCount(1, timber);
    SetCount(2, blocks);
    SetCount(3, talents);
}
```

- [ ] **Step 4: Compile**

Run: `tools/unity-check.sh`
Expected: `Compiled clean.`

- [ ] **Step 5: Validate content**

Run: `node tools/validate-content.mjs`
Expected: exits 0.

- [ ] **Step 6: Manual test**

Run: `tools/unity-check.sh --open`, press Play, dismiss the check-in toast, open the backpack.
Expected: the materials strip now shows four cells, the last one "Talentos"/"Talents" with the balance the toast just paid.

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/UI/BackpackPanel.cs \
  Assets/Resources/Data/locales/pt-BR/ui.json Assets/Resources/Data/locales/en/ui.json
git commit -m "Show the talent balance in the backpack materials strip"
```

---

### Task 6: Full verification pass

**Files:** none (verification only)

**Interfaces:** none — this task only runs the project's existing gates against everything the prior five tasks changed.

- [ ] **Step 1: Compile**

Run: `tools/unity-check.sh`
Expected: `Compiled clean.`

- [ ] **Step 2: Content validation**

Run: `node tools/validate-content.mjs`
Expected: exits 0 (locale parity for every `checkin.*`/`backpack.material.talents` key, no hardcoded strings in the new files).

- [ ] **Step 3: Acceptance criteria**

Run: `tools/acceptance.sh`
Expected: `ALL CRITERIA PASSED`, including the six new `check-in ...` lines from Task 2, once per locale.

- [ ] **Step 4: End-to-end**

Run: `tools/e2e.sh`
Expected: builds and plays the opening plus a full day in every locale without an unresolved string or a logged error — the check-in toast fires during this run (a fresh save has no `lastCheckInDate`), so a regression in `CheckInToast` or `ModalRoot` interaction would show up here as a screenshot with a stuck modal or a missed-click failure.

- [ ] **Step 5: No commit** — this task only verifies; nothing here changes files.
