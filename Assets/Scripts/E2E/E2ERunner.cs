using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SheepGate.Art;
using SheepGate.Contest;
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
    /// Plays a built player's whole season without a person at the keyboard, screenshots it, and
    /// fails the run on anything a person would have noticed.
    ///
    /// It exists because of two lessons this project already paid for. The acceptance harness
    /// constructs systems directly and never composes a scene, so it proves rules and not
    /// reachability — the trial was once unreachable in a built game while every rule about it
    /// passed. And the bugs that have actually shipped were invisible rather than broken: UI drawn
    /// under an opaque panel, components silently dropped. Neither logged anything, and neither
    /// could be reasoned about. Both are obvious in a screenshot.
    ///
    /// So this runner only ever touches the game the way a finger does: it raycasts the EventSystem
    /// at a control's own screen position and requires that control to be what the ray actually
    /// hits before it dispatches a click. Calling Button.onClick directly would pass happily on a
    /// button buried under a full-screen fade, which is precisely the failure it is here to catch.
    ///
    /// WHAT IT PLAYS IS READ FROM <see cref="GameData.Stages"/>, never from a list here. The season
    /// grew from three days to nine while this file still named three, and a harness whose coverage
    /// is spelled out in its own source is a harness that silently stops covering the end of the
    /// game the day somebody adds to it. The loop dispatches on <see cref="StageDef.type"/> and on
    /// what each stage DECLARES — the same shape <see cref="StageDirector"/> uses — so a tenth stage
    /// is played by this file with no edit at all.
    ///
    /// COVERAGE IS TIERED, AND THE TIER IS THE KNOB BETWEEN WHAT IS PROVEN AND WHAT THE RUN COSTS.
    /// The full battery — the missing-string sweep, panel-order checks, HUD checks and dense
    /// screenshots — runs on three stages, chosen from the table rather than by number: the first
    /// stage, the stage that declares <c>reveals_page</c>, and the stage that declares
    /// <c>terminal</c>. Those are the opening, the reveal with the reader tap behind it, and the
    /// ending with the record, the second reader tap and the vocation. Every other stage is
    /// traversed cheaply: it is reached, its panels are recognised, its beat is played, and its day
    /// rolls over. The two reader taps are never in the cheap tier and never will be — they are the
    /// deep_read doors, and they are the reason this file is worth the minutes it costs.
    ///
    /// Enable with -e2e. Always pair it with -data-path: this drives the real SaveSystem.
    /// </summary>
    public sealed class E2ERunner : MonoBehaviour
    {
        /// <summary>
        /// Moves a contest gets beyond its own declared limit before the run calls it stuck.
        ///
        /// Added to the contest's <c>turn_limit</c> rather than being a number of its own: two
        /// contests with different limits shared one hand-tuned 14 until this became a sum, and a
        /// cap sized against one fight is a cap that either hangs on the other or passes a limit
        /// that is not being enforced.
        /// </summary>
        const int ContestTurnSlack = 6;

        /// <summary>
        /// How long an offered tap on a contest move waits before giving the turn up.
        ///
        /// Deliberately far shorter than <c>StepTimeoutSeconds</c>. A move the fight will accept is
        /// tappable within a frame or two, so anything longer is time spent re-probing a control
        /// the Page or the outcome card has already covered — and with the loop's cap, several of
        /// those in a row is the difference between a fight that resolves and a watchdog.
        /// </summary>
        const float ContestTapTimeoutSeconds = 2f;

        /// <summary>Panels one morning may stack before the run calls it stuck.</summary>
        const int MorningPanelCap = 6;

        const string EnableFlag = "-e2e";
        const string OutputFlag = "-e2e-out";

        /// <summary>
        /// Seeds a save at one stage and starts the run there. AUTHORING CONVENIENCE ONLY.
        ///
        /// Writing a stage's content and then waiting four minutes to see it is how a check stops
        /// being run, so there has to be a way to jump. What there must NOT be is a gate that jumps:
        /// reachability from the first frame is the entire reason this file exists, and a harness
        /// that starts at stage six would have passed happily on the season in which stages four
        /// through nine could not be reached at all. <c>tools/e2e.sh</c> only forwards this when a
        /// person asks for it by name, and the run says loudly in its own log that it is not a gate.
        /// </summary>
        const string StartStageFlag = "-e2e-start-stage";

        /// <summary>The marker Loc and ScriptureService render when a string could not be resolved.</summary>
        const char MissingMarkerOpen = '⟨';

        const float StepTimeoutSeconds = 30f;

        /// <summary>
        /// How long the dialogue driver will tap without ANYTHING on screen changing before it calls
        /// the conversation stuck.
        ///
        /// A stall budget rather than a total budget, and the difference matters: the opening runs
        /// sixty-odd advances through camera moves and character walks, so a total cap large enough
        /// to cover it is a cap too large to catch a real stall, and one small enough to catch a
        /// stall fails the opening on a slow machine. Progress is read off the screen — the spoken
        /// line, the node being played, the panel on top — so the clock only runs while taps are
        /// achieving nothing.
        /// </summary>
        const float DialogueStallSeconds = 20f;

        /// <summary>
        /// Executed taps one wait may spend. A backstop under <see cref="DialogueStallSeconds"/>
        /// rather than the primary bound: it counts taps that were actually dispatched, never
        /// frames spent waiting, so a long cutscene cannot exhaust it.
        /// </summary>
        const int MaxDialogueAdvances = 400;

        /// <summary>
        /// Real seconds of no frame AND no step before the detector calls it a stall. Generously
        /// above the longest legitimate quiet stretch — a stage's scripted beat can hold the screen
        /// for a few seconds — and far below the shell's own 990s ceiling, so the report always
        /// arrives before the kill that used to be the only outcome.
        /// </summary>
        const int StallSeconds = 90;

        /// <summary>Seconds allowed for a stage's own scripted beat to begin once its morning is clear.</summary>
        const float BeatTimeoutSeconds = 45f;

        /// <summary>
        /// How long the run waits for a night that must never come. Sized against the daylight
        /// clock rather than generously: the split opens within a frame or two of the capacity
        /// running out on every other day, so anything longer is time spent proving nothing.
        /// </summary>
        const float TerminalHoldProofSeconds = 3f;

        /// <summary>Season length assumed for the run ceiling when the stage table cannot be read.</summary>
        const int FallbackStageCount = 9;

        readonly List<string> _failures = new List<string>();
        readonly List<string> _steps = new List<string>();
        readonly List<string> _shots = new List<string>();

        /// <summary>
        /// Buttons this run knows how to leave a panel by, in the order it tries them.
        ///
        /// Hoisted from the three call sites that each spelled the same trio, because the cost of an
        /// unknown button is not the step that wanted it: dusk waits on <c>ModalRoot.IsOpen</c>, so a
        /// panel nobody can close stops the day ending at all, and the run then reports a stall
        /// thirty seconds and one step away from the cause. One list is also the only way to extend
        /// the vocabulary once for every caller when a stage introduces a panel.
        ///
        /// THE ORDER IS LOAD BEARING. Continue comes before Option_0 because a check-in that has
        /// been answered keeps its options on screen — they are only tinted and made
        /// non-interactable — so a run that preferred an option would answer once and then tap a
        /// dead control forever. Continue is built inactive and shown only once the day is checked
        /// in, and <see cref="Find"/> ignores inactive objects, so preferring it can never skip the
        /// answer.
        /// </summary>
        static readonly string[] ModalExitButtons =
        {
            "Continue", "Option_0", "Confirm", "CloseButton", "Close", "Play"
        };

        /// <summary>
        /// How long the run waits for a resident to take a step of their own. Generous next to the
        /// pause NpcWander draws between steps, because the pause is randomised per resident and the
        /// village stands still for anything modal that turns up while the run is watching.
        /// </summary>
        const float WanderTimeoutSeconds = 20f;

        /// <summary>
        /// The name this run types into the field. It is what a finger would enter rather than
        /// words the game authored, and every assertion about it compares against this constant
        /// instead of reading it off a table. Long enough to clear
        /// <see cref="CharacterPresets.MinNameLength"/>, short enough never to reach
        /// <see cref="CharacterPresets.MaxNameLength"/>, and the same in every locale so the two
        /// runs assert the identical thing.
        /// </summary>
        const string TypedName = "Kai";

        /// <summary>The four facings the preview strip draws, in the order it draws them.</summary>
        static readonly string[] CreationFacings = { "down", "left", "right", "up" };

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
        static readonly string[] BackpackTabs = { "Profile", "Hair", "Outfit", "Accessory", "Materials" };

        /// <summary>
        /// What character creation settled on, held so the backpack can be asked whether it agrees.
        ///
        /// The two screens spoke different vocabularies until this run — creation wrote art
        /// indices, the backpack read catalogue ids — so "they now agree" is the claim worth
        /// carrying across a whole day of script rather than re-deriving at the backpack, where a
        /// re-derivation would only ever agree with itself.
        /// </summary>
        string _creationCharacterId;

        readonly List<string> _creationEquipped = new List<string>();
        int _creationSkin = -1;
        string _creationPlayerName;

        /// <summary>
        /// A copy of the figure, taken so two choices can be compared after the second one has
        /// already overwritten the live state. <see cref="AppearanceState"/> is a live object on
        /// the run, so holding a reference to it and comparing later compares a thing with itself.
        /// </summary>
        struct LookSnapshot
        {
            public int Body;
            public int Skin;
            public int Hair;
            public int Top;
            public int Legs;
            public int Accessory;

            public static LookSnapshot Of(AppearanceState look)
            {
                var copy = new LookSnapshot();
                if (look == null)
                {
                    return copy;
                }

                copy.Body = look.body;
                copy.Skin = look.skin;
                copy.Hair = look.hair;
                copy.Top = look.top;
                copy.Legs = look.legs;
                copy.Accessory = look.accessory;
                return copy;
            }

            public override string ToString()
            {
                return "body=" + Body + " skin=" + Skin + " hair=" + Hair
                       + " top=" + Top + " legs=" + Legs + " accessory=" + Accessory;
            }
        }

        string _outputDirectory;
        string _runLocale = Locales.Source;
        bool _finished;

        /// <summary>Screenshot ordinal within the stage being played, so shots sort chronologically.</summary>
        int _shotOrdinal;
        int _shotStageDay = -1;

        /// <summary>Chapter the reveal's reader opened, so the ending's reader can be proven to differ.</summary>
        string _pageChapterRef;

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
            //
            // It does NOT cover the other way a player stops rendering here. A window the system
            // considers fully occluded — which is what a second player launched at the same size on
            // the same screen produces — is suspended regardless of this flag, and then every step
            // of this class stops with it, because they are all `yield return null`. That one is
            // fixed in tools/e2e.sh by running the locales one at a time; do not go looking for it
            // here, and do not conclude from this line that focus has been ruled out.
            Application.runInBackground = true;

            // -data-path does not cover PlayerPrefs, and this run taps the language toggle. Without
            // this the run would change the language a person gets on their next launch.
            Locales.SuppressPersistence = true;

            // Before the Boot scene reads the save, and that ordering is the only reason a seed can
            // work at all: this hook runs BeforeSceneLoad, BootSequence.Run() runs from the scene.
            SeedStartStageIfAsked();

            var host = new GameObject("E2ERunner");
            DontDestroyOnLoad(host);
            host.AddComponent<E2ERunner>();
        }

        /// <summary>
        /// The stage this run was asked to start on, or 1 for the cold run that is the actual gate.
        /// </summary>
        static int StartStage
        {
            get
            {
                int parsed;
                string requested = ReadFlagValue(StartStageFlag);
                return !string.IsNullOrEmpty(requested) && int.TryParse(requested, out parsed) && parsed > 1
                    ? parsed
                    : 1;
            }
        }

        /// <summary>
        /// Writes a save standing on the requested stage, so the run lands in the village instead of
        /// replaying the opening.
        ///
        /// A minimal seed on purpose: a fresh state with its day moved. It is not a substitute for
        /// having played the stages before it and it never pretends to be — the wall is unbuilt, no
        /// flag is raised and the reveal has not happened, so a seeded run is a way to LOOK at a
        /// stage, never a way to assert anything about the season that leads to it.
        /// </summary>
        /// <summary>Stands in for the name a real run would have typed. Authoring only.</summary>
        const string SeededPlayerName = "Hanani";

        static void SeedStartStageIfAsked()
        {
            int day = StartStage;
            if (day <= 1)
            {
                return;
            }

            try
            {
                GameState seeded = GameState.NewGame();
                seeded.day = day;

                // A name, because a save standing on stage 6 belongs to somebody who went through
                // character creation, and a seeded state without one is a save no player could
                // ever have. That used to be invisible; it stopped being so when the launch screen
                // started deciding what to show from whether the run has a name, and a nameless
                // stage-6 save would be shown the new player's splash.
                seeded.playerName = SeededPlayerName;

                SaveSystem.Save(seeded);
                Debug.LogWarning("[E2E] Seeded a save at stage " + day +
                    ". THIS RUN IS NOT A GATE: every stage before " + day +
                    " is skipped, so it proves nothing about reachability.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[E2E] Could not seed a save at stage " + day + ": " + exception.Message);
            }
        }

        /// <summary>
        /// Hard ceiling on a whole run, derived from the season rather than hand tuned.
        ///
        /// Without a ceiling a stalled step is indistinguishable from a slow one and the process
        /// sits there forever, which is worse than a failure: a failure gets read, a hang gets
        /// killed by whoever notices first. What it must NOT be is a constant whose comment has to
        /// be re-argued every time the season changes length — that comment was rewritten once when
        /// the run went from one day to three, and the number it defends went stale again the moment
        /// the season went to nine.
        ///
        /// THE MARGIN IS NOT MEASURED. Forty seconds a stage plus a minute for the openings was
        /// picked against a projection, not against an observed nine-stage run — nobody has watched
        /// one yet — and the authoring locale's run is the expensive one, carrying three openings
        /// rather than one because it switches language and switches back. If a passing run ever
        /// lands near this ceiling, the multiplier is the knob and a near miss should be read as a
        /// margin to widen rather than as a hang to debug.
        ///
        /// Read at Start and not at install time, because the stage table has not loaded when the
        /// runtime hook fires.
        /// </summary>
        static float RunTimeoutSeconds
        {
            get
            {
                StageDef[] stages = GameData.Stages;
                int count = stages != null && stages.Length > 0 ? stages.Length : FallbackStageCount;
                return 60f + 40f * count;
            }
        }

        /// <summary>
        /// Frames the player has actually drawn, incremented from <see cref="Update"/> and read by
        /// a thread that is not the player's. Volatile because those are two different threads and
        /// the whole question this answers is whether one of them has stopped.
        /// </summary>
        volatile int _framesDrawn;

        /// <summary>Steps recorded so far. Same contract as <see cref="_framesDrawn"/>.</summary>
        volatile int _stepsRecorded;

        /// <summary>The last step's label, for the stall report. Written under a lock-free single writer.</summary>
        volatile string _lastStepLabel = "(nothing recorded yet)";

        void Update()
        {
            _framesDrawn++;
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
            Debug.Log("[E2E] Started. locale=" + _runLocale + " out=" + _outputDirectory
                + " stages=" + (GameData.Stages != null ? GameData.Stages.Length : 0));

            StartCoroutine(Watchdog());
            StartStallDetector();
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
            float ceiling = RunTimeoutSeconds;
            float deadline = Time.realtimeSinceStartup + ceiling;
            while (!_finished && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            if (_finished)
            {
                yield break;
            }

            _failures.Add("the run did not finish within " + ceiling + "s");
            Debug.Log("[E2E] FAIL watchdog — the run did not finish within " + ceiling + "s");
            WriteResult();
            Application.Quit(1);
        }

        // ------------------------------------------------------------------ the script

        /// <summary>
        /// The launch screen, which is in front of everything else on every run and is two
        /// different screens depending on what the save holds.
        ///
        /// On a cold run there is no character, so it is a wordmark that fades on its own: the run
        /// waits it out rather than tapping, because a splash with nothing to press is exactly what
        /// a new player gets and hurrying it would prove something no player experiences. On a
        /// seeded run — <c>--from-stage</c> — the save has a character, so it is the returning
        /// player's screen and there is a button; the run presses it, which is the only automated
        /// coverage that screen has.
        ///
        /// Waited for rather than assumed present: TitleScreen shows once per LAUNCH, and a run
        /// that reloads the scene (the language toggle does) will not see it a second time.
        /// </summary>
        IEnumerator ClearTheTitleScreen(int startStage)
        {
            const float appearSeconds = 2f;
            float deadline = Time.realtimeSinceStartup + appearSeconds;

            while (Time.realtimeSinceStartup < deadline && !ModalRoot.IsOpen)
            {
                yield return null;
            }

            if (!ModalRoot.IsOpen)
            {
                Record("the launch screen appeared", false,
                    "nothing modal was on screen within " + appearSeconds + "s of the first frame");
                yield break;
            }

            GameObject play = Find("Play");
            if (play != null)
            {
                Record("the launch screen offers the run its save back", true, "Play is on screen");
                yield return TapObject(play, "press Play on the launch screen");
            }
            else
            {
                Record("the launch screen is a splash on a cold run", startStage <= 1,
                    startStage <= 1
                        ? "no Play button, as a run with no character should have"
                        : "a seeded run reached stage " + startStage + " with no character to show, " +
                          "which means SeedStartStageIfAsked stopped naming the player");
            }

            // Either path ends the same way: the screen fades itself out, and nothing else may
            // start until it has, because everything behind it is held by ModalRoot being open.
            float clearBy = Time.realtimeSinceStartup + 5f;
            while (ModalRoot.IsOpen && Time.realtimeSinceStartup < clearBy)
            {
                yield return null;
            }

            Record("the launch screen handed the game over", !ModalRoot.IsOpen,
                ModalRoot.IsOpen ? "it was still on screen 5s later" : "the world has the screen");
        }

        IEnumerator RunScript()
        {
            int startStage = StartStage;

            yield return ClearTheTitleScreen(startStage);

            // 1-3. The opening: the first thing a player sees, then the one screen that asks them
            // for something. Creation is DRIVEN IN FULL rather than skipped past with QuickStart,
            // because creation is where the wardrobe the rest of the game reads is written — a run
            // that skips it proves nothing about the clothes every later stage renders.
            // Skipped only on a seeded authoring run, which starts already standing in the village
            // because IntroCutscene does not attach past the first stage.
            if (startStage <= 1)
            {
                yield return WaitForObject("DialogueCanvas", "the opening dialogue");
                yield return Capture(1, "opening");

                yield return AdvanceDialogueUntil("CharacterCreationCanvas", "character creation");
                yield return Capture(1, "creation");
                CheckLanguageToggle();

                yield return CreateACharacter();
            }

            // 4. Through the rest of the opening to the game proper. The condition is that the HUD
            // is actually on screen, not that its object exists: HUD.SetVisible toggles
            // Canvas.enabled and leaves the GameObject active, so "the object is there" is true
            // through the whole cutscene and would screenshot a fade.
            yield return AdvanceDialogueUntil(IsHudVisible, "the HUD");
            yield return Capture(1, "hud");

            // The readouts moved into the drawer, so reading one means opening it first.
            yield return OpenHudMenu();
            yield return Capture(1, "hud-menu");
            CheckHud();

            // 5. The whole-screen sweep. Cheap, and it catches every missing string at once.
            CheckNoMissingStrings();

            // 6. The village is populated, not staged: with nothing on screen asking for anything,
            // somebody should be walking around out there.
            yield return VerifyResidentsWander();
            yield return VerifyTheOutsideIsNotWalkable();

            // 7. Help: the one screen that tells a lost player what to do next.
            yield return VerifyHelpPanel();

            // 8. The progression map draws one stop per declared stage now, on a pan-and-zoom
            // viewport. Exercise the public button so a beautiful map hidden behind a broken route
            // cannot pass, and walk every marker so a season that grew cannot leave its last stages
            // off the sheet unnoticed.
            yield return VerifyProgressMap();

            // 9. The other public button on the HUD, and the only screen in the game where a tap
            // changes the character. Nothing automated had ever opened it.
            yield return VerifyBackpack();

            // 9b. The table, when the build has one. Every defect the group screen has had was
            // found on a phone and not by this harness, because this harness had never opened it.
            yield return VerifyTheTable();

            // 10. The toggle, in the authoring locale's run only — one pass is enough to prove the
            // path, and it is the only thing here that exercises a language change at runtime
            // rather than a language pinned at boot. Everything else in this file would pass on a
            // build whose toggle did nothing at all, which is exactly what it once did.
            if (Locales.Active == Locales.Source)
            {
                yield return SwitchLanguageAndVerify();
            }

            // 11. The whole season, stage by stage, read from the table. This replaces the three
            // hand-written day scripts: a season that grows a stage grows a leg of the run with it,
            // instead of quietly going untested past the last one somebody remembered to write.
            yield return PlayTheSeason();
        }

        /// <summary>
        /// Opens the group screen against a real server, makes a table, lets it refresh twice,
        /// sounds the trumpet, declines the call, and closes it.
        ///
        /// <b>Why the two refreshes are the point.</b> The screen redraws its rows on every poll,
        /// and the first version added a layout group to a row on every redraw; the second poll got
        /// a null from Unity and threw, from the moment a trumpet appeared — on a phone, in the
        /// second hour of playing it, and past four green gates. A logged exception fails this run,
        /// so standing here for one poll interval is the assertion.
        ///
        /// <b>What this cannot prove.</b> The raid itself: it opens at the trumpet's hour, and the
        /// hour is two hours out. The server's turn arithmetic is covered by its own tests; what a
        /// second phone sees is covered by nobody, which the group design document says in §09.
        ///
        /// Skipped, visibly, when no -table-url was given: without one the drawer has no button and
        /// the screen does not exist, which is the property that lets the solo build ship unchanged.
        /// tools/e2e.sh starts a server and passes the URL, so on the gate this is never skipped.
        /// </summary>
        IEnumerator VerifyTheTable()
        {
            if (!TableService.Available)
            {
                Skip("the group screen", "no -table-url; the drawer has no button and the screen does not exist");
                yield break;
            }

            yield return OpenHudMenu();
            yield return Tap("TableButton", "Obra em grupo in the drawer");
            yield return WaitForObject("CreateTable", "the group screen's two doors");
            Record("the group screen is the top modal", ModalRoot.TopId == "table", "top " + DescribeTopModal());
            yield return Capture(1, "table-doors");

            yield return Tap("CreateTable", "Criar uma obra");
            yield return WaitUntil(() => { string c = TextOf("Code"); return c != null && c.Length == 6; },
                "a six-character code from the server");
            string code = TextOf("Code") ?? string.Empty;
            bool readable = code.Length == 6 && code.IndexOfAny(new[] { '0', 'O', '1', 'I', 'L' }) < 0;
            Record("the code can be read aloud", readable, "code=" + code);

            yield return WaitForObject("SeatsFree", "the empty seats, in one line");
            yield return WaitUntil(() => FindByPrefix("Event_") != null, "the first feed line");
            yield return Capture(1, "table-made");

            // One full poll interval, plus a frame: the second redraw of every row.
            float until = Time.realtimeSinceStartup + TablePollSeconds + 1f;
            while (Time.realtimeSinceStartup < until) yield return null;
            Record("the group screen survives a second poll", Find("SeatsFree") != null && ModalRoot.TopId == "table",
                "still on screen after " + TablePollSeconds + "s");

            yield return Tap("Trumpet", "Tocar a trombeta");
            yield return WaitForObject("NotToday", "the call with its two answers");
            Record("sounding it counts as coming", TextOf("CallMine") == Loc.T("table.call.you_sounded"),
                "CallMine=" + Quote(TextOf("CallMine")));
            yield return Capture(1, "table-call");

            yield return Tap("NotToday", "Não consigo hoje");
            yield return WaitUntil(() => TextOf("CallMine") == Loc.T("table.call.you_cannot"),
                "the answer echoed back");
            Record("not coming is an answer the screen keeps", TextOf("CallMine") == Loc.T("table.call.you_cannot"),
                "CallMine=" + Quote(TextOf("CallMine")));

            yield return TapTheTableClose();
            yield return WaitUntil(() => ModalRoot.TopId != "table", "the group screen to close");
            Record("the group screen closes", ModalRoot.TopId != "table", "top " + DescribeTopModal());
        }

        /// <summary>Matches the screen's own poll interval; a change there should change this.</summary>
        const float TablePollSeconds = 6f;

        /// <summary>
        /// The table's Fechar, and not any other panel's: several screens name their close control
        /// the same, so this one is found under the card the table modal owns.
        /// </summary>
        IEnumerator TapTheTableClose()
        {
            GameObject target = null;
            foreach (Transform transform in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (transform == null || transform.name != "Close") continue;
                GameObject go = transform.gameObject;
                if (!IsInLiveScene(go) || !go.activeInHierarchy) continue;
                if (go.GetComponentInParent<TablePanel>() == null) continue;
                target = go;
                break;
            }

            if (target == null)
            {
                Record("tap the table's Fechar", false, "no active Close under a TablePanel");
                yield break;
            }

            yield return TapObject(target, "tap the table's Fechar");
        }

        static GameObject FindByPrefix(string prefix)
        {
            foreach (Transform transform in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (transform == null || !transform.name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                GameObject go = transform.gameObject;
                if (IsInLiveScene(go) && go.activeInHierarchy) return go;
            }
            return null;
        }

        /// <summary>
        /// Plays every stage the season declares, in order, and proves the season ENDS.
        ///
        /// The three literal statements this replaced named day one, day two and day three, which
        /// is the shape that let six stages ship unreachable behind a harness that reported itself
        /// green. Reading the table means the run covers whatever the table says, and it means the
        /// last assertion here — that the terminal stage was reached and named the vocation — is a
        /// statement about the season rather than about the number nine. An off-by-one in the
        /// terminal stage reads as a hang otherwise, and a hang is the one failure nobody diagnoses.
        /// </summary>
        IEnumerator PlayTheSeason()
        {
            StageDef[] stages = GameData.Stages;
            if (stages == null || stages.Length == 0)
            {
                Record("the season has a stage table", false,
                    "GameData.Stages is empty, so there is nothing to play");
                yield break;
            }

            Record("the season declares its stages", true, stages.Length + " stage(s) to play");

            int startStage = StartStage;
            StageDef terminal = null;
            int played = 0;

            // A run seeded onto the dedication's day laid nothing, and the last stage now waits
            // for its gate segment to stand. Hand the seeded run the courses it skipped, in the
            // scene rather than in the seed: GameData is not loaded when the seed is written, and
            // the director watches the live wall, so work applied here starts the beat.
            if (startStage > 1)
            {
                GiveASeededRunItsGate(startStage);
            }

            // Counted before anything is played, so the tally below is a promise made in advance
            // rather than a description of whatever happened to run.
            int expected = 0;
            for (int i = 0; i < stages.Length; i++)
            {
                if (stages[i] != null && !string.IsNullOrEmpty(stages[i].id) && stages[i].day >= startStage)
                {
                    expected++;
                }
            }

            for (int i = 0; i < stages.Length; i++)
            {
                StageDef stage = stages[i];
                if (stage == null || string.IsNullOrEmpty(stage.id) || stage.day < startStage)
                {
                    // A null or blank entry is already an error from GameData.VerifyStages; failing
                    // again here would only repeat a diagnosis the log already carries.
                    continue;
                }

                yield return PlayStage(stage);
                played++;

                if (stage.terminal)
                {
                    terminal = stage;
                    break;
                }
            }

            Record("every stage was played", played == expected && expected > 0,
                played + " of " + expected + " stage(s) reached" +
                (startStage > 1 ? " (seeded from stage " + startStage + ", not a gate)" : ""));

            Record("the season reaches its last stage", terminal != null,
                terminal != null
                    ? "stage \"" + terminal.id + "\" (day " + terminal.day + ") declared terminal and was played"
                    : "no stage declaring terminal was ever reached");
        }

        /// <summary>
        /// Completes the gate segment for a run seeded onto the stage that closes it. Not a gate:
        /// the record the ending writes about earned work already says so for any seeded run.
        /// </summary>
        static void GiveASeededRunItsGate(int startStage)
        {
            StageDef stage = GameData.Stage(startStage);
            if (stage == null || !stage.terminal || !stage.closes_gate)
            {
                return;
            }

            WallSystem wall = UnityEngine.Object.FindFirstObjectByType<WallSystem>();
            string gate = GateSegmentId();
            if (wall == null || string.IsNullOrEmpty(gate) || !wall.Contains(gate) || wall.IsComplete(gate))
            {
                return;
            }

            wall.ApplyWork(gate, wall.RemainingUnits(gate));
            Debug.LogWarning("[E2E] Seeded on the dedication's day: \"" + gate + "\" was handed its courses, which no played run gets.");
        }

        /// <summary>
        /// One stage, from its morning to the morning after it.
        ///
        /// The dispatch mirrors <see cref="StageDirector"/> deliberately: the type chooses between a
        /// gathering and a fight, and everything else acts on what the stage DECLARES. Keeping the
        /// two the same shape is what stops the harness proving a season the director does not play.
        /// </summary>
        IEnumerator PlayStage(StageDef stage)
        {
            bool full = IsFullBatteryStage(stage);
            string label = "stage " + stage.day + " (" + stage.id + ")";
            Debug.Log("[E2E] " + label + " type=" + stage.type + " tier=" + (full ? "full" : "traversal"));

            GameState state = TryGetState();
            int startingDay = state != null ? state.day : -1;

            if (IsFirstStage(stage))
            {
                // The opening stage has no morning — it opens in a cutscene — and it is the only
                // stage whose daylight clock, mat and check-in ordering are asserted end to end.
                yield return PlayADayToItsEnd(stage);
            }
            else
            {
                Record(label + " was reached", state != null && state.day == stage.day,
                    "the run is standing on day " + (state != null ? state.day : -1));

                yield return ClearTheMorning(stage, full);
                yield return Capture(stage.day, "stage");

                if (full)
                {
                    yield return CheckHudThroughTheDrawer();
                    CheckNoMissingStrings();
                }

                yield return PlayTheBeat(stage, full);

                if (stage.finishes_wall)
                {
                    yield return VerifyTheWallIsFinished(stage);
                }

                if (stage.closes_gate)
                {
                    yield return PlayGateStage(stage);
                }

                if (!stage.terminal)
                {
                    yield return SpendTheDay(stage);
                }
            }

            if (stage.terminal)
            {
                yield return VerifyTheLastStageHasNoTomorrow(stage);
            }
            else if (startingDay > 0)
            {
                GameState after = TryGetState();
                Record(label + " rolled over", after != null && after.day == startingDay + 1,
                    "day " + startingDay + " -> " + (after != null ? after.day : -1));
            }
        }

        /// <summary>
        /// Spends what the last stage has left and proves the night never comes.
        ///
        /// Asserted by trying rather than by observing, and the difference is everything. Reading
        /// "the day did not advance" one frame after the reveal closes proves nothing: no day
        /// advances in a frame. What proves the hold is scoped right is doing the one thing that
        /// ends every OTHER day — spending the last of the work — and watching the split refuse to
        /// open. That is the exact inverse of the defect that made six stages unreachable: the hold
        /// was taken unconditionally and never released, so the calendar stopped wherever it was
        /// taken. It has to be taken here, and only here, and it has to hold.
        /// </summary>
        IEnumerator VerifyTheLastStageHasNoTomorrow(StageDef stage)
        {
            DayCycle cycle = UnityEngine.Object.FindFirstObjectByType<DayCycle>();
            ResourceSystem resources = ResourceSystem.Find();
            GameState state = TryGetState();

            if (cycle == null || resources == null || state == null)
            {
                Record("the last stage has no tomorrow", false,
                    "cycle=" + (cycle != null) + " resources=" + (resources != null) + " state=" + (state != null));
                yield break;
            }

            int startingDay = state.day;
            resources.Spend(state.workCapacity);

            // Long enough for a day that was going to end to have ended: the split opens off the
            // daylight clock within a frame or two of the capacity running out.
            yield return new WaitForSecondsRealtime(TerminalHoldProofSeconds);

            Record("the last stage has no tomorrow",
                state.day == startingDay && !EndDayPanel.IsOpen && cycle.IsDuskHeld,
                "day " + startingDay + " -> " + state.day + ", split open=" + EndDayPanel.IsOpen +
                ", dusk pending=" + cycle.IsDuskPending + ", held=" + cycle.IsDuskHeld);

            yield return Capture(stage.day, "no-tomorrow");
        }

        /// <summary>
        /// Whether this stage gets the whole battery of checks or a cheap traversal.
        ///
        /// Chosen from what the table DECLARES rather than by day number, so the tier follows the
        /// beats when a season is re-ordered: the opening, the stage that turns chapter and verse
        /// on, and the stage that ends the season. Those three carry the opening's ordering rules,
        /// both deep_read doors and the reveal; nothing else in the season is unique enough to be
        /// worth its screenshots.
        /// </summary>
        static bool IsFullBatteryStage(StageDef stage)
        {
            return stage != null && (IsFirstStage(stage) || stage.reveals_page || stage.terminal);
        }

        static bool IsFirstStage(StageDef stage)
        {
            StageDef[] stages = GameData.Stages;
            return stages != null && stages.Length > 0 && stages[0] != null && stage != null
                   && stages[0].day == stage.day;
        }

        /// <summary>Runs whatever this stage's type puts between its morning and its ordinary day.</summary>
        IEnumerator PlayTheBeat(StageDef stage, bool full)
        {
            switch (stage.type)
            {
                case StageTypes.Rest:
                case StageTypes.Gate:
                    yield return PlayCutsceneStage(stage, full);
                    break;

                case StageTypes.Battle:
                case StageTypes.Boss:
                    yield return PlayContestStage(stage, full);
                    break;

                default:
                    // intro and work stages have nothing scripted in them. Not a failure — most of
                    // the season is exactly this — but named, so a stage type nobody wrote a branch
                    // for cannot pass as an ordinary day.
                    if (stage.type != StageTypes.Intro && stage.type != StageTypes.Work)
                    {
                        Record("stage " + stage.day + " has a beat this run can play", false,
                            "type \"" + stage.type + "\" has no branch here");
                    }
                    break;
            }
        }

        // ------------------------------------------------------------------ the three stage kinds

        /// <summary>
        /// Waits out a stage whose beat is an authored gathering, and taps through it.
        ///
        /// The director holds the day open for six quiet seconds before it plays, so this cannot
        /// simply wait for a control to appear: the residents have something to say on that morning
        /// and a line waits for a tap, so a run that waited without tapping would be waiting for a
        /// beat that its own silence is holding back.
        /// </summary>
        IEnumerator PlayCutsceneStage(StageDef stage, bool full)
        {
            string nodeId = stage.cutscene_node;
            if (string.IsNullOrEmpty(nodeId))
            {
                Record("stage " + stage.day + " names a gathering", false,
                    "a " + stage.type + " stage with no cutscene_node has no beat to play");
                yield break;
            }

            bool seen = false;
            yield return AdvanceDialogueUntil(
                delegate
                {
                    if (CurrentDialogueNode() == nodeId) seen = true;
                    return seen && !DialogueIsPlaying();
                },
                "the gathering \"" + nodeId + "\" on stage " + stage.day,
                true,
                BeatTimeoutSeconds);

            Record("stage " + stage.day + " played its gathering", seen,
                seen ? "\"" + nodeId + "\" was on screen" : "\"" + nodeId + "\" never played");

            if (full)
            {
                yield return Capture(stage.day, "gathering");
                CheckNoMissingStrings();
            }
        }

        /// <summary>
        /// Fights the contest this stage names, and proves it is THIS contest rather than a replay
        /// of an earlier one.
        ///
        /// A season with two contests has three failure modes that share a shape, and all three are
        /// silent: the run-wide resolved flag short-circuiting the second fight into the first one's
        /// ending, the page arriving twice, and the move the page unlocked being taken back. None of
        /// them throws, none of them logs, and each of them reads as a deliberate ending. So the
        /// assertions here are about identity — which fight is running, whose page is on screen —
        /// and not only about a fight happening.
        /// </summary>
        IEnumerator PlayContestStage(StageDef stage, bool full)
        {
            ContestConfig config = ContestConfigFor(stage);
            string firstMove = FirstMoveObjectName(config);

            if (config == null || firstMove == null)
            {
                Record("stage " + stage.day + " has a contest to fight", false,
                    "contest.json defines no usable moves for \"" + (stage.contest ?? "none") + "\"");
                yield break;
            }

            yield return AdvanceDialogueUntil(() => Find(firstMove) != null,
                "the contest \"" + stage.contest + "\" on stage " + stage.day, true, BeatTimeoutSeconds);

            MoraleContest contest = UnityEngine.Object.FindFirstObjectByType<MoraleContest>();
            Record("stage " + stage.day + " fights its own contest",
                contest != null && contest.ContestId == stage.contest,
                contest == null
                    ? "no MoraleContest in the scene"
                    : "the component is standing in for \"" + (contest.ContestId ?? "nothing") +
                      "\", the stage declares \"" + stage.contest + "\"");

            yield return Capture(stage.day, "contest");
            if (full)
            {
                CheckNoMissingStrings();
            }

            // Whether the page-gated move is offered before the page has been shown is a different
            // question on the reveal stage than on a later one, and both answers are load bearing.
            VerifyThePageGatedMove(stage, config);

            // The Page is NOT waited for here. It arrives on the turn the contest's own config
            // names, which means a move has to be played first — waiting for it before the fight
            // has started is a thirty-second timeout on a panel that was never going to be there
            // yet. It is handled inside the turn loop, where it actually happens.
            yield return PlayOutTheContest(stage, config);
            yield return Capture(stage.day, "outcome");
            yield return Tap("Continue", "the outcome of \"" + stage.contest + "\"");
        }

        /// <summary>
        /// The beat the whole build exists to produce, and the first of the two deep_read doors.
        ///
        /// Saber mais is the only way into the chapter reader, and that tap is where deep_read
        /// begins. The event ITSELF is deliberately not asserted here: it needs twenty seconds of
        /// visible time and sixty percent of a chapter scrolled, and it accumulates only while the
        /// player's window has focus — which a run launched from a terminal, alongside another
        /// locale's run, cannot promise. An assertion that flakes on window focus is worse than no
        /// assertion, and lowering the bar so a script could clear it would change what the number
        /// means. What IS asserted is everything up to the human: the door opens, the chapter behind
        /// it is real and readable, and the open was recorded.
        /// </summary>
        IEnumerator PlayThePageAndTheReader(StageDef stage, ContestConfig config)
        {
            yield return WaitForObject("CloseButton", "A Página on stage " + stage.day);
            yield return Capture(stage.day, "the-page");
            CheckNoMissingStrings();

            yield return OpenTheReaderAndComeBack("ReadButton", "saber mais", stage, "page-reader",
                delegate(string chapterRef) { _pageChapterRef = chapterRef; });

            yield return Tap("CloseButton", "closing A Página");

            string gatedMove = PageGatedMoveObjectName(config);
            if (gatedMove != null)
            {
                yield return WaitForObject(gatedMove, "the move the Page unlocks");
                Record("the Page unlocked its move", Find(gatedMove) != null,
                    gatedMove + " is in the menu once the Page has been read");
            }

            yield return Capture(stage.day, "unlocked");
        }

        /// <summary>
        /// The ending: the doors hung with the player's name on them, the second deep_read door,
        /// and the vocation named once at the very end.
        ///
        /// The gate panel is a screen of its own with its own way on, so waiting for the reveal
        /// straight after the gathering waits for the screen after the one that is up.
        /// </summary>
        IEnumerator PlayGateStage(StageDef stage)
        {
            yield return WaitForObject("KnowMore", "the gate closed with the player's name");
            yield return Capture(stage.day, "gate");
            CheckNoMissingStrings();

            // The panel is not the ending; the doors are. CloseTheGate runs BEFORE this screen
            // opens, so a gate segment that never finished — a wrong id in the table, a wall the
            // work path refused — still produces exactly this panel, exactly this record and
            // exactly this reveal, over a wall with a hole in it. Nothing on screen says so.
            WallSystem wall = UnityEngine.Object.FindFirstObjectByType<WallSystem>();
            string gateSegment = GateSegmentId();
            Record("the doors are hung on the segment the table names",
                wall != null && !string.IsNullOrEmpty(gateSegment) && wall.IsComplete(gateSegment),
                wall == null ? "no WallSystem in the scene"
                    : string.IsNullOrEmpty(gateSegment) ? "no stage declares a gate segment"
                    : "\"" + gateSegment + "\" is at course " + wall.StageOf(gateSegment) +
                      " of " + WallSystem.StagesPerSegment);

            Record("the wall is whole at the dedication",
                wall != null && wall.CompletedStages == wall.TotalStages,
                wall == null ? "no WallSystem in the scene"
                    : wall.CompletedStages + " of " + wall.TotalStages + " course(s) standing");

            // The doors hang on work this run laid, not on courses the ending handed over. The
            // night crew serves the sheltered segments first and only reaches this one when
            // nothing else is unfinished, so on a season where the wall is finished by stage 8
            // the whole cost of the gate segment has to have come from the day's capacity.
            int gateCost = wall != null && !string.IsNullOrEmpty(gateSegment) ? wall.TotalCost(gateSegment) : 0;
            Record("the gate was earned by the run's own work",
                gateCost > 0 && _gateWorkLaid >= gateCost && (StartStage <= 1),
                StartStage > 1 ? "seeded from stage " + StartStage + ", so the work was not this run's"
                    : "laid " + _gateWorkLaid + " unit(s) against a cost of " + gateCost);

            yield return OpenTheReaderAndComeBack("KnowMore", "the record's chapter", stage, "gate-reader",
                delegate(string chapterRef)
                {
                    Record("the ending opens its own chapter",
                        !string.IsNullOrEmpty(chapterRef) && chapterRef != _pageChapterRef,
                        "the reveal opened \"" + (_pageChapterRef ?? "nothing") +
                        "\", the ending opened \"" + (chapterRef ?? "nothing") + "\"");
                });

            yield return Tap("Continue", "past the gate");

            yield return WaitForObject("Close", "the vocation reveal");
            yield return Capture(stage.day, "reveal");
            CheckNoMissingStrings();

            GameState state = TryGetState();
            Record("the season names a vocation", state != null && state.HasFlag(GameFlags.VocationRevealed),
                state == null ? "no game state" : "vocation_revealed=" + state.HasFlag(GameFlags.VocationRevealed));

            // The name on the screen, not only the flag. The screenshot above lands during the
            // entrance fade and is often blank, so the text is read rather than looked at, and it
            // has to be one of the six authored names rather than the fallback.
            string revealed = TextOf("Name");
            Record("the reveal shows an authored vocation name", IsAuthoredVocationName(revealed),
                "Name=" + Quote(revealed));

            yield return Tap("Close", "closing the reveal");

            // The ending: the wall with the names on it, the six vocations, the chapter after.
            yield return WaitForObject("SeasonEndClose", "the ending after the reveal");
            yield return Capture(stage.day, "season-end");
            CheckNoMissingStrings();
            Record("the ending offers the chapter after the season and a new work",
                Find("ReadOn") != null && Find("SeasonEndRestart") != null,
                "ReadOn=" + (Find("ReadOn") != null) + " restart=" + (Find("SeasonEndRestart") != null));
            yield return Tap("SeasonEndClose", "closing the ending");
        }

        /// <summary>True when the text is one of the six display names vocations.json authors for this locale.</summary>
        static bool IsAuthoredVocationName(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            foreach (VocationDef vocation in GameData.Vocations ?? Array.Empty<VocationDef>())
            {
                if (vocation != null && !string.IsNullOrEmpty(vocation.display) && vocation.display == text)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Taps a door into the chapter reader, proves what is behind it, and closes it again.
        ///
        /// Both deep_read doors go through here so neither can be asserted more weakly than the
        /// other. The chapter is read off the live reader rather than from a constant, which is what
        /// makes the ending's assertion — that it opened a DIFFERENT chapter from the reveal's — an
        /// end-to-end proof that the gate panel was handed its own chapter. That call site can be
        /// wrong silently: the two-argument overload still compiles and still opens a reader, it
        /// just opens the wrong chapter, and nothing on screen says so.
        /// </summary>
        IEnumerator OpenTheReaderAndComeBack(string doorName, string doorDescription, StageDef stage,
            string shotName, Action<string> onChapter)
        {
            yield return Tap(doorName, doorDescription);
            yield return WaitForObject("Close", "the chapter reader");
            yield return Capture(stage.day, shotName);

            string chapterRef = ChapterReaderUI.Current != null ? ChapterReaderUI.Current.ChapterRef : null;
            Record("the reader on stage " + stage.day + " is open on a chapter",
                !string.IsNullOrEmpty(chapterRef),
                "ChapterReaderUI.Current.ChapterRef = \"" + (chapterRef ?? "nothing") + "\"");

            VerifyChapterIsReadable(stage, chapterRef);
            VerifyChapterOpenWasRecorded(stage, chapterRef);

            if (onChapter != null)
            {
                onChapter(chapterRef);
            }

            CheckNoMissingStrings();
            yield return Tap("Close", "closing the reader on stage " + stage.day);
        }

        /// <summary>
        /// The chapter behind a door has to be a chapter, not a single verse and not an empty shell.
        ///
        /// A placeholder build generates chapters with exactly one verse, so the verse-count test is
        /// tolerated there explicitly rather than left to fail for a reason nobody would read as
        /// "this build carries no licensed text". The door still has to open; what is relaxed is
        /// only how much is behind it.
        /// </summary>
        void VerifyChapterIsReadable(StageDef stage, string chapterRef)
        {
            if (string.IsNullOrEmpty(chapterRef))
            {
                return;
            }

            ChapterEntry chapter = null;
            try
            {
                chapter = ScriptureService.GetChapter(chapterRef);
            }
            catch (Exception exception)
            {
                Record("the reader's chapter resolves on stage " + stage.day, false,
                    chapterRef + ": " + exception.Message);
                return;
            }

            int verses = chapter != null && chapter.verses != null ? chapter.verses.Length : 0;
            bool placeholder = ScriptureService.IsPlaceholderBuild;
            Record("the reader's chapter has something to read on stage " + stage.day,
                verses > (placeholder ? 0 : 1),
                chapterRef + " has " + verses + " verse(s)" + (placeholder ? " (placeholder build)" : ""));
        }

        /// <summary>
        /// The open reached the funnel. Read back out of the run's own telemetry file rather than
        /// asserted on a field, because what matters is that the event was WRITTEN — a reader that
        /// opens beautifully and records nothing is a door the product cannot see anybody using.
        /// </summary>
        void VerifyChapterOpenWasRecorded(StageDef stage, string chapterRef)
        {
            if (string.IsNullOrEmpty(chapterRef))
            {
                return;
            }

            Telemetry.Flush();
            bool recorded = TelemetryRecorded(TelemetryEvents.ChapterOpened, chapterRef);
            Record("opening the reader on stage " + stage.day + " was recorded", recorded,
                recorded
                    ? TelemetryEvents.ChapterOpened + " carries " + chapterRef
                    : "no " + TelemetryEvents.ChapterOpened + " line for " + chapterRef + " in " + TelemetryPath());
        }

        static string TelemetryPath()
        {
            return Path.Combine(AppPaths.DataRoot, "telemetry.jsonl");
        }

        /// <summary>
        /// True when this run's telemetry file carries an event of that name naming that reference.
        ///
        /// A substring match over one line rather than a parse: the sink writes compact JSON with
        /// no spaces, the two keys are stable, and a JSON reader here would be a second
        /// implementation of the file format that could disagree with the one under test.
        /// </summary>
        static bool TelemetryRecorded(string eventName, string reference)
        {
            string path = TelemetryPath();
            string[] lines;
            try
            {
                lines = File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>();
            }
            catch (Exception)
            {
                return false;
            }

            string eventToken = "\"event\":\"" + eventName + "\"";
            string refToken = "\"ref\":\"" + reference + "\"";
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line != null && line.Contains(eventToken) && line.Contains(refToken))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether the page-gated move is offered, and what that answer is supposed to be here.
        ///
        /// Before the reveal it must be absent — that is the rule the page exists to make true.
        /// AFTER the reveal, on any later contest, it must be PRESENT: the page happens once in a
        /// season, and a second fight that re-locked the move would be taking back something the
        /// player earned, which is the direction rule 7 forbids and which nothing on screen would
        /// announce.
        /// </summary>
        void VerifyThePageGatedMove(StageDef stage, ContestConfig config)
        {
            string gatedMove = PageGatedMoveObjectName(config);
            if (gatedMove == null)
            {
                return;
            }

            bool offered = Find(gatedMove) != null;

            if (stage.reveals_page)
            {
                Record("the page-gated move is locked before the Page on stage " + stage.day, !offered,
                    offered ? gatedMove + " is offered before the Page has been shown"
                            : gatedMove + " is not in the menu");
            }
            else
            {
                GameState state = TryGetState();
                bool pageAlreadyShown = state != null && state.HasFlag(GameFlags.PageShown);
                if (pageAlreadyShown)
                {
                    Record("a later contest keeps what the Page unlocked", offered,
                        offered ? gatedMove + " is still in the menu"
                                : gatedMove + " was taken back after the Page had already been read");
                }
            }
        }

        /// <summary>
        /// Takes turns until the contest resolves, and watches for the page arriving where it must
        /// not.
        ///
        /// Bounded by the contest's OWN declared limit plus slack, so a fight that never ends means
        /// the limit is not being enforced rather than that this cap was sized against a different
        /// fight. The move order is discovered from the contest's config: a hardcoded list of four
        /// names is a list that silently stops covering a fifth move, and stage eight has one.
        /// </summary>
        IEnumerator PlayOutTheContest(StageDef stage, ContestConfig config)
        {
            string[] moves = MoveObjectNames(config);
            int cap = (config != null && config.turn_limit > 0 ? config.turn_limit : 8) + ContestTurnSlack;
            bool pageHandled = false;
            bool pageIntruded = false;

            for (int turn = 0; turn < cap; turn++)
            {
                if (Find("Continue") != null)
                {
                    Record("the contest \"" + stage.contest + "\" resolved", true,
                        "an outcome was reached in " + turn + " move(s)");
                    break;
                }

                // The Page interrupts the fight on the turn its config names, which is why it is
                // met here rather than waited for outside: it needs a move played first, and the
                // contest is paused underneath it until it is dismissed.
                if (ThePageIsOpen())
                {
                    if (stage.reveals_page && !pageHandled)
                    {
                        yield return PlayThePageAndTheReader(stage, config);
                        pageHandled = true;
                        continue;
                    }

                    // A page on a stage that carries none — or a second one on the stage that does
                    // — is the beat the whole build exists to produce, spent again on a fight that
                    // was never meant to carry it. It fails silently otherwise: the panel simply
                    // opens. Dismissed rather than left up, so the run reports the fault instead of
                    // stalling behind it and reporting a stall.
                    pageIntruded = true;
                    yield return Tap("CloseButton", "dismissing a Page that should not be here");
                    continue;
                }

                GameObject move = null;
                for (int i = 0; i < moves.Length && move == null; i++)
                {
                    move = Find(moves[i]);
                }

                if (move == null)
                {
                    yield return new WaitForSeconds(0.2f);
                    continue;
                }

                // Finding a move is not evidence the contest will accept one, and no check made
                // before the tap can be: the Page opens on the turn its config names and the
                // outcome card lands when resolve runs out, both as a CONSEQUENCE of the move just
                // played, so either can arrive while the tap below is mid-retry on the next one.
                // A guard here would still lose that race — it was tried, and the run failed
                // identically.
                //
                // So the tap is offered rather than asserted. If the screen changed underneath it,
                // say nothing and let the top of the loop meet the Page or the outcome properly on
                // the next pass. The fight still has to resolve, and the check after this loop is
                // what fails if these moves were genuinely unreachable — one honest sentence about
                // the contest, instead of a stack of true sentences about a scrim.
                yield return TapObject(move, "tap " + move.name, false, ContestTapTimeoutSeconds);
                if (!_lastTapLanded)
                {
                    yield return new WaitForSeconds(0.2f);
                    continue;
                }

                yield return new WaitForSeconds(0.4f);
            }

            if (Find("Continue") == null)
            {
                Record("the contest \"" + stage.contest + "\" resolved", false,
                    "no outcome after " + cap + " turn(s) of trying");
            }

            if (stage.reveals_page)
            {
                Record("the Page arrived inside the fight that carries it", pageHandled,
                    pageHandled ? "A Página opened and its reader was reached"
                        : "the reveal stage fought its whole contest and no page ever came");
            }
            else
            {
                Record("the Page stays away from stage " + stage.day, !pageIntruded,
                    pageIntruded
                        ? "A Página opened on a stage that does not declare the reveal"
                        : "no page on a stage that carries none");
            }
        }

        /// <summary>
        /// The wall is finished except for the one segment the closing stage hangs its doors on.
        ///
        /// This is what makes the last stage mean anything: a dedication over a half-built wall is
        /// the ending playing on top of a season that never happened, and it looks identical.
        /// </summary>
        IEnumerator VerifyTheWallIsFinished(StageDef stage)
        {
            WallSystem wall = UnityEngine.Object.FindFirstObjectByType<WallSystem>();
            if (wall == null)
            {
                Record("the wall is finished on stage " + stage.day, false, "no WallSystem in the scene");
                yield break;
            }

            string gateSegment = GateSegmentId();

            // Segment by segment rather than by a total, and the difference is not pedantry: the
            // night crew builds on whatever segment is open, including the one the gate will hang
            // on, so a course standing there is ordinary play. A total course count would therefore
            // read high and fail a run whose wall is exactly right. What the stage promises is that
            // every OTHER segment is finished, and that is what gets asserted.
            yield return WaitUntil(() => EverySegmentButTheGateIsComplete(wall, gateSegment),
                "the wall to be finished except for the gate");

            var unfinished = new List<string>();
            foreach (string id in wall.SegmentIds)
            {
                if (string.IsNullOrEmpty(id) || id == gateSegment) continue;
                if (!wall.IsComplete(id)) unfinished.Add(id + " at course " + wall.StageOf(id));
            }

            Record("stage " + stage.day + " finishes the wall but the gate", unfinished.Count == 0,
                unfinished.Count == 0
                    ? "every segment but \"" + (gateSegment ?? "none declared") + "\" is standing, and that one is at course "
                      + (gateSegment != null ? wall.StageOf(gateSegment).ToString() : "-")
                    : "still short: [" + string.Join(", ", unfinished) + "]");
        }

        /// <summary>
        /// True when A Página itself is the panel on top.
        ///
        /// Asked of the modal stack by id rather than by looking for a button called CloseButton,
        /// because the backpack's close button carries that same name. The two cannot be open at
        /// once in an ordinary run, but the assertion this feeds — "no page on a stage that carries
        /// none" — is one that fails a whole gate, and a gate that can go red because two unrelated
        /// panels share a control name is a gate somebody stops trusting.
        /// </summary>
        static bool ThePageIsOpen()
        {
            return ModalRoot.IsOpen && ModalRoot.TopId == ThePagePanel.ModalId;
        }

        static bool EverySegmentButTheGateIsComplete(WallSystem wall, string gateSegment)
        {
            foreach (string id in wall.SegmentIds)
            {
                if (string.IsNullOrEmpty(id) || id == gateSegment) continue;
                if (!wall.IsComplete(id)) return false;
            }

            return true;
        }

        /// <summary>The segment the closing stage hangs its doors on, read from the table.</summary>
        static string GateSegmentId()
        {
            foreach (StageDef stage in GameData.Stages ?? Array.Empty<StageDef>())
            {
                if (stage != null && stage.closes_gate && !string.IsNullOrEmpty(stage.gate_segment))
                {
                    return stage.gate_segment;
                }
            }

            return null;
        }

        // ------------------------------------------------------------------ the ordinary day

        /// <summary>
        /// Spends the day's work and confirms the split the spent day opens.
        ///
        /// <see cref="PlayADayToItsEnd"/> asserts the shape of the loop on the opening stage and
        /// stops at the morning after it. This is the same loop with nothing to prove about it, run
        /// for the only reason that the stage after it cannot be reached without it.
        ///
        /// <b>The work goes on the gate segment first.</b> The dedication no longer hands the
        /// player their segment: the last stage waits until it is standing, and a run that only
        /// spent its capacity would reach day 9 with a segment at course zero and a season that
        /// never ends. So each day lays what it can on the segment the table names, the way a
        /// player who understood the assignment would, and spends the rest. The total laid is kept
        /// so the ending can assert that the gate was earned by this run's own hands.
        /// </summary>
        IEnumerator SpendTheDay(StageDef stage)
        {
            string dayName = "stage " + stage.day;

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

            LayTheDayOnTheGate(resources, state);
            resources.Spend(state.workCapacity);
            yield return WaitForTheSplit(cycle, dayName);
            yield return Tap("Confirm", "the split on " + dayName);
            yield return WaitUntil(() => state.day > startingDay && !cycle.IsResolving, "the morning after " + dayName);
        }

        /// <summary>Work units this run has laid on the gate segment with its own capacity.</summary>
        int _gateWorkLaid;

        /// <summary>
        /// Lays as much of today's capacity as the gate segment can still take, through the wall
        /// system directly. Pathing to the wall and tapping it is the interaction layer's business
        /// and is proved elsewhere; what this proves is that a season's worth of ordinary days is
        /// enough to earn the gate.
        /// </summary>
        void LayTheDayOnTheGate(ResourceSystem resources, GameState state)
        {
            WallSystem wall = UnityEngine.Object.FindFirstObjectByType<WallSystem>();
            string gate = GateSegmentId();
            if (wall == null || string.IsNullOrEmpty(gate) || !wall.Contains(gate))
            {
                return;
            }

            int units = Mathf.Min(state.workCapacity, wall.RemainingUnits(gate));
            if (units <= 0)
            {
                return;
            }

            if (wall.ApplyWork(gate, units) && resources.Spend(units))
            {
                _gateWorkLaid += units;
                state.Bump(GameFlags.LaidCounter(state.day), units);
            }
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
        /// step away from the cause. A resident's conversation does the same thing through the input
        /// lock, and a branch left on screen holds it indefinitely because a tap on a decision does
        /// nothing at all. Hence all three halves of this: keep clearing panels, keep answering
        /// branches, and if the split still never opens, say which of the three conditions in
        /// <see cref="DayCycle.DuskWaits"/> is the one holding it.
        /// </summary>
        IEnumerator WaitForTheSplit(DayCycle cycle, string dayName)
        {
            float deadline = Time.realtimeSinceStartup + StepTimeoutSeconds;

            while (!EndDayPanel.IsOpen && Time.realtimeSinceStartup < deadline)
            {
                if (ModalRoot.IsOpen)
                {
                    GameObject action = FindModalExit();
                    if (action != null)
                    {
                        yield return TapObject(action, "clear " + action.name + " holding the night of " + dayName);
                        yield return new WaitForSeconds(0.25f);
                        continue;
                    }
                }

                GameObject branch = FindOpenChoice();
                if (branch != null)
                {
                    yield return TapObject(branch, "take " + branch.name + " holding the night of " + dayName);
                    yield return new WaitForSeconds(0.25f);
                    continue;
                }

                yield return null;
            }

            if (!EndDayPanel.IsOpen)
            {
                Record("waited for the split on " + dayName, false, "it never happened within "
                    + StepTimeoutSeconds + "s — dusk pending=" + (cycle != null && cycle.IsDuskPending)
                    + " held=" + (cycle != null && cycle.IsDuskHeld)
                    + " input locked=" + InputLock.IsLocked
                    + " modal=" + DescribeTopModal());
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
        IEnumerator ClearTheMorning(StageDef stage, bool full)
        {
            string dayName = "stage " + stage.day;
            bool captured = false;

            for (int panel = 0; panel < MorningPanelCap && ModalRoot.IsOpen; panel++)
            {
                if (!captured && full)
                {
                    yield return Capture(stage.day, "morning");
                    CheckNoMissingStrings();
                    captured = true;
                }

                GameObject action = FindModalExit();
                if (action == null)
                {
                    Record("the morning of " + dayName + " could be cleared", false,
                        "a modal is open with no button this run knows: " + DescribeTopModal());
                    yield break;
                }

                yield return TapObject(action, "clear " + action.name + " on the morning of " + dayName);
                yield return new WaitForSeconds(0.25f);
            }

            Record("the morning of " + dayName + " is clear", !ModalRoot.IsOpen,
                ModalRoot.IsOpen ? "still on " + DescribeTopModal() : "nothing is holding the day");
        }

        /// <summary>The first exit this run knows on whatever panel is up, or null when it knows none.</summary>
        static GameObject FindModalExit()
        {
            for (int i = 0; i < ModalExitButtons.Length; i++)
            {
                GameObject found = Find(ModalExitButtons[i]);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// The first branch on screen, when a conversation is waiting on a decision.
        ///
        /// Separate from <see cref="FindModalExit"/> because a decision is not a panel: nothing has
        /// been pushed onto the modal stack, so every "is anything in the way" test reads clear
        /// while the input lock quietly holds the night open. Tapping the dialogue catcher does not
        /// help either — <c>Advance</c> refuses while branches are up, on purpose, because the only
        /// way out of a decision is to take one.
        /// </summary>
        static GameObject FindOpenChoice()
        {
            // Asked of the dialogue system before the scene, and that ordering is what makes this
            // callable every frame: the answer is almost always no, and a "no" that costs a full
            // scene scan is a "no" the tap loop cannot afford sixty times a second.
            DialogueSystem dialogue = Dialogue();
            if (dialogue == null || !dialogue.IsAwaitingChoice)
            {
                return null;
            }

            return Find("Choice0");
        }

        /// <summary>
        /// Names the panel that is up and the buttons on it. A panel that failed to open and a panel
        /// nobody can close read identically as "stuck" otherwise, and they are not the same bug.
        /// </summary>
        static string DescribeTopModal()
        {
            if (!ModalRoot.IsOpen)
            {
                return "none";
            }

            var names = new List<string>();
            foreach (Button button in Resources.FindObjectsOfTypeAll<Button>())
            {
                if (button == null || !IsInLiveScene(button.gameObject) || !button.gameObject.activeInHierarchy)
                {
                    continue;
                }

                names.Add(button.gameObject.name);
                if (names.Count >= 12)
                {
                    break;
                }
            }

            return (ModalRoot.TopId ?? "unnamed") + " with buttons [" + string.Join(", ", names) + "]";
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

        // ------------------------------------------------------------------ contest lookup

        static ContestConfig ContestConfigFor(StageDef stage)
        {
            if (stage == null || string.IsNullOrEmpty(stage.contest) || GameData.Contests == null)
            {
                return null;
            }

            ContestConfig config;
            return GameData.Contests.TryGetValue(stage.contest, out config) ? config : null;
        }

        /// <summary>
        /// The move buttons of a contest, strongest first, discovered from its own config.
        ///
        /// Ordered by how much resolve a move takes off, so the run finishes a fight rather than
        /// grinding it to the turn limit — and derived rather than listed, because a hardcoded
        /// preference order is a list that keeps passing while quietly never touching the move a
        /// later contest added.
        /// </summary>
        static string[] MoveObjectNames(ContestConfig config)
        {
            if (config == null || config.moves == null)
            {
                return Array.Empty<string>();
            }

            var ordered = new List<ContestMoveDef>();
            foreach (ContestMoveDef move in config.moves)
            {
                if (move != null && !string.IsNullOrEmpty(move.id))
                {
                    ordered.Add(move);
                }
            }

            ordered.Sort((a, b) => a.resolve_delta.CompareTo(b.resolve_delta));

            var names = new string[ordered.Count];
            for (int i = 0; i < ordered.Count; i++)
            {
                names[i] = "Move_" + ordered[i].id;
            }

            return names;
        }

        /// <summary>A move every player has from the first turn, used as "the fight is on screen".</summary>
        static string FirstMoveObjectName(ContestConfig config)
        {
            if (config == null || config.moves == null)
            {
                return null;
            }

            foreach (ContestMoveDef move in config.moves)
            {
                if (move != null && !string.IsNullOrEmpty(move.id)
                    && !move.unlocked_by_page && string.IsNullOrEmpty(move.unlocked_by_flag))
                {
                    return "Move_" + move.id;
                }
            }

            return null;
        }

        static string PageGatedMoveObjectName(ContestConfig config)
        {
            if (config == null || config.moves == null)
            {
                return null;
            }

            foreach (ContestMoveDef move in config.moves)
            {
                if (move != null && move.unlocked_by_page && !string.IsNullOrEmpty(move.id))
                {
                    return "Move_" + move.id;
                }
            }

            return null;
        }

        // ------------------------------------------------------------------ the progression map

        IEnumerator VerifyProgressMap()
        {
            yield return Tap("MapButton", "the progression map button");
            yield return WaitForObject("WorldMapViewCanvas", "the progression map");
            yield return null;

            Record("the progression map opens from the HUD", WorldMapView.IsOpen,
                "WorldMapView.IsOpen=" + WorldMapView.IsOpen);

            MapViewportController viewport = UnityEngine.Object.FindFirstObjectByType<MapViewportController>();
            Record("the progression map is explored section by section",
                viewport != null && viewport.CanPanHorizontally,
                viewport == null ? "no MapViewportController" :
                "horizontal pan=" + viewport.CanPanHorizontally + " scale=" + viewport.CurrentScale.ToString("0.00"));

            // Every stage the season declares has a stop on the sheet. Driving the viewport to each
            // stage's own focus before looking is not politeness: the map is clipped and pannable
            // now, so a marker that exists but sits outside the window is exactly as unfindable to a
            // player as one that was never drawn, and the two report identically without this.
            yield return VerifyEveryStageHasAStop(viewport);

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
            Record("the progression map makes the current position explicit",
                Find("CurrentLocationRing") != null && Find("CurrentLocationLabel") != null,
                "ring=" + (Find("CurrentLocationRing") != null) +
                " label=" + (Find("CurrentLocationLabel") != null));

            if (viewport != null)
            {
                yield return FocusOnStage(viewport, 1);
                yield return Tap("MapNode2Marker", "the second progression annotation");
                Record("selecting a map annotation updates the one detail card",
                    TextOf("SelectedNodeTitle") == Loc.T("world.progress_map.day.2.title"),
                    "selected title=\"" + (TextOf("SelectedNodeTitle") ?? "missing") + "\"");

                yield return FocusOnStage(viewport, 0);
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
            yield return Capture(1, "progress-map");

            yield return Tap("CloseMap", "the progression map close button");
            yield return WaitUntil(() => !WorldMapView.IsOpen, "the progression map to close");
        }

        /// <summary>
        /// Walks the viewport to every declared stage and reports which stops are missing by name.
        ///
        /// Counting them would answer "eight of nine" without saying which one, and the one that
        /// goes missing is always the last — the ending, drawn off the edge or past the map's own
        /// title-key list. Naming it is the difference between a failure that gets fixed and a
        /// failure that gets re-diagnosed.
        ///
        /// PRESENCE FOR EVERY STAGE, A REAL FINGER FOR THE LAST ONE. Presence is a hierarchy
        /// question and the viewport clips rather than deactivates, so finding a marker proves it
        /// was drawn and nothing more. Tapping proves it can be reached, which is the stronger
        /// claim and the one nine stops on a pannable sheet put at risk — but a tap that lands under
        /// a neighbour's label fails a gate over a cosmetic overlap, and a gate that goes red for a
        /// cosmetic reason on its first run is a gate somebody turns off. So the tap is spent where
        /// the new risk actually is: the last stop, the one at the far end of a road that carried
        /// three stops when it was painted. The second stop is tapped below for the detail card, so
        /// both ends of the sheet are proven reachable.
        /// </summary>
        IEnumerator VerifyEveryStageHasAStop(MapViewportController viewport)
        {
            StageDef[] stages = GameData.Stages;
            int expected = stages != null ? stages.Length : 0;
            var missing = new List<string>();

            for (int i = 0; i < expected; i++)
            {
                yield return FocusOnStage(viewport, i);

                string markerName = "MapNode" + (i + 1) + "Marker";
                if (Find(markerName) == null)
                {
                    missing.Add(markerName);
                }
            }

            Record("the progression map has a stop for every stage", expected > 0 && missing.Count == 0,
                expected + " stage(s) declared, " + (expected - missing.Count) + " drawn" +
                (missing.Count > 0 ? " — missing [" + string.Join(", ", missing) + "]" : ""));

            if (expected > 1 && missing.Count == 0)
            {
                yield return FocusOnStage(viewport, expected - 1);
                yield return Tap("MapNode" + expected + "Marker", "the last stop on the road");
                Record("the last stop names something", !string.IsNullOrEmpty(TextOf("SelectedNodeTitle")),
                    "selected title=\"" + (TextOf("SelectedNodeTitle") ?? "missing") + "\"");
            }

            yield return FocusOnStage(viewport, 0);
            yield return Tap("MapNode1Marker", "back to the first stop");
        }

        IEnumerator FocusOnStage(MapViewportController viewport, int stageIndex)
        {
            if (viewport == null)
            {
                yield break;
            }

            viewport.FocusNormalized(MapChartArt.JourneyFocusAnchor(stageIndex));
            yield return null;
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

                if (IsMapSpriteName(image.sprite.name))
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

                if (IsMapSpriteName(image.sprite.name) && !MapChartArt.UsesExactPalette(image.sprite))
                {
                    count++;
                }
            }

            return count;
        }

        static bool IsMapSpriteName(string spriteName)
        {
            spriteName = spriteName ?? string.Empty;
            return spriteName.StartsWith("map_progress_", StringComparison.Ordinal) ||
                   spriteName.StartsWith("map_node_", StringComparison.Ordinal) ||
                   spriteName.StartsWith("map_reward_", StringComparison.Ordinal);
        }

        // ------------------------------------------------------------------ the opening stage

        /// <summary>
        /// Spends the opening day's work and watches the day end on its own.
        ///
        /// This is the only step that exercises <see cref="DayCycle"/>'s Update loop, and it has to
        /// live here: the acceptance harness drives the night through the component's synchronous
        /// path with the component disabled, so the daylight clock never runs there at all. What
        /// this asserts is the shape of the loop end to end in a real composed scene — the light
        /// falls as the work is spent, the split arrives with nobody asking for it, and the morning
        /// that follows is a new day with its capacity back.
        ///
        /// The work is spent through the resource system rather than by walking the player to the
        /// wall and tapping it. Pathing across the village is the interaction layer's business and
        /// is not what changed; what changed is what the clock does once the capacity is gone.
        /// </summary>
        IEnumerator PlayADayToItsEnd(StageDef stage)
        {
            DayCycle cycle = UnityEngine.Object.FindFirstObjectByType<DayCycle>();
            ResourceSystem resources = ResourceSystem.Find();
            GameState state = TryGetState();

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
            yield return Capture(stage.day, "afternoon");

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
                yield return Capture(stage.day, "turning-in");

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

            // The opening stage has no morning — it opens in a cutscene — so its check-in is owed at
            // the evening instead, and it has to arrive BEFORE the split rather than on top of it.
            // Asserting the order matters as much as asserting it appears: dusk waits on an open
            // panel, so a check-in that turned up late would hold the night open indefinitely.
            yield return WaitForObject("Option_0", "the check-in owed at the opening stage's dusk");
            Record("the first day is checked in before its split", !EndDayPanel.IsOpen,
                EndDayPanel.IsOpen ? "the split opened over the check-in" : "the split is waiting for it");
            yield return Capture(stage.day, "check-in");
            CheckNoMissingStrings();

            GameObject checkInAnswer = Find("Option_0");
            if (checkInAnswer != null)
            {
                yield return TapObject(checkInAnswer, "answer the opening stage's check-in");
                yield return Tap("Continue", "close the opening stage's check-in");
            }

            yield return WaitUntil(() => EndDayPanel.IsOpen, "the split to open itself");

            Record("the day ends without being asked to", EndDayPanel.IsOpen,
                "the split opened with no button pressed");
            Record("dusk is darker than the afternoon was", cycle.NightAmount > halfway,
                "night amount " + cycle.NightAmount.ToString("0.000") + " vs " + halfway.ToString("0.000"));
            yield return Capture(stage.day, "dusk");

            // A day with nothing left to spend cannot be turned down, so the split has no way back.
            Record("a spent day offers no way out of the night", Find("Defer") == null,
                Find("Defer") == null ? "no defer button" : "a defer button is on a forced night");

            yield return Tap("Confirm", "the split");
            yield return WaitUntil(() => state.day > startingDay && !cycle.IsResolving, "the morning");

            Record("the morning gives the day its work back", state.workCapacity == state.workCapacityMax,
                "capacity " + state.workCapacity + "/" + state.workCapacityMax);
            Record("the new day has its light back", cycle.NightAmount < 0.02f,
                "night amount " + cycle.NightAmount.ToString("0.000"));
            yield return Capture(stage.day, "first-light");
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
        /// Taps the other language, proves the words actually changed, and comes back.
        ///
        /// Reloading the scene is not what changes the language: Loc, GameData and ScriptureService
        /// are statics that outlive a scene load, and only the Boot scene re-runs the boot sequence.
        /// So this asserts on the loaded tables AND on a label read off the screen afterwards —
        /// the first would catch the content failing to reload, the second catches the screen
        /// failing to rebuild with it.
        ///
        /// AND IT SWITCHES BACK, which it did not used to. A one-way toggle meant the authoring
        /// locale's run played its whole season in the other language while every screenshot and
        /// every result file still carried the authoring locale's name — so nobody was ever sweeping
        /// pt-BR's stages for missing strings, and the two runs were quietly covering the same
        /// language twice. The round trip costs one more opening and buys back half the coverage the
        /// two-locale run exists to provide.
        /// </summary>
        IEnumerator SwitchLanguageAndVerify()
        {
            string target = Locales.Next(Locales.Active);

            // Read with the drawer open, because that is where the label is now. Taken before the
            // switch so the comparison at the end is between two states of the same control.
            yield return OpenHudMenu();
            string before = TextOf("Day");

            yield return TapTheLanguageChip(target);

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

            // And the drawer has to be opened AGAIN, because the switch rebuilt the HUD and the
            // rebuild closes it. Reading here against a closed drawer finds nothing, and "nothing"
            // fails both assertions below as though the language switch had wiped the label —
            // which is a far more alarming sentence than the truth, that this run shut its own
            // drawer two lines ago.
            yield return OpenHudMenu();
            yield return Capture(1, "switched");

            string after = TextOf("Day");
            Record("the screen rebuilt in " + target,
                !string.IsNullOrEmpty(after) && after != before,
                "was \"" + (before ?? "nothing") + "\", now \"" + (after ?? "nothing") + "\"");

            GameState state = TryGetState();
            int day = state != null ? Mathf.Max(1, state.day) : 1;
            Record("the label matches the new table", after == Loc.T("hud.day", day),
                "expected \"" + Loc.T("hud.day", day) + "\", found \"" + (after ?? "nothing") + "\"");

            CheckNoMissingStrings();

            // Back to the language this run is named for, so the season below is played and
            // screenshotted in the locale the result file claims.
            yield return TapTheLanguageChip(_runLocale);
            Record("switching back to " + _runLocale + " restores the run's language",
                Loc.LoadedLocale == _runLocale && GameData.LoadedLocale == _runLocale
                && ScriptureService.LoadedLocale == _runLocale,
                "Loc=" + Loc.LoadedLocale + " GameData=" + GameData.LoadedLocale +
                " Scripture=" + ScriptureService.LoadedLocale);

            yield return AdvanceDialogueUntil(IsHudVisible, "the HUD again after switching back");
            yield return CheckHudThroughTheDrawer();
            CheckNoMissingStrings();
        }

        /// <summary>
        /// Opens the settings panel and taps one language chip.
        ///
        /// In the village the language lives one tap deeper than it does in character creation: the
        /// HUD carries a settings button and the chips live on the panel it opens. Tapping straight
        /// at a chip here reported a missing control, which read as the toggle being broken rather
        /// than as this run not having opened the panel.
        /// </summary>
        IEnumerator TapTheLanguageChip(string target)
        {
            // The drawer first, because that is where the settings button lives now. In the village
            // the language sits one tap deeper than it does in character creation: the HUD carries a
            // settings button and the chips live on the panel it opens. Tapping straight at a chip
            // reports a missing control, which reads as the toggle being broken rather than as this
            // run never having opened the drawer.
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

        /// <summary>
        /// Opens the drawer, reads the HUD through it, and puts the drawer back.
        ///
        /// The day readout moved into the drawer when the HUD was cut down to four controls, and
        /// <see cref="CheckHud"/> is synchronous so it cannot open anything: against a closed
        /// drawer it reads "nothing" and reports the HUD as unlocalised, which is a true sentence
        /// about a label that is simply not on screen.
        ///
        /// Closing it again is the half that matters mid-season. The drawer covers the village
        /// behind a scrim, so a stage that checked the HUD and walked away would send every
        /// remaining tap of that stage into the scrim — and those would fail as covered controls,
        /// nowhere near the line that left it open.
        /// </summary>
        IEnumerator CheckHudThroughTheDrawer()
        {
            yield return OpenHudMenu();
            CheckHud();

            HUD open = UnityEngine.Object.FindFirstObjectByType<HUD>();
            if (open != null && open.IsMenuOpen)
            {
                yield return Tap("MenuButton", "the HUD menu button, putting the drawer back");
            }
        }

        /// <summary>The HUD reads from the same locale table the rest of the run does.</summary>
        void CheckHud()
        {
            GameState state = TryGetState();
            if (state == null)
            {
                Record("the run has a game state", false, "ServiceLocator has no GameState");
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
            return TapObject(target, step, true, StepTimeoutSeconds);
        }

        /// <summary>
        /// True when the last <see cref="TapObject"/> actually delivered its click. Only meaningful
        /// straight after an offered tap; an asserted one records its own verdict.
        /// </summary>
        bool _lastTapLanded;

        /// <summary>
        /// The tap, with the verdict made optional.
        ///
        /// <paramref name="recordFailure"/> false turns an assertion into an OFFER: the tap is
        /// attempted, <see cref="_lastTapLanded"/> says whether it landed, and nothing is written
        /// down either way. That exists for one situation and should not spread beyond it — a loop
        /// whose screen can legitimately change underneath it between choosing a control and
        /// reaching it. The contest is the case: the Page opens on the turn its config names and
        /// the outcome card lands the moment resolve runs out, both of them a consequence of the
        /// move just played, and both arriving while this method is mid-retry on the next one.
        /// Asserting there produces "covered by Modal_the_page/Scrim", which is true, and which
        /// reads exactly like the z-order bug the cover check exists to catch.
        ///
        /// The safety net is that the caller still has to prove the fight ended. A move that is
        /// genuinely unreachable makes the contest fail to resolve, and THAT is the failure worth
        /// reporting — one honest sentence about the fight instead of a stack of true sentences
        /// about a scrim.
        /// </summary>
        IEnumerator TapObject(GameObject target, string step, bool recordFailure, float timeoutSeconds)
        {
            _lastTapLanded = false;

            EventSystem events = EventSystem.current;
            if (events == null)
            {
                if (recordFailure) Record(step, false, "there is no EventSystem in the scene");
                yield break;
            }

            var rect = target.transform as RectTransform;
            if (rect == null)
            {
                if (recordFailure) Record(step, false, PathOf(target) + " is not a RectTransform");
                yield break;
            }

            // A control below the fold of a scroll view is not the same thing as a control under
            // an opaque panel, and the raycast alone cannot tell them apart: both are a ray that
            // reaches nothing. Do what a player does before judging it unreachable.
            yield return ScrollIntoView(target);

            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
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

                    // Retry the scroll as well as the ray. A menu built this frame has no layout
                    // yet, so the first attempt above measured corners that were all zero and
                    // moved nothing; once the layout lands, this is the frame that finds it.
                    yield return ScrollIntoView(target);
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
                if (recordFailure) Record(step, false, reason);
                yield break;
            }

            var pointer = new PointerEventData(events) { position = screenPoint };
            ExecuteEvents.Execute(reached, pointer, ExecuteEvents.pointerClickHandler);
            _lastTapLanded = true;
            if (recordFailure) Record(step, true, "clicked " + PathOf(reached));
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

        /// <summary>
        /// Taps the conversation forward until something is true, one tap per frame.
        ///
        /// THE PACE IS THE SINGLE BIGGEST COST IN THIS FILE AND IT USED TO BE A SLEEP. Every advance
        /// was followed by an unconditional quarter second, and the opening alone spends sixty-odd
        /// advances before character creation with forty more after the language switch — roughly
        /// half a minute of a fifty-six second run doing nothing at all. Nothing needed the sleep:
        /// <c>DialogueSystem.Advance</c> runs synchronously off the tap, completing a line that is
        /// still typing and moving on when it is not, so the next tap is legal on the next frame. A
        /// person mashing through a conversation they have read before does exactly this.
        ///
        /// What replaces the sleep is a stall clock rather than a total budget, because the run does
        /// still have to sit through camera moves and character walks where a tap lands on nothing.
        /// Progress is read off the screen — the line being spoken, the node playing, the panel on
        /// top — so the clock runs only while tapping is achieving nothing, and a slow machine is
        /// never mistaken for a stuck one.
        /// </summary>
        /// <param name="clearPanels">
        /// Also dismisses any panel that turns up on the way. Off by default, because most waits
        /// here are for something that lives *inside* a panel and closing it would step over the
        /// thing being waited for. It is on for a stage's scripted beat, where the opposite is true:
        /// the director only starts once the world is quiet, and the day's check-in arrives after
        /// the morning has already been cleared — so a run that only taps dialogue waits forever for
        /// a beat that is being held open by the panel it is ignoring.
        /// </param>
        IEnumerator AdvanceDialogueUntil(Func<bool> condition, string description, bool clearPanels = false,
            float stallSeconds = DialogueStallSeconds)
        {
            int advances = 0;
            string signature = null;
            float stallDeadline = Time.realtimeSinceStartup + stallSeconds;

            while (advances < MaxDialogueAdvances && Time.realtimeSinceStartup < stallDeadline)
            {
                if (condition())
                {
                    Record("reached " + description, true, "after " + advances + " advance(s)");
                    yield break;
                }

                string now = ScreenSignature();
                if (now != signature)
                {
                    signature = now;
                    stallDeadline = Time.realtimeSinceStartup + stallSeconds;
                }

                // A decision is not a panel and a tap on the catcher does nothing while one is up,
                // so a branch left unanswered stalls this loop AND holds the night open behind it.
                GameObject branch = FindOpenChoice();
                if (branch != null)
                {
                    yield return TapObject(branch, "take " + branch.name + " on the way to " + description);
                    advances++;
                    stallDeadline = Time.realtimeSinceStartup + stallSeconds;
                    continue;
                }

                if (clearPanels && ModalRoot.IsOpen)
                {
                    GameObject panelAction = FindModalExit();
                    if (panelAction != null)
                    {
                        yield return TapObject(panelAction,
                            "clear " + panelAction.name + " on the way to " + description);
                        advances++;
                        stallDeadline = Time.realtimeSinceStartup + stallSeconds;
                        continue;
                    }
                }

                DialogueTapCatcher catcher = Catcher();
                if (catcher != null)
                {
                    ExecuteEvents.Execute(
                        catcher.gameObject,
                        new PointerEventData(EventSystem.current),
                        ExecuteEvents.pointerClickHandler);
                    advances++;
                }

                yield return null;
            }

            if (condition())
            {
                Record("reached " + description, true, "after " + advances + " advance(s)");
                yield break;
            }

            Record("reached " + description, false,
                advances >= MaxDialogueAdvances
                    ? "still not present after " + advances + " dialogue advances"
                    : "nothing on screen changed for " + stallSeconds + "s — " + ScreenSignature());
        }

        /// <summary>
        /// A cheap fingerprint of what the player would be looking at, used to tell a slow beat from
        /// a stuck one. It deliberately does NOT include the number of taps this run has dispatched:
        /// counting our own taps would keep resetting the stall clock forever, which is the whole
        /// failure the clock exists to catch.
        ///
        /// The revealed length of the spoken line is in here on purpose. Without it a single long
        /// node — a cutscene that plays one conversation over half a minute of camera work — looks
        /// identical from frame to frame and would trip the clock while it was working perfectly.
        /// </summary>
        static string ScreenSignature()
        {
            DialogueSystem dialogue = Dialogue();
            Text body = DialogueBody();

            return (dialogue != null ? dialogue.CurrentNodeId ?? "-" : "-")
                   + "|" + (dialogue != null && dialogue.IsAwaitingChoice)
                   + "|" + ModalRoot.OpenCount + ":" + (ModalRoot.TopId ?? "-")
                   + "|" + (body != null && body.text != null ? body.text.Length : 0);
        }

        static bool DialogueIsPlaying()
        {
            DialogueSystem dialogue = Dialogue();
            return dialogue != null && dialogue.IsPlaying;
        }

        static string CurrentDialogueNode()
        {
            DialogueSystem dialogue = Dialogue();
            return dialogue != null ? dialogue.CurrentNodeId : null;
        }

        // ------------------------------------------------------------------ cached hot lookups
        //
        // EVERY LOOKUP IN THIS FILE IS A FULL SCENE SCAN. Find and FindActive both walk
        // Resources.FindObjectsOfTypeAll, which is fine at a handful of calls a second and is not
        // fine at a dozen a frame — and a dozen a frame is exactly what the tap loop became once
        // the quarter-second sleep between advances came out of it. Caching these three is what
        // keeps that change a saving instead of a trade: without it the run would spend in scans
        // what it stopped spending in sleep, and be slower for the trouble.
        //
        // The cached references are Unity objects, so a scene reload — which this run causes twice,
        // switching language and switching back — turns them null through Unity's own equality and
        // the next call re-finds them. That is why the guard is a null check rather than a
        // generation counter this file would have to remember to bump.

        static DialogueSystem _dialogueCache;
        static Text _dialogueBodyCache;
        static DialogueTapCatcher _catcherCache;

        static DialogueSystem Dialogue()
        {
            if (_dialogueCache == null)
            {
                _dialogueCache = FindActive<DialogueSystem>();
            }

            return _dialogueCache;
        }

        /// <summary>
        /// The label the spoken line is typed into. Cached even while its panel is hidden, because
        /// <c>DialogueUI.Hide</c> deactivates the root and blanks the text rather than destroying
        /// it — so the reference stays good and reading a blank string is the right answer anyway.
        ///
        /// SCOPED TO THE DIALOGUE CANVAS, and it has to be. "Body" is the house name for the main
        /// paragraph of a card, so the reveal panel, the contest log and every verse row of the
        /// chapter reader carry one too — and the reader carries dozens. An unscoped search would
        /// cache whichever the scene happened to enumerate first, and the stall detector would then
        /// be watching a label that never changes while the conversation it is meant to be watching
        /// went past.
        /// </summary>
        static Text DialogueBody()
        {
            if (_dialogueBodyCache == null)
            {
                foreach (Text text in Resources.FindObjectsOfTypeAll<Text>())
                {
                    if (text == null || text.name != "Body" || !IsInLiveScene(text.gameObject)) continue;
                    if (!HasAncestorNamed(text.transform, "DialogueCanvas")) continue;

                    _dialogueBodyCache = text;
                    break;
                }
            }

            return _dialogueBodyCache;
        }

        static bool HasAncestorNamed(Transform from, string name)
        {
            for (Transform t = from; t != null; t = t.parent)
            {
                if (t.name == name) return true;
            }
            return false;
        }

        /// <summary>The tap catcher, or null while no line is on screen — its root is deactivated then.</summary>
        static DialogueTapCatcher Catcher()
        {
            if (_catcherCache == null)
            {
                _catcherCache = FindActive<DialogueTapCatcher>();
            }

            return _catcherCache != null && _catcherCache.gameObject.activeInHierarchy ? _catcherCache : null;
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
                + ModalRoot.OpenCount + ", top " + DescribeTopModal());
        }

        /// <summary>
        /// Writes a screenshot named so the directory reads in the order the run played.
        ///
        /// Named for the STAGE and not for a hand-kept number. The old scheme numbered shots across
        /// the whole run, which meant inserting a beat renumbered everything after it and which had
        /// already started lying: "10-trial" sorted before "10a-morning-day-three", and the morning
        /// happens first. A two-digit stage day carries the season's order for free, and the ordinal
        /// within the stage resets at every stage, so inserting a beat renumbers that stage's shots
        /// and nothing else.
        /// </summary>
        IEnumerator Capture(int stageDay, string beat)
        {
            if (stageDay != _shotStageDay)
            {
                _shotStageDay = stageDay;
                _shotOrdinal = 0;
            }

            _shotOrdinal++;

            string fileName = stageDay.ToString("00") + "-" + _shotOrdinal.ToString("00")
                              + "-" + beat + "-" + _runLocale + ".png";
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
            Record("screenshot " + beat + " (stage " + stageDay + ")", written,
                written ? path : "the file was never written");
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

        // ------------------------------------------------------------------ the stall detector

        /// <summary>
        /// A watchdog on a REAL THREAD, because the other one cannot fire when it matters.
        ///
        /// <see cref="Watchdog"/> is a coroutine, so it advances on <c>yield return null</c> — one
        /// tick per frame. Every silent hang this harness has had (30/08, 01/09, twice on 02/09,
        /// again on 03/09) looked identical from outside: no exception, no log line, nothing at all
        /// until the shell killed the player 990 seconds later. A coroutine watchdog can say nothing
        /// about that, because the thing it would report is the thing that stopped it.
        ///
        /// <b>This does not fix the hang. It measures it</b>, and it measures the one distinction
        /// four investigations could not make from the outside: <c>_framesDrawn</c> is incremented
        /// from Update and read from here. Frozen frames and a frozen run are two different bugs
        /// with the same silence —
        ///   * frames stopped, steps stopped  → the PLAYER stopped being drawn. Look outside Unity:
        ///     window occlusion, the compositor, the machine.
        ///   * frames moving, steps stopped   → the RUN is stuck in a wait that never comes true.
        ///     Look at the last step, which is named in the report.
        ///
        /// Writes with <see cref="File"/> rather than <see cref="Debug"/>: the log pump is the
        /// player's, and a report about a stalled player may not be handed to it. Then exits hard,
        /// so the shell gets a process that ended instead of one it has to kill.
        /// </summary>
        void StartStallDetector()
        {
            string path = Path.Combine(_outputDirectory, "stall-" + _runLocale + ".txt");
            var thread = new System.Threading.Thread(() => WatchForAStall(path));
            thread.IsBackground = true;
            thread.Name = "e2e-stall-detector";
            thread.Start();
        }

        void WatchForAStall(string reportPath)
        {
            const int pollMs = 1000;
            int lastFrames = -1;
            int lastSteps = -1;
            int quietSeconds = 0;

            while (!_finished)
            {
                System.Threading.Thread.Sleep(pollMs);

                int frames = _framesDrawn;
                int steps = _stepsRecorded;

                if (frames != lastFrames || steps != lastSteps)
                {
                    lastFrames = frames;
                    lastSteps = steps;
                    quietSeconds = 0;
                    continue;
                }

                quietSeconds++;
                if (quietSeconds < StallSeconds)
                {
                    continue;
                }

                bool framesStopped = frames == lastFrames;
                string verdict = framesStopped
                    ? "THE PLAYER STOPPED DRAWING. Frames did not advance either, so this is not a "
                      + "stuck wait — the process stopped being run at all. Look outside Unity: window "
                      + "occlusion, the compositor, the machine going to sleep."
                    : "THE RUN IS STUCK. Frames kept advancing while no step was recorded, so the "
                      + "player is fine and a wait in the script never came true. The last step names "
                      + "where.";

                try
                {
                    File.WriteAllText(reportPath, string.Join("\n", new[]
                    {
                        "e2e stalled",
                        "locale        " + _runLocale,
                        "last step     " + _lastStepLabel,
                        "steps         " + steps,
                        "frames drawn  " + frames,
                        "quiet for     " + quietSeconds + "s",
                        "",
                        verdict,
                        ""
                    }));
                }
                catch (Exception)
                {
                    // A detector that throws while reporting a stall would hide the stall it found.
                }

                // 3 rather than 1: a stall is not a failed assertion, and a report that reads like
                // one sends the next person looking for a broken step.
                Environment.Exit(3);
            }
        }

        // ------------------------------------------------------------------ reporting

        void OnLog(string message, string stackTrace, LogType type)
        {
            // An error logged during the run is a failure even when the screen looks right:
            // a missing locale key reports itself this way before anyone sees the marker.
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                _failures.Add("logged " + type + ": " + message);
            }
        }

        /// <summary>
        /// A step this run is not in a position to judge, named in the report rather than left out
        /// of it.
        ///
        /// There is exactly one case, and it used to print four FAILs: a seeded run
        /// (<c>--from-stage</c>) never drives character creation, so nothing in it can know what
        /// creation chose, and the four assertions that compare the backpack against that choice
        /// were failing on every seeded run since the flag existed. They were not regressions and
        /// they were not findings — they were the mode. A gate mode that always prints failures
        /// teaches the person reading it to skip failures, which is the opposite of what a gate is
        /// for.
        ///
        /// A skip is not a pass: it does not clear the step, it records that this run could not
        /// take it. The cold run every gate actually uses still takes all four.
        /// </summary>
        void Skip(string step, string why)
        {
            _steps.Add("SKIP  " + step + " — " + why);
            Debug.Log("[E2E] SKIP " + step + " — " + why);
        }

        /// <summary>
        /// Whether this run drove character creation, and therefore knows what it chose. False on a
        /// seeded run, which starts standing in the village with creation already behind it.
        /// </summary>
        bool DroveCreation { get { return !string.IsNullOrEmpty(_creationCharacterId); } }

        void Record(string step, bool passed, string detail)
        {
            _stepsRecorded++;
            _lastStepLabel = step;
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

        /// <summary>
        /// Tries to go on with nothing chosen, which is the first thing a hurried player does.
        ///
        /// Rule 7 decides what has to happen: the button stays live and answers with a sentence,
        /// rather than being greyed out and answering with nothing. So both halves are asserted —
        /// the sentence arrived, and the screen did not advance behind it.
        ///
        /// The sentence arrives on a fade, so the frame after the tap is too early to read it.
        /// </summary>
        IEnumerator AskBeforeChoosing()
        {
            yield return Tap("ToCustomise", "going on before choosing");
            yield return WaitUntil(() => !string.IsNullOrEmpty(TextOf("ChoiceMessage")),
                "the ask to choose one first");

            string ask = TextOf("ChoiceMessage");
            Record("going on before choosing asks rather than refuses",
                !string.IsNullOrEmpty(ask) && ask.IndexOf(MissingMarkerOpen) < 0,
                "ChoiceMessage = \"" + (ask ?? "nothing") + "\"");
            Record("going on before choosing goes nowhere",
                Find("Step1") != null && Find("Step2") == null,
                "step one up=" + (Find("Step1") != null) + " step two up=" + (Find("Step2") != null));
        }

        /// <summary>
        /// Picks a skin tone, which is the one thing on this screen that belongs to the person
        /// playing rather than to the character they chose.
        ///
        /// Every tone has to be offered — this screen is the only surface in the whole game that
        /// can set one — and the tap has to land on <c>skin</c> without disturbing <c>body</c>:
        /// the two share one sprite index, and a screen that wrote the packed value would clamp
        /// the build to 1 and lose both halves at once.
        /// </summary>
        IEnumerator ChooseATone(GameState state)
        {
            var missingTones = new List<string>();
            for (int offered = 0; offered < AppearanceState.SkinTones; offered++)
            {
                if (Find("ToneOption_" + offered) == null)
                {
                    missingTones.Add("ToneOption_" + offered);
                }
            }

            Record("every skin tone is offered", missingTones.Count == 0,
                missingTones.Count == 0
                    ? AppearanceState.SkinTones + " swatches on screen"
                    : "no swatch named " + Listed(missingTones));

            if (missingTones.Count > 0)
            {
                yield break;
            }

            int buildBefore = state.appearance.body;
            int toneBefore = state.appearance.skin;

            // Two along rather than one, so the assertion cannot pass on an off-by-one that lands
            // on the neighbouring swatch.
            int tone = (toneBefore + 2) % AppearanceState.SkinTones;

            yield return TapInList("ToneOption_" + tone, "the skin tone " + tone);
            yield return null;

            Record("the tone is the player's own to set", state.appearance.skin == tone,
                "skin " + toneBefore + " -> " + state.appearance.skin + ", asked for " + tone);
            Record("setting the tone leaves the build alone", state.appearance.body == buildBefore,
                "body " + buildBefore + " -> " + state.appearance.body);
            Record("the chosen tone is the one wearing the ring",
                IsChildShowing(Find("ToneOption_" + tone), "Selected")
                && IsChildShowing(Find("ToneOption_" + tone), "SelectedMark"),
                "ring=" + IsChildShowing(Find("ToneOption_" + tone), "Selected")
                + " mark=" + IsChildShowing(Find("ToneOption_" + tone), "SelectedMark"));
        }

        /// <summary>
        /// Taps one card and then the other, and proves the choice reaches the figure.
        ///
        /// The assertion that matters is on <see cref="AppearanceState"/> and not on the button:
        /// a card that lights its ring while the character underneath stays put looks entirely
        /// correct until the player walks out of the house as somebody else. It is recorded in
        /// three parts on purpose. The build follows the character, the worn set follows the
        /// character, and the tone follows neither — that last one is decision 1b, and a single
        /// combined signature would report all three as one indistinguishable failure.
        /// </summary>
        IEnumerator ChooseBetweenTheTwo(GameState state, string[] ids)
        {
            yield return TapInList("CharacterCard_" + ids[0], "choosing " + ids[0]);
            yield return null;

            LookSnapshot first = LookSnapshot.Of(state.appearance);
            string firstId = state.characterId;
            string firstWorn = Listed(state.equippedItems);

            yield return TapInList("CharacterCard_" + ids[1], "choosing " + ids[1]);
            yield return null;

            LookSnapshot second = LookSnapshot.Of(state.appearance);
            string secondWorn = Listed(state.equippedItems);

            Record("the character carries its own build", first.Body != second.Body,
                ids[0] + " -> " + first + " | " + ids[1] + " -> " + second);
            Record("the character carries its own clothes", firstWorn != secondWorn,
                ids[0] + " wears " + firstWorn + "; " + ids[1] + " wears " + secondWorn);
            Record("choosing a character writes which one it is",
                firstId == ids[0] && state.characterId == ids[1],
                "characterId " + (firstId ?? "empty") + " -> " + (state.characterId ?? "empty"));

            // Decision 1b, asserted where it can actually break: the character supplies a build and
            // never a tone, so a floor laid over the figure must leave the tone exactly as it found
            // it. A catalogue block that named skin would show up here as the tone moving on its own.
            Record("choosing a character never touches the tone", first.Skin == second.Skin,
                "skin " + first.Skin + " -> " + second.Skin);

            GameObject chosen = Find("CharacterCard_" + ids[1]);
            GameObject other = Find("CharacterCard_" + ids[0]);
            Record("the card that was chosen is the one wearing the ring",
                IsChildShowing(chosen, "Selected") && !IsChildShowing(other, "Selected"),
                ids[1] + " ring=" + IsChildShowing(chosen, "Selected")
                + ", " + ids[0] + " ring=" + IsChildShowing(other, "Selected"));

            // Rule 2: never colour alone. The ring is clay and the mark is a check, and a player
            // who cannot tell clay from the card behind it still has the check.
            Record("the chosen card carries a mark and not only a colour",
                IsChildShowing(chosen, "SelectedMark"),
                "SelectedMark under CharacterCard_" + ids[1] + " showing="
                + IsChildShowing(chosen, "SelectedMark"));
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
        /// Starts the run, and reads back what creation wrote.
        ///
        /// The reload is the point of the whole feature. It is done by asking
        /// <see cref="SaveSystem.Load"/> for the file — the exact call the boot sequence makes —
        /// rather than by restarting the process, which one run cannot do; what it proves is that
        /// the choice is on disk in a shape the next boot will read, and not merely in an object
        /// that happens to still be in memory.
        ///
        /// The badge assertion is here rather than at the backpack because this is the moment it
        /// could have been spent. On a fresh save every free item is unlocked and never seen, so a
        /// creation screen that marked its rows seen would leave the first backpack open with
        /// nothing at all to announce — spending the one moment a badge exists for.
        /// </summary>
        IEnumerator ConfirmTheCharacter(GameState state)
        {
            _creationCharacterId = state.characterId;
            _creationEquipped.Clear();
            _creationEquipped.AddRange(state.equippedItems ?? new List<string>());
            _creationSkin = state.appearance.skin;
            _creationPlayerName = state.playerName;

            int build = state.appearance.body;
            int badgeBefore = Wardrobe.NewCount(state);

            yield return Tap("Start", "starting the run");
            yield return WaitUntil(() => Find("CharacterCreationCanvas") == null,
                "creation to hand the game back");
            yield return SkipCreation("Start left the screen up");

            Record("creation stored which character it made",
                !string.IsNullOrEmpty(state.characterId)
                && state.characterId == _creationCharacterId
                && CharacterPresets.Get(state.characterId) != null,
                "characterId = " + (state.characterId ?? "empty"));
            Record("creation dressed the character it made",
                state.equippedItems != null && state.equippedItems.Count > 0,
                "wearing " + Listed(_creationEquipped));

            GameState reloaded = SaveSystem.Load();
            bool survived = reloaded != null
                && reloaded.characterId == _creationCharacterId
                && Listed(reloaded.equippedItems) == Listed(_creationEquipped)
                && reloaded.appearance != null
                && reloaded.appearance.skin == _creationSkin
                && reloaded.appearance.body == build
                && reloaded.playerName == _creationPlayerName;

            Record("the character survives the next boot", survived,
                reloaded == null
                    ? "there is no save on disk to boot from"
                    : "on disk: character=" + (reloaded.characterId ?? "empty")
                      + " wearing " + Listed(reloaded.equippedItems)
                      + " tone=" + (reloaded.appearance != null ? reloaded.appearance.skin : -1)
                      + " build=" + (reloaded.appearance != null ? reloaded.appearance.body : -1)
                      + " name=\"" + (reloaded.playerName ?? "nothing") + "\""
                      + " | in the run: character=" + (_creationCharacterId ?? "empty")
                      + " wearing " + Listed(_creationEquipped)
                      + " tone=" + _creationSkin + " build=" + build
                      + " name=\"" + (_creationPlayerName ?? "nothing") + "\"");

            Record("creation leaves the backpack something to announce", badgeBefore > 0,
                "Wardrobe.NewCount=" + badgeBefore
                + " on leaving creation — creation never marks a piece seen");
        }

        /// <summary>
        /// The one screen in the opening that asks the player for something, played the way a
        /// person plays it: try to go on before choosing, look at both characters, pick a tone,
        /// type a name, walk the three slots, be turned down by a piece that has not opened yet,
        /// put on one that has, go back and find nothing taken away, and start.
        ///
        /// It replaces one tap on QuickStart, and it is worth the minutes because this screen is
        /// where the wardrobe the rest of the game reads gets written. Until now creation wrote six
        /// art indices and the backpack read catalogue ids, and nothing anywhere proved the two
        /// halves agreed — the acceptance harness constructs the wardrobe directly and never
        /// composes the screen that fills it, so a creation screen that dressed nobody would have
        /// passed every rule about the wardrobe it left empty.
        ///
        /// Every assertion here reads state or hierarchy. Nothing reads a pixel: what the figure
        /// looks like is the art library's business, and what this asserts is that the ints
        /// underneath it changed and that the row the player tapped is the row wearing the ring.
        /// </summary>
        IEnumerator CreateACharacter()
        {
            GameState state = TryGetState();
            if (state == null || state.appearance == null)
            {
                Record("creation has a run to write into", false,
                    state == null ? "there is no game state" : "the run carries no appearance");
                yield return SkipCreation("there is nothing to write a character into");
                yield break;
            }

            yield return WaitForObject("Step1", "the character choice");

            string[] ids = CharacterPresets.Ids();
            Record("creation offers a character to be", ids.Length >= 2,
                ids.Length + " preset(s) authored: " + Listed(ids));

            var missingCards = new List<string>();
            for (int i = 0; i < ids.Length; i++)
            {
                if (Find("CharacterCard_" + ids[i]) == null)
                {
                    missingCards.Add(ids[i]);
                }
            }

            Record("every authored character has a card on screen", ids.Length >= 2 && missingCards.Count == 0,
                missingCards.Count == 0
                    ? "cards for " + Listed(ids)
                    : "no card for " + Listed(missingCards));

            RecordEscapeHatch("the character choice");
            Record("the character choice is never a dead end", IsInteractable("ToCustomise"),
                "ToCustomise interactable=" + IsInteractable("ToCustomise")
                + " — a control at 0.40 opacity is the void rule 7 forbids");

            CheckNoMissingStrings();
            yield return Capture("02-creation");
            CheckLanguageToggle();

            if (ids.Length < 2 || missingCards.Count > 0)
            {
                // Everything below chooses between two cards. Without them each step would spend a
                // tap timeout proving the same one thing, and thirty seconds apiece is enough to
                // push the whole run past its watchdog.
                yield return SkipCreation("there are not two cards on screen to choose between");
                yield break;
            }

            yield return AskBeforeChoosing();
            yield return ChooseBetweenTheTwo(state, ids);
            yield return ChooseATone(state);
            yield return TypeAName(state);

            yield return Tap("ToCustomise", "going on to the look");
            yield return WaitForObject("Step2", "the look");
            Record("the character choice steps aside for the look", Find("Step1") == null,
                Find("Step1") == null ? "step one is away" : "both steps are on screen at once");

            yield return DriveTheLook(state);
            yield return ConfirmTheCharacter(state);
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

            // Before anything is put on or taken off, because this is the one frame where the
            // sheet is showing what character creation left behind and nothing else. The two
            // screens spoke different vocabularies until this run; this is the assertion that
            // says they now agree.
            if (DroveCreation)
            {
                RecordCreationChoiceIsWorn(state);
                RecordWornRowIsRinged(state, CharacterSlot.Hair);
            }
            else
            {
                Skip("the backpack agrees with what creation chose",
                    "this run was seeded past creation, so nothing here knows what it chose");
                Skip("the Hair tab rings the piece that is on",
                    "seeded runs wear the catalogue's defaults, which no row is ringed for");
            }

            yield return WearSomething(state);
            yield return TapSomethingNotFoundYet(state);
            yield return Capture("04a-backpack-hair");

            yield return SelectBackpackTab("Outfit", "04b-backpack-outfit");
            if (DroveCreation)
            {
                RecordWornRowIsRinged(state, CharacterSlot.Outfit);
            }
            else
            {
                Skip("the Outfit tab rings the piece that is on", "seeded past creation");
            }

            yield return SelectBackpackTab("Accessory", "04c-backpack-accessory");
            if (DroveCreation)
            {
                RecordWornRowIsRinged(state, CharacterSlot.Accessory);
            }
            else
            {
                Skip("the Accessory tab rings the piece that is on", "seeded past creation");
            }
            yield return SelectBackpackTab("Materials", "04d-backpack-materials");

            RecordMaterialsTab();
            Record("a section with no slots takes the slot bar with it", Find("SegmentHair") == null,
                "SegmentHair active=" + (Find("SegmentHair") != null));

            // Perfil is PR #7's headline screen, and this branch's rewrite of the runner dropped
            // the only steps that had ever selected it — so the meter, the missions and the studies
            // went back to being carried by the review that wrote them. Restored from main on the
            // merge, unchanged, because losing coverage is not a thing a season is allowed to do
            // quietly. Unlike Aparência it is a panel of its own, so SelectBackpackTab's exemption
            // does not apply and TabProfile is asserted like any other panel.
            yield return SelectBackpackTab("Profile", "04e-backpack-profile");
            RecordProfileTab();

            // Back through Aparência, which is the only way to a wardrobe now. This is the one
            // selection that has to move the layout in both directions — neither Perfil nor Itens
            // has a character above its list or a bar under it — so a sheet that can leave the
            // wardrobe and not come back fails here rather than in a screenshot somebody looks at
            // next week.
            yield return SelectBackpackTab("Appearance", null);
            yield return SelectBackpackTab("Hair", null);
            Record("leaving a section with no character brings the character back",
                Find("CharacterStage") != null,
                Find("CharacterStage") != null ? "CharacterStage is up again" : "the stage never came back");
        }

        /// <summary>
        /// The second step: the preview, the three slots, and the two taps that prove a locked row
        /// is an invitation rather than a wall.
        /// </summary>
        IEnumerator DriveTheLook(GameState state)
        {
            PresetDef chosen = CharacterPresets.Get(state.characterId);
            string chosenName = TextOf("ChosenName");
            Record("the look says who you chose",
                chosen != null && !string.IsNullOrEmpty(chosenName)
                && chosenName.IndexOf(MissingMarkerOpen) < 0 && chosenName == chosen.suggested_name,
                "ChosenName = \"" + (chosenName ?? "nothing") + "\", preset "
                + (state.characterId ?? "empty") + " suggests \""
                + (chosen != null ? chosen.suggested_name : "nothing") + "\"");

            var missingCells = new List<string>();
            for (int i = 0; i < CreationFacings.Length; i++)
            {
                if (Find("Cell_" + CreationFacings[i]) == null)
                {
                    missingCells.Add("Cell_" + CreationFacings[i]);
                }
            }

            // Four facings and not one, because the accessories hang off named body parts now — the
            // coil of rope on the right shoulder, the map tube across the back — and the back and
            // side facings are the only place a player ever sees them.
            Record("the look is shown from all four sides",
                Find("Preview") != null && missingCells.Count == 0,
                missingCells.Count == 0
                    ? "Preview draws " + Listed(CreationFacings)
                    : "missing " + Listed(missingCells));

            yield return WaitForObject("SlotSegments", "the slot bar");

            // The bar is read off Wardrobe.BadgedSlots rather than off a list spelled out here,
            // because that is the list the screen builds it from. A run holding its own copy would
            // agree with the screen right up until somebody added a slot to one of them.
            IReadOnlyList<CharacterSlot> slots = Wardrobe.BadgedSlots;
            var missingCellsOnBar = new List<string>();
            for (int i = 0; i < slots.Count; i++)
            {
                if (Find("Segment" + slots[i]) == null)
                {
                    missingCellsOnBar.Add("Segment" + slots[i]);
                }
            }

            Record("the slot bar has one cell for every wardrobe slot",
                slots.Count > 0 && missingCellsOnBar.Count == 0,
                missingCellsOnBar.Count == 0
                    ? slots.Count + " cell(s) on the bar"
                    : "no cell named " + Listed(missingCellsOnBar));

            if (slots.Count > 0)
            {
                RecordOnlyCreationTab(slots[0], "the look opens on the first slot");
            }

            CheckNoMissingStrings();
            yield return Capture("02b-creation-look");

            for (int i = 0; i < slots.Count; i++)
            {
                yield return SelectCreationTab(slots[i]);
                RecordSlotOptions(state, slots[i]);
            }

            // Back to the slot the two taps below live on, which also proves the bar goes both ways.
            yield return SelectCreationTab(CharacterSlot.Hair);

            yield return TapACreationPieceNotFoundYet(state);
            yield return WearAPieceInCreation(state);
            yield return GoBackAndKeepEverything(state);
        }

        /// <summary>
        /// Goes back to the character choice, taps the same card again, and finds nothing taken
        /// away.
        ///
        /// This is rule 7 in its most literal form. Choosing a character seeds the worn set from
        /// the preset, so re-picking the one already chosen would discard every swap the player
        /// made — a player who went back to look at the other card and changed their mind would be
        /// punished for looking. The tone and the typed name ride along in the same assertion:
        /// both are the player's, and neither belongs to the character.
        /// </summary>
        IEnumerator GoBackAndKeepEverything(GameState state)
        {
            string chosenId = state.characterId;
            string wornHair = Wardrobe.EquippedInSlot(state, CharacterSlot.Hair);
            int tone = state.appearance.skin;
            string typed = state.playerName;

            yield return Tap("BackToCharacter", "going back to the character choice");
            yield return WaitForObject("Step1", "the character choice again");
            Record("going back leaves the look behind rather than losing it",
                Find("Step2") == null && state.characterId == chosenId,
                "step two up=" + (Find("Step2") != null) + ", characterId=" + (state.characterId ?? "empty"));

            yield return TapInList("CharacterCard_" + chosenId, "choosing " + chosenId + " a second time");
            yield return null;

            Record("choosing the same character again takes nothing back",
                Wardrobe.EquippedInSlot(state, CharacterSlot.Hair) == wornHair
                && state.appearance.skin == tone && state.playerName == typed,
                "hair " + (wornHair ?? "nothing") + " -> "
                + (Wardrobe.EquippedInSlot(state, CharacterSlot.Hair) ?? "nothing")
                + ", tone " + tone + " -> " + state.appearance.skin
                + ", name \"" + (typed ?? "nothing") + "\" -> \"" + (state.playerName ?? "nothing") + "\"");

            yield return Tap("ToCustomise", "back to the look");
            yield return WaitForObject("Step2", "the look again");
            RecordEscapeHatch("the look");
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

            // Aparência opens whichever wardrobe list was last used rather than a panel of its own,
            // so the panel assertion does not apply to it. Everything else about the tap does.
            if (tab != "Appearance")
            {
                RecordOnlyBackpackTab(tab, "selecting Segment" + tab + " shows Tab" + tab + " and nothing else");
            }
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
        /// Taps one slot and proves the whole bar moved with it: its list is the only one on
        /// screen, and the cell that was tapped is the one wearing the fill.
        ///
        /// The suffix comes from the slot rather than from a list spelled out here, and the order
        /// comes from <see cref="Wardrobe.BadgedSlots"/> — the same list the screen builds its bar
        /// from. A run that kept its own copy would agree right up until somebody added a slot.
        /// </summary>
        IEnumerator SelectCreationTab(CharacterSlot slot)
        {
            string tab = slot.ToString();
            GameObject segment = Find("Segment" + tab);
            if (segment == null)
            {
                Record("select Segment" + tab, false, "no active object named Segment" + tab);
                yield break;
            }

            yield return TapObject(segment, "select Segment" + tab);
            yield return null;

            RecordOnlyCreationTab(slot, "selecting Segment" + tab + " shows Tab" + tab + " and nothing else");
            Record("selecting Segment" + tab + " repaints the slot bar", IsShowing("SegmentFill" + tab),
                "SegmentFill" + tab + " showing=" + IsShowing("SegmentFill" + tab));
        }

        /// <summary>
        /// Leaves creation by the escape hatch when the drive above could not run, or when Start
        /// did not take.
        ///
        /// Not politeness, and not a softening of any verdict — the failure that sent the run here
        /// is already recorded. It is that nothing downstream of this screen can happen while it is
        /// up: the opening waits on creation to finish, so a run that gave up in here without
        /// leaving would spend its 120 dialogue advances reaching for a HUD that cannot arrive and
        /// then fail every remaining step — the backpack, all three days, the trial — for one
        /// reason a whole run away. One failure is worth reading; thirty with the same cause bury it.
        ///
        /// QuickStart is the way out because it is the one control on this screen that has kept its
        /// name through every version of it, which makes this fallback diagnostic against a screen
        /// that has been rebuilt as well as against one that has broken.
        /// </summary>
        IEnumerator SkipCreation(string reason)
        {
            if (Find("CharacterCreationCanvas") == null)
            {
                yield break;
            }

            Record("creation could be played through", false, reason);

            yield return Tap("QuickStart", "the way straight past creation");
            yield return WaitUntil(() => Find("CharacterCreationCanvas") == null,
                "creation to hand the game back");
        }

        /// <summary>
        /// Taps a piece the player has not found yet, in creation this time.
        ///
        /// The same rule-7 claim the backpack makes, on the screen where it is made first: the row
        /// stays a live control, the tap goes somewhere, the answer is one calm sentence, and
        /// nothing about the character moves. The interactability is recorded outright rather than
        /// left implied by the tap succeeding, because "no disabled control" is the half of rule 7
        /// that a passing tap does not say out loud.
        /// </summary>
        IEnumerator TapACreationPieceNotFoundYet(GameState state)
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
                Record("creation still has a piece to find", false,
                    "every hair item is already unlocked on day " + state.day);
                yield break;
            }

            GameObject chip = Find("Chip_" + target.id);
            if (chip == null)
            {
                Record("creation draws a row for " + target.id, false,
                    "no active object named Chip_" + target.id);
                yield break;
            }

            Button button = chip.GetComponent<Button>();
            Record("a piece not found yet is still a live control", button != null && button.interactable,
                button == null
                    ? "Chip_" + target.id + " has no Button"
                    : "interactable=" + button.interactable
                      + " — a control at 0.40 opacity is the void rule 7 forbids");

            int before = state.appearance.hair;
            string wornBefore = Listed(state.equippedItems);

            yield return ScrollIntoView(chip);
            yield return TapObject(chip, "tap " + target.id + ", which has not been found yet");
            yield return WaitUntil(() => !string.IsNullOrEmpty(TextOf("RefusalMessage")),
                "the refusal to be spelled out");

            string sentence = TextOf("RefusalMessage");
            string expected = Loc.T(Wardrobe.KeyRefusalLocked);

            Record("creation answers a locked piece with a sentence",
                !string.IsNullOrEmpty(sentence) && sentence.IndexOf(MissingMarkerOpen) < 0,
                "RefusalMessage = \"" + (sentence ?? "missing") + "\"");
            Record("the refusal in creation is the one the wardrobe handed back", sentence == expected,
                "expected \"" + expected + "\", found \"" + (sentence ?? "missing") + "\"");
            Record("being turned down in creation costs nothing",
                state.appearance.hair == before && !Wardrobe.IsEquipped(state, target.id)
                && Listed(state.equippedItems) == wornBefore,
                "hair " + before + " -> " + state.appearance.hair + ", worn " + wornBefore
                + " -> " + Listed(state.equippedItems));
        }

        /// <summary>
        /// Finds a control, brings it into its list's viewport, and taps it.
        ///
        /// Creation puts its cards, its tones and its name field in one scroll view, so on a short
        /// screen the tone row can start below the fold — and <see cref="TapObject"/> correctly
        /// refuses to click through a viewport mask, which would read as a missing control.
        /// </summary>
        IEnumerator TapInList(string objectName, string description)
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

            yield return ScrollIntoView(target);
            yield return TapObject(target, "tap " + description);
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
        /// Types a name and proves it reaches the run immediately rather than at confirm.
        ///
        /// The field is written through the component rather than through a raycast, because there
        /// is no finger API for a keyboard and the thing being proved is what the screen does with
        /// a keystroke, not what uGUI does with a caret. <see cref="InputField.text"/>'s setter
        /// raises <c>onValueChanged</c>, which is the keystroke the screen listens for.
        ///
        /// Immediately is the whole assertion. A language switch reloads the scene, and a name
        /// held in a local field until confirm would be gone the moment a player changed language
        /// halfway through creation.
        /// </summary>
        IEnumerator TypeAName(GameState state)
        {
            GameObject field = Find("NameInput");
            InputField input = field != null ? field.GetComponent<InputField>() : null;
            if (input == null)
            {
                Record("creation takes a name", false,
                    field == null ? "no active object named NameInput" : "NameInput has no InputField");
                yield break;
            }

            input.text = TypedName;
            yield return null;

            Record("creation takes a name", input.text == TypedName,
                "NameInput = \"" + input.text + "\"");
            Record("the name reaches the run the moment it is typed", state.playerName == TypedName,
                "playerName = \"" + (state.playerName ?? "nothing") + "\", typed \"" + TypedName + "\"");

            string hint = TextOf("NameHint");
            Record("the name field says its piece without blocking anyone",
                hint == null || hint.IndexOf(MissingMarkerOpen) < 0,
                "NameHint = \"" + (hint ?? "nothing to say") + "\"");
        }

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
        /// <summary>
        /// Every cell outside the map is drawn, and none of it is walkable.
        ///
        /// This exists because the two halves stopped agreeing on screen. The outside used to be a
        /// near-black band, so "you cannot go there" was obvious; it now draws the same ground the
        /// city stands on, with the fallen wall scattered over it, and the only thing still saying
        /// where the world ends is the stone. Appearance and walkability became separate questions
        /// that same day, and nothing asserted the second one.
        ///
        /// The bug this guards against has shipped here before: a space was once read as a second
        /// ground character, which left the whole outer border walkable and let the player stroll
        /// around the wall and out of the village. That was caught by eye, when the outside looked
        /// wrong. It would not be caught by eye now, because the outside looks right.
        /// </summary>
        IEnumerator VerifyTheOutsideIsNotWalkable()
        {
            TilemapBuilder map = TilemapBuilder.Instance;
            if (map == null || map.Walkable == null)
            {
                Record("the outside of the map is not walkable", false, "no tilemap in the scene");
                yield break;
            }

            int voidCells = 0;
            int walkableVoid = 0;
            string firstOffender = null;

            for (int x = 0; x < map.Width; x++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    if (map.KindAt(x, y) != TilemapBuilder.CellKind.Void)
                    {
                        continue;
                    }

                    voidCells++;
                    if (!map.Walkable[x, y])
                    {
                        continue;
                    }

                    walkableVoid++;
                    if (firstOffender == null)
                    {
                        firstOffender = "(" + x + ", " + y + ")";
                    }
                }
            }

            // A map with no void at all would make the assertion below pass while proving nothing,
            // which is the shape of failure this file exists to refuse.
            Record("the map has an outside to test", voidCells > 0,
                voidCells + " void cell(s) of " + (map.Width * map.Height));

            Record("the outside of the map is not walkable", walkableVoid == 0,
                walkableVoid == 0
                    ? voidCells + " void cell(s), none walkable"
                    : walkableVoid + " of " + voidCells + " void cell(s) are walkable, first at " + firstOffender);

            yield break;
        }

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
        /// Puts on a piece the player does have, and proves the figure moved with it.
        ///
        /// The piece is found at runtime rather than named here: several locked pieces write the
        /// same art indices as free ones, so "a different row" is not the same thing as "a
        /// different look", and an id spelled into a test breaks the day a designer renames a
        /// hairstyle. What is asserted is the pair — the int on the character AND the ring on the
        /// row — because either one alone passes on a screen that is lying about the other.
        /// </summary>
        IEnumerator WearAPieceInCreation(GameState state)
        {
            int before = state.appearance.hair;

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
                Record("creation offers a piece to put on", false,
                    "no unlocked hair item writes a layer different from " + before);
                yield break;
            }

            GameObject chip = Find("Chip_" + target.id);
            if (chip == null)
            {
                Record("creation draws a row for " + target.id, false,
                    "no active object named Chip_" + target.id);
                yield break;
            }

            yield return ScrollIntoView(chip);
            yield return TapObject(chip, "put on " + target.id);
            yield return null;

            Record("putting a piece on in creation changes the character",
                state.appearance.hair == target.art.hair.Value && state.appearance.hair != before,
                "hair layer " + before + " -> " + state.appearance.hair + ", " + target.id
                + " writes " + target.art.hair.Value);
            Record("the piece creation put on is the one wearing the ring",
                IsChildShowing(chip, "Selected"),
                "Selected under Chip_" + target.id + " showing=" + IsChildShowing(chip, "Selected"));
            Record("creation agrees the piece is on", Wardrobe.IsEquipped(state, target.id),
                "Wardrobe.IsEquipped(" + target.id + ")=" + Wardrobe.IsEquipped(state, target.id));

            // The sentence from the refusal a moment ago belonged to that tap and not to this one.
            Record("a piece going on clears the last refusal",
                string.IsNullOrEmpty(TextOf("RefusalMessage")),
                "RefusalMessage = \"" + (TextOf("RefusalMessage") ?? "nothing") + "\"");
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

        static bool HasMaterialCount(string cardName)
        {
            int parsed;
            return int.TryParse(ChildTextOf(Find(cardName), "Count"), out parsed);
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

        /// <summary>Whether a named control exists and would accept a click right now.</summary>
        static bool IsInteractable(string objectName)
        {
            GameObject go = Find(objectName);
            Selectable selectable = go != null ? go.GetComponent<Selectable>() : null;
            return selectable != null && selectable.IsInteractable();
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

        /// <summary>One readable line out of a list of ids, for the detail on a PASS or a FAIL.</summary>
        static string Listed(IEnumerable<string> values)
        {
            if (values == null)
            {
                return "nothing";
            }

            var joined = string.Join(", ", new List<string>(values).ToArray());
            return string.IsNullOrEmpty(joined) ? "nothing" : joined;
        }

        /// <summary>
        /// Whether the backpack opened wearing what character creation put on.
        ///
        /// This is the end-to-end claim of the whole wardrobe, and it can only be made here.
        /// Creation and the backpack are two screens a whole day of script apart, and until now
        /// nothing joined them: creation wrote art indices, the backpack read catalogue ids, and
        /// each of them was self-consistent about a different thing. So the ids are the ones
        /// creation reported on its way out, held since, rather than re-read from the wardrobe —
        /// a re-read would only ever agree with itself.
        ///
        /// The character id is asserted beside the clothes on purpose. Both signature accessories
        /// are free and either character can wear either one, so inference from the worn set alone
        /// reports the wrong character the moment one of them puts on the other's piece.
        /// </summary>
        void RecordCreationChoiceIsWorn(GameState state)
        {
            if (string.IsNullOrEmpty(_creationCharacterId))
            {
                Record("the backpack agrees with what creation chose", false,
                    "creation never reported a character for this run");
                return;
            }

            Record("the backpack knows which character creation made",
                Wardrobe.CharacterId(state) == _creationCharacterId,
                "Wardrobe.CharacterId=" + (Wardrobe.CharacterId(state) ?? "nothing")
                + ", creation chose " + _creationCharacterId);

            var lost = new List<string>();
            for (int i = 0; i < _creationEquipped.Count; i++)
            {
                if (!Wardrobe.IsEquipped(state, _creationEquipped[i]))
                {
                    lost.Add(_creationEquipped[i]);
                }
            }

            Record("the backpack opens wearing what creation put on", lost.Count == 0,
                "creation left " + Listed(_creationEquipped)
                + (lost.Count == 0 ? ", all of it still on" : ", missing " + Listed(lost)));

            Record("the tone creation set is still the tone",
                state.appearance != null && state.appearance.skin == _creationSkin,
                "skin=" + (state.appearance != null ? state.appearance.skin : -1)
                + ", creation set " + _creationSkin);
        }

        /// <summary>
        /// The way past creation for a player in a hurry: present, and accepting a tap.
        ///
        /// Asserted rather than taken, because a run has exactly one character creation in it and
        /// spending it on the skip would leave everything the screen actually does undriven. What
        /// this catches is the failure that matters most about a skip button — that it quietly
        /// stopped existing, or stopped being tappable, on one of the two steps.
        /// </summary>
        void RecordEscapeHatch(string step)
        {
            Record("there is a way straight past " + step, IsInteractable("QuickStart"),
                Find("QuickStart") == null
                    ? "no active object named QuickStart"
                    : "QuickStart interactable=" + IsInteractable("QuickStart"));
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
        /// <summary>
        /// The profile section: the engagement meter and the two headings under it.
        ///
        /// Ported back from main during the merge that brought the season onto it. Rule 10 is why
        /// this asserts the meter is drawn and never what it reads.
        /// </summary>
        void RecordProfileTab()
        {
            bool drawn = Find("EngagementMeter") != null && Find("MissionsHeading") != null
                && Find("StudiesHeading") != null;
            Record("the profile section draws its meter and both headings", drawn,
                "meter=" + (Find("EngagementMeter") != null)
                + " missions=" + (Find("MissionsHeading") != null)
                + " studies=" + (Find("StudiesHeading") != null));

            Record("the profile section gives its column the whole sheet", Find("CharacterStage") == null,
                Find("CharacterStage") == null
                    ? "the character stage is away"
                    : "the stage is still standing over the profile");

            Record("the profile section takes the slot bar with it too", Find("SegmentHair") == null,
                "SegmentHair active=" + (Find("SegmentHair") != null));
        }

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
        /// Asserts that exactly one of creation's slot panels is on screen — the same assertion the
        /// backpack makes about its own five, and for the same reason: two lists drawn over one
        /// another logs nothing and throws nothing.
        /// </summary>
        void RecordOnlyCreationTab(CharacterSlot slot, string step)
        {
            IReadOnlyList<CharacterSlot> slots = Wardrobe.BadgedSlots;
            var showing = new List<string>();
            for (int i = 0; i < slots.Count; i++)
            {
                string name = "Tab" + slots[i];
                if (Find(name) != null)
                {
                    showing.Add(name);
                }
            }

            Record(step, showing.Count == 1 && showing[0] == "Tab" + slot,
                showing.Count == 0 ? "no slot panel is on screen at all" : "showing " + Listed(showing));
        }

        /// <summary>
        /// What one slot offers on the first day of a run: two pieces free, every other piece drawn
        /// in full beside them with the condition that opens it written out.
        ///
        /// The count is decision 2 and the drawing is rule 7, and neither can be read off a
        /// screenshot: a wardrobe that quietly offered four would look like a generous wardrobe,
        /// and a wardrobe that hid what it could not yet offer would look like a small one. Both
        /// are read off the catalogue and the hierarchy instead.
        ///
        /// The sentence is checked three ways because each way covers the others' blind spot.
        /// Emptiness catches a row that drew no condition at all; the resolution marker catches a
        /// key missing from ui.json; and the raw key catches the catalogue's own miss, which
        /// renders as <c>[key]</c> and would sail straight past a sweep looking for angle quotes.
        /// </summary>
        void RecordSlotOptions(GameState state, CharacterSlot slot)
        {
            string tab = slot.ToString();
            CatalogItemDef[] items = Wardrobe.ItemsForSlot(slot);

            var free = new List<string>();
            var locked = new List<CatalogItemDef>();
            for (int i = 0; i < items.Length; i++)
            {
                CatalogItemDef item = items[i];
                if (item == null || string.IsNullOrEmpty(item.id))
                {
                    continue;
                }

                if (Wardrobe.IsUnlocked(state, item.id))
                {
                    free.Add(item.id);
                }
                else
                {
                    locked.Add(item);
                }
            }

            Record("the " + tab + " slot opens with two options", free.Count == 2,
                free.Count + " free (" + Listed(free) + ") and " + locked.Count + " not found yet");

            var undrawn = new List<string>();
            var mute = new List<string>();
            var colourOnly = new List<string>();

            for (int i = 0; i < items.Length; i++)
            {
                CatalogItemDef item = items[i];
                if (item == null || string.IsNullOrEmpty(item.id))
                {
                    continue;
                }

                if (Find("Chip_" + item.id) == null)
                {
                    undrawn.Add(item.id);
                }
            }

            Record("every " + tab + " piece is drawn, the ones not found yet included", undrawn.Count == 0,
                undrawn.Count == 0
                    ? items.Length + " row(s) on screen"
                    : "no row for " + Listed(undrawn) + " — a piece not found yet is drawn in full, never blanked");

            for (int i = 0; i < locked.Count; i++)
            {
                CatalogItemDef item = locked[i];
                GameObject chip = Find("Chip_" + item.id);
                if (chip == null)
                {
                    continue;
                }

                string sentence = ChildTextOf(chip, "Text/Unlock");
                string key = UnlockEvaluator.LocaleKey(item.unlock_condition);
                string expected = UnlockEvaluator.Sentence(item.unlock_condition);

                bool spelledOut = !string.IsNullOrEmpty(sentence)
                    && sentence.IndexOf(MissingMarkerOpen) < 0
                    && (string.IsNullOrEmpty(key) || sentence.IndexOf(key, StringComparison.Ordinal) < 0)
                    && sentence == expected;

                if (!spelledOut)
                {
                    mute.Add(item.id + " = \"" + (sentence ?? "nothing") + "\" (wardrobe says \""
                             + (expected ?? "nothing") + "\")");
                }

                if (!IsChildShowing(chip, "Text/Name/Status"))
                {
                    colourOnly.Add(item.id);
                }
            }

            Record("every " + tab + " piece not found yet says how to open it", mute.Count == 0,
                mute.Count == 0
                    ? locked.Count + " condition(s) written out in full"
                    : Listed(mute));

            // Rule 2. A padlock is the word beside the colour, and a row that leans on the colour
            // alone says nothing at all to a player who cannot see the difference.
            Record("a " + tab + " piece not found yet carries an icon and not only a colour",
                colourOnly.Count == 0,
                colourOnly.Count == 0
                    ? "every locked row shows its status icon"
                    : "no status icon on " + Listed(colourOnly));
        }

        /// <summary>
        /// The piece worn in one slot is the row wearing the ring on that slot's tab.
        ///
        /// Read off the wardrobe rather than off the id creation reported, because by the time the
        /// later tabs are reached the run has already swapped a piece of hair — what is being
        /// proved is that the sheet draws what the player is wearing now, whoever put it there.
        /// </summary>
        void RecordWornRowIsRinged(GameState state, CharacterSlot slot)
        {
            string worn = Wardrobe.EquippedInSlot(state, slot);
            GameObject chip = string.IsNullOrEmpty(worn) ? null : Find("Chip_" + worn);

            Record("the " + slot + " tab rings the piece that is on",
                chip != null && IsChildShowing(chip, "Selected"),
                string.IsNullOrEmpty(worn)
                    ? "nothing is worn in the " + slot + " slot"
                    : "Chip_" + worn + " present=" + (chip != null)
                      + " ring=" + IsChildShowing(chip, "Selected"));
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

    }
}
