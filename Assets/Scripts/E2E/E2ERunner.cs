using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SheepGate.Core;
using SheepGate.Dialogue;
using SheepGate.Player;
using SheepGate.Scripture;
using SheepGate.UI;
using SheepGate.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SheepGate.E2E
{
    /// <summary>
    /// Plays the opening of a built player without a person at the keyboard, screenshots it, and
    /// fails the run on anything a person would have noticed.
    ///
    /// It exists because of two lessons this project already paid for. The acceptance harness
    /// constructs systems directly and never composes a scene, so it proves rules and not
    /// reachability — day 3 was once unreachable in a built game while every rule about it passed.
    /// And the bugs that have actually shipped were invisible rather than broken: UI drawn under an
    /// opaque panel, components silently dropped. Neither logged anything, and neither could be
    /// reasoned about. Both are obvious in a screenshot.
    ///
    /// So this runner only ever touches the game the way a finger does: it raycasts the EventSystem
    /// at a control's own screen position and requires that control to be what the ray actually
    /// hits before it dispatches a click. Calling Button.onClick directly would pass happily on a
    /// button buried under a full-screen fade, which is precisely the failure it is here to catch.
    ///
    /// Enable with -e2e. Always pair it with -data-path: this drives the real SaveSystem.
    /// </summary>
    public sealed class E2ERunner : MonoBehaviour
    {
        /// <summary>Moves the trial gets before the run calls it stuck. The contest's own limit
        /// is eight; anything past that means the limit is not being enforced.</summary>
        const int ContestTurnCap = 14;

        /// <summary>Panels one morning may stack before the run calls it stuck.</summary>
        const int MorningPanelCap = 6;

        const string EnableFlag = "-e2e";
        const string OutputFlag = "-e2e-out";

        /// <summary>The marker Loc and ScriptureService render when a string could not be resolved.</summary>
        const char MissingMarkerOpen = '⟨';

        const float StepTimeoutSeconds = 30f;
        const int MaxDialogueAdvances = 120;

        /// <summary>
        /// Hard ceiling on a whole run. Without it a stalled step is indistinguishable from a slow
        /// one and the process sits there forever, which is worse than a failure: a failure gets
        /// read, a hang gets killed by whoever notices first.
        /// </summary>
        /// Raised from 240 when the run grew from one day to three: days two and three add two
        /// mornings, a whole trial and the reader, and a ceiling that a passing run cannot clear
        /// reports every slow run as a hang.
        const float RunTimeoutSeconds = 600f;

        readonly List<string> _failures = new List<string>();
        readonly List<string> _steps = new List<string>();
        readonly List<string> _shots = new List<string>();

        string _outputDirectory;
        string _runLocale = Locales.Source;
        bool _finished;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (!HasFlag(EnableFlag))
            {
                return;
            }

            // A macOS player stops rendering when its window loses focus, and this one is launched
            // from a terminal that keeps it. Without this, the first WaitForEndOfFrame never
            // returns and the run hangs before its first screenshot.
            Application.runInBackground = true;

            // -data-path does not cover PlayerPrefs, and this run taps the language toggle. Without
            // this the run would change the language a person gets on their next launch.
            Locales.SuppressPersistence = true;

            var host = new GameObject("E2ERunner");
            DontDestroyOnLoad(host);
            host.AddComponent<E2ERunner>();
        }

        IEnumerator Start()
        {
            _outputDirectory = ReadFlagValue(OutputFlag) ?? Path.Combine(AppPaths.DataRoot, "e2e");
            Directory.CreateDirectory(_outputDirectory);

            // The locale this run *started* in, held for the whole run.
            //
            // Every artefact is named for it, and it must not be read live: the run switches
            // language halfway through on purpose, so a live read had the pt-BR run writing its
            // screenshots and its result file under "en" — quietly overwriting the artefacts the
            // English run had just produced, and reporting itself as the English run in the log.
            _runLocale = Locales.Active;

            Application.logMessageReceived += OnLog;
            Debug.Log("[E2E] Started. locale=" + _runLocale + " out=" + _outputDirectory);

            StartCoroutine(Watchdog());
            yield return RunScript();

            _finished = true;
            Application.logMessageReceived -= OnLog;
            WriteResult();

            // A player build ignores the exit code unless it is given one explicitly.
            Application.Quit(_failures.Count == 0 ? 0 : 1);
        }

        /// <summary>Ends the process if the script stops making progress, so a stall is reported.</summary>
        IEnumerator Watchdog()
        {
            float deadline = Time.realtimeSinceStartup + RunTimeoutSeconds;
            while (!_finished && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            if (_finished)
            {
                yield break;
            }

            _failures.Add("the run did not finish within " + RunTimeoutSeconds + "s");
            Debug.Log("[E2E] FAIL watchdog — the run did not finish within " + RunTimeoutSeconds + "s");
            WriteResult();
            Application.Quit(1);
        }

        // ------------------------------------------------------------------ the script

        IEnumerator RunScript()
        {
            // 1. The opening. Nothing has been touched yet; this is the first thing a player sees.
            yield return WaitForObject("DialogueCanvas", "the opening dialogue");
            yield return Capture("01-opening");

            // 2. Through the opening to the one screen that asks the player for something.
            yield return AdvanceDialogueUntil("CharacterCreationCanvas", "character creation");
            yield return Capture("02-creation");
            CheckLanguageToggle();

            // 3. Take the quick route out of creation, the way a player in a hurry would.
            yield return Tap("QuickStart", "the quick-start button");

            // 4. Through the rest of the opening to the game proper. The condition is that the HUD
            // is actually on screen, not that its object exists: HUD.SetVisible toggles
            // Canvas.enabled and leaves the GameObject active, so "the object is there" is true
            // through the whole cutscene and would screenshot a fade.
            yield return AdvanceDialogueUntil(IsHudVisible, "the HUD");
            yield return Capture("03-hud");
            CheckHud();

            // 5. The whole-screen sweep. Cheap, and it catches every missing string at once.
            CheckNoMissingStrings();

            // 6. The toggle, in the authoring locale's run only — one pass is enough to prove the
            // path, and it is the only thing here that exercises a language change at runtime
            // rather than a language pinned at boot. Everything else in this file would pass on a
            // build whose toggle did nothing at all, which is exactly what it once did.
            if (Locales.Active == Locales.Source)
            {
                yield return SwitchLanguageAndVerify();
            }

            // 7. A whole day, ended by nobody.
            yield return PlayADayToItsEnd();

            // 8. Day two, and the night that follows it.
            yield return SpendTheDay("day two", "09");

            // 9. Day three: the trial, A Página, the reader and the reveal.
            yield return PlayDayThree();
        }

        // ------------------------------------------------------------------ the rest of the run

        /// <summary>
        /// Clears the morning report, spends the day, and confirms the split the spent day opens.
        ///
        /// <see cref="PlayADayToItsEnd"/> asserts the shape of the new loop on day one and stops at
        /// the morning after it. This is the same loop with nothing to prove about it, run for the
        /// only reason that day three cannot be reached without it.
        /// </summary>
        IEnumerator SpendTheDay(string dayName, string prefix)
        {
            yield return ClearTheMorning(dayName, prefix);

            DayCycle cycle = UnityEngine.Object.FindFirstObjectByType<DayCycle>();
            ResourceSystem resources = ResourceSystem.Find();
            GameState state = TryGetState();

            if (cycle == null || resources == null || state == null)
            {
                Record(dayName + " can be played", false,
                    "cycle=" + (cycle != null) + " resources=" + (resources != null) + " state=" + (state != null));
                yield break;
            }

            int startingDay = state.day;
            yield return Capture(prefix + "-" + dayName.Replace(" ", "-"));

            resources.Spend(state.workCapacity);
            yield return WaitForTheSplit(cycle, dayName);
            yield return Tap("Confirm", "the split on " + dayName);
            yield return WaitUntil(() => state.day > startingDay && !cycle.IsResolving, "the morning after " + dayName);

            Record(dayName + " rolled over", state.day == startingDay + 1,
                "day " + startingDay + " -> " + state.day);
        }

        /// <summary>
        /// Waits for the split, clearing anything that turns up while waiting, and naming what is
        /// holding the night if it never comes.
        ///
        /// A morning does not stack all of its panels at once: the report arrives with the morning,
        /// but the day's check-in waits for a quiet world, so it can land well after
        /// <see cref="ClearTheMorning"/> has already declared the screen clear. Because dusk waits
        /// on <c>ModalRoot.IsOpen</c>, a panel that shows up late does not merely sit there — it
        /// stops the day ending at all, and the only symptom is a timeout thirty seconds and one
        /// step away from the cause. Hence both halves of this: keep clearing, and if the split
        /// still never opens, say which of the three conditions in <see cref="DayCycle.DuskWaits"/>
        /// is the one holding it.
        /// </summary>
        IEnumerator WaitForTheSplit(DayCycle cycle, string dayName)
        {
            float deadline = Time.realtimeSinceStartup + StepTimeoutSeconds;

            while (!EndDayPanel.IsOpen && Time.realtimeSinceStartup < deadline)
            {
                if (ModalRoot.IsOpen)
                {
                    GameObject action = Find("Continue") ?? Find("Option_0") ?? Find("Confirm");
                    if (action != null)
                    {
                        yield return TapObject(action, "clear " + action.name + " holding the night of " + dayName);
                        yield return new WaitForSeconds(0.35f);
                        continue;
                    }
                }

                yield return null;
            }

            if (!EndDayPanel.IsOpen)
            {
                Record("waited for the split on " + dayName, false, "it never happened within "
                    + StepTimeoutSeconds + "s — dusk pending=" + (cycle != null && cycle.IsDuskPending)
                    + " held=" + (cycle != null && cycle.IsDuskHeld)
                    + " input locked=" + InputLock.IsLocked
                    + " modal=" + (ModalRoot.IsOpen ? (ModalRoot.TopId ?? "unnamed") : "none"));
            }
        }

        /// <summary>
        /// Clears whatever the morning put on the screen before the day can be spent.
        ///
        /// A morning stacks more than one panel — the report of the night, and the day's check-in —
        /// and the order is not something a script should assume. It matters more than it looks:
        /// dusk waits on <c>ModalRoot.IsOpen</c>, so a panel left up does not merely sit there, it
        /// stops the day ending at all. That is what this found the first time it ran, and the
        /// symptom was a thirty-second timeout half an hour away from the cause.
        /// </summary>
        IEnumerator ClearTheMorning(string dayName, string prefix)
        {
            bool captured = false;

            for (int panel = 0; panel < MorningPanelCap && ModalRoot.IsOpen; panel++)
            {
                if (!captured)
                {
                    yield return Capture(prefix + "-morning-" + dayName.Replace(" ", "-"));
                    CheckNoMissingStrings();
                    captured = true;
                }

                // Dismissing comes before answering, and the order is the whole trick. A quiz
                // that has been answered keeps its options on screen — they are only tinted and
                // made non-interactable — so a run that preferred Option_0 would answer once and
                // then tap a dead option for as long as the loop allowed, never reaching the
                // Continue that closes the panel. Continue only exists once the day is checked in
                // (DailyQuiz builds it inactive and shows it in OnOptionChosen) and Find ignores
                // inactive objects, so preferring it can never skip the answer.
                GameObject action = Find("Continue") ?? Find("Option_0") ?? Find("Confirm");
                if (action == null)
                {
                    Record("the morning of " + dayName + " could be cleared", false,
                        "a modal is open with no button this run knows: " + (ModalRoot.TopId ?? "unnamed"));
                    yield break;
                }

                yield return TapObject(action, "clear " + action.name + " on the morning of " + dayName);
                yield return new WaitForSeconds(0.35f);
            }

            Record("the morning of " + dayName + " is clear", !ModalRoot.IsOpen,
                ModalRoot.IsOpen ? "still on " + (ModalRoot.TopId ?? "unnamed") : "nothing is holding the day");
        }

        /// <summary>
        /// Day three, which is the half of the game that nothing automated has ever reached.
        ///
        /// Two verification rounds found the trial unreachable in a built player while every
        /// unit-level rule about it passed: <c>MoraleContest.Begin()</c> had no runtime caller at
        /// all. The acceptance harness cannot catch that, because it constructs systems directly
        /// and never composes a scene. This can, and that is the only reason it is worth the
        /// minutes it costs.
        /// </summary>
        IEnumerator PlayDayThree()
        {
            yield return ClearTheMorning("day three", "10a");

            // Day three opens itself, but only into a quiet world: Day3Director wants six seconds
            // with no panel up and no line half spoken before it starts the trial. The residents
            // have something to say on that morning, and a line waits for a tap — so waiting here
            // rather than advancing would wait for a menu that is being held back by the very
            // dialogue nobody is dismissing.
            yield return AdvanceDialogueUntil(() => Find("Move_hold_line") != null, "the trial", true);
            yield return Capture("10-trial");
            CheckNoMissingStrings();

            Record("half and half is locked before the Page", Find("Move_half_and_half") == null,
                Find("Move_half_and_half") == null
                    ? "the move is not in the menu"
                    : "the move is offered before the Page has been shown");

            // One move, and then the beat the whole POC exists to test.
            yield return Tap("Move_hold_line", "holding the line");
            yield return WaitForObject("CloseButton", "A Página");
            yield return Capture("11-the-page");
            CheckNoMissingStrings();

            // Saber mais is the only way into the chapter reader, and that tap is where deep_read
            // begins. The event itself is deliberately not asserted: it needs twenty seconds of
            // visible time and sixty percent of the chapter scrolled, which measures a person
            // rather than a script, and faking it would corrupt the one number this product
            // exists to collect. What this proves is that the door opens.
            yield return Tap("ReadButton", "saber mais");
            yield return WaitForObject("Close", "the chapter reader");
            yield return Capture("12-reader");
            yield return Tap("Close", "closing the reader");

            yield return Tap("CloseButton", "closing A Página");
            yield return WaitForObject("Move_half_and_half", "half and half");
            Record("the Page unlocked half and half", Find("Move_half_and_half") != null,
                "the move is in the menu once the Page has been read");
            yield return Capture("13-unlocked");

            yield return PlayOutTheTrial();
            yield return Capture("14-outcome");
            yield return Tap("Continue", "the trial outcome");

            // The gate closes with the player's name on it before the vocation is named, and it is
            // a panel of its own (gate_closed) with its own way on. Waiting for the reveal's Close
            // straight after the trial waits for the screen after the one that is up.
            yield return WaitForObject("KnowMore", "the gate closed with the player's name");
            yield return Capture("15-gate");
            CheckNoMissingStrings();

            yield return Tap("Continue", "past the gate");

            yield return WaitForObject("Close", "the vocation reveal");
            yield return Capture("16-reveal");
            CheckNoMissingStrings();
        }

        /// <summary>
        /// Takes turns until the trial resolves. Bounded rather than open ended: the contest has a
        /// turn limit of eight, so a loop that never finished would mean the limit is not being
        /// enforced — worth failing on rather than hanging on.
        /// </summary>
        IEnumerator PlayOutTheTrial()
        {
            string[] preferred = { "Move_half_and_half", "Move_show_watch", "Move_call_others", "Move_hold_line" };

            for (int turn = 0; turn < ContestTurnCap; turn++)
            {
                if (Find("Continue") != null)
                {
                    Record("the trial resolved", true, "an outcome was reached in " + turn + " move(s)");
                    yield break;
                }

                GameObject move = null;
                for (int i = 0; i < preferred.Length && move == null; i++)
                {
                    move = Find(preferred[i]);
                }

                if (move == null)
                {
                    yield return new WaitForSeconds(0.25f);
                    continue;
                }

                yield return TapObject(move, "tap " + move.name);
                yield return new WaitForSeconds(0.4f);
            }

            Record("the trial resolved", Find("Continue") != null,
                "no outcome after " + ContestTurnCap + " moves");
        }

        static GameState TryGetState()
        {
            try
            {
                return ServiceLocator.Get<GameState>();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Spends the day's work and watches the day end on its own.
        ///
        /// This is the only step that exercises <see cref="DayCycle"/>'s Update loop, and it has to
        /// live here: the acceptance harness drives the night through the component's synchronous
        /// path with the component disabled, so the daylight clock never runs there at all. What
        /// this asserts is the shape of the new loop end to end in a real composed scene — the
        /// light falls as the work is spent, the split arrives with nobody asking for it, and the
        /// morning that follows is a new day with its capacity back.
        ///
        /// The work is spent through the resource system rather than by walking the player to the
        /// wall and tapping it. Pathing across the village is the interaction layer's business and
        /// is not what changed; what changed is what the clock does once the capacity is gone.
        /// </summary>
        IEnumerator PlayADayToItsEnd()
        {
            DayCycle cycle = UnityEngine.Object.FindFirstObjectByType<DayCycle>();
            ResourceSystem resources = ResourceSystem.Find();
            GameState state = null;
            try
            {
                state = ServiceLocator.Get<GameState>();
            }
            catch (Exception)
            {
                state = null;
            }

            if (cycle == null || resources == null || state == null)
            {
                Record("the day can be played", false,
                    "cycle=" + (cycle != null) + " resources=" + (resources != null) + " state=" + (state != null));
                yield break;
            }

            int startingDay = state.day;
            Record("the day starts with its light up", cycle.NightAmount < 0.02f,
                "night amount " + cycle.NightAmount.ToString("0.000"));

            // Half the day's work. The light must move, and the split must not arrive yet.
            int capacity = state.workCapacity;
            resources.Spend(capacity / 2);
            yield return WaitForTheLight(cycle);

            float halfway = cycle.NightAmount;
            Record("spending the day pulls the light down",
                Mathf.Abs(halfway - DayCycle.DaylightFor(0.5f)) < 0.01f,
                "night amount " + halfway.ToString("0.000") + " after spending " + (capacity / 2)
                + ", expected " + DayCycle.DaylightFor(0.5f).ToString("0.000"));

            // The clock has to be legible, not merely present. A half-spent day used to settle at
            // a 6% wash, which is a number that moved and a screen that did not.
            float wash = halfway * DayCycle.MaxTintAlpha;
            Record("half a day is visibly later than dawn", wash > 0.08f,
                "the wash at half a day is " + wash.ToString("0.000") + " alpha");
            Record("half a day is not the end of one", !EndDayPanel.IsOpen && !cycle.IsDuskPending,
                "dusk pending=" + cycle.IsDuskPending + " split open=" + EndDayPanel.IsOpen);
            yield return Capture("05-afternoon");

            // The mat, and the way back off it. Interact() rather than a walk across the village:
            // pathing is the interaction layer's business and is not what changed here.
            RestPoint mat = UnityEngine.Object.FindFirstObjectByType<RestPoint>();
            Record("there is a mat to turn in on", mat != null && mat.IsAvailable,
                mat == null ? "no RestPoint in the scene" : "available=" + mat.IsAvailable);

            if (mat != null)
            {
                mat.Interact();
                yield return WaitUntil(() => EndDayPanel.IsOpen, "the split the mat asked for");
                Record("the mat brings the evening on", EndDayPanel.IsOpen,
                    "split open=" + EndDayPanel.IsOpen);

                // Turning in early is a choice, so it has to be one that can be taken back.
                Record("an early night can be turned down", Find("Defer") != null,
                    Find("Defer") != null ? "the split offers a way back" : "no defer button on a day with work left");
                yield return Capture("06-turning-in");

                yield return Tap("Defer", "the way back to the day");
                yield return WaitUntil(() => !EndDayPanel.IsOpen, "the split to close");
                yield return WaitForTheLight(cycle);

                Record("turning the night down gives the day back",
                    state.day == startingDay && !cycle.IsDuskPending
                    && Mathf.Abs(cycle.NightAmount - DayCycle.DaylightFor(0.5f)) < 0.01f,
                    "day " + state.day + ", dusk pending=" + cycle.IsDuskPending
                    + ", night amount " + cycle.NightAmount.ToString("0.000"));
            }

            // The rest of it. Nothing is tapped from here on: the day has to end by itself.
            resources.Spend(state.workCapacity);

            yield return WaitUntil(() => EndDayPanel.IsOpen, "the split to open itself");

            Record("the day ends without being asked to", EndDayPanel.IsOpen,
                "the split opened with no button pressed");
            Record("dusk is darker than the afternoon was", cycle.NightAmount > halfway,
                "night amount " + cycle.NightAmount.ToString("0.000") + " vs " + halfway.ToString("0.000"));
            yield return Capture("07-dusk");

            // A day with nothing left to spend cannot be turned down, so the split has no way back.
            Record("a spent day offers no way out of the night", Find("Defer") == null,
                Find("Defer") == null ? "no defer button" : "a defer button is on a forced night");

            yield return Tap("Confirm", "the split");
            yield return WaitUntil(() => state.day > startingDay && !cycle.IsResolving, "the morning");

            Record("the night rolls the day over", state.day == startingDay + 1,
                "day " + startingDay + " -> " + state.day);
            Record("the morning gives the day its work back", state.workCapacity == state.workCapacityMax,
                "capacity " + state.workCapacity + "/" + state.workCapacityMax);
            Record("the new day has its light back", cycle.NightAmount < 0.02f,
                "night amount " + cycle.NightAmount.ToString("0.000"));
            yield return Capture("08-morning");
        }

        /// <summary>
        /// Waits for the daylight to stop moving. The light slides toward its target rather than
        /// snapping, so reading it a few frames after the work is spent reads a value in transit.
        /// </summary>
        IEnumerator WaitForTheLight(DayCycle cycle)
        {
            float deadline = Time.realtimeSinceStartup + StepTimeoutSeconds;
            float previous = float.NaN;
            int steady = 0;

            while (steady < 3 && Time.realtimeSinceStartup < deadline)
            {
                float now = cycle.NightAmount;
                steady = Mathf.Approximately(now, previous) ? steady + 1 : 0;
                previous = now;
                yield return null;
            }
        }

        /// <summary>Waits for a condition, and records a failure rather than hanging on it.</summary>
        IEnumerator WaitUntil(Func<bool> condition, string description)
        {
            float deadline = Time.realtimeSinceStartup + StepTimeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            if (!condition())
            {
                Record("waited for " + description, false, "it never happened within " + StepTimeoutSeconds + "s");
            }
        }

        /// <summary>
        /// Taps the other language and proves the words actually changed.
        ///
        /// Reloading the scene is not what changes the language: Loc, GameData and ScriptureService
        /// are statics that outlive a scene load, and only the Boot scene re-runs the boot sequence.
        /// So this asserts on the loaded tables AND on a label read off the screen afterwards —
        /// the first would catch the content failing to reload, the second catches the screen
        /// failing to rebuild with it.
        /// </summary>
        IEnumerator SwitchLanguageAndVerify()
        {
            string target = Locales.Next(Locales.Active);
            string before = TextOf("Day");

            // In the village the language lives one tap deeper than it does in character creation:
            // the HUD carries a settings button and the chips live on the panel it opens. Tapping
            // straight at a chip here reported a missing control, which read as the toggle being
            // broken rather than as this run not having opened the panel.
            yield return Tap("SettingsButton", "the settings button");
            Record("the settings panel opened", ModalRoot.IsOpen && ModalRoot.TopId == SettingsPanel.ModalId,
                "top modal is " + (ModalRoot.TopId ?? "nothing"));

            yield return Tap("Locale_" + target, "the " + target + " language chip");

            float deadline = Time.realtimeSinceStartup + StepTimeoutSeconds;
            while (Loc.LoadedLocale != target && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Record("switching to " + target + " reloads the strings", Loc.LoadedLocale == target,
                "Loc is on " + Loc.LoadedLocale);
            Record("switching to " + target + " reloads the content", GameData.LoadedLocale == target,
                "GameData is on " + GameData.LoadedLocale);
            Record("switching to " + target + " reloads the scripture",
                ScriptureService.LoadedLocale == target,
                "ScriptureService is on " + ScriptureService.LoadedLocale);

            // The scene restarts the opening, so getting back to the HUD is the way to read a
            // label that the switch was supposed to change.
            yield return AdvanceDialogueUntil(IsHudVisible, "the HUD again after switching");
            yield return Capture("04-switched");

            string after = TextOf("Day");
            Record("the screen rebuilt in " + target,
                !string.IsNullOrEmpty(after) && after != before,
                "was \"" + (before ?? "nothing") + "\", now \"" + (after ?? "nothing") + "\"");
            Record("the label matches the new table", after == Loc.T("hud.day", 1),
                "expected \"" + Loc.T("hud.day", 1) + "\", found \"" + (after ?? "nothing") + "\"");

            CheckNoMissingStrings();
        }

        // ------------------------------------------------------------------ assertions

        /// <summary>
        /// Fails when any label on screen is a resolution marker. This is the check that makes a
        /// half-translated build impossible to ship: it does not care which key is missing or which
        /// screen it is on, only that a player would be looking at ⟨something⟩ right now.
        /// </summary>
        void CheckNoMissingStrings()
        {
            var offenders = new List<string>();
            foreach (Text text in Resources.FindObjectsOfTypeAll<Text>())
            {
                if (text == null || string.IsNullOrEmpty(text.text)) continue;
                if (!IsInLiveScene(text.gameObject)) continue;
                if (text.text.IndexOf(MissingMarkerOpen) >= 0)
                {
                    offenders.Add(PathOf(text.gameObject) + " = " + text.text);
                }
            }

            Record("no unresolved strings on screen", offenders.Count == 0,
                offenders.Count == 0 ? "every label resolved" : string.Join(" | ", offenders));
        }

        /// <summary>The toggle must show the language actually running, and must not offer it again.</summary>
        void CheckLanguageToggle()
        {
            GameObject activeChip = Find("Locale_" + Locales.Active);
            Record("the language toggle shows " + Locales.Active, activeChip != null,
                activeChip != null ? "chip present" : "no chip named Locale_" + Locales.Active);

            if (activeChip == null) return;

            Button button = activeChip.GetComponent<Button>();
            Record("the active language is not offered again", button != null && !button.interactable,
                button == null ? "no Button component" : "interactable=" + button.interactable);
        }

        /// <summary>The HUD reads from the same locale table the rest of the run does.</summary>
        void CheckHud()
        {
            GameState state = null;
            try
            {
                state = ServiceLocator.Get<GameState>();
            }
            catch (Exception exception)
            {
                Record("the run has a game state", false, exception.Message);
                return;
            }

            string expected = Loc.T("hud.day", Mathf.Max(1, state.day));
            string actual = TextOf("Day");
            Record("the HUD day reads the locale string", actual == expected,
                "expected \"" + expected + "\", found \"" + (actual ?? "nothing") + "\"");
        }

        // ------------------------------------------------------------------ driving

        /// <summary>
        /// Clicks a control the way the EventSystem would: a raycast at its own centre, and a
        /// refusal to dispatch unless that ray reaches it. A control that is present in the
        /// hierarchy but covered by something else fails here rather than passing quietly.
        /// </summary>
        IEnumerator Tap(string objectName, string description)
        {
            GameObject target = null;
            float deadline = Time.realtimeSinceStartup + StepTimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                target = Find(objectName);
                if (target != null) break;
                yield return null;
            }

            if (target == null)
            {
                Record("tap " + description, false, "no active object named " + objectName);
                yield break;
            }

            yield return TapObject(target, "tap " + description);
        }

        /// <summary>
        /// Raycasts at the control until the ray actually reaches it, then clicks whatever it
        /// reached.
        ///
        /// The retry is not politeness. A panel built this frame has not been through a canvas
        /// batch yet, and until it has, every graphic on it still reports depth -1 — which the
        /// graphic raycaster skips outright, so a perfectly live button raycasts to nothing at
        /// all. Judging on the first frame reported the settings panel's language chips as
        /// unreachable when they were merely one frame old.
        ///
        /// What the retry must not do is soften the verdict: a control genuinely under an opaque
        /// panel still fails, having spent the timeout proving it.
        /// </summary>
        IEnumerator TapObject(GameObject target, string step)
        {
            EventSystem events = EventSystem.current;
            if (events == null)
            {
                Record(step, false, "there is no EventSystem in the scene");
                yield break;
            }

            var rect = target.transform as RectTransform;
            if (rect == null)
            {
                Record(step, false, PathOf(target) + " is not a RectTransform");
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + StepTimeoutSeconds;
            var hits = new List<RaycastResult>();
            Vector2 screenPoint = Vector2.zero;
            GameObject reached = null;
            string reason = null;

            while (true)
            {
                screenPoint = ScreenCentreOf(rect);
                var probe = new PointerEventData(events) { position = screenPoint };
                hits.Clear();
                events.RaycastAll(probe, hits);

                // Only the topmost hit matters: that is the one a finger would reach. Looking
                // further down the list would find the target under an opaque panel and click it
                // anyway, which is the exact bug this method exists to catch.
                if (hits.Count == 0)
                {
                    reason = PathOf(target) + " is not hit by a ray at " + screenPoint;
                }
                else if (!IsSelfOrDescendant(hits[0].gameObject, target))
                {
                    reason = PathOf(target) + " is covered by " + PathOf(hits[0].gameObject)
                             + " at " + screenPoint;
                }
                else
                {
                    // A ray reaching the control is not the same as the control accepting a click.
                    // A panel that slides in holds its CanvasGroup non-interactable until the
                    // animation ends, and uGUI drops the click in silence: Button.Press returns
                    // early on IsInteractable, no exception, no log, nothing on screen. Judging on
                    // the ray alone therefore reported "clicked" for a tap that did nothing at all,
                    // and every assertion after it failed somewhere else — which is how A Página's
                    // Saber mais looked like a broken button for a whole evening.
                    var selectable = target.GetComponent<Selectable>();
                    if (selectable != null && !selectable.IsInteractable())
                    {
                        reason = PathOf(target) + " is not accepting clicks yet at " + screenPoint;
                    }
                    else
                    {
                        reached = hits[0].gameObject;
                        break;
                    }
                }

                if (Time.realtimeSinceStartup >= deadline)
                {
                    break;
                }

                yield return null;
            }

            if (reached == null)
            {
                Record(step, false, reason);
                yield break;
            }

            var pointer = new PointerEventData(events) { position = screenPoint };
            ExecuteEvents.Execute(reached, pointer, ExecuteEvents.pointerClickHandler);
            Record(step, true, "clicked " + PathOf(reached));
            yield return null;
        }

        /// <summary>
        /// Taps through dialogue until the named object appears. The tap goes to the real catcher,
        /// so a dialogue that has stopped receiving input stalls here instead of being stepped over.
        /// </summary>
        IEnumerator AdvanceDialogueUntil(string objectName, string description)
        {
            return AdvanceDialogueUntil(() => Find(objectName) != null, description);
        }

        /// <summary>
        /// True when the HUD is on screen and nothing is covering it. Both halves matter: the
        /// cutscene hides the HUD by disabling its canvas rather than its object, and it fades the
        /// screen to black over the top of everything between beats.
        /// </summary>
        static bool IsHudVisible()
        {
            GameObject hud = Find("HUDCanvas");
            if (hud == null) return false;

            Canvas canvas = hud.GetComponent<Canvas>();
            if (canvas == null || !canvas.enabled) return false;

            GameObject fadeCanvas = Find("IntroFadeCanvas");
            if (fadeCanvas != null)
            {
                Image fade = fadeCanvas.GetComponentInChildren<Image>();
                if (fade != null && fade.color.a > 0.05f) return false;
            }

            return true;
        }

        /// <param name="clearPanels">
        /// Also dismisses any panel that turns up on the way. Off by default, because most waits
        /// here are for something that lives *inside* a panel and closing it would step over the
        /// thing being waited for. It is on for the trial, where the opposite is true: Day3Director
        /// only starts once the world is quiet, and the day's check-in arrives after the morning
        /// has already been cleared — so a run that only taps dialogue waits forever for a beat
        /// that is being held open by the panel it is ignoring.
        /// </param>
        IEnumerator AdvanceDialogueUntil(Func<bool> condition, string description, bool clearPanels = false)
        {
            for (int advance = 0; advance < MaxDialogueAdvances; advance++)
            {
                if (condition())
                {
                    Record("reached " + description, true, "after " + advance + " advance(s)");
                    yield break;
                }

                if (clearPanels && ModalRoot.IsOpen)
                {
                    GameObject panelAction = Find("Continue") ?? Find("Option_0") ?? Find("Confirm");
                    if (panelAction != null)
                    {
                        yield return TapObject(panelAction,
                            "clear " + panelAction.name + " on the way to " + description);
                        yield return new WaitForSeconds(0.35f);
                        continue;
                    }
                }

                DialogueTapCatcher catcher = FindActive<DialogueTapCatcher>();
                if (catcher != null)
                {
                    ExecuteEvents.Execute(
                        catcher.gameObject,
                        new PointerEventData(EventSystem.current),
                        ExecuteEvents.pointerClickHandler);
                }

                // Cutscenes move the camera and walk characters between lines, so a tap that lands
                // during a transition is simply ignored. Waiting a beat is what a person does too.
                yield return new WaitForSeconds(0.25f);
            }

            Record("reached " + description, false,
                "still not present after " + MaxDialogueAdvances + " dialogue advances");
        }

        IEnumerator WaitForObject(string objectName, string description)
        {
            float deadline = Time.realtimeSinceStartup + StepTimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (Find(objectName) != null)
                {
                    Record("reached " + description, true, objectName + " is on screen");
                    yield break;
                }
                yield return null;
            }

            // Naming what IS on screen turns "it never appeared" into a diagnosis. A panel that
            // failed to open and a panel that opened under something else read identically
            // otherwise, and they are not the same bug.
            Record("reached " + description, false, objectName + " never appeared — modal depth "
                + ModalRoot.OpenCount + ", top " + (ModalRoot.IsOpen ? (ModalRoot.TopId ?? "unnamed") : "none"));
        }

        IEnumerator Capture(string name)
        {
            string fileName = name + "-" + _runLocale + ".png";
            string path = Path.Combine(_outputDirectory, fileName);

            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(path);

            // CaptureScreenshot returns before the file lands; the next frames are when it appears.
            for (int frame = 0; frame < 60 && !File.Exists(path); frame++)
            {
                yield return null;
            }

            bool written = File.Exists(path);
            if (written) _shots.Add(fileName);
            Record("screenshot " + name, written, written ? path : "the file was never written");
        }

        // ------------------------------------------------------------------ scene lookup

        /// <summary>
        /// Finds an active object by name across every root, including DontDestroyOnLoad.
        /// GameObject names are English identifiers by convention, which is what makes them usable
        /// as test handles: they do not change when the language does.
        /// </summary>
        static GameObject Find(string name)
        {
            foreach (Transform transform in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (transform == null || transform.name != name) continue;
                GameObject go = transform.gameObject;
                if (IsInLiveScene(go) && go.activeInHierarchy) return go;
            }
            return null;
        }

        static T FindActive<T>() where T : Component
        {
            foreach (T component in Resources.FindObjectsOfTypeAll<T>())
            {
                if (component == null) continue;
                if (IsInLiveScene(component.gameObject) && component.gameObject.activeInHierarchy) return component;
            }
            return null;
        }

        /// <summary>Excludes prefabs and assets, which FindObjectsOfTypeAll also returns.</summary>
        static bool IsInLiveScene(GameObject go)
        {
            return go != null && go.scene.IsValid() && go.hideFlags == HideFlags.None;
        }

        static string TextOf(string objectName)
        {
            GameObject go = Find(objectName);
            if (go == null) return null;
            Text text = go.GetComponent<Text>();
            return text != null ? text.text : null;
        }

        static bool IsSelfOrDescendant(GameObject candidate, GameObject target)
        {
            for (Transform t = candidate.transform; t != null; t = t.parent)
            {
                if (t.gameObject == target) return true;
            }
            return false;
        }

        static Vector2 ScreenCentreOf(RectTransform rect)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector3 centre = (corners[0] + corners[2]) * 0.5f;

            Canvas canvas = rect.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay && canvas.worldCamera != null)
            {
                return RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, centre);
            }

            // Screen-space overlay: world corners are already in screen coordinates.
            return new Vector2(centre.x, centre.y);
        }

        static string PathOf(GameObject go)
        {
            var builder = new StringBuilder(go.name);
            for (Transform t = go.transform.parent; t != null; t = t.parent)
            {
                builder.Insert(0, t.name + "/");
            }
            return builder.ToString();
        }

        // ------------------------------------------------------------------ reporting

        void OnLog(string message, string stackTrace, LogType type)
        {
            // An error logged during the opening is a failure even when the screen looks right:
            // a missing locale key reports itself this way before anyone sees the marker.
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                _failures.Add("logged " + type + ": " + message);
            }
        }

        void Record(string step, bool passed, string detail)
        {
            _steps.Add((passed ? "PASS  " : "FAIL  ") + step + " — " + detail);
            if (!passed)
            {
                _failures.Add(step + ": " + detail);
            }
            Debug.Log("[E2E] " + (passed ? "PASS " : "FAIL ") + step + " — " + detail);
        }

        void WriteResult()
        {
            var report = new StringBuilder();
            report.AppendLine("{");
            report.AppendLine("  \"locale\": " + Quote(_runLocale) + ",");
            report.AppendLine("  \"passed\": " + (_failures.Count == 0 ? "true" : "false") + ",");
            report.AppendLine("  \"steps\": [");
            for (int i = 0; i < _steps.Count; i++)
            {
                report.AppendLine("    " + Quote(_steps[i]) + (i < _steps.Count - 1 ? "," : ""));
            }
            report.AppendLine("  ],");
            report.AppendLine("  \"screenshots\": [");
            for (int i = 0; i < _shots.Count; i++)
            {
                report.AppendLine("    " + Quote(_shots[i]) + (i < _shots.Count - 1 ? "," : ""));
            }
            report.AppendLine("  ],");
            report.AppendLine("  \"failures\": [");
            for (int i = 0; i < _failures.Count; i++)
            {
                report.AppendLine("    " + Quote(_failures[i]) + (i < _failures.Count - 1 ? "," : ""));
            }
            report.AppendLine("  ]");
            report.AppendLine("}");

            string path = Path.Combine(_outputDirectory, "result-" + _runLocale + ".json");
            try
            {
                File.WriteAllText(path, report.ToString());
                Debug.Log("[E2E] Result -> " + path);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[E2E] Could not write " + path + ": " + exception.Message);
            }

            Debug.Log("[E2E] " + (_failures.Count == 0
                ? "ALL STEPS PASSED (" + _runLocale + ")"
                : _failures.Count + " FAILURE(S) (" + _runLocale + ")"));
        }

        static string Quote(string value)
        {
            var builder = new StringBuilder("\"");
            foreach (char c in value ?? string.Empty)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20) builder.Append("\\u").Append(((int)c).ToString("x4"));
                        else builder.Append(c);
                        break;
                }
            }
            return builder.Append('"').ToString();
        }

        // ------------------------------------------------------------------ command line

        static bool HasFlag(string flag)
        {
            string[] args = SafeArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        static string ReadFlagValue(string flag)
        {
            string[] args = SafeArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            }
            return null;
        }

        static string[] SafeArgs()
        {
            try
            {
                return Environment.GetCommandLineArgs() ?? Array.Empty<string>();
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }
    }
}
