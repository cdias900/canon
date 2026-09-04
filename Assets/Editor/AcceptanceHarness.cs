using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using SheepGate.Contest;
using SheepGate.Core;
using SheepGate.Economy;
using SheepGate.Player;
using SheepGate.Scripture;
using SheepGate.UI;
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
    /// NUMBERED CRITERIA COME FROM §13; the letter-prefixed ones do not, and the prefix is how you
    /// tell. <c>L1</c>-<c>L3</c> are the localization checks that arrived with the content split,
    /// and <c>S1</c>-<c>S3</c> are the three the nine-stage season needed: that the stage table
    /// holds together, that a save written by the three-day build comes forward without losing
    /// anything, and that a move a flag unlocks is shut without it. Reusing a §13 number for a check §13 does not contain would make the report
    /// unreadable against the document it is named for.
    ///
    /// EVERY CHECK THAT USED TO NAME A DAY NOW READS THE SEASON. The old shape — day 3 for the
    /// contest, night 1 for the watch, NEH.4 for the reader — passed happily on a season in which
    /// six of the nine stages could not be reached at all, because it was asking about the part
    /// that had not moved. Where a criterion is about a stage, it loops the stages; where it is
    /// about a night, it loops the nights; where it is about a chapter, it derives the chapters
    /// from what the content actually cites.
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
                    SeasonIsWhole();
                    WallProgressNeverRegresses();
                    SaveRoundTrip();
                    ContestRules();
                    FlagGatedMovesAreShut(sourceDialogue);
                    TheGateIsEarned();
                    TheNightKeepsTheDaysPromises();
                    TheOpeningIsNotAMenu();
                    VocationResolution();
                    NightDiffers();
                    TheVigilCostsTheNight();
                    ACostedMoveCostsTheDay();
                    TheCodexNamesEveryGate();
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

            // Every contest, not just the one GameData.Contest happens to resolve to. The season has
            // two now and they draw overlapping but different move sets, so a move that exists only
            // in the second fight had no string check at all while this read a single config — which
            // is exactly the shape of the keep_working interlock: a move in the shared table with no
            // entry in either locale's file, invisible until the fight opens.
            if (GameData.Contests != null)
            {
                foreach (KeyValuePair<string, ContestConfig> pair in GameData.Contests)
                {
                    if (pair.Value == null || pair.Value.moves == null) continue;
                    foreach (ContestMoveDef move in pair.Value.moves)
                    {
                        if (move == null) continue;
                        if (string.IsNullOrEmpty(move.display)) gaps.Add("move.display:" + pair.Key + "/" + move.id);
                        if (string.IsNullOrEmpty(move.description)) gaps.Add("move.description:" + pair.Key + "/" + move.id);
                    }
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

            EveryReachableChapterIsReadable();
        }

        /// <summary>
        /// 10 — every chapter a player can reach opens on a whole chapter, not on one verse.
        ///
        /// This used to name <c>NEH.4</c> and nothing else, while the season shipped several more —
        /// and the ones the check never looked at were the ones that could fail: a citation renders
        /// perfectly from <c>verses</c> while its chapter is absent, so Saber mais opens on a shell
        /// and the tap the entire product is measured by leads nowhere. The manifest's own note
        /// records that exact failure having shipped once, with most citations carrying a dead door.
        ///
        /// The set is DERIVED, so a chapter added by content is covered the day it is cited: the
        /// chapter of every <c>verse</c> in every dialogue line, the chapter of every contest's
        /// <c>page_verse</c>, and the two chapters the ending's gate panel opens — those last read
        /// off <see cref="SheepGate.World.StageDirector"/>'s own fields rather than spelled again
        /// here, because a copy of a constant is a copy that goes stale on the day the original
        /// moves and reports green while it does.
        ///
        /// A placeholder build is tolerated EXPLICITLY. Its generator synthesizes chapters with
        /// exactly one verse, so "more than one verse" could never pass on one — by accident rather
        /// than by decision, which meant the tolerance looked like a bug and the failure looked like
        /// missing content. The door still has to exist there; only how much is behind it is relaxed.
        /// </summary>
        static void EveryReachableChapterIsReadable()
        {
            var chapters = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var pair in GameData.Dialogue)
            {
                if (pair.Value?.lines == null) continue;
                foreach (var line in pair.Value.lines)
                {
                    AddChapterOf(chapters, line?.verse);
                }
            }

            if (GameData.Contests != null)
            {
                foreach (KeyValuePair<string, ContestConfig> pair in GameData.Contests)
                {
                    if (pair.Value != null) AddChapterOf(chapters, pair.Value.page_verse);
                }
            }

            // The page a vigil returns opens its chapter from the morning report, so it is a door
            // like any citation's and its chapter has to be there to open.
            if (GameData.Stages != null)
            {
                foreach (StageDef stage in GameData.Stages)
                {
                    AddChapterOf(chapters, stage != null ? stage.vigil_verse : null);
                }
            }

            // Every gate card in the codex carries Saber mais into its verse's chapter, from the
            // first hour of a run, so those chapters are doors like any citation's.
            if (GameData.Gates != null)
            {
                foreach (GateDef gate in GameData.Gates)
                {
                    AddChapterOf(chapters, gate != null ? gate.verse : null);
                }
            }

            var gateRefs = new List<string>();
            AddDirectorChapter(chapters, gateRefs, "GateChapterRef");
            AddDirectorChapter(chapters, gateRefs, "GateRecordRef");
            Check("10 the ending's chapters are readable from the code that opens them", gateRefs.Count == 2,
                gateRefs.Count == 2
                    ? "StageDirector opens [" + string.Join(", ", gateRefs) + "]"
                    : "StageDirector no longer declares GateChapterRef and GateRecordRef as string constants; " +
                      "found [" + string.Join(", ", gateRefs) + "] — the ending's chapters are now unchecked");

            bool placeholder = ScriptureService.IsPlaceholderBuild;
            int minimumVerses = placeholder ? 1 : 2;
            var thin = new List<string>();

            foreach (string chapterRef in chapters)
            {
                ChapterEntry chapter = null;
                try
                {
                    chapter = ScriptureService.GetChapter(chapterRef);
                }
                catch (Exception exception)
                {
                    thin.Add(chapterRef + " (" + exception.Message + ")");
                    continue;
                }

                int verses = chapter != null && chapter.verses != null ? chapter.verses.Length : 0;
                if (verses < minimumVerses)
                {
                    thin.Add(chapterRef + " (" + verses + " verse(s))");
                }
            }

            Check("10 every reachable chapter is readable", chapters.Count > 0 && thin.Count == 0,
                chapters.Count + " chapter(s) reachable from content [" + string.Join(", ", chapters) + "]" +
                (placeholder ? ", placeholder build so one verse is enough" : "") +
                (thin.Count > 0 ? " — too thin to read: [" + string.Join(", ", thin) + "]" : ""));
        }

        static void AddChapterOf(SortedSet<string> chapters, string verseRef)
        {
            if (string.IsNullOrEmpty(verseRef)) return;

            string chapterRef = ScriptureService.ChapterRefOf(verseRef.Trim());
            if (!string.IsNullOrEmpty(chapterRef)) chapters.Add(chapterRef);
        }

        /// <summary>
        /// Reads one of the director's private chapter constants and adds it to the set.
        ///
        /// Reflection rather than a second copy of the literal: the whole point of checking the
        /// ending's chapters is that the gate panel can be handed the wrong one silently — the older
        /// two-argument overload still compiles and still opens a reader — so a check that carried
        /// its own copy of the expected value would agree with itself forever.
        /// </summary>
        static void AddDirectorChapter(SortedSet<string> chapters, List<string> found, string fieldName)
        {
            try
            {
                FieldInfo field = typeof(SheepGate.World.StageDirector).GetField(
                    fieldName, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
                string value = field != null && field.IsLiteral ? field.GetRawConstantValue() as string : null;
                if (string.IsNullOrEmpty(value)) return;

                found.Add(value);
                chapters.Add(value);
            }
            catch (Exception exception)
            {
                Report.AppendLine("  note  could not read StageDirector." + fieldName + " — " + exception.Message);
            }
        }

        /// <summary>
        /// Every stage the season declares holds together, checked outside the runtime that loads it.
        ///
        /// <see cref="GameData"/> asserts these at load and logs an error per miss, which the e2e
        /// run promotes to a failure — but only for someone who has already built and launched a
        /// player. This is the same set of invariants where a content author meets them: a table
        /// with two terminal stages, or a reveal on no stage at all, is a season that plays wrong
        /// rather than a season that crashes, and the symptom is a run that quietly never ends.
        /// </summary>
        static void SeasonIsWhole()
        {
            StageDef[] stages = GameData.Stages;
            if (stages == null || stages.Length == 0)
            {
                Check("S1 the season declares its stages", false, "stages.json is missing or empty");
                return;
            }

            var faults = new List<string>();
            int terminal = 0;
            int closesGate = 0;
            int revealsPage = 0;
            int revealsVocation = 0;
            int finishesWall = -1;
            int gateStage = -1;

            for (int i = 0; i < stages.Length; i++)
            {
                StageDef stage = stages[i];
                if (stage == null || string.IsNullOrEmpty(stage.id))
                {
                    faults.Add("entry " + i + " has no id");
                    continue;
                }

                if (stage.day != i + 1) faults.Add(stage.id + " is day " + stage.day + " at position " + (i + 1));

                if (stage.terminal) { terminal++; if (i != stages.Length - 1) faults.Add(stage.id + " is terminal but not last"); }
                if (stage.closes_gate)
                {
                    closesGate++;
                    gateStage = i;
                    if (string.IsNullOrEmpty(stage.gate_segment)) faults.Add(stage.id + " closes the gate but names no segment");
                }
                if (stage.reveals_page) revealsPage++;
                if (stage.reveals_vocation) revealsVocation++;
                if (stage.finishes_wall) finishesWall = i;

                if (!string.IsNullOrEmpty(stage.contest) &&
                    (GameData.Contests == null || !GameData.Contests.ContainsKey(stage.contest)))
                {
                    faults.Add(stage.id + " names contest \"" + stage.contest + "\", which contest.json does not define");
                }

                if (!string.IsNullOrEmpty(stage.cutscene_node) && !GameData.Dialogue.ContainsKey(stage.cutscene_node))
                {
                    faults.Add(stage.id + " names gathering \"" + stage.cutscene_node + "\", which this locale does not carry");
                }

                VerseEntry vigilPage;
                if (!string.IsNullOrEmpty(stage.vigil_verse) &&
                    (!ScriptureService.TryGetVerse(stage.vigil_verse.Trim(), out vigilPage) || vigilPage == null || string.IsNullOrEmpty(vigilPage.text)))
                {
                    faults.Add(stage.id + " offers a vigil on \"" + stage.vigil_verse + "\", which verses.json does not carry");
                }

                if (!string.IsNullOrEmpty(stage.closing_node) && !GameData.Dialogue.ContainsKey(stage.closing_node))
                {
                    faults.Add(stage.id + " names closing node \"" + stage.closing_node + "\", which this locale does not carry");
                }

                if (stage.terminal && !string.IsNullOrEmpty(stage.vigil_verse))
                {
                    faults.Add(stage.id + " is terminal and offers a vigil, but a terminal stage has no night to keep it on");
                }

                if (!string.IsNullOrEmpty(stage.reward_item) && CharacterCatalog.Item(stage.reward_item) == null)
                {
                    faults.Add(stage.id + " features item \"" + stage.reward_item + "\", which the catalogue does not carry");
                }

                if (stage.map_anchor == null || stage.map_anchor.Length != 2) faults.Add(stage.id + " has no two-entry map_anchor");
                if (stage.map_focus == null || stage.map_focus.Length != 2) faults.Add(stage.id + " has no two-entry map_focus");
            }

            if (terminal != 1) faults.Add(terminal + " stage(s) declare terminal, expected exactly 1");
            if (closesGate != 1) faults.Add(closesGate + " stage(s) close the gate, expected exactly 1");
            if (revealsPage != 1) faults.Add(revealsPage + " stage(s) reveal the page, expected exactly 1");
            if (revealsVocation != 1) faults.Add(revealsVocation + " stage(s) reveal the vocation, expected exactly 1");
            if (finishesWall >= 0 && gateStage >= 0 && finishesWall >= gateStage)
            {
                faults.Add("the wall is finished at position " + (finishesWall + 1) +
                    ", which is not before the gate at " + (gateStage + 1));
            }

            Check("S1 every stage is reachable and whole", faults.Count == 0,
                stages.Length + " stage(s)" +
                (faults.Count == 0 ? ", every invariant holds" : " — " + string.Join("; ", faults)));
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

            // The first segment the data declares, not a name typed here. A wall that is re-cut into
            // different segments is a data change, and a check that hardcodes one of the old ids
            // fails for a reason that has nothing to do with the rule it is defending.
            string id = GameData.WallSegments != null && GameData.WallSegments.Length > 0
                ? GameData.WallSegments[0].id
                : null;

            if (string.IsNullOrEmpty(id) || !wall.Contains(id))
            {
                Check("05 the wall has a first segment", false,
                    "wall_segments.json declares " +
                    (GameData.WallSegments != null ? GameData.WallSegments.Length : 0) +
                    " segment(s) and the wall could not build \"" + (id ?? "nothing") + "\"");
                UnityEngine.Object.DestroyImmediate(wall.gameObject);
                return;
            }

            for (int i = 0; i < 200 && wall.StageOf(id) < 2; i++) wall.ApplyWork(id, 1);
            int reached = wall.StageOf(id);
            Check("05 work advances stages", reached >= 2, id + " reached stage " + reached);

            wall.ApplyWork(id, 1); // partial progress into the next stage
            wall.DamageSegment(id);
            int afterDamage = wall.StageOf(id);
            Check("09 damage never regresses a finished stage", afterDamage >= reached,
                "stage " + reached + " before damage, " + afterDamage + " after");

            for (int i = 0; i < 500 && !wall.IsComplete(id); i++) wall.ApplyWork(id, 1);
            Check("05 segment completes", wall.IsComplete(id), id + " stage " + wall.StageOf(id));

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
                ThreeDaySaveMigratesForward();
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

            // A stage in the middle of the season rather than the second one. A round trip that only
            // ever wrote a low day would keep passing on a build whose day clamp had a ceiling set
            // against the old three-day season — the save would come back clamped, the assertion
            // would compare it against the same small number, and a player resuming at stage seven
            // would silently be handed stage three.
            int day = MiddleStageDay();
            original.day = day;
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
                      && loaded.day == day
                      && loaded.rubble == 7
                      && loaded.HasFlag(GameFlags.WatchPostedD1)
                      && loaded.Counter("talked_hananias") == 2
                      && loaded.segments.Count == original.segments.Count
                      && loaded.segments[0].stage == 3;

            Check("05 save round-trips", ok,
                loaded == null ? "load returned null"
                    : "day=" + loaded.day + " (wrote " + day + ") rubble=" + loaded.rubble +
                      " flag=" + loaded.HasFlag(GameFlags.WatchPostedD1) +
                      " stage=" + (loaded.segments.Count > 0 ? loaded.segments[0].stage : -1));
        }

        /// <summary>A stage past the middle of the season, or the second day if the table is unreadable.</summary>
        static int MiddleStageDay()
        {
            StageDef[] stages = GameData.Stages;
            return stages != null && stages.Length > 2 ? (stages.Length / 2) + 1 : 2;
        }

        /// <summary>
        /// S2 — a save written by the three-day build comes forward without losing anything.
        ///
        /// This gets a criterion of its own because the migration is the single piece of the season
        /// change that can silently destroy a run somebody has already played. Everything else in
        /// this plan fails loudly or not at all; a migration that clears a flag, walks a day
        /// backwards or resets a course does its damage once, quietly, on somebody else's machine.
        ///
        /// The expected day is written here as a LITERAL and deliberately not derived from the stage
        /// table. A migration step is a fixed historical mapping from one named schema to the next:
        /// deriving the answer from live data would mean this check agreed with any future edit that
        /// made the mapping data-driven, which is precisely the change that would break it.
        /// SaveSystem's own consistency check warns if the table moves the trial out from under the
        /// literals, so the two guards cover each other.
        /// </summary>
        static void ThreeDaySaveMigratesForward()
        {
            const int LegacySchemaVersion = 2;
            const int LegacyFinalDay = 3;

            // Where the migration lands a finished three-day run: the stage AFTER the trial, because
            // that trial was already fought and replaying it would hand the player a day they have
            // already seen. See SaveSystem.MigrateToNineStageSeason for the whole argument.
            const int ExpectedDayWhenTheTrialWasFought = 7;

            GameState before = GameState.NewGame();
            before.schemaVersion = LegacySchemaVersion;
            before.day = LegacyFinalDay;
            before.SetFlag(GameFlags.VocationRevealed);
            before.SetFlag(GameFlags.ContestResolved);
            before.SetFlag(GameFlags.WatchPostedD1);
            before.SetFlag(GameFlags.WatchPostedD2);
            before.SetFlag(GameFlags.PageShown);
            before.Bump("talked_hananias", 3);

            var stagesBefore = new Dictionary<string, int>();
            for (int i = 0; i < before.segments.Count; i++)
            {
                WallSegmentState segment = before.segments[i];
                if (segment == null || string.IsNullOrEmpty(segment.id)) continue;
                segment.stage = i == 0 ? 4 : 2;
                stagesBefore[segment.id] = segment.stage;
            }

            var flagsBefore = new List<string>(before.flags);

            SaveSystem.Save(before);
            GameState after = SaveSystem.Load();

            if (after == null)
            {
                Check("S2 a three-day save migrates forward", false, "load returned null");
                return;
            }

            var faults = new List<string>();

            if (after.schemaVersion != GameState.CurrentSchemaVersion)
            {
                faults.Add("schemaVersion is " + after.schemaVersion +
                    ", expected " + GameState.CurrentSchemaVersion);
            }

            if (after.day != ExpectedDayWhenTheTrialWasFought)
            {
                faults.Add("day is " + after.day + ", expected " + ExpectedDayWhenTheTrialWasFought +
                    " (the stage after the trial, because the trial had already been fought)");
            }

            if (after.day < before.day)
            {
                faults.Add("the season walked backwards, from day " + LegacyFinalDay + " to " + after.day);
            }

            foreach (string flag in flagsBefore)
            {
                if (!after.HasFlag(flag)) faults.Add("the flag \"" + flag + "\" was cleared");
            }

            if (!after.HasFlag(GameFlags.ContestResolvedFor("raid")))
            {
                faults.Add("the legacy contest_resolved was not raised to its keyed name, so the raid replays");
            }

            foreach (KeyValuePair<string, int> pair in stagesBefore)
            {
                WallSegmentState segment = after.Segment(pair.Key);
                if (segment == null)
                {
                    faults.Add("the segment \"" + pair.Key + "\" is gone");
                }
                else if (segment.stage < pair.Value)
                {
                    faults.Add("the segment \"" + pair.Key + "\" fell from course " +
                        pair.Value + " to " + segment.stage);
                }
            }

            if (string.IsNullOrEmpty(after.seasonId)) faults.Add("the save carries no seasonId");

            Check("S2 a three-day save migrates forward", faults.Count == 0,
                faults.Count == 0
                    ? "day " + LegacyFinalDay + " -> " + after.day + ", schema " + LegacySchemaVersion +
                      " -> " + after.schemaVersion + ", " + flagsBefore.Count +
                      " flag(s) kept, no course lowered"
                    : string.Join("; ", faults));
        }

        /// <summary>
        /// 07 + 08 + 09 — the days before a fight decide it, the Page unlocks the strong move, and
        /// losing is not a game over.
        ///
        /// RUN ONCE PER CONTEST THE SEASON DECLARES, not once at the literal day the only contest
        /// used to be on. The preparation flags are read per stage too, from
        /// <see cref="GameFlags.WatchPostedForDay"/> against that stage's own previous night, which
        /// is the flag the contest actually reads — a check that set <c>watch_posted_d2</c> by name
        /// went on passing while the boss on stage eight was being tuned against a night that had
        /// nothing to do with it, because a flag for a night nobody played is simply absent and the
        /// absence reads as "no guard stood".
        /// </summary>
        static void ContestRules()
        {
            var fought = new List<string>();

            foreach (StageDef stage in GameData.Stages ?? Array.Empty<StageDef>())
            {
                if (stage == null || string.IsNullOrEmpty(stage.contest)) continue;
                if (stage.type != StageTypes.Battle && stage.type != StageTypes.Boss) continue;

                fought.Add(stage.contest);
                ContestPreparationDecidesTheFight(stage);
            }

            Check("07 the season has a contest to check", fought.Count > 0,
                fought.Count > 0 ? "checked [" + string.Join(", ", fought) + "]"
                    : "no stage declares a battle or a boss");

            // The trumpet flag has to survive the trip through JSON in both directions: a default
            // that failed to apply would silence the assault, and a field the loader ignored would
            // sound the horn at the mockery. Acceptance cannot hear audio, so it reads the rows.
            var sounding = new List<string>();
            var silent = new List<string>();
            foreach (KeyValuePair<string, ContestConfig> pair in GameData.Contests ?? new Dictionary<string, ContestConfig>())
            {
                if (pair.Value == null) continue;
                (pair.Value.trumpet ? sounding : silent).Add(pair.Key);
            }

            Check("07 the trumpet sounds where the row says and nowhere else",
                sounding.Count > 0 && silent.Count > 0,
                "sounding [" + string.Join(", ", sounding) + "], silent [" + string.Join(", ", silent) + "]");

            ThePageGatesItsMove();

            Check("09 no game over exists", !HasGameOverSymbol(),
                "no type or member named GameOver in the assembly");
        }

        /// <summary>Two runs into the same fight, one prepared and one not, differing only in the flags.</summary>
        static void ContestPreparationDecidesTheFight(StageDef stage)
        {
            string previousNight = GameFlags.WatchPostedForDay(stage.day - 1);

            MoraleContest harsh = BuildContestAt(stage, "HarnessContestHarsh", delegate(GameState state)
            {
                // No guard on the night before, and the invitation down to the plain accepted.
                state.SetFlag(GameFlags.AcceptedInvite);
            });

            MoraleContest kind = BuildContestAt(stage, "HarnessContestKind", delegate(GameState state)
            {
                state.SetFlag(previousNight);
                state.SetFlag(GameFlags.RefusedInvite);
            });

            Check("07 the days before \"" + stage.contest + "\" decide it",
                harsh != null && kind != null && harsh.EnemyResolveMax > kind.EnemyResolveMax,
                harsh == null || kind == null ? "the contest could not be built"
                    : "enemy resolve " + harsh.EnemyResolveMax + " (neglected) vs " +
                      kind.EnemyResolveMax + " (prepared), reading " + previousNight);

            if (harsh != null) UnityEngine.Object.DestroyImmediate(harsh.gameObject);
            if (kind != null) UnityEngine.Object.DestroyImmediate(kind.gameObject);
        }

        /// <summary>
        /// S4 — the gate is earned, never handed over. The last stage takes its hold only once the
        /// segment it hangs the doors on is standing; until then the last day is a working day —
        /// the mat works, a night on it puts every pile back without moving the date — and the
        /// morning says how many courses are left. The negative half lives here on purpose: the
        /// end-to-end run earns the gate and can only prove the happy path, and the failure this
        /// guards against — a season that ends on a wall the player never built — is invisible on
        /// screen, because the ending looks identical either way.
        /// </summary>
        static void TheGateIsEarned()
        {
            StageDef terminal = null;
            foreach (StageDef stage in GameData.Stages ?? new StageDef[0])
            {
                if (stage != null && stage.terminal) { terminal = stage; break; }
            }

            if (terminal == null || !terminal.closes_gate || string.IsNullOrEmpty(terminal.gate_segment))
            {
                Check("S4 the season declares a gate to earn", false,
                    terminal == null ? "no stage declares terminal"
                        : "the terminal stage closes no gate or names no segment");
                return;
            }

            GameState state;
            WallSystem wall = NewWallSystem(out state);
            state.day = terminal.day;

            var cycleHost = new GameObject("HarnessGateCycle");
            var cycle = cycleHost.AddComponent<DayCycle>();
            cycle.enabled = false;
            ServiceLocator.Register(cycle);
            ServiceLocator.Register(wall);

            var faults = new List<string>();
            string gate = terminal.gate_segment;

            if (!wall.Contains(gate))
            {
                faults.Add("the wall has no segment \"" + gate + "\"");
            }

            if (StageDirector.GateIsEarned(terminal))
            {
                faults.Add("a bare segment counts as earned");
            }

            int left = StageDirector.GateCoursesLeft(terminal);
            if (left != WallSystem.StagesPerSegment)
            {
                faults.Add("a bare segment reports " + left + " course(s) left, expected " + WallSystem.StagesPerSegment);
            }

            if (!cycle.CanRest)
            {
                faults.Add("the mat is dead on the last day before the gate is earned");
            }

            // A night on the last day: the date stays, the piles come back.
            state.counters[RubblePile.StoneTakenPrefix + "0"] = state.day;
            state.counters[RubblePile.TimberTakenPrefix + "2"] = state.day;
            cycle.EndDay(6, 6);
            if (state.day != terminal.day)
            {
                faults.Add("a night on the last day moved the date to " + state.day);
            }

            if (state.Counter(RubblePile.StoneTakenPrefix + "0") != 0 || state.Counter(RubblePile.TimberTakenPrefix + "2") != 0)
            {
                faults.Add("the piles stayed empty after a night on the last day");
            }

            if (state.Counter(DayCycle.ExtraMorningsCounter) != 1)
            {
                faults.Add("the extra morning was not counted (" + state.Counter(DayCycle.ExtraMorningsCounter) + ")");
            }

            NightSummary morning = NightSummary.FromState(state, state.day);
            if (morning.gateCoursesLeft != WallSystem.StagesPerSegment)
            {
                faults.Add("the morning report carries " + morning.gateCoursesLeft + " course(s) left, expected " + WallSystem.StagesPerSegment);
            }

            for (int i = 0; i < 500 && !wall.IsComplete(gate); i++) wall.ApplyWork(gate, 1);
            if (!StageDirector.GateIsEarned(terminal))
            {
                faults.Add("a standing segment does not count as earned");
            }

            if (StageDirector.GateCoursesLeft(terminal) != 0)
            {
                faults.Add("a standing segment still reports courses left");
            }

            cycle.HoldDusk(DayCycle.HoldFinalDay);
            if (cycle.CanRest)
            {
                faults.Add("the mat still works while the dedication holds the day");
            }

            Check("S4 the gate is earned, never handed over", faults.Count == 0,
                faults.Count == 0
                    ? "\"" + gate + "\" waits at course 0 with the mat alive and the piles refilled by a night, and counts once it stands"
                    : string.Join("; ", faults));

            UnityEngine.Object.DestroyImmediate(cycleHost);
            UnityEngine.Object.DestroyImmediate(wall.gameObject);
        }

        /// S5 — what a day's choice promised, the night delivers, with the number the morning
        /// prints. Three promises: the hour given to the Tekoites (stage 3) comes back on the
        /// player's own segment the next morning, once; the cleared path (stage 5) doubles that
        /// night's crew, once; and from the guard stage every two people land a unit instead of
        /// every three. None of these run in the end-to-end harness, which never talks to a
        /// resident after the first day, so this is the only place they execute.
        /// </summary>
        static void TheNightKeepsTheDaysPromises()
        {
            GameState state;
            WallSystem wall = NewWallSystem(out state);
            var cycleHost = new GameObject("HarnessNightCycle");
            var cycle = cycleHost.AddComponent<DayCycle>();
            cycle.enabled = false;
            ServiceLocator.Register(cycle);
            ServiceLocator.Register(wall);

            var faults = new List<string>();
            string yours = wall.PrimaryExposedSegmentId;

            // Half the crew on the work, before the guard: 6 / 3 = 2, doubled by the path.
            int plain = DayCycle.NightWorkUnits(6, DayCycle.HalfAndHalfStage - 1);
            int armed = DayCycle.NightWorkUnits(6, DayCycle.HalfAndHalfStage);
            if (plain != 2 || armed != 3)
            {
                faults.Add("six workers make " + plain + " unit(s) before the guard and " + armed + " from it, expected 2 and 3");
            }

            state.day = 5;
            state.SetFlag(GameFlags.PathCleared);
            state.SetFlag(GameFlags.TekoaHelped);
            cycle.EndDay(6, 6);

            int built = state.Counter(DayCycle.NightWorkCounter);
            if (built != plain * 2)
            {
                faults.Add("the cleared path made the night land " + built + " unit(s), expected " + plain * 2);
            }

            if (state.Counter(DayCycle.NightPathClearedCounter) != 1)
            {
                faults.Add("the morning was not told the path was cleared");
            }

            if (state.Counter(DayCycle.NightTekoaReturnedCounter) != 1)
            {
                faults.Add("the Tekoites returned " + state.Counter(DayCycle.NightTekoaReturnedCounter) + " unit(s), expected 1");
            }

            WallSegmentState mine = state.Segment(yours);
            int onYours = mine != null ? mine.workInStage + mine.stage : 0;
            if (onYours < 1)
            {
                faults.Add("nothing landed on \"" + yours + "\" the morning after the Tekoites' hour");
            }

            NightSummary morning = NightSummary.FromState(state, state.day);
            if (!morning.pathCleared || morning.tekoaReturned != 1)
            {
                faults.Add("the summary reads pathCleared=" + morning.pathCleared + " tekoa=" + morning.tekoaReturned);
            }

            // Both promises are kept once. The next night is an ordinary one.
            cycle.EndDay(6, 6);
            if (state.Counter(DayCycle.NightWorkCounter) != plain || state.Counter(DayCycle.NightPathClearedCounter) != 0
                || state.Counter(DayCycle.NightTekoaReturnedCounter) != 0)
            {
                faults.Add("the second night still carried the promises: work=" + state.Counter(DayCycle.NightWorkCounter)
                    + " path=" + state.Counter(DayCycle.NightPathClearedCounter)
                    + " tekoa=" + state.Counter(DayCycle.NightTekoaReturnedCounter));
            }

            Check("S5 the night keeps the day's promises", faults.Count == 0,
                faults.Count == 0
                    ? "cleared path doubled 2 to 4, the Tekoites' hour landed on \"" + yours + "\", the armed night lands 3, and none of it twice"
                    : string.Join("; ", faults));

            UnityEngine.Object.DestroyImmediate(cycleHost);
            UnityEngine.Object.DestroyImmediate(wall.gameObject);
        }

        /// <summary>
        /// S3 — a move that a FLAG unlocks is shut without the flag, open with it, and something in
        /// the content still raises it.
        ///
        /// Criterion 08 proves this for the move A Página unlocks, and stops there because in the
        /// three-day build that was the only gated move there was. The season added a second kind:
        /// <c>keep_working</c> exists only for a player who refused the invitation six stages
        /// earlier. Nothing asserted that gate, and the way it fails is the failure this project
        /// keeps meeting — not a crash, but a move that quietly never appears, in a fight most runs
        /// reach having accepted the invitation, so the absence looks exactly like the rule working.
        ///
        /// <b>Both halves are needed, and the second is the one worth the trouble.</b> The gate
        /// itself is a two-line read of <see cref="MoraleContest.IsMoveAvailable"/>. But a gate that
        /// works perfectly on a flag no content grants is correct code nothing calls: the move would
        /// be unreachable in a played game while every rule about it passed. So the flag is also
        /// traced back to a dialogue node that raises it — through <c>grants.set_flag</c> on the
        /// node or on any of its branches, which is how the refusal actually writes it.
        ///
        /// Nothing here names <c>letters</c>, <c>keep_working</c> or <c>refused_invite</c>. It reads
        /// the contest table the way criterion 06 reads the nights: whatever moves declare a flag
        /// gate, in whatever contest, are the moves this checks. A move gated by BOTH the page and a
        /// flag is reported and skipped rather than half-tested — the page half needs a fight played
        /// to turn 2, which is criterion 08's job, not this one's.
        /// </summary>
        static void FlagGatedMovesAreShut(IReadOnlyDictionary<string, DialogueNode> sourceDialogue)
        {
            if (GameData.Contests == null || GameData.Stages == null)
            {
                Check("S3 a flag-gated move is shut without its flag", false,
                    "no contest table or no stage table to read");
                return;
            }

            var faults = new List<string>();
            var checkedMoves = new List<string>();
            var skipped = new List<string>();

            foreach (KeyValuePair<string, ContestConfig> pair in GameData.Contests)
            {
                ContestConfig config = pair.Value;
                if (config == null || config.moves == null) continue;

                foreach (ContestMoveDef move in config.moves)
                {
                    if (move == null || string.IsNullOrEmpty(move.unlocked_by_flag)) continue;

                    if (move.unlocked_by_page)
                    {
                        skipped.Add(pair.Key + "/" + move.id + " (page and flag; 08 owns the page half)");
                        continue;
                    }

                    StageDef stage = StageFighting(pair.Key);
                    if (stage == null)
                    {
                        faults.Add("no stage declares the contest \"" + pair.Key + "\", so \"" +
                            move.id + "\" can never be reached");
                        continue;
                    }

                    string flag = move.unlocked_by_flag;

                    // ONE CONTEST AT A TIME, asked and thrown away before the next is built.
                    // IsMoveAvailable reads the state out of the ServiceLocator when it is called,
                    // not when the contest was built, and BuildContestAt clears the registry — so
                    // holding two contests and asking them afterwards asks the same state twice,
                    // whichever was built last. Criterion 07 can hold both because it compares
                    // EnemyResolveMax, which is fixed at Begin(). This one cannot.
                    bool openWithout = MoveIsOffered(stage, move.id, null);
                    bool openWith = MoveIsOffered(stage, move.id, delegate(GameState state)
                    {
                        state.SetFlag(flag);
                    });

                    if (openWithout)
                    {
                        faults.Add("\"" + move.id + "\" is offered in \"" + pair.Key +
                            "\" without \"" + flag + "\"");
                    }

                    if (!openWith)
                    {
                        faults.Add("\"" + move.id + "\" stays shut in \"" + pair.Key +
                            "\" even with \"" + flag + "\" set, so the choice that earns it pays nothing");
                    }

                    string granting = NodeGranting(sourceDialogue, flag);
                    if (granting == null)
                    {
                        faults.Add("no dialogue node grants \"" + flag + "\", so \"" + move.id +
                            "\" is correct code nothing calls");
                    }

                    checkedMoves.Add(pair.Key + "/" + move.id + " <- " + flag +
                        (granting != null ? " (" + granting + ")" : ""));
                }
            }

            string note = skipped.Count > 0 ? "; skipped [" + string.Join(", ", skipped.ToArray()) + "]" : "";

            if (checkedMoves.Count == 0 && faults.Count == 0)
            {
                Check("S3 a flag-gated move is shut without its flag", true,
                    "no contest declares a flag-gated move" + note);
                return;
            }

            Check("S3 a flag-gated move is shut without its flag", faults.Count == 0,
                faults.Count == 0
                    ? "checked [" + string.Join(", ", checkedMoves.ToArray()) + "]" + note
                    : string.Join("; ", faults.ToArray()));
        }

        /// <summary>
        /// Whether the move is offered in a fresh run of the stage's contest, prepared as asked.
        /// Built, asked and destroyed inside one call, because the answer depends on registry state
        /// that the next build would replace.
        /// </summary>
        static bool MoveIsOffered(StageDef stage, string moveId, Action<GameState> prepare)
        {
            MoraleContest contest = BuildContestAt(stage, "HarnessMoveGate", prepare);
            if (contest == null) return false;

            bool offered = contest.IsMoveAvailable(moveId);
            UnityEngine.Object.DestroyImmediate(contest.gameObject);
            return offered;
        }

        /// <summary>
        /// 15 — a run with no character is never offered a launch screen to press through.
        ///
        /// §04 decides that the opening is a cutscene and not a menu: the first thing a NEW player
        /// sees has to be a city rather than a form. The launch screen added for returning players
        /// is the obvious way to break that rule by accident — one condition inverted and every new
        /// player meets a Play button before they meet the game.
        ///
        /// Asserted on the predicate rather than by composing the screen, and that is a real limit
        /// worth stating: this proves TitleScreen.HasCharacter answers correctly for a fresh state
        /// and for a named one. It does not prove GameScene calls it, which is what the e2e's cold
        /// run covers — a Play button on the opening would fail there, in the step that asserts a
        /// cold run gets a splash and no button.
        /// </summary>
        static void TheOpeningIsNotAMenu()
        {
            GameState fresh = GameState.NewGame();
            bool freshOffered = SheepGate.UI.TitleScreen.HasCharacter(fresh);

            GameState named = GameState.NewGame();
            named.playerName = "Hanani";
            bool namedOffered = SheepGate.UI.TitleScreen.HasCharacter(named);

            var faults = new List<string>();
            if (freshOffered) faults.Add("a new run would be shown the returning player's screen");
            if (!namedOffered) faults.Add("a run with a name would not be shown its own character");

            Check("15 the opening is a cutscene and not a menu", faults.Count == 0,
                faults.Count == 0
                    ? "no character -> splash only; named -> figure and Play"
                    : string.Join("; ", faults.ToArray()));
        }

        /// <summary>The first stage that fights the named contest, or null if none does.</summary>
        static StageDef StageFighting(string contestId)
        {
            StageDef[] stages = GameData.Stages;
            if (stages == null) return null;

            foreach (StageDef stage in stages)
            {
                if (stage != null && stage.contest == contestId) return stage;
            }

            return null;
        }

        /// <summary>
        /// Id of a dialogue node that raises the flag, on the node itself or on one of its branches,
        /// or null when nothing in the content does. The branch case is the one that matters: a
        /// refusal is a choice, and the flag rides on the choice rather than on the node.
        /// </summary>
        static string NodeGranting(IReadOnlyDictionary<string, DialogueNode> dialogue, string flag)
        {
            if (dialogue == null || string.IsNullOrEmpty(flag)) return null;

            foreach (KeyValuePair<string, DialogueNode> pair in dialogue)
            {
                DialogueNode node = pair.Value;
                if (node == null) continue;

                if (node.grants != null && node.grants.set_flag == flag) return pair.Key;

                if (node.choices == null) continue;
                foreach (DialogueChoice choice in node.choices)
                {
                    if (choice != null && choice.grants != null && choice.grants.set_flag == flag)
                    {
                        return pair.Key + "/" + choice.id;
                    }
                }
            }

            return null;
        }

        static MoraleContest BuildContestAt(StageDef stage, string hostName, Action<GameState> prepare)
        {
            GameState state = GameState.NewGame();
            state.day = stage.day;
            if (prepare != null) prepare(state);

            ServiceLocator.Clear();
            ServiceLocator.Register(state);

            var host = new GameObject(hostName);
            host.AddComponent<WallSystem>().Build(null);
            var contest = host.AddComponent<MoraleContest>();
            contest.Begin(stage.contest);
            return contest;
        }

        /// <summary>
        /// 08 — the reveal lands inside the fight that carries it, and the move behind it is shut
        /// until it does.
        ///
        /// Asserted against the DATA rather than against <c>MoraleContest.PageTurn</c>. That
        /// constant is a self-heal for a contest that names a passage and forgets its turn, so
        /// checking it proves the recovery value is what it always was and says nothing at all about
        /// whether the shipped season's reveal can happen. What has to be true is that the stage
        /// declaring the reveal names a contest whose page turn falls inside its own turn limit and
        /// whose passage resolves — a page scheduled for turn nine of an eight-turn fight is a
        /// season whose one payoff silently never arrives.
        /// </summary>
        static void ThePageGatesItsMove()
        {
            StageDef revealing = null;
            foreach (StageDef stage in GameData.Stages ?? Array.Empty<StageDef>())
            {
                if (stage != null && stage.reveals_page) revealing = stage;
            }

            if (revealing == null)
            {
                Check("08 one stage carries the reveal", false, "no stage declares reveals_page");
                return;
            }

            ContestConfig config = null;
            if (GameData.Contests != null && !string.IsNullOrEmpty(revealing.contest))
            {
                GameData.Contests.TryGetValue(revealing.contest, out config);
            }

            if (config == null)
            {
                Check("08 the reveal has a fight to happen in", false,
                    "stage \"" + revealing.id + "\" declares the reveal but names contest \"" +
                    (revealing.contest ?? "nothing") + "\", which contest.json does not define");
                return;
            }

            Check("08 the Page arrives inside the fight that carries it",
                config.page_turn > 0 && config.page_turn <= config.turn_limit,
                "\"" + config.id + "\" puts the page on turn " + config.page_turn +
                " of a fight that lasts " + config.turn_limit);

            VerseEntry verse;
            bool resolves = !string.IsNullOrEmpty(config.page_verse)
                            && ScriptureService.TryGetVerse(config.page_verse, out verse);
            Check("08 the Page's passage resolves", resolves,
                "\"" + config.id + "\" shows " + (config.page_verse ?? "no reference"));

            ContestMoveDef gated = null;
            foreach (ContestMoveDef move in config.moves ?? Array.Empty<ContestMoveDef>())
            {
                if (move != null && move.unlocked_by_page) gated = move;
            }

            if (gated == null)
            {
                Check("08 the Page unlocks something", false,
                    "no move in \"" + config.id + "\" declares unlocked_by_page, so the reveal changes nothing");
                return;
            }

            MoraleContest contest = BuildContestAt(revealing, "HarnessContestPage", null);
            Check("08 the move the Page unlocks is shut before it",
                contest != null && !contest.IsMoveAvailable(gated.id),
                contest == null ? "the contest could not be built"
                    : gated.id + " available at turn " + contest.Turn + ": " +
                      contest.IsMoveAvailable(gated.id));

            if (contest != null) UnityEngine.Object.DestroyImmediate(contest.gameObject);
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
            StageDef[] stages = GameData.Stages;
            if (stages == null || stages.Length == 0)
            {
                Check("06 the season has nights to resolve", false, "stages.json is missing or empty");
                return;
            }

            var damageFaults = new List<string>();
            var flagFaults = new List<string>();
            var quiet = new List<string>();
            int nights = 0;

            // EVERY NIGHT, not only the first. The rule used to be proven on night one and assumed
            // for the rest, which is exactly how the write side came to record the watch on two
            // nights out of nine while every check about it passed: night one was the one being
            // asked. A night that damages nothing is checked here too, because night_threat is a
            // graft onto the one and only end-of-day path and the way it fails is by resolving a
            // night that should have been quiet — or by turning the whole season quiet.
            foreach (StageDef stage in stages)
            {
                if (stage == null || stage.terminal) continue;

                nights++;
                string damagedWithWatch;
                string damagedWithout;
                bool flagWithWatch = ResolveNight(stage, true, out damagedWithWatch);
                bool flagWithout = ResolveNight(stage, false, out damagedWithout);

                if (damagedWithWatch != null)
                {
                    damageFaults.Add("night " + stage.day + " damaged \"" + damagedWithWatch + "\" with a watch posted");
                }

                if (stage.night_threat)
                {
                    if (damagedWithout == null)
                    {
                        damageFaults.Add("night " + stage.day + " threatens the wall but damaged nothing unwatched");
                    }
                }
                else
                {
                    quiet.Add(stage.id);
                    if (damagedWithout != null)
                    {
                        damageFaults.Add("night " + stage.day + " declares no threat but damaged \"" +
                            damagedWithout + "\"");
                    }
                }

                if (!flagWithWatch || flagWithout)
                {
                    flagFaults.Add("night " + stage.day + ": " + GameFlags.WatchPostedForDay(stage.day) +
                        " = " + flagWithWatch + " with a watch, " + flagWithout + " without");
                }
            }

            Check("06 only the unwatched night damages the wall", nights > 0 && damageFaults.Count == 0,
                nights + " night(s) resolved, " + quiet.Count + " of them declaring no threat [" +
                string.Join(", ", quiet) + "]" +
                (damageFaults.Count > 0 ? " — " + string.Join("; ", damageFaults) : ""));

            Check("06 the watch is recorded on every night it is posted",
                nights > 0 && flagFaults.Count == 0,
                flagFaults.Count == 0 ? nights + " night(s) recorded their watch correctly"
                    : string.Join("; ", flagFaults));
        }

        // 16 — the vigil costs the night's work, returns a page, and moves nothing else.
        //
        // Rule 8 in three assertions per night. With a watch posted, a vigil lands zero work where
        // an ordinary night lands some, and it is the only thing that writes the vigil flag; with
        // the same watch it damages exactly what the ordinary night damages (nothing), because it
        // is not a power. Without a watch, a vigil on a threat night still loses the wall — prayer
        // alone loses the wall, in the rule's own words. And the page it returns resolves in
        // verses.json with its chapter behind it, or the morning would show a marker where the
        // whole return on the night's work should be.
        static void TheVigilCostsTheNight()
        {
            StageDef[] stages = GameData.Stages;
            if (stages == null || stages.Length == 0)
            {
                Check("16 the season has nights to keep a vigil on", false, "stages.json is missing or empty");
                return;
            }

            var faults = new List<string>();
            var offered = new List<string>();
            var silent = new List<string>();

            foreach (StageDef stage in stages)
            {
                if (stage == null || stage.terminal) continue;

                if (!DayCycle.VigilOffers(stage.day))
                {
                    silent.Add(stage.id);

                    // Asking for a vigil where none is offered is an ordinary night, not a night
                    // that cost the work and showed nothing.
                    NightOutcome forced = ResolveNightWithVigil(stage, true, true);
                    if (forced.vigilFlag || forced.work <= 0)
                    {
                        faults.Add("night " + stage.day + " offers no page but a forced vigil " +
                            (forced.vigilFlag ? "wrote the flag" : "landed no work"));
                    }
                    continue;
                }

                offered.Add(stage.id);

                VerseEntry verse;
                string reference = stage.vigil_verse.Trim();
                if (!ScriptureService.TryGetVerse(reference, out verse) || verse == null || string.IsNullOrEmpty(verse.text))
                {
                    faults.Add("night " + stage.day + " returns " + reference + ", which has no text");
                }

                ChapterEntry chapter;
                string chapterRef = ScriptureService.ChapterRefOf(reference);
                if (string.IsNullOrEmpty(chapterRef) || !ScriptureService.TryGetChapter(chapterRef, out chapter) || chapter == null)
                {
                    faults.Add("night " + stage.day + " returns " + reference + ", whose chapter " + (chapterRef ?? "?") + " the reader cannot open");
                }

                NightOutcome vigil = ResolveNightWithVigil(stage, true, true);
                NightOutcome plain = ResolveNightWithVigil(stage, true, false);
                NightOutcome alone = ResolveNightWithVigil(stage, false, true);

                if (vigil.work != 0)
                {
                    faults.Add("night " + stage.day + ": a vigil still landed " + vigil.work + " of work");
                }

                if (plain.work <= 0)
                {
                    faults.Add("night " + stage.day + ": an ordinary night landed no work, so the vigil's price is invisible");
                }

                if (!vigil.vigilFlag || plain.vigilFlag)
                {
                    faults.Add("night " + stage.day + ": " + GameFlags.VigilKeptForDay(stage.day) +
                        " = " + vigil.vigilFlag + " with a vigil, " + plain.vigilFlag + " without");
                }

                if (vigil.damaged != plain.damaged)
                {
                    faults.Add("night " + stage.day + ": the vigil changed the damage (" +
                        (vigil.damaged ?? "none") + " vs " + (plain.damaged ?? "none") + ")");
                }

                if (!vigil.watchFlag)
                {
                    faults.Add("night " + stage.day + ": the vigil lost the watch that was posted beside it");
                }

                if (stage.night_threat && alone.damaged == null)
                {
                    faults.Add("night " + stage.day + ": a vigil with no watch kept the wall whole — prayer alone must lose the wall");
                }

                if (!stage.night_threat && alone.damaged != null)
                {
                    faults.Add("night " + stage.day + " declares no threat but a vigil with no watch damaged \"" + alone.damaged + "\"");
                }
            }

            Check("16 the vigil costs the night and returns the page",
                offered.Count > 0 && faults.Count == 0,
                offered.Count + " night(s) offer a vigil [" + string.Join(", ", offered) + "]" +
                (silent.Count > 0 ? ", " + silent.Count + " offer none [" + string.Join(", ", silent) + "]" : "") +
                (faults.Count > 0 ? " — " + string.Join("; ", faults) : ""));
        }

        struct NightOutcome
        {
            public int work;
            public string damaged;
            public bool vigilFlag;
            public bool watchFlag;
        }

        /// <summary>
        /// One night on a fresh run standing on this stage, resolved without the fade, with the
        /// split fixed at six and six (a watch) or twelve and none (no watch).
        /// </summary>
        static NightOutcome ResolveNightWithVigil(StageDef stage, bool postWatch, bool vigil)
        {
            GameState state = GameState.NewGame();
            state.day = stage.day;
            ServiceLocator.Clear();
            ServiceLocator.Register(state);

            var host = new GameObject("HarnessVigil");
            host.AddComponent<WallSystem>().Build(null);
            var cycle = host.AddComponent<DayCycle>();
            cycle.enabled = false;
            cycle.EndDay(postWatch ? 6 : 12, postWatch ? 6 : 0, vigil);

            var outcome = new NightOutcome
            {
                work = cycle.LastNightWorkApplied,
                damaged = cycle.LastNightDamagedSegment,
                vigilFlag = state.HasFlag(GameFlags.VigilKeptForDay(stage.day)),
                watchFlag = state.HasFlag(GameFlags.WatchPostedForDay(stage.day))
            };

            UnityEngine.Object.DestroyImmediate(host);
            return outcome;
        }

        // 17 — a costed move is priced in the day: shut below its price, spends exactly its price,
        // and is offered once. The famine's grammar is deliberately losing a resource, and a move
        // that said it cost the day and did not would be the discount rule 18 forbids in another
        // coat. Driven through ApplyPlayerMove by reflection, the way the harness already reaches
        // what the fight keeps private, because the turn loop is a coroutine the editor never runs.
        /// <summary>
        /// 18 — the codex names every gate of the chapter with a verse that resolves, in both
        /// languages, and marks exactly one of them as the player's: the one whose segment the
        /// terminal stage closes.
        ///
        /// Data-level on purpose. The panel is a list read off gates.json, and what can go wrong
        /// with it is content: a verse the corpus does not carry (the card shows the missing
        /// marker), a gate without its strings in one locale (a bare id on screen), or a segment
        /// that names nothing the wall builds (the mark lands nowhere). The e2e opens the real
        /// panel and counts the cards; this proves what the cards would say.
        /// </summary>
        static void TheCodexNamesEveryGate()
        {
            GateDef[] gates = GameData.Gates;
            if (gates == null || gates.Length == 0)
            {
                Check("18 the codex names every gate", false, "gates.json is missing or empty");
                return;
            }

            var faults = new List<string>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var segments = new HashSet<string>(StringComparer.Ordinal);
            foreach (WallSegmentDef segment in GameData.WallSegments ?? Array.Empty<WallSegmentDef>())
            {
                if (segment != null && !string.IsNullOrEmpty(segment.id)) segments.Add(segment.id);
            }

            string playersSegment = null;
            foreach (StageDef stage in GameData.Stages ?? Array.Empty<StageDef>())
            {
                if (stage != null && stage.terminal && !string.IsNullOrEmpty(stage.gate_segment))
                {
                    playersSegment = stage.gate_segment;
                    break;
                }
            }

            int marked = 0;
            foreach (GateDef gate in gates)
            {
                if (gate == null || string.IsNullOrEmpty(gate.id))
                {
                    faults.Add("a gate row has no id");
                    continue;
                }

                if (!ids.Add(gate.id)) faults.Add(gate.id + " is listed twice");
                if (string.IsNullOrEmpty(gate.display)) faults.Add(gate.id + " has no display in this locale");
                if (string.IsNullOrEmpty(gate.builders)) faults.Add(gate.id + " has no builders line in this locale");

                if (string.IsNullOrEmpty(gate.verse))
                {
                    faults.Add(gate.id + " names no verse");
                }
                else if (!ScriptureService.TryGetVerse(gate.verse.Trim(), out _))
                {
                    faults.Add(gate.id + ": " + gate.verse + " does not resolve");
                }

                if (!string.IsNullOrEmpty(gate.segment))
                {
                    if (!segments.Contains(gate.segment)) faults.Add(gate.id + " names segment " + gate.segment + ", which the wall does not build");
                    if (gate.segment == playersSegment) marked++;
                }
            }

            if (string.IsNullOrEmpty(playersSegment))
            {
                faults.Add("no terminal stage names a gate_segment, so no gate can be the player's");
            }
            else if (marked != 1)
            {
                faults.Add(marked + " gate(s) carry the terminal stage's segment " + playersSegment + "; exactly one should");
            }

            Check("18 the codex names every gate", faults.Count == 0,
                gates.Length + " gate(s), the player's on " + (playersSegment ?? "nothing") +
                (faults.Count > 0 ? " — [" + string.Join("; ", faults) + "]" : ""));
        }

        static void ACostedMoveCostsTheDay()
        {
            var costed = new List<KeyValuePair<string, ContestMoveDef>>();
            foreach (KeyValuePair<string, ContestConfig> pair in GameData.Contests ?? new Dictionary<string, ContestConfig>())
            {
                if (pair.Value == null || pair.Value.moves == null) continue;
                foreach (ContestMoveDef move in pair.Value.moves)
                {
                    if (move != null && move.costs_work > 0) costed.Add(new KeyValuePair<string, ContestMoveDef>(pair.Key, move));
                }
            }

            if (costed.Count == 0)
            {
                Check("17 a costed move costs the day", false, "no contest declares a move with costs_work");
                return;
            }

            var faults = new List<string>();
            MethodInfo apply = typeof(MoraleContest).GetMethod("ApplyPlayerMove", BindingFlags.Instance | BindingFlags.NonPublic);
            if (apply == null)
            {
                Check("17 a costed move costs the day", false, "MoraleContest.ApplyPlayerMove is no longer reachable by reflection");
                return;
            }

            foreach (KeyValuePair<string, ContestMoveDef> entry in costed)
            {
                string contestId = entry.Key;
                ContestMoveDef move = entry.Value;
                StageDef stage = StageFighting(contestId);
                if (stage == null)
                {
                    faults.Add(contestId + "/" + move.id + " costs work but no stage fights that contest");
                    continue;
                }

                // Below the price: on the menu but shut.
                MoraleContest poor = BuildContestWithADay(stage, "HarnessCostedPoor", move.costs_work - 1);
                if (poor.IsMoveAvailable(move.id))
                {
                    faults.Add(contestId + "/" + move.id + " is offered with " + (move.costs_work - 1) + " of work left, below its price of " + move.costs_work);
                }
                UnityEngine.Object.DestroyImmediate(poor.gameObject);

                // At the price: offered, spends exactly the price, then shut for the rest of the fight.
                int start = move.costs_work + 2;
                MoraleContest able = BuildContestWithADay(stage, "HarnessCostedAble", start);
                GameState state;
                ServiceLocator.TryGet(out state);

                if (!able.IsMoveAvailable(move.id))
                {
                    faults.Add(contestId + "/" + move.id + " is shut with " + start + " of work left");
                }
                else
                {
                    apply.Invoke(able, new object[] { move.id });
                    int left = state != null ? state.workCapacity : -1;
                    if (left != start - move.costs_work)
                    {
                        faults.Add(contestId + "/" + move.id + " left " + left + " of work from " + start + ", expected " + (start - move.costs_work));
                    }

                    if (able.IsMoveAvailable(move.id))
                    {
                        faults.Add(contestId + "/" + move.id + " is offered a second time in the same fight");
                    }
                }
                UnityEngine.Object.DestroyImmediate(able.gameObject);
            }

            Check("17 a costed move costs the day", faults.Count == 0,
                faults.Count == 0
                    ? costed.Count + " costed move(s) priced in the day, single use"
                    : string.Join("; ", faults));
        }

        /// <summary>A fight on this stage with a day of the given capacity behind it.</summary>
        static MoraleContest BuildContestWithADay(StageDef stage, string hostName, int capacity)
        {
            GameState state = GameState.NewGame();
            state.day = stage.day;
            state.workCapacity = Mathf.Max(0, capacity);
            state.workCapacityMax = Mathf.Max(state.workCapacityMax, state.workCapacity);

            ServiceLocator.Clear();
            ServiceLocator.Register(state);

            var host = new GameObject(hostName);
            host.AddComponent<WallSystem>().Build(null);
            ServiceLocator.Register(host.AddComponent<ResourceSystem>());
            var contest = host.AddComponent<MoraleContest>();
            contest.Begin(stage.contest);
            return contest;
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
        static bool ResolveNight(StageDef stage, bool postWatch, out string damagedSegment)
        {
            GameState state = GameState.NewGame();
            state.day = stage.day;
            ServiceLocator.Clear();
            ServiceLocator.Register(state);

            var host = new GameObject("HarnessNight");
            host.AddComponent<WallSystem>().Build(null);
            var cycle = host.AddComponent<DayCycle>();
            cycle.enabled = false;
            cycle.EndDay(postWatch ? 6 : 12, postWatch ? 6 : 0);

            damagedSegment = cycle.LastNightDamagedSegment;
            bool watchFlag = state.HasFlag(GameFlags.WatchPostedForDay(stage.day));

            UnityEngine.Object.DestroyImmediate(host);
            return watchFlag;
        }

        // The first launch of a calendar day is noticed once and says how long the player was
        // away. It pays nothing and resets nothing: the streak and the talents it paid are gone.
        static void CheckInSchedule()
        {
            var today = new DateTime(2026, 1, 10);

            GameState state = GameState.NewGame();
            DailyCheckIn.Result first = DailyCheckIn.Apply(state, today);
            Check("the first launch of a run is noticed and was away zero days",
                first.First && first.DaysAway == 0 && state.talents == 0,
                "first=" + first.First + " away=" + first.DaysAway + " talents=" + state.talents);

            DailyCheckIn.Result sameDay = DailyCheckIn.Apply(state, today);
            Check("a second launch the same day is not noticed twice",
                !sameDay.First && sameDay.DaysAway == 0,
                "first=" + sameDay.First + " away=" + sameDay.DaysAway);

            DailyCheckIn.Result afterGap = DailyCheckIn.Apply(state, today.AddDays(7));
            Check("a launch after a week away says seven days, and costs nothing",
                afterGap.First && afterGap.DaysAway == 7 && state.talents == 0 && state.checkInStreak == 0,
                "first=" + afterGap.First + " away=" + afterGap.DaysAway + " talents=" + state.talents
                + " streak=" + state.checkInStreak);

            DailyCheckIn.Result nextDay = DailyCheckIn.Apply(state, today.AddDays(8));
            Check("the next day is one day away",
                nextDay.First && nextDay.DaysAway == 1, "away=" + nextDay.DaysAway);
        }
    }
}
