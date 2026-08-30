using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SheepGate.Art;
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
        /// How long the run waits for a resident to take a step of their own. Generous next to the
        /// pause NpcWander draws between steps, because the pause is randomised per resident and the
        /// village stands still for anything modal that turns up while the run is watching.
        /// </summary>
        const float WanderTimeoutSeconds = 20f;

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

            // The readouts moved into the drawer, so reading one means opening it first.
            yield return OpenHudMenu();
            yield return Capture("03b-hud-menu");
            CheckHud();

            // 5. The whole-screen sweep. Cheap, and it catches every missing string at once.
            CheckNoMissingStrings();

            // 6. The village is populated, not staged: with nothing on screen asking for anything,
            // somebody should be walking around out there.
            yield return VerifyResidentsWander();

            // 7. Help: the one screen that tells a lost player what to do next.
            yield return VerifyHelpPanel();

            // 8. The HUD map is a real progression surface now: generated sprites underneath,
            // localized state and reward copy above, and catalogue conditions deciding what is
            // actually open. Exercise the public button so a beautiful map hidden behind a broken
            // route cannot pass.
            yield return VerifyProgressMap();

            // 9. The other public button on the HUD, and the only screen in the game where a tap
            // changes the character. Nothing automated had ever opened it.
            yield return VerifyBackpack();

            // 10. The toggle, in the authoring locale's run only — one pass is enough to prove the
            // path, and it is the only thing here that exercises a language change at runtime
            // rather than a language pinned at boot. Everything else in this file would pass on a
            // build whose toggle did nothing at all, which is exactly what it once did.
            if (Locales.Active == Locales.Source)
            {
                yield return SwitchLanguageAndVerify();
            }

            // 11. A whole day, ended by nobody.
            yield return PlayADayToItsEnd();

            // 12. Day two, and the night that follows it.
            yield return SpendTheDay("day two", "09");

            // 13. Day three: the trial, A Página, the reader and the reveal.
            yield return PlayDayThree();
        }

        // ------------------------------------------------------------------ the rest of the run

        /// <summary>
        /// Opens help and proves it says something.
        ///
        /// The assertion is that an instruction is on screen and that it resolves — a panel whose
        /// one job is to answer "what now?" fails silently if its string table drifts, and a
        /// missing-key marker in the one place a lost player looks is the worst place to have one.
        /// The specific step is not asserted: which instruction is current depends on what this run
        /// has done by now, and pinning it here would make the panel's own logic untestable from
        /// the outside.
        /// </summary>
        IEnumerator VerifyHelpPanel()
        {
            yield return Tap("HelpButton", "the help button");
            yield return WaitForObject("CurrentStep", "the current instruction");

            string instruction = TextOf("CurrentStep");
            Record("help says what to do next",
                !string.IsNullOrEmpty(instruction) && instruction.IndexOf(MissingMarkerOpen) < 0,
                "instruction=\"" + (instruction ?? "nothing") + "\"");

            yield return Capture("03c-help");
            yield return Tap("Close", "the help panel's close button");
        }

        /// <summary>
        /// Opens the HUD drawer, which is where everything except the menu button now lives.
        ///
        /// A step of its own rather than a tap inlined into each caller, because the failure it
        /// prevents is the confusing kind: tapping straight at a control that is inside a closed
        /// drawer reports a missing control, which reads as that control being broken rather than
        /// as this run never having opened the drawer. The language chips already taught this
        /// project that lesson once, one level up.
        /// </summary>
        IEnumerator OpenHudMenu()
        {
            HUD hud = UnityEngine.Object.FindFirstObjectByType<HUD>();
            if (hud != null && hud.IsMenuOpen)
            {
                yield break;
            }

            yield return Tap("MenuButton", "the HUD menu button");

            hud = UnityEngine.Object.FindFirstObjectByType<HUD>();
            Record("the HUD menu opens", hud != null && hud.IsMenuOpen,
                hud == null ? "no HUD in the scene" : "IsMenuOpen=" + hud.IsMenuOpen);
        }

        /// <summary>
        /// Proves the village is inhabited rather than staged: with nothing asking for the player's
        /// attention, at least one resident walks off the cell they were spawned on.
        ///
        /// Worth a step of its own because the failure mode here is silent rather than broken.
        /// <see cref="NpcWander"/> holds the whole village still while a modal owns the input, while
        /// a conversation plays and from dusk onwards, so a hold that never lifted would look
        /// exactly like the feature having been dropped. Hence the detail on failure: it says
        /// whether the village was being held at the moment the run gave up, which is the difference
        /// between a bug in the wander and a bug in what is allowed to hold it.
        /// </summary>
        IEnumerator VerifyResidentsWander()
        {
            NpcActor[] residents = UnityEngine.Object.FindObjectsByType<NpcActor>(FindObjectsSortMode.None);
            if (residents.Length == 0)
            {
                Record("residents wander", false, "no residents in the scene");
                yield break;
            }

            Dictionary<NpcActor, Vector2Int> spawned = new Dictionary<NpcActor, Vector2Int>(residents.Length);
            for (int i = 0; i < residents.Length; i++)
            {
                if (residents[i] != null)
                {
                    spawned[residents[i]] = residents[i].Cell;
                }
            }

            NpcActor walker = null;
            float deadline = Time.realtimeSinceStartup + WanderTimeoutSeconds;

            while (walker == null && Time.realtimeSinceStartup < deadline)
            {
                foreach (KeyValuePair<NpcActor, Vector2Int> entry in spawned)
                {
                    if (entry.Key != null && entry.Key.Cell != entry.Value)
                    {
                        walker = entry.Key;
                        break;
                    }
                }

                yield return null;
            }

            if (walker == null)
            {
                Record("residents wander", false,
                    residents.Length + " resident(s) held their cell for " + WanderTimeoutSeconds +
                    "s (village held: " + NpcWander.VillageIsHeld() + ")");
                yield break;
            }

            Record("residents wander", true,
                walker.NpcId + " walked " + spawned[walker] + " -> " + walker.Cell);
        }

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

        IEnumerator VerifyProgressMap()
        {
            yield return Tap("MapButton", "the progression map button");
            yield return WaitForObject("WorldMapViewCanvas", "the progression map");
            yield return null;

            Record("the progression map opens from the HUD", WorldMapView.IsOpen,
                "WorldMapView.IsOpen=" + WorldMapView.IsOpen);
            Record("the progression map has all three real days",
                Find("MapNode1Marker") != null && Find("MapNode2Marker") != null && Find("MapNode3Marker") != null,
                "day1=" + (Find("MapNode1Marker") != null) + " day2=" + (Find("MapNode2Marker") != null) +
                " day3=" + (Find("MapNode3Marker") != null));
            Record("the progression map reports catalogue unlocks", Find("ProgressSummary") != null &&
                Find("UnlockCount") != null && CharacterCatalog.LoadedLocale == Locales.Active,
                "summary=" + (Find("ProgressSummary") != null) + " count=" + (Find("UnlockCount") != null) +
                " catalog locale=" + CharacterCatalog.LoadedLocale);
            Record("the progression map uses generated image sprites", CountMapSprites() >= 5,
                "found " + CountMapSprites() + " generated map sprites on screen");
            Record("the progression map discloses one node detail at a time",
                Find("SelectedNodeTitle") != null && Find("MapNode1Card") == null &&
                TextOf("SelectedNodeTitle") == Loc.T("world.progress_map.day.1.title"),
                "selected title=\"" + (TextOf("SelectedNodeTitle") ?? "missing") +
                "\" legacy card=" + (Find("MapNode1Card") != null));

            MapViewportController viewport = UnityEngine.Object.FindFirstObjectByType<MapViewportController>();
            Record("the progression map is explored section by section",
                viewport != null && viewport.CanPanHorizontally,
                viewport == null ? "no MapViewportController" :
                "horizontal pan=" + viewport.CanPanHorizontally + " scale=" + viewport.CurrentScale.ToString("0.00"));
            Record("the progression map makes the current position explicit",
                Find("CurrentLocationRing") != null && Find("CurrentLocationLabel") != null,
                "ring=" + (Find("CurrentLocationRing") != null) +
                " label=" + (Find("CurrentLocationLabel") != null));

            if (viewport != null)
            {
                viewport.FocusNormalized(MapChartArt.JourneyFocusAnchor(1));
                yield return null;
                yield return Tap("MapNode2Marker", "the second progression annotation");
                Record("selecting a map annotation updates the one detail card",
                    TextOf("SelectedNodeTitle") == Loc.T("world.progress_map.day.2.title"),
                    "selected title=\"" + (TextOf("SelectedNodeTitle") ?? "missing") + "\"");

                viewport.FocusNormalized(MapChartArt.JourneyFocusAnchor(0));
                yield return null;
                yield return Tap("MapNode1Marker", "the current progression annotation");
            }

            float scaleBefore = viewport != null ? viewport.CurrentScale : 0f;
            RectTransform currentCard = Find("MapNode1Label") != null
                ? Find("MapNode1Label").transform as RectTransform
                : null;
            float cardWidthBefore = ScreenWidth(currentCard);
            yield return Tap("ZoomIn", "the progression map zoom-in button");
            yield return null;
            Record("the progression map zooms through a real control",
                viewport != null && viewport.CurrentScale > scaleBefore,
                "scale " + scaleBefore.ToString("0.00") + " -> " +
                (viewport != null ? viewport.CurrentScale.ToString("0.00") : "missing"));
            float cardWidthAfter = ScreenWidth(currentCard);
            Record("map labels stay readable instead of scaling with the terrain",
                cardWidthBefore > 0f && Mathf.Abs(cardWidthAfter - cardWidthBefore) / cardWidthBefore < 0.03f,
                "card width " + cardWidthBefore.ToString("0.0") + " -> " + cardWidthAfter.ToString("0.0"));
            yield return Tap("ZoomOut", "the progression map zoom-out button");

            int offPalette = CountOffPaletteMapSprites();
            Record("generated map art uses only the exact world palette", offPalette == 0,
                offPalette + " generated sprite(s) contained an out-of-palette pixel");

            CheckNoMissingStrings();
            yield return Capture("04-progress-map");

            yield return Tap("CloseMap", "the progression map close button");
            yield return WaitUntil(() => !WorldMapView.IsOpen, "the progression map to close");
        }

        static int CountMapSprites()
        {
            int count = 0;
            foreach (Image image in Resources.FindObjectsOfTypeAll<Image>())
            {
                if (image == null || image.sprite == null || !IsInLiveScene(image.gameObject))
                {
                    continue;
                }

                string spriteName = image.sprite.name ?? string.Empty;
                if (spriteName.StartsWith("map_progress_", StringComparison.Ordinal) ||
                    spriteName.StartsWith("map_node_", StringComparison.Ordinal) ||
                    spriteName.StartsWith("map_reward_", StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        static float ScreenWidth(RectTransform rect)
        {
            return rect != null ? rect.rect.width * rect.lossyScale.x : 0f;
        }

        static int CountOffPaletteMapSprites()
        {
            int count = 0;
            foreach (Image image in Resources.FindObjectsOfTypeAll<Image>())
            {
                if (image == null || image.sprite == null || !IsInLiveScene(image.gameObject))
                {
                    continue;
                }

                string spriteName = image.sprite.name ?? string.Empty;
                bool isMapSprite = spriteName.StartsWith("map_progress_", StringComparison.Ordinal) ||
                    spriteName.StartsWith("map_node_", StringComparison.Ordinal) ||
                    spriteName.StartsWith("map_reward_", StringComparison.Ordinal);
                if (isMapSprite && !MapChartArt.UsesExactPalette(image.sprite))
                {
                    count++;
                }
            }

            return count;
        }

        // ------------------------------------------------------------------ the backpack

        /// <summary>
        /// The four tabs, in the order the segmented control draws them.
        ///
        /// Every handle on the sheet is one of these names with a prefix — "TabHair" is the panel,
        /// "SegmentHair" the cell that selects it, "SegmentFillHair" the paint that says it is the
        /// one you are on, "SegmentBadgeHair" the dot that says it holds something unseen. They are
        /// composed rather than spelled out four times each, because the prefix is what names the
        /// part and the suffix is what names the tab, and a run that got one of the four wrong by
        /// hand would report a missing tab rather than a typo.
        /// </summary>
        static readonly string[] BackpackTabs = { "Hair", "Outfit", "Accessory", "Materials" };

        /// <summary>
        /// Opens the backpack, walks every tab, puts a piece on, is turned down by a piece the
        /// player has not found yet, and watches the HUD's badge get spent.
        ///
        /// None of that had ever been driven. The one hand test that reached the sheet was
        /// interrupted before it got past the first tab, so four interactions a player performs in
        /// their first minute with the backpack were carried entirely by the review that wrote
        /// them — and this is precisely the shape of failure this file exists for. The button could
        /// have been unreachable, a tab could have drawn two lists on top of each other, a row
        /// could have lit its ring without moving the figure, and every rule about the wardrobe
        /// would still have passed in the acceptance harness, which constructs the wardrobe
        /// directly and never composes a screen for it to live on.
        ///
        /// The badge count is read <b>before</b> the button is tapped. The sheet spends the first
        /// tab's badges while it is still being built, so a count taken after the tap is a count
        /// taken after the thing being measured has already happened.
        /// </summary>
        IEnumerator VerifyBackpack()
        {
            GameState state = TryGetState();
            if (state == null)
            {
                Record("the backpack has a run to read", false, "there is no game state");
                yield break;
            }

            int badgeBefore = Wardrobe.NewCount(state);
            string pillBefore = BackpackBadgeCount();

            Record("the HUD carries a backpack button", Find("BackpackButton") != null,
                Find("BackpackButton") != null ? "BackpackButton is on the HUD" : "no BackpackButton on the HUD");
            Record("the backpack badge has something to spend", badgeBefore > 0,
                "Wardrobe.NewCount=" + badgeBefore + ", pill \"" + (pillBefore ?? "hidden") + "\"");

            yield return Tap("BackpackButton", "the backpack button");
            yield return WaitUntil(() => ModalRoot.IsOpen && ModalRoot.TopId == BackpackPanel.ModalId,
                "the backpack sheet");

            bool open = BackpackPanel.IsOpen && ModalRoot.TopId == BackpackPanel.ModalId;
            Record("the backpack opens from the HUD", open,
                "BackpackPanel.IsOpen=" + BackpackPanel.IsOpen + ", top modal is "
                + (ModalRoot.IsOpen ? (ModalRoot.TopId ?? "unnamed") : "none"));

            if (!open)
            {
                // Every step below would fail for this one reason, and each of them spends a tap
                // timeout proving it. Thirty seconds apiece is enough to push the whole run past
                // its watchdog, and a hang gets read as "the run is broken" rather than as this.
                yield break;
            }

            yield return DriveBackpack(state);
            yield return CloseBackpack();

            // The HUD recounts off Wardrobe.Changed as well as polling it, so the number is already
            // right by the time the sheet is gone; the frame is for the pill's own layout.
            yield return null;

            int badgeAfter = Wardrobe.NewCount(state);
            string pillAfter = BackpackBadgeCount();
            string expectedPill = badgeAfter > 0 ? badgeAfter.ToString() : null;

            Record("looking at a wardrobe tab spends its badges", badgeAfter < badgeBefore,
                "Wardrobe.NewCount " + badgeBefore + " -> " + badgeAfter);

            // The whole badge contract in one line: a pill the player cannot clear is a pill that
            // nags forever, and the only way to clear this one is to look at the tabs it counts.
            Record("every wardrobe tab looked at leaves nothing to clear", badgeAfter == 0,
                "Wardrobe.NewCount=" + badgeAfter + " after all three wardrobe tabs were opened");
            Record("the HUD pill says what the wardrobe says", pillAfter == expectedPill,
                "pill \"" + (pillBefore ?? "hidden") + "\" -> \"" + (pillAfter ?? "hidden")
                + "\", wardrobe " + badgeBefore + " -> " + badgeAfter);
        }

        /// <summary>
        /// Everything the sheet does, in the order a player would do it.
        ///
        /// This records and never returns a verdict, and it may give up at any point: closing the
        /// sheet is <see cref="VerifyBackpack"/>'s job and happens whatever went on in here. That
        /// split is not tidiness. Dusk waits on <c>ModalRoot.IsOpen</c>, and none of the three
        /// buttons the day loop knows how to clear — Continue, Option_0, Confirm — exists on this
        /// sheet, so a backpack left open would not fail here at all; it would hold the night open
        /// two steps later and time out somewhere with nothing to do with the backpack.
        /// </summary>
        IEnumerator DriveBackpack(GameState state)
        {
            yield return WaitForObject("SlotSegments", "the backpack tab bar");

            // One sweep covers all four tabs at once. The sweep reads every Text in the live scene
            // rather than every visible one, and the three tabs that are not showing are built and
            // merely inactive — so this is the cheapest moment in the run to catch a missing key on
            // a screen nobody is looking at.
            CheckNoMissingStrings();

            RecordOnlyBackpackTab("Hair", "the sheet opens on the first wardrobe tab");
            Record("the first tab is painted as the one you are on", IsShowing("SegmentFillHair"),
                "SegmentFillHair showing=" + IsShowing("SegmentFillHair"));
            Record("the dot on SegmentHair is spent by the sheet opening on it",
                !IsShowing("SegmentBadgeHair"),
                "SegmentBadgeHair showing=" + IsShowing("SegmentBadgeHair"));

            yield return WearSomething(state);
            yield return TapSomethingNotFoundYet(state);
            yield return Capture("04a-backpack-hair");

            yield return SelectBackpackTab("Outfit", "04b-backpack-outfit");
            yield return SelectBackpackTab("Accessory", "04c-backpack-accessory");
            yield return SelectBackpackTab("Materials", "04d-backpack-materials");

            RecordMaterialsTab();

            // Back to the tab it opened on. This is the only selection that has to move the layout
            // in both directions — the materials tab has no character above its list — so a sheet
            // that can leave the wardrobe and not come back fails here rather than in a screenshot
            // somebody looks at next week.
            yield return SelectBackpackTab("Hair", null);
            Record("leaving the materials tab brings the character back", Find("CharacterStage") != null,
                Find("CharacterStage") != null ? "CharacterStage is up again" : "the stage never came back");
        }

        /// <summary>
        /// Taps one tab and proves the whole control moved with it: its list is the only one on
        /// screen, the cell that was tapped is the one wearing the fill, and the dot it may have
        /// been carrying is gone.
        ///
        /// The second assertion is what separates "the list changed" from "the tab bar knows which
        /// tab you are on", and the third is the badge rule at tab scale — a dot that survives
        /// being looked at is the nagging version of the same feature.
        /// </summary>
        IEnumerator SelectBackpackTab(string tab, string shot)
        {
            GameObject segment = Find("Segment" + tab);
            if (segment == null)
            {
                Record("select Segment" + tab, false, "no active object named Segment" + tab);
                yield break;
            }

            bool dotBefore = IsShowing("SegmentBadge" + tab);

            yield return TapObject(segment, "select Segment" + tab);
            yield return null;

            RecordOnlyBackpackTab(tab, "selecting Segment" + tab + " shows Tab" + tab + " and nothing else");
            Record("selecting Segment" + tab + " repaints the tab bar", IsShowing("SegmentFill" + tab),
                "SegmentFill" + tab + " showing=" + IsShowing("SegmentFill" + tab));
            Record("the dot on Segment" + tab + " does not survive being looked at",
                !IsShowing("SegmentBadge" + tab),
                dotBefore ? "it was showing and is spent" : "there was nothing on it to spend");

            if (!string.IsNullOrEmpty(shot))
            {
                yield return Capture(shot);
            }
        }

        /// <summary>
        /// Asserts that exactly one of the four tab panels is on screen.
        ///
        /// Both halves are load-bearing. A tab that fails to show its own list is a dead control,
        /// and two lists drawn over one another is the invisible-rather-than-broken bug this whole
        /// file exists for: nothing logs, nothing throws, and the hierarchy looks entirely correct
        /// right up until somebody reads the screenshot.
        /// </summary>
        void RecordOnlyBackpackTab(string tab, string step)
        {
            var showing = new List<string>();
            for (int i = 0; i < BackpackTabs.Length; i++)
            {
                if (Find("Tab" + BackpackTabs[i]) != null)
                {
                    showing.Add("Tab" + BackpackTabs[i]);
                }
            }

            Record(step, showing.Count == 1 && showing[0] == "Tab" + tab,
                showing.Count == 0 ? "no tab panel is on screen at all" : "showing " + string.Join(", ", showing));
        }

        /// <summary>
        /// Puts on a piece of hair the player is not wearing, and proves the character changed.
        ///
        /// The piece is chosen at runtime rather than named here, for two reasons that are both
        /// about the catalogue being content. Several locked pieces write the same art indices as
        /// pieces that are free, so "a different row" is not the same thing as "a different look",
        /// and this assertion is worth nothing unless the piece picked writes a hair layer that
        /// differs from the one already on the figure. And an id spelled into a test is a test that
        /// breaks the day a designer renames a hairstyle, which teaches nobody anything.
        ///
        /// What it asserts is the pair, and the pair is the whole point: the ring on the row AND
        /// the int on the character. A ring that lights while the figure stays put looks completely
        /// correct until the player closes the sheet and finds themselves wearing what they had on
        /// before.
        /// </summary>
        IEnumerator WearSomething(GameState state)
        {
            int before = state.appearance != null ? state.appearance.hair : -1;

            CatalogItemDef target = null;
            CatalogItemDef[] items = Wardrobe.ItemsForSlot(CharacterSlot.Hair);
            for (int i = 0; i < items.Length && target == null; i++)
            {
                CatalogItemDef item = items[i];
                if (item == null || item.art == null || !item.art.hair.HasValue) continue;
                if (item.art.hair.Value == before) continue;
                if (!Wardrobe.IsUnlocked(state, item.id)) continue;
                if (Wardrobe.IsEquipped(state, item.id)) continue;
                target = item;
            }

            if (target == null)
            {
                Record("the wardrobe offers a piece to put on", false,
                    "no unlocked hair item writes a layer different from " + before);
                yield break;
            }

            GameObject chip = Find("Chip_" + target.id);
            if (chip == null)
            {
                Record("the wardrobe draws a row for " + target.id, false,
                    "no active object named Chip_" + target.id);
                yield break;
            }

            yield return ScrollIntoView(chip);
            yield return TapObject(chip, "put on " + target.id);
            yield return null;

            int after = state.appearance != null ? state.appearance.hair : -1;
            Record("putting a piece on changes the character",
                after != before && after == target.art.hair.Value,
                "hair layer " + before + " -> " + after + ", " + target.id + " writes " + target.art.hair.Value);
            Record("the piece that went on is the one wearing the ring", IsChildShowing(chip, "Selected"),
                "Selected under Chip_" + target.id + " showing=" + IsChildShowing(chip, "Selected"));
            Record("the wardrobe agrees the piece is on", Wardrobe.IsEquipped(state, target.id),
                "Wardrobe.IsEquipped(" + target.id + ")=" + Wardrobe.IsEquipped(state, target.id));
        }

        /// <summary>
        /// Taps a piece the player has not found yet and reads the answer out loud.
        ///
        /// Rule 7 is what makes this a real interaction rather than a dead one: a locked row keeps
        /// its art, its name, its description and its unlock sentence, and it stays interactable —
        /// so the tap has to go somewhere, and where it goes is one calm sentence beside the
        /// figure. What is asserted is that the sentence is a sentence: not empty, not a raw locale
        /// key, not the row's own name, and the exact words the wardrobe handed back.
        ///
        /// The last two assertions cover each other's blind spot. Equality alone would pass on
        /// garbage — if the key were missing from ui.json, both sides would render the same
        /// ⟨backpack.refusal.locked⟩ and agree perfectly — and the marker check is what catches
        /// that. The name check is what catches the other direction, a run reading the wrong label
        /// off the screen and calling a row title a refusal.
        /// </summary>
        IEnumerator TapSomethingNotFoundYet(GameState state)
        {
            CatalogItemDef target = null;
            CatalogItemDef[] items = Wardrobe.ItemsForSlot(CharacterSlot.Hair);
            for (int i = 0; i < items.Length && target == null; i++)
            {
                CatalogItemDef item = items[i];
                if (item != null && !string.IsNullOrEmpty(item.id) && !Wardrobe.IsUnlocked(state, item.id))
                {
                    target = item;
                }
            }

            if (target == null)
            {
                Record("the wardrobe still has a piece to find", false,
                    "every hair item is already unlocked on day " + state.day);
                yield break;
            }

            GameObject chip = Find("Chip_" + target.id);
            if (chip == null)
            {
                Record("the wardrobe draws a row for " + target.id, false,
                    "no active object named Chip_" + target.id
                    + " — a piece not found yet is drawn in full, never blanked");
                yield break;
            }

            int before = state.appearance != null ? state.appearance.hair : -1;

            yield return ScrollIntoView(chip);
            yield return TapObject(chip, "tap " + target.id + ", which has not been found yet");

            // The sentence arrives on a fade, so the frame after the tap is too early to read it.
            yield return WaitUntil(() => !string.IsNullOrEmpty(TextOf("RefusalMessage")),
                "the refusal to be spelled out");

            string sentence = TextOf("RefusalMessage");
            string itemName = target.display;
            string expected = Loc.T(Wardrobe.KeyRefusalLocked);

            Record("a piece not found yet answers with a sentence", !string.IsNullOrEmpty(sentence),
                "RefusalMessage = \"" + (sentence ?? "missing") + "\"");
            Record("the refusal resolves to real words",
                !string.IsNullOrEmpty(sentence) && sentence.IndexOf(MissingMarkerOpen) < 0,
                "RefusalMessage = \"" + (sentence ?? "missing") + "\"");
            Record("the refusal is a refusal and not the row's own name",
                !string.IsNullOrEmpty(sentence) && sentence != itemName,
                "refusal \"" + (sentence ?? "missing") + "\" against name \"" + (itemName ?? "unnamed") + "\"");
            Record("the refusal is the one the wardrobe handed back", sentence == expected,
                "expected \"" + expected + "\", found \"" + (sentence ?? "missing") + "\"");

            // Rule 7 from the other side: being turned down costs nothing. Nothing went on, and the
            // figure is exactly where the equip before this left it.
            int after = state.appearance != null ? state.appearance.hair : -1;
            Record("being turned down changes nothing about the character",
                after == before && !Wardrobe.IsEquipped(state, target.id),
                "hair layer " + before + " -> " + after + ", equipped=" + Wardrobe.IsEquipped(state, target.id));

            CheckNoMissingStrings();
        }

        /// <summary>
        /// The materials tab, which is the one tab that is not a wardrobe: no character above it,
        /// no rows to wear, three cards and a number on each.
        ///
        /// The dot assertion is the one worth spelling out. "SlotSegments" holds a segment that is
        /// not a slot: materials are not catalogue items, they have no seen-state, and nothing
        /// anywhere can mark them looked at. A dot on that cell would therefore be a gold pill with
        /// nothing behind it that no amount of looking could ever clear — which is exactly the
        /// nagging the badge is built to avoid everywhere else.
        /// </summary>
        void RecordMaterialsTab()
        {
            bool grid = Find("MaterialsGrid") != null && Find("Material_stone") != null
                && Find("Material_timber") != null && Find("Material_blocks") != null;
            Record("the materials tab draws its grid", grid,
                "grid=" + (Find("MaterialsGrid") != null) + " stone=" + (Find("Material_stone") != null)
                + " timber=" + (Find("Material_timber") != null) + " blocks=" + (Find("Material_blocks") != null));

            Record("every material card carries its number",
                HasMaterialCount("Material_stone") && HasMaterialCount("Material_timber")
                && HasMaterialCount("Material_blocks"),
                "stone=\"" + (ChildTextOf(Find("Material_stone"), "Count") ?? "missing")
                + "\" timber=\"" + (ChildTextOf(Find("Material_timber"), "Count") ?? "missing")
                + "\" blocks=\"" + (ChildTextOf(Find("Material_blocks"), "Count") ?? "missing") + "\"");

            Record("the materials tab gives the grid the whole sheet", Find("CharacterStage") == null,
                Find("CharacterStage") == null
                    ? "the character stage is away"
                    : "the stage is still standing over the grid");

            Record("the materials segment never carries a dot", !IsShowing("SegmentBadgeMaterials"),
                !IsShowing("SegmentBadgeMaterials")
                    ? "no dot on a segment that is not a slot"
                    : "SegmentBadgeMaterials is showing, and nothing can ever clear it");
        }

        // ------------------------------------------------------------------ reading the sheet

        /// <summary>
        /// Scrolls a row into the middle of its list before anyone tries to tap it.
        ///
        /// <see cref="TapObject"/> raycasts at a control's own centre and refuses to click anything
        /// but the control it reaches, which is the whole reason it is worth trusting — and it
        /// means a row scrolled off the bottom of its viewport fails as "covered by the viewport"
        /// rather than being clicked through a mask. The wardrobe shows about two rows at a time
        /// and every piece the player has not found yet is authored below them, so without this the
        /// only rows a run could ever reach would be the first two.
        ///
        /// The list is moved directly rather than dragged, for the same reason the progression map
        /// is focused directly before its annotations are tapped: a flick is the interaction
        /// layer's business, and what is being proved here is what happens to the row at the end of
        /// one.
        /// </summary>
        static IEnumerator ScrollIntoView(GameObject target)
        {
            var rect = target != null ? target.transform as RectTransform : null;
            ScrollRect scroll = target != null ? target.GetComponentInParent<ScrollRect>() : null;
            if (rect == null || scroll == null || scroll.content == null || scroll.viewport == null)
            {
                yield break;
            }

            float scrollable = scroll.content.rect.height - scroll.viewport.rect.height;
            if (scrollable <= 0f)
            {
                yield break;
            }

            // Measured in the content's own space, so whichever pivot the layout happens to use
            // does not enter into it.
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector3 centre = scroll.content.InverseTransformPoint((corners[0] + corners[2]) * 0.5f);

            float fromTop = scroll.content.rect.yMax - centre.y;
            float offset = Mathf.Clamp(fromTop - scroll.viewport.rect.height * 0.5f, 0f, scrollable);

            scroll.velocity = Vector2.zero;
            scroll.verticalNormalizedPosition = 1f - offset / scrollable;

            // One frame for the move to land, one for the canvas batch: a graphic that has not been
            // through one still reports depth -1, which the graphic raycaster skips outright.
            yield return null;
            yield return null;
        }

        /// <summary>
        /// Whether a named graphic is actually in front of the player: present, active, and not
        /// switched off at the component.
        ///
        /// Both spellings of "hidden" are in use on this sheet. A tab panel goes away with
        /// SetActive; a status icon goes away with Image.enabled so that its slot keeps the width
        /// the row was measured against. A check that knew only one of them would read a hidden
        /// thing as shown, which is the wrong way round for an assertion to be wrong.
        /// </summary>
        static bool IsShowing(string objectName)
        {
            GameObject go = Find(objectName);
            if (go == null)
            {
                return false;
            }

            Graphic graphic = go.GetComponent<Graphic>();
            return graphic == null || graphic.enabled;
        }

        /// <summary>
        /// The same question about a named child of one object. Used for a row's own ring, because
        /// every row on the sheet has a child called "Selected" and only the worn one shows it — a
        /// lookup by name alone would answer with somebody else's.
        /// </summary>
        static bool IsChildShowing(GameObject parent, string childName)
        {
            Transform child = parent != null ? parent.transform.Find(childName) : null;
            if (child == null || !child.gameObject.activeInHierarchy)
            {
                return false;
            }

            Graphic graphic = child.GetComponent<Graphic>();
            return graphic == null || graphic.enabled;
        }

        static string ChildTextOf(GameObject parent, string childName)
        {
            Transform child = parent != null ? parent.transform.Find(childName) : null;
            if (child == null)
            {
                return null;
            }

            Text text = child.GetComponent<Text>();
            return text != null ? text.text : null;
        }

        static bool HasMaterialCount(string cardName)
        {
            int parsed;
            return int.TryParse(ChildTextOf(Find(cardName), "Count"), out parsed);
        }

        /// <summary>
        /// What the HUD's badge is showing, or null when there is no pill at all — which is what
        /// zero looks like, because a badge reading 0 is a notice that nothing happened.
        ///
        /// Read through the badge itself rather than by name: every material card carries a child
        /// called "Count" too, so a name lookup taken while the sheet is open would answer with a
        /// stone count and call it a badge.
        /// </summary>
        static string BackpackBadgeCount()
        {
            GameObject badge = Find("BackpackBadge");
            if (badge == null)
            {
                return null;
            }

            Text text = badge.GetComponentInChildren<Text>(true);
            return text != null ? text.text : null;
        }

        /// <summary>
        /// Shuts the sheet, and makes sure it is shut even when its button could not be reached.
        ///
        /// The fallback is not politeness. Dusk waits on <c>ModalRoot.IsOpen</c> and none of the
        /// buttons the day loop knows how to clear exists here, so a backpack left open would not
        /// fail in this method at all — it would hold the night open two steps later and time out
        /// thirty seconds and one whole day away from the cause. Closing it from code is not
        /// something a player can do, so it is reported as the failure it is rather than quietly
        /// standing in for the button.
        /// </summary>
        IEnumerator CloseBackpack()
        {
            yield return Tap("CloseButton", "closing the backpack");
            yield return WaitUntil(() => !BackpackPanel.IsOpen, "the backpack to close");

            if (BackpackPanel.IsOpen || ModalRoot.IsOpen)
            {
                Record("the backpack closes from its own button", false,
                    "it had to be closed from code — the sheet was still up on "
                    + (ModalRoot.IsOpen ? (ModalRoot.TopId ?? "unnamed") : "nothing")
                    + ", and an open modal holds the night open");
                ModalRoot.CloseId(BackpackPanel.ModalId);
                yield return null;
                yield break;
            }

            Record("the backpack closes from its own button", true, "the sheet is down and the village is back");
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

            // Day one has no morning — it opens in a cutscene — so its check-in is owed at the
            // evening instead, and it has to arrive BEFORE the split rather than on top of it.
            // Asserting the order matters as much as asserting it appears: dusk waits on an open
            // panel, so a check-in that turned up late would hold the night open indefinitely.
            yield return WaitForObject("Option_0", "the day-one check-in");
            Record("the first day is checked in before its split", !EndDayPanel.IsOpen,
                EndDayPanel.IsOpen ? "the split opened over the check-in" : "the split is waiting for it");
            yield return Capture("06b-check-in");
            CheckNoMissingStrings();

            GameObject checkInAnswer = Find("Option_0");
            if (checkInAnswer != null)
            {
                yield return TapObject(checkInAnswer, "answer the day-one check-in");
                yield return Tap("Continue", "close the day-one check-in");
            }

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

            // Read with the drawer open, because that is where the label is now. Taken before the
            // switch so the comparison at the end is between two states of the same control.
            yield return OpenHudMenu();
            string before = TextOf("Day");

            // In the village the language lives one tap deeper than it does in character creation:
            // the HUD carries a settings button and the chips live on the panel it opens. Tapping
            // straight at a chip here reported a missing control, which read as the toggle being
            // broken rather than as this run not having opened the panel.
            yield return OpenHudMenu();
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
            yield return OpenHudMenu();
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
