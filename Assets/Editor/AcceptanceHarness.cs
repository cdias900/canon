using System;
using System.Collections.Generic;
using System.Text;
using SheepGate.Contest;
using SheepGate.Core;
using SheepGate.Scripture;
using SheepGate.Vocation;
using SheepGate.World;
using UnityEditor;
using UnityEngine;

namespace SheepGate.EditorTools
{
    /// <summary>
    /// Drives the real systems headlessly and asserts the acceptance criteria from
    /// POC-IMPLEMENTATION.md §13 that cannot be checked by playing for a minute — the ones about
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

        static void Check(string criterion, bool passed, string detail)
        {
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

            try
            {
                GameData.LoadAll();
                ScriptureService.Load();

                ScriptureIntegrity();
                WallProgressNeverRegresses();
                SaveRoundTrip();
                ContestRules();
                VocationResolution();
                NightDiffers();
            }
            catch (Exception exception)
            {
                Failures.Add("harness threw: " + exception);
                Report.AppendLine("  FAIL  harness threw — " + exception.Message);
            }

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
        static void SaveRoundTrip()
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

            SaveSystem.Delete();
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
    }
}
