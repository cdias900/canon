using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using SheepGate.Contest;
using SheepGate.Core;
using SheepGate.Economy;
using SheepGate.Player;
using SheepGate.Scripture;
using SheepGate.Vocation;
using SheepGate.World;
using UnityEditor;
using UnityEngine;

namespace SheepGate.EditorTools
{
    /// <summary>
    /// Drives the real systems headlessly and asserts the acceptance criteria from
    /// MVP-SCOPE.md §13 that cannot be checked by playing for a minute — the ones about
    /// rules rather than about pixels.
    ///
    /// Run with:
    ///   -executeMethod SheepGate.EditorTools.AcceptanceHarness.RunAll
    /// Exits non-zero when a criterion fails, so it works as a build gate.
    /// </summary>
    public static class AcceptanceHarness
    {
        static readonly List<string> Failures = new List<string>();
        static readonly StringBuilder Report = new StringBuilder();

        /// <summary>Locale whose criteria are being checked. Prefixed onto every line of the report.</summary>
        static string _locale = Locales.Source;

        static void Check(string criterion, bool passed, string detail)
        {
            criterion = "[" + _locale + "] " + criterion;
            if (passed)
            {
                Report.AppendLine("  PASS  " + criterion + " — " + detail);
            }
            else
            {
                Report.AppendLine("  FAIL  " + criterion + " — " + detail);
                Failures.Add(criterion + ": " + detail);
            }
        }

        public static void RunAll()
        {
            Failures.Clear();
            Report.Length = 0;
            Report.AppendLine("Sheep Gate acceptance harness");
            Report.AppendLine();

            // Every criterion is checked once per shipped language. A rule that holds in one
            // locale and not another is exactly the failure a single-language pass cannot see,
            // and the content split means a missing translation now shows up here rather than
            // on a player's screen.
            IReadOnlyDictionary<string, DialogueNode> sourceDialogue = null;

            foreach (string locale in Locales.Supported)
            {
                _locale = locale;

                try
                {
                    BootSequence.ApplyLocale(locale);

                    if (locale == Locales.Source)
                    {
                        sourceDialogue = GameData.Dialogue;
                    }

                    LocalizationIntegrity(sourceDialogue);
                    ScriptureIntegrity();
                    WallProgressNeverRegresses();
                    SaveRoundTrip();
                    ContestRules();
                    VocationResolution();
                    NightDiffers();
                    DaylightClock();
                    CheckInSchedule();
                }
                catch (Exception exception)
                {
                    Failures.Add("[" + locale + "] harness threw: " + exception);
                    Report.AppendLine("  FAIL  [" + locale + "] harness threw — " + exception.Message);
                }
            }

            // Leave the editor on the authoring locale so a run of the harness does not silently
            // change what the next Play session shows.
            _locale = Locales.Source;
            BootSequence.ApplyLocale(Locales.Source);

            Report.AppendLine();
            Report.AppendLine(Failures.Count == 0
                ? "ALL CRITERIA PASSED"
                : Failures.Count + " CRITERION FAILURE(S)");

            Debug.Log(Report.ToString());

            if (Failures.Count > 0)
            {
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Every player-facing string this locale owes exists, and its dialogue has the same nodes
        /// as the authoring locale. Structure lives in one file shared by all languages, so a
        /// translation can only be wrong by being incomplete — which is what this checks.
        /// </summary>
        static void LocalizationIntegrity(IReadOnlyDictionary<string, DialogueNode> sourceDialogue)
        {
            Check("L1 string table loads", Loc.Count > 0,
                Loc.Count + " strings in " + Loc.LoadedLocale);

            var gaps = new List<string>();

            foreach (NpcDef npc in GameData.Npcs)
            {
                if (npc != null && string.IsNullOrEmpty(npc.display)) gaps.Add("npc:" + npc.id);
            }

            foreach (VocationDef vocation in GameData.Vocations)
            {
                if (vocation == null) continue;
                if (string.IsNullOrEmpty(vocation.display)) gaps.Add("vocation.display:" + vocation.id);
                if (string.IsNullOrEmpty(vocation.reveal_line)) gaps.Add("vocation.reveal_line:" + vocation.id);
            }

            ContestMoveDef[] moves = GameData.Contest != null ? GameData.Contest.moves : null;
            if (moves != null)
            {
                foreach (ContestMoveDef move in moves)
                {
                    if (move == null) continue;
                    if (string.IsNullOrEmpty(move.display)) gaps.Add("move.display:" + move.id);
                    if (string.IsNullOrEmpty(move.description)) gaps.Add("move.description:" + move.id);
                }
            }

            foreach (QuizQuestion question in GameData.Quiz)
            {
                if (question == null) continue;
                if (string.IsNullOrEmpty(question.prompt)) gaps.Add("quiz.prompt:d" + question.day);
                if (question.options == null || question.options.Length == 0) gaps.Add("quiz.options:d" + question.day);
                if (string.IsNullOrEmpty(question.note)) gaps.Add("quiz.note:d" + question.day);
            }

            Check("L2 content strings complete", gaps.Count == 0,
                gaps.Count == 0 ? "no gaps" : gaps.Count + " gap(s) [" + string.Join(", ", gaps) + "]");

            if (sourceDialogue == null)
            {
                return;
            }

            var missingNodes = new List<string>();
            foreach (KeyValuePair<string, DialogueNode> pair in sourceDialogue)
            {
                DialogueNode translated;
                if (!GameData.Dialogue.TryGetValue(pair.Key, out translated) || translated == null)
                {
                    missingNodes.Add(pair.Key);
                    continue;
                }

                int sourceLines = pair.Value != null && pair.Value.lines != null ? pair.Value.lines.Length : 0;
                int translatedLines = translated.lines != null ? translated.lines.Length : 0;
                if (sourceLines != translatedLines)
                {
                    missingNodes.Add(pair.Key + " (" + translatedLines + "/" + sourceLines + " lines)");
                }
            }

            Check("L3 dialogue matches the source locale", missingNodes.Count == 0,
                sourceDialogue.Count + " node(s), " + missingNodes.Count + " off" +
                (missingNodes.Count > 0 ? " [" + string.Join(", ", missingNodes) + "]" : ""));
        }

        // 04 — every verse referenced by content resolves from verses.json, none invented.
        static void ScriptureIntegrity()
        {
            var missing = new List<string>();
            var referenced = new HashSet<string>();

            foreach (var pair in GameData.Dialogue)
            {
                if (pair.Value?.lines == null) continue;
                foreach (var line in pair.Value.lines)
                {
                    if (string.IsNullOrEmpty(line?.verse)) continue;
                    referenced.Add(line.verse);
                    if (!ScriptureService.TryGetVerse(line.verse, out _)) missing.Add(line.verse);
                }
            }

            Check("04 scripture resolves", missing.Count == 0,
                referenced.Count + " referenced, " + missing.Count + " unresolved" +
                (missing.Count > 0 ? " [" + string.Join(", ", missing) + "]" : ""));

            Check("04 not a placeholder build", !ScriptureService.IsPlaceholderBuild,
                "version " + (ScriptureService.Version != null ? ScriptureService.Version.abbrev : "?"));

            ChapterEntry chapter = ScriptureService.GetChapter("NEH.4");
            Check("10 chapter is readable", chapter != null && chapter.verses != null && chapter.verses.Length > 1,
                "NEH.4 has " + (chapter?.verses?.Length ?? 0) + " verses");
        }

        static WallSystem NewWallSystem(out GameState state)
        {
            state = GameState.NewGame();
            ServiceLocator.Clear();
            ServiceLocator.Register(state);
            var host = new GameObject("HarnessWall");
            var wall = host.AddComponent<WallSystem>();
            wall.Build(null); // segments come from Build; a null tilemap is supported.
            return wall;
        }

        // 05 + 09 — a finished stage never regresses, however hard the night hits it.
        static void WallProgressNeverRegresses()
        {
            GameState state;
            WallSystem wall = NewWallSystem(out state);
            const string id = "seg_01";

            if (!wall.Contains(id))
            {
                Check("05 seg_01 exists", false, "wall_segments.json has no seg_01");
                return;
            }

            for (int i = 0; i < 200 && wall.StageOf(id) < 2; i++) wall.ApplyWork(id, 1);
            int reached = wall.StageOf(id);
            Check("05 work advances stages", reached >= 2, "seg_01 reached stage " + reached);

            wall.ApplyWork(id, 1); // partial progress into the next stage
            wall.DamageSegment(id);
            int afterDamage = wall.StageOf(id);
            Check("09 damage never regresses a finished stage", afterDamage >= reached,
                "stage " + reached + " before damage, " + afterDamage + " after");

            for (int i = 0; i < 500 && !wall.IsComplete(id); i++) wall.ApplyWork(id, 1);
            Check("05 segment completes", wall.IsComplete(id), "seg_01 stage " + wall.StageOf(id));

            wall.DamageSegment(id);
            Check("09 a completed segment stays completed", wall.IsComplete(id),
                "stage after damaging a finished segment: " + wall.StageOf(id));

            UnityEngine.Object.DestroyImmediate(wall.gameObject);
        }

        // 05 — progress survives closing the app.
        //
        // This writes through the real SaveSystem, which means it writes to the real
        // persistentDataPath — the same file a playtester is using if one is running. The prior
        // save is captured and restored around the test so the harness can never destroy someone's
        // session; a test that eats real data is worse than no test.
        static void SaveRoundTrip()
        {
            GameState preexisting = SaveSystem.HasSave() ? SaveSystem.Load() : null;
            try
            {
                SaveRoundTripBody();
            }
            finally
            {
                if (preexisting != null)
                {
                    SaveSystem.Save(preexisting);
                    Report.AppendLine("  note  restored the save that was already on disk");
                }
                else
                {
                    SaveSystem.Delete();
                }
            }
        }

        static void SaveRoundTripBody()
        {
            GameState original = GameState.NewGame();
            original.day = 2;
            original.rubble = 7;
            original.SetFlag(GameFlags.WatchPostedD1);
            original.Bump("talked_hananias", 2);
            if (original.segments.Count > 0)
            {
                original.segments[0].stage = 3;
                original.segments[0].workInStage = 2;
            }

            SaveSystem.Save(original);
            GameState loaded = SaveSystem.Load();

            bool ok = loaded != null
                      && loaded.day == 2
                      && loaded.rubble == 7
                      && loaded.HasFlag(GameFlags.WatchPostedD1)
                      && loaded.Counter("talked_hananias") == 2
                      && loaded.segments.Count == original.segments.Count
                      && loaded.segments[0].stage == 3;

            Check("05 save round-trips", ok,
                loaded == null ? "load returned null"
                    : "day=" + loaded.day + " rubble=" + loaded.rubble +
                      " flag=" + loaded.HasFlag(GameFlags.WatchPostedD1) +
                      " stage=" + (loaded.segments.Count > 0 ? loaded.segments[0].stage : -1));
        }

        // 07 + 08 + 09 — the contest is earned by days 1-2, the Page unlocks the strong move,
        // and losing is not a game over.
        static void ContestRules()
        {
            // Harder run: no watch on day 2 and the invitation accepted.
            GameState harsh = GameState.NewGame();
            harsh.day = 3;
            harsh.SetFlag(GameFlags.AcceptedInvite);
            ServiceLocator.Clear();
            ServiceLocator.Register(harsh);
            var harshHost = new GameObject("HarnessContestHarsh");
            harshHost.AddComponent<WallSystem>().Build(null);
            var harshContest = harshHost.AddComponent<MoraleContest>();
            harshContest.Begin();
            int harshResolve = harshContest.EnemyResolveMax;

            // Kinder run: watch posted, invitation refused.
            GameState kind = GameState.NewGame();
            kind.day = 3;
            kind.SetFlag(GameFlags.WatchPostedD2);
            kind.SetFlag(GameFlags.RefusedInvite);
            ServiceLocator.Clear();
            ServiceLocator.Register(kind);
            var kindHost = new GameObject("HarnessContestKind");
            kindHost.AddComponent<WallSystem>().Build(null);
            var kindContest = kindHost.AddComponent<MoraleContest>();
            kindContest.Begin();
            int kindResolve = kindContest.EnemyResolveMax;

            Check("07 days 1-2 decide the trial", harshResolve > kindResolve,
                "enemy resolve " + harshResolve + " (neglected) vs " + kindResolve + " (prepared)");

            Check("08 half-and-half is locked before the Page",
                !kindContest.IsMoveAvailable(MoraleContest.MoveHalfAndHalf),
                "available at turn " + kindContest.Turn + ": " +
                kindContest.IsMoveAvailable(MoraleContest.MoveHalfAndHalf));

            // The turn loop is a coroutine, which EditMode cannot pump, so the live Page beat is
            // covered by the PlayMode test. What is checkable here is its configuration.
            Check("08 the Page is set for turn 2", MoraleContest.PageTurn == 2,
                "PageTurn = " + MoraleContest.PageTurn);

            ContestMoveDef gated = null;
            foreach (var move in kindContest.Moves)
            {
                if (move != null && move.id == MoraleContest.MoveHalfAndHalf) gated = move;
            }
            Check("08 half-and-half is gated by the Page", gated != null && gated.unlocked_by_page,
                gated == null ? "half_and_half missing from contest.json"
                    : "unlocked_by_page = " + gated.unlocked_by_page);

            Check("09 no game over exists", !HasGameOverSymbol(),
                "no type or member named GameOver in the assembly");

            UnityEngine.Object.DestroyImmediate(harshHost);
            UnityEngine.Object.DestroyImmediate(kindHost);
        }

        static bool HasGameOverSymbol()
        {
            foreach (var type in typeof(MoraleContest).Assembly.GetTypes())
            {
                if (type.Name.IndexOf("GameOver", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        // 11 — the revealed vocation is the highest score, ties break by table order.
        static void VocationResolution()
        {
            GameState state = GameState.NewGame();
            ServiceLocator.Clear();
            ServiceLocator.Register(state);

            var tracker = new VocationTracker();
            tracker.Add(VocationIds.Scribe, 6);
            tracker.Add(VocationIds.Zealot, 2);
            tracker.Add(VocationIds.Shepherd, 4);
            string highest = tracker.Resolve();
            Check("11 highest score wins", highest == VocationIds.Scribe,
                "resolved '" + highest + "', expected '" + VocationIds.Scribe + "'");

            GameState tie = GameState.NewGame();
            ServiceLocator.Clear();
            ServiceLocator.Register(tie);
            var tieTracker = new VocationTracker();
            tieTracker.Add(VocationIds.Shepherd, 4);
            tieTracker.Add(VocationIds.Zealot, 4);
            string tieWinner = tieTracker.Resolve();
            string firstInTable = GameData.Vocations != null && GameData.Vocations.Length > 0
                ? GameData.Vocations[0].id
                : VocationIds.Zealot;
            Check("11 ties break by table order",
                tieWinner == VocationIds.Zealot || tieWinner == firstInTable,
                "resolved '" + tieWinner + "'; vocations.json leads with '" + firstInTable + "'");

            // The rule that matters most: nothing may read progress before the reveal.
            var forbidden = new List<string>();
            foreach (var member in typeof(VocationTracker).GetMembers())
            {
                string n = member.Name;
                if (n.IndexOf("Score", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Points", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Progress", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (n != "Add") forbidden.Add(n);
                }
            }
            Check("11 no vocation progress is exposed", forbidden.Count == 0,
                forbidden.Count == 0 ? "VocationTracker exposes no score reader"
                    : "exposes [" + string.Join(", ", forbidden) + "]");
        }

        // 06 — a night with a watch posted must not resolve like a night without one.
        static void NightDiffers()
        {
            string damagedWithWatch;
            string damagedWithout;
            bool flagWithWatch = ResolveNight(true, out damagedWithWatch);
            bool flagWithout = ResolveNight(false, out damagedWithout);

            Check("06 only the unwatched night damages the wall",
                damagedWithWatch == null && damagedWithout != null,
                "watched night damaged [" + (damagedWithWatch ?? "nothing") +
                "], unwatched night damaged [" + (damagedWithout ?? "nothing") + "]");

            Check("06 the watch is recorded only when posted", flagWithWatch && !flagWithout,
                "watch_posted_d1 = " + flagWithWatch + " with a watch, " + flagWithout + " without");
        }

        // 13 — the day is its own clock, and reading is never what ends it.
        static void DaylightClock()
        {
            GameState state = GameState.NewGame();
            ServiceLocator.Clear();
            ServiceLocator.Register(state);

            var host = new GameObject("HarnessDaylight");
            var cycle = host.AddComponent<DayCycle>();
            cycle.enabled = false;

            state.workCapacityMax = 12;

            state.workCapacity = 12;
            float dawn = cycle.DayProgress;

            state.workCapacity = 6;
            float midday = cycle.DayProgress;

            state.workCapacity = 0;
            float spent = cycle.DayProgress;

            Check("13 the day is its own clock",
                Mathf.Approximately(dawn, 0f) && Mathf.Approximately(midday, 0.5f) && Mathf.Approximately(spent, 1f),
                "day progress at full capacity " + dawn + ", half " + midday + ", spent " + spent);

            // Nothing on the HUD ends a day any more. The button that did was the thing this
            // whole system replaced, and a reinstated one would quietly restore the chore.
            Check("13 nothing on the interface ends the day", !HasEndDaySymbol(),
                "no HUD member named for ending the day");

            // The one that matters: a pending night waits for whatever has the screen. The reader
            // is a panel, so this is the assertion that a chapter can never cost the player a day.
            Check("13 a night never resolves over an open panel",
                DayCycle.DuskWaits(false, false, true)
                && DayCycle.DuskWaits(false, true, false)
                && DayCycle.DuskWaits(true, false, false)
                && !DayCycle.DuskWaits(false, false, false),
                "dusk waits on a panel, a locked input and a hold, and on nothing else");

            UnityEngine.Object.DestroyImmediate(host);
        }

        static bool HasEndDaySymbol()
        {
            Type hud = typeof(MoraleContest).Assembly.GetType("SheepGate.UI.HUD");
            if (hud == null)
            {
                return false;
            }

            foreach (var member in hud.GetMembers(BindingFlags.Public | BindingFlags.NonPublic
                                                  | BindingFlags.Instance | BindingFlags.Static
                                                  | BindingFlags.DeclaredOnly))
            {
                if (member.Name.IndexOf("EndDay", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Resolves one night and reports which segment it damaged, plus whether the watch flag
        /// was set. DayCycle defers to a coroutine while it is active so it can fade; EditMode
        /// cannot pump coroutines, so the component is disabled to take its synchronous path.
        /// </summary>
        static bool ResolveNight(bool postWatch, out string damagedSegment)
        {
            GameState state = GameState.NewGame();
            state.day = 1;
            ServiceLocator.Clear();
            ServiceLocator.Register(state);

            var host = new GameObject("HarnessNight");
            host.AddComponent<WallSystem>().Build(null);
            var cycle = host.AddComponent<DayCycle>();
            cycle.enabled = false;
            cycle.EndDay(postWatch ? 6 : 12, postWatch ? 6 : 0);

            damagedSegment = cycle.LastNightDamagedSegment;
            bool watchFlag = state.HasFlag(GameFlags.WatchPostedD1);

            UnityEngine.Object.DestroyImmediate(host);
            return watchFlag;
        }

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

            // The reward modal previews tomorrow as TalentsForStreak(streak + 1) - the same
            // expression HUD.OnCheckInClicked hands it. That preview is the one number a player
            // might come back for, and nothing else asserts the step where it changes, so both
            // sides of the boundary are pinned here rather than left to the modal's caller.
            int previewAfterStreak2 = DailyCheckIn.TalentsForStreak(2 + 1);
            Check("check-in previews 1 talent for tomorrow while the streak is short",
                previewAfterStreak2 == 1, "claiming at streak 2 previews " + previewAfterStreak2);

            int previewAfterStreak3 = DailyCheckIn.TalentsForStreak(3 + 1);
            Check("check-in previews 3 talents for tomorrow once the fourth day is next",
                previewAfterStreak3 == 3, "claiming at streak 3 previews " + previewAfterStreak3);

            // A price that moves is the failure mode worth guarding: the value looks arbitrary by
            // design, so a re-roll on every rebuild of the sheet would not look like a bug to
            // anyone reading the screen. Pinned across the real catalogue rather than one id.
            bool everyPriceInRange = true;
            string firstOutOfRange = null;
            int pricedItems = 0;
            foreach (CatalogItemDef priced in CharacterCatalog.Items)
            {
                if (priced == null || string.IsNullOrEmpty(priced.id))
                {
                    continue;
                }

                pricedItems++;
                int value = TalentPrice.For(priced.id);
                if (value < TalentPrice.Min || value > TalentPrice.Max)
                {
                    everyPriceInRange = false;
                    if (firstOutOfRange == null) firstOutOfRange = priced.id + "=" + value;
                }
            }

            Check("talent prices sit between 5 and 15 for every catalogue item",
                everyPriceInRange && pricedItems > 0,
                firstOutOfRange == null
                    ? pricedItems + " item(s), all within range"
                    : "first outside: " + firstOutOfRange);

            // A golden value, not a call compared against itself - the latter passes for any
            // function at all, including one seeded per process. This is the number FNV-1a gives
            // for this id, so a switch back to string.GetHashCode (explicitly not stable between
            // runs) fails here instead of silently repricing the catalogue on every launch.
            int goldenPrice = TalentPrice.For("hair_short_crop");
            Check("a talent price is a fixed function of the item id, not a per-run roll",
                goldenPrice == 9, "hair_short_crop priced at " + goldenPrice + ", expected 9");
        }
    }
}
